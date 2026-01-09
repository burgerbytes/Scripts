// GUID: 30f201f35d336bf4d840162cd6fd1fde
////////////////////////////////////////////////////////////

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using System.Runtime.CompilerServices;

public class BattleManager : MonoBehaviour
{
    public static BattleManager Instance { get; private set; }

    public enum BattleState { Idle, BattleStart, PlayerPhase, EnemyPhase, BattleEnd }
    public enum PlayerActionType { None, Ability1, Ability2 }
    public enum IntentType { Attack }

    [Serializable]
    private class PartyMemberRuntime
    {
        public string name = "Ally";
        public GameObject avatarGO;
        public Animator animator;
        public HeroStats stats;

        public bool hasActedThisRound;
        public bool IsDead => stats == null || stats.CurrentHp <= 0;
    }

    public struct EnemyIntent
    {
        public IntentType type;
        public Monster enemy;
        public int targetPartyIndex;
    }

    // REQUIRED BY PartyHUDSlot.cs
    public struct PartyMemberSnapshot
    {
        public string Name;
        public int HP;
        public int MaxHP;
        public int Stamina;
        public int MaxStamina;
        public bool IsDead;
        public bool HasActedThisRound;

        public bool IsBlocking;
        public int Shield;

        public float HP01 => MaxHP <= 0 ? 0f : Mathf.Clamp01((float)HP / MaxHP);
        public float Stamina01 => MaxStamina <= 0 ? 0f : Mathf.Clamp01((float)Stamina / MaxStamina);
    }

    [Header("Run / Resources")]
    [SerializeField] private ResourcePool resourcePool;

    [Header("Party (Run Instance)")]
    [SerializeField] private Transform[] partySpawnPoints;
    [SerializeField] private GameObject[] partyMemberPrefabs = new GameObject[3];
    [SerializeField] private Transform partyRoot;
    [SerializeField] private int partySize = 3;

    [Header("Encounter / Spawn")]
    [SerializeField] private GameObject[] monsterPrefabs;
    [SerializeField] private Transform[] monsterSpawnPoints;
    [SerializeField] private int minMonstersPerEncounter = 1;
    [SerializeField] private int maxMonstersPerEncounter = 3;

    [Header("Damage Numbers")]
    [SerializeField] private DamageNumber damageNumberPrefab;
    [SerializeField] private Vector3 damageNumberWorldOffset = new Vector3(0f, 1.2f, 0f);
    [SerializeField] private Vector3 damageNumberRandomJitter = new Vector3(0.2f, 0.15f, 0f);

    [Header("Start-of-Run Rewards (First Battle Only)")]
    [SerializeField] private bool showStartRewardsOnFirstBattle = true;
    [SerializeField] private int startRewardChoices = 2;
    [SerializeField] private bool includeSkipOption = true;

    [SerializeField] private PostBattleRewardPanel startRewardPanel;
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private PostBattleFlowController postBattleFlow;

    [Header("Post-Battle Rewards (After Each Victory)")]
    [SerializeField] private bool enablePostBattleRewards = true;
    [SerializeField] private Vector2Int postBattleRewardChoicesRange = new Vector2Int(2, 5);
    [SerializeField] private bool includeSkipOptionPostBattle = false;

    [Tooltip("Optional override. If null, BattleManager will reuse Start Reward Panel.")]
    [SerializeField] private PostBattleRewardPanel postBattleRewardPanel;

    [Header("External Systems")]
    [SerializeField] private StretchController stretchController;
    [SerializeField] private ScrollingBackground scrollingBackground;

    [Header("Input / Targeting")]
    [SerializeField] private bool allowClickToSelectMonsterTarget = true;
    [SerializeField] private bool ignoreClicksOverUI = true;

    
    [Header("Enemy Lunge (No Animation Clips)")]
    [Tooltip("How far the enemy sprite/visual lunges toward the target during an attack (world units).")]
    [SerializeField] private float enemyLungeDistance = 0.35f;
    [Tooltip("Seconds to move from start to lunge peak.")]
    [SerializeField] private float enemyLungeForwardSeconds = 0.12f;
    [Tooltip("Seconds to hold at the lunge peak before returning.")]
    [SerializeField] private float enemyLungeHoldSeconds = 0.05f;
    [Tooltip("Seconds to move from lunge peak back to start.")]
    [SerializeField] private float enemyLungeBackSeconds = 0.12f;

[Header("Debug")]
    [SerializeField] private bool logFlow = false;

    public event Action<BattleState> OnBattleStateChanged;
    public event Action<int> OnActivePartyMemberChanged;
    public event Action OnPartyChanged;
    public event Action<List<EnemyIntent>> OnEnemyIntentsPlanned;

    public BattleState CurrentState => _state;
    public bool IsPlayerPhase => _state == BattleState.PlayerPhase;
    public bool IsEnemyPhase => _state == BattleState.EnemyPhase;
    public bool IsResolving => _resolving;
    public int PartyCount => _party != null ? _party.Count : 0;
    public int ActivePartyIndex => _activePartyIndex;

    private BattleState _state = BattleState.Idle;

    private readonly List<Monster> _activeMonsters = new List<Monster>();
    private readonly List<EnemyIntent> _plannedIntents = new List<EnemyIntent>();

    private List<PartyMemberRuntime> _party = new List<PartyMemberRuntime>(3);
    private int _activePartyIndex = 0;

    private PlayerActionType _pendingAction = PlayerActionType.None;
    private AbilityDefinitionSO _pendingAbility;
    private int _pendingActorIndex = -1;

    private bool _awaitingEnemyTarget = false;
    private Monster _selectedEnemyTarget;

    private bool _resolving;
    // Used to sync damage/removal to the exact impact frame of the caster's animation.
    private bool _waitingForImpact;
    private bool _impactFired;
    private bool _attackFinished;
    private Camera _mainCam;

    private Coroutine _startBattleRoutine;
    private Coroutine _enemyTurnRoutine;

    private bool _startupRewardHandled;

    private bool _postBattleRunning;

    // ✅ IMPORTANT: finds inactive objects too (e.g., UI panels disabled by default)
    private static T FindInSceneIncludingInactive<T>() where T : UnityEngine.Object
    {
        var all = Resources.FindObjectsOfTypeAll<T>();
        for (int i = 0; i < all.Length; i++)
        {
            var obj = all[i];
            if (obj == null) continue;

            if (obj is Component c)
            {
                if (c.gameObject != null && c.gameObject.scene.IsValid())
                    return obj;
            }
            else if (obj is GameObject go)
            {
                if (go.scene.IsValid())
                    return obj;
            }
        }
        return null;
    }

    private void Awake()
    {
        
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[BattleManager] Duplicate instance detected. Existing={Instance.name} ({Instance.GetInstanceID()}), New={name} ({GetInstanceID()}). Using the new instance.", this);
        }
        Instance = this;
_mainCam = Camera.main;

        // Prefer inspector refs; if missing, auto-find (INCLUDING INACTIVE)
        if (resourcePool == null) resourcePool = FindInSceneIncludingInactive<ResourcePool>();
        if (stretchController == null) stretchController = FindInSceneIncludingInactive<StretchController>();
        if (postBattleFlow == null) postBattleFlow = FindInSceneIncludingInactive<PostBattleFlowController>();
        if (inventory == null) inventory = FindInSceneIncludingInactive<PlayerInventory>();
        if (startRewardPanel == null) startRewardPanel = FindInSceneIncludingInactive<PostBattleRewardPanel>();
        if (postBattleRewardPanel == null) postBattleRewardPanel = startRewardPanel;


        StartNewRun();
    }

    private void Start()
    {
        StartBattle();
    }

    private void Update()
    {
        if (!IsPlayerPhase || !_awaitingEnemyTarget || _resolving || !allowClickToSelectMonsterTarget)
            return;

        if (!Input.GetMouseButtonDown(0))
            return;

        // NOTE: When selecting an enemy target, we intentionally DO NOT block clicks just because the pointer is over UI.
        // In many Unity UI setups, an invisible full-screen Image/Panel may still be a raycast target, causing
        // EventSystem.current.IsPointerOverGameObject() to return true everywhere and breaking targeting.
        // If you truly want to block clicks on specific UI, do it via a dedicated input-blocker overlay.
        if (ignoreClicksOverUI && !_awaitingEnemyTarget && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            if (logFlow) Debug.Log("[Battle] Click ignored because pointer is over UI.");
            return;
        }

        Monster clicked = TryGetClickedMonster();
        if (clicked != null && _activeMonsters.Contains(clicked) && !clicked.IsDead)
            SelectEnemyTarget(clicked);
    }

    // Called by AnimatorImpactEvents (Animation Events).
    public void NotifyAttackImpact()
    {
        _impactFired = true;
    }

    // Optional: call via animation event at the end of the attack animation if desired.
    public void NotifyAttackFinished()
    {
        _attackFinished = true;
    }

    public void StartNewRun()
    {
        _startupRewardHandled = false;

        CleanupExistingEncounter();
        DestroyPartyAvatars();

        if (resourcePool != null)
            resourcePool.ResetForNewRun(0, 0, 0, 0);

        _party = new List<PartyMemberRuntime>(partySize);

        int count = Mathf.Clamp(partySize, 1, 3);
        for (int i = 0; i < count; i++)
        {
            PartyMemberRuntime m = new PartyMemberRuntime();
            m.name = $"Ally {i + 1}";

            GameObject prefab = (partyMemberPrefabs != null && i < partyMemberPrefabs.Length) ? partyMemberPrefabs[i] : null;
            if (prefab == null)
            {
                Debug.LogError($"[BattleManager] Missing party prefab for slot {i}. Assign Party Member Prefabs size 3.");
                _party.Add(m);
                continue;
            }

            Transform spawn = (partySpawnPoints != null && i < partySpawnPoints.Length) ? partySpawnPoints[i] : null;
            Vector3 pos = spawn != null ? spawn.position : Vector3.zero;

            GameObject go = Instantiate(prefab, pos, Quaternion.identity, partyRoot);
            m.avatarGO = go;
            m.animator = go.GetComponentInChildren<Animator>(true);
            m.stats = go.GetComponentInChildren<HeroStats>(true);

            if (m.stats == null)
                Debug.LogError($"[BattleManager] Party prefab slot {i} has no HeroStats component.");

            if (m.stats != null)
                m.stats.ResetForNewRun();

            _party.Add(m);
        }

        _activePartyIndex = GetFirstAlivePartyIndex();
        OnActivePartyMemberChanged?.Invoke(_activePartyIndex);
        NotifyPartyChanged();
    }

    private void DestroyPartyAvatars()
    {
        if (_party == null) return;
        for (int i = 0; i < _party.Count; i++)
        {
            if (_party[i] != null && _party[i].avatarGO != null)
                Destroy(_party[i].avatarGO);
        }
    }

    public PartyMemberSnapshot GetPartyMemberSnapshot(int index)
    {
        Debug.Log($"[BattleManager] GetPartyMemberSnapshot.");
        if (!IsValidPartyIndex(index))
            return default;

        var m = _party[index];
        var hs = m.stats;

        int hp = hs != null ? hs.CurrentHp : 0;
        int maxHp = hs != null ? hs.MaxHp : 0;

        int stamina = hs != null ? Mathf.RoundToInt(hs.CurrentStamina) : 0;
        int maxStamina = hs != null ? hs.MaxStamina : 0;

        int shield = hs != null ? hs.Shield : 0;

        return new PartyMemberSnapshot
        {
            Name = string.IsNullOrEmpty(m.name) ? $"Ally {index + 1}" : m.name,
            HP = hp,
            MaxHP = maxHp,
            Stamina = stamina,
            MaxStamina = maxStamina,
            IsDead = m.IsDead,
            HasActedThisRound = m.hasActedThisRound,
            Shield = shield,
            IsBlocking = shield > 0
        };
    }

    public void SetActivePartyMember(int index)
    {
        if (!IsPlayerPhase) return;
        if (!IsValidPartyIndex(index)) return;

        _activePartyIndex = index;
        OnActivePartyMemberChanged?.Invoke(_activePartyIndex);
        NotifyPartyChanged();
    }

    public void BeginAbilityUseFromMenu(HeroStats hero, AbilityDefinitionSO ability)
    {
        Debug.Log($"[BattleManager] BeginAbilityUseFromMenu");
        if (!IsPlayerPhase || _resolving) return;
        if (hero == null || ability == null) return;

        int actorIndex = GetPartyIndexForHero(hero);
        if (!IsValidPartyIndex(actorIndex)) return;

        PartyMemberRuntime actor = _party[actorIndex];
        if (actor.IsDead) return;

        if (_pendingAction != PlayerActionType.None) return;

        ResourceCost cost = GetEffectiveCost(actor.stats, ability);

        _pendingActorIndex = actorIndex;
        _pendingAbility = ability;
        _selectedEnemyTarget = null;

        // Reset animation-sync flags for this cast.
        _waitingForImpact = false;
        _impactFired = false;
        _attackFinished = false;

        _pendingAction = PlayerActionType.Ability1;

        if (ability.targetType == AbilityTargetType.Enemy)
        {
            _awaitingEnemyTarget = true;
            if (logFlow) Debug.Log($"[Battle] Awaiting ENEMY target for {ability.abilityName}");
        }
        else
        {
            _awaitingEnemyTarget = false;
            StartCoroutine(ResolvePendingAbility());
        }

        NotifyPartyChanged();
    }

    public void SelectEnemyTarget(Monster target)
    {
        Debug.Log("SelectEnemyTarget called");
        if (!IsPlayerPhase) return;
        if (!_awaitingEnemyTarget || _pendingAbility == null) return;
        if (target == null || target.IsDead) return;

        _selectedEnemyTarget = target;
        _awaitingEnemyTarget = false;

        StartCoroutine(ResolvePendingAbility());
    }

    public void StartBattle()
    {
        if (_resolving) return;

        if (_startBattleRoutine != null)
            StopCoroutine(_startBattleRoutine);

        _startBattleRoutine = StartCoroutine(StartBattleRoutine());
    }

    /// <summary>
    /// Ends the player's turn immediately and begins the enemy phase.
    /// Intended to be called by the End Turn button.
    /// </summary>
    public void EndTurn()
    {
        // Only allow during player phase and when not already mid-resolution/enemy turn.
        if (!IsPlayerPhase) return;
        if (_resolving) return;
        if (_enemyTurnRoutine != null) return;

        // If there are no enemies, nothing to do.
        if (_activeMonsters == null || _activeMonsters.Count == 0) return;

        _enemyTurnRoutine = StartCoroutine(EnemyPhaseRoutine());
    }

    private IEnumerator EnemyPhaseRoutine()
    {
        // Enter enemy phase.
        SetState(BattleState.EnemyPhase);

        // Make sure no pending target selection / casts linger.
        CancelPendingAbility();

        // If intents were never planned (or were cleared), plan them now.
        if (_plannedIntents.Count == 0) PlanEnemyIntents();

        // Copy, so if visuals subscribe and mutate we still have a stable execution list.
        var intentsToExecute = new List<EnemyIntent>(_plannedIntents);

        // Clear visuals immediately after we commit to executing them.
        _plannedIntents.Clear();
        OnEnemyIntentsPlanned?.Invoke(new List<EnemyIntent>(_plannedIntents));
        NotifyPartyChanged();

        // Execute each intent.
        for (int i = 0; i < intentsToExecute.Count; i++)
        {
            var intent = intentsToExecute[i];
            if (intent.enemy == null || intent.enemy.IsDead) continue;

            int targetIdx = intent.targetPartyIndex;

            // If target is invalid/dead, retarget to a living hero.
            if (!IsValidPartyIndex(targetIdx) || _party[targetIdx].IsDead)
                targetIdx = GetRandomLivingTargetIndex();

            if (!IsValidPartyIndex(targetIdx)) break;

            
HeroStats targetStats = _party[targetIdx].stats;
GameObject targetGO = _party[targetIdx].avatarGO;

Transform targetTf = targetGO != null ? targetGO.transform : (targetStats != null ? targetStats.transform : null);

// Lunge/translate attack (no animation clips required). Damage is applied at the lunge peak.
yield return EnemyLungeAttack(intent.enemy, targetTf, () =>
{
    if (targetStats == null) return;

    int hpBefore = targetStats.CurrentHp;
    int raw = intent.enemy.GetDamage();

    // Apply damage (HeroStats handles defense).
    targetStats.TakeDamage(raw);

    int dealt = Mathf.Max(0, hpBefore - targetStats.CurrentHp);

    if (targetGO != null)
        SpawnDamageNumber(targetGO.transform.position, dealt);
});

NotifyPartyChanged();

// If all heroes are dead, end battle for now (you can wire a defeat flow later). (you can wire a defeat flow later).
            if (IsPartyDefeated())
            {
                Debug.Log("[BattleManager] Party defeated (enemy phase).", this);
                SetState(BattleState.BattleEnd);
                _enemyTurnRoutine = null;
                yield break;
            }
        }

        // Back to player phase: reset round flags and re-plan intents for the next enemy phase preview.
        ResetPartyRoundFlags();
        PlanEnemyIntents();

        SetState(BattleState.PlayerPhase);

        _activePartyIndex = GetFirstAlivePartyIndex();
        OnActivePartyMemberChanged?.Invoke(_activePartyIndex);

        NotifyPartyChanged();

        _enemyTurnRoutine = null;
    }

    private bool IsPartyDefeated()
    {
        for (int i = 0; i < PartyCount; i++)
        {
            if (_party[i] != null && !_party[i].IsDead)
                return false;
        }
        return true;
    }

private Transform GetEnemyVisualTransform(Monster enemy)
{
    if (enemy == null) return null;

    // Prefer a SpriteRenderer child (most 2D enemies).
    var sr = enemy.GetComponentInChildren<SpriteRenderer>(true);
    if (sr != null) return sr.transform;

    // Fallback: any child transform (so we don't move a shared root if that's undesirable).
    if (enemy.transform.childCount > 0) return enemy.transform.GetChild(0);

    return enemy.transform;
}

private IEnumerator LungeTranslate(Transform mover, Vector3 from, Vector3 to, float seconds)
{
    if (mover == null) yield break;

    if (seconds <= 0f)
    {
        mover.position = to;
        yield break;
    }

    float t = 0f;
    while (t < seconds)
    {
        t += Time.deltaTime;
        float a = Mathf.Clamp01(t / seconds);
        mover.position = Vector3.Lerp(from, to, a);
        yield return null;
    }
    mover.position = to;
}

private IEnumerator EnemyLungeAttack(Monster enemy, Transform target, Action applyDamage)
{
    // This reproduces the old "rudimentary" translation attack:
    // - Move enemy visual toward the target (lunge)
    // - Apply damage at peak/impact
    // - Move back to start

    if (enemy == null) yield break;

    Transform visual = GetEnemyVisualTransform(enemy);
    if (visual == null)
    {
        applyDamage?.Invoke();
        yield break;
    }

    Vector3 start = visual.position;

    Vector3 targetPos = target != null ? target.position : start;
    Vector3 dir = (targetPos - start);
    dir.z = 0f;
    if (dir.sqrMagnitude < 0.0001f) dir = Vector3.right;

    dir.Normalize();

    Vector3 peak = start + dir * Mathf.Max(0f, enemyLungeDistance);

    // Forward
    yield return LungeTranslate(visual, start, peak, enemyLungeForwardSeconds);

    // Impact
    applyDamage?.Invoke();

    // Small hold at peak (feel/snap)
    if (enemyLungeHoldSeconds > 0f)
        yield return new WaitForSeconds(enemyLungeHoldSeconds);

    // Back
    yield return LungeTranslate(visual, peak, start, enemyLungeBackSeconds);
}




    private IEnumerator StartBattleRoutine()
    {
        CleanupExistingEncounter();
        SetState(BattleState.BattleStart);

        ResetPartyRoundFlags();
        SpawnEncounterMonsters();

        if (_activeMonsters.Count == 0)
        {
            SetState(BattleState.Idle);
            yield break;
        }

        if (stretchController != null) stretchController.SetEncounterActive(true);
        if (scrollingBackground != null) scrollingBackground.SetPaused(true);

        bool doStartReward = showStartRewardsOnFirstBattle && !_startupRewardHandled;
        if (doStartReward)
        {
            _startupRewardHandled = true;

            List<ItemOptionSO> pool = postBattleFlow != null ? postBattleFlow.GetItemOptionPool() : null;

            if (pool != null && pool.Count > 0 && startRewardPanel != null)
            {
                List<ItemOptionSO> rolled = RollUnique(pool, Mathf.Clamp(startRewardChoices, 1, pool.Count));
                if (includeSkipOption) rolled.Add(BuildRuntimeSkipOption());

                ItemOptionSO chosen = null;
                bool picked = false;

                startRewardPanel.Show(rolled, (opt) =>
                {
                    chosen = opt;
                    picked = true;
                });

                yield return new WaitUntil(() => picked);

                startRewardPanel.Hide();

                if (chosen != null && chosen.item != null && inventory != null)
                    inventory.Add(chosen.item, chosen.quantity);
            }
            else if (logFlow)
            {
                Debug.Log(
                    $"[Battle] Start reward skipped. poolCount={(pool != null ? pool.Count : -1)}, startRewardPanel={(startRewardPanel != null ? startRewardPanel.name : "NULL")}",
                    this);
            }
        }

        PlanEnemyIntents();

        SetState(BattleState.PlayerPhase);
        _activePartyIndex = GetFirstAlivePartyIndex();
        OnActivePartyMemberChanged?.Invoke(_activePartyIndex);

        NotifyPartyChanged();
    }

    private IEnumerator ResolvePendingAbility()
    {
        if (_pendingAbility == null || !IsValidPartyIndex(_pendingActorIndex))
        {
            CancelPendingAbility();
            yield break;
        }

        // Capture references up-front so end-of-battle cleanup (or other flows)
        // can't null out _pendingAbility mid-coroutine.
        AbilityDefinitionSO ability = _pendingAbility;
        Debug.Log($"[BattleManager] Confirmed/casting ability: {ability.name}", this);

        PartyMemberRuntime actor = _party[_pendingActorIndex];
        HeroStats actorStats = actor.stats;
        if (actorStats == null || actor.IsDead)
        {
            CancelPendingAbility();
            yield break;
        }

        Monster enemyTarget = _selectedEnemyTarget;
        if (ability.targetType == AbilityTargetType.Enemy)
        {
            if (enemyTarget == null || enemyTarget.IsDead)
            {
                _awaitingEnemyTarget = true;
                yield break;
            }
        }

        ResourceCost cost = GetEffectiveCost(actorStats, ability);
        //int actorPoolId = actorStats.ResourcePool.GetInstanceID();
        int id = RuntimeHelpers.GetHashCode(resourcePool);
        if (resourcePool == null || !resourcePool.TrySpend(cost))
        {
            CancelPendingAbility();
            yield break;
        }
        // From this point, the cast is committed (resources spent). Block other actions.
        _resolving = true;

        // Trigger caster attack animation for the pending ability.
        // Some abilities may use impact-sync (Animation Event) and others may not.

        Animator anim = actor.animator;
        if (anim == null && actor.avatarGO != null)
            anim = actor.avatarGO.GetComponentInChildren<Animator>(true);

        // Reset flags for this cast (only matters if we choose to wait for impact).
        _waitingForImpact = false;
        _impactFired = false;
        _attackFinished = false;
        // Decide behavior by ability name
        bool useImpactSync = false;
        string stateToPlay = null;

        if (anim != null)
        {
            // Per-character profile (Option B)
            var profile = anim.GetComponentInParent<CasterAnimationProfile>();

            switch (ability.name)
            {
                case "Slash":
                    useImpactSync = true;
                    // Allow per-character override/mapping if profile exists
                    stateToPlay = profile != null ? profile.GetAttackStateForAbility("Slash") : null;
                    if (string.IsNullOrWhiteSpace(stateToPlay))
                        stateToPlay = "fighter_basic_attack"; // fallback
                    break;

                case "Pyre":
                    useImpactSync = true; // if you want damage to happen on a specific frame
                    stateToPlay = profile != null ? profile.GetAttackStateForAbility("Pyre") : null;
                    if (string.IsNullOrWhiteSpace(stateToPlay))
                        stateToPlay = "mage_basic_attack"; // fallback example
                    break;

                case "Backstab":
                    useImpactSync = true; // if you want damage to happen on a specific frame
                    stateToPlay = profile != null ? profile.GetAttackStateForAbility("Backstab") : null;
                    if (string.IsNullOrWhiteSpace(stateToPlay))
                        stateToPlay = "ninja_backstab"; // fallback example
                    break;

                default:
                    // Default: still play *something* (or skip animation if you prefer)
                    useImpactSync = false; // default to immediate apply unless you want all abilities synced
                    stateToPlay = profile != null ? profile.GetAttackStateForAbility(ability.name) : null;
                    // If profile doesn't have an entry, you can fall back to a generic cast/attack
                    if (string.IsNullOrWhiteSpace(stateToPlay))
                        stateToPlay = "fighter_basic_attack"; // safe default until you add more states
                    break;
            }

            if (useImpactSync)
                _waitingForImpact = true; // your existing coroutine should wait until _impactFired (or timeout)

            anim.Play(stateToPlay, 0, 0f);
        }


		if (ability.targetType == AbilityTargetType.Enemy && enemyTarget != null)
        {
            // If this ability uses impact-sync and the caster has an animator playing the attack,
            // wait for the impact frame event before applying damage.
            if (useImpactSync && anim != null)
            {
                _waitingForImpact = true;

                // Give the animator one frame to enter the state.
                yield return null;

                float elapsed = 0f;
                const float failSafeSeconds = 3.0f; // prevents a soft-lock if the animation event is missing.
                while (!_impactFired && elapsed < failSafeSeconds)
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                _waitingForImpact = false;
            }

            int dealt = enemyTarget.TakeDamageFromAbility(
				abilityBaseDamage: ability.baseDamage,
                classAttackModifier: actorStats.ClassAttackModifier,
				element: ability.element);

            SpawnDamageNumber(enemyTarget.transform.position, dealt);
            actorStats.ApplyOnHitEffectsTo(enemyTarget);

            if (enemyTarget.IsDead)
            {
                actorStats.GainXP(5);
                RemoveMonster(enemyTarget);
                PlanEnemyIntents();
            }
        }


		if (ability.targetType == AbilityTargetType.Self && ability.shieldAmount > 0)
        {
			actorStats.AddShield(ability.shieldAmount);
        }

        actor.hasActedThisRound = true;

        _resolving = false;

        if (AbilityCastState.Instance != null)
            AbilityCastState.Instance.ClearCast();

        CancelPendingAbility();
        NotifyPartyChanged();
    }

    private ResourceCost GetEffectiveCost(HeroStats actor, AbilityDefinitionSO ability)
    {
        if (ability == null) return default;
        return ability.cost;
    }

    private void CancelPendingAbility()
    {
        _pendingAction = PlayerActionType.None;
        _pendingAbility = null;
        _pendingActorIndex = -1;
        _awaitingEnemyTarget = false;
        _selectedEnemyTarget = null;

        _waitingForImpact = false;
        _impactFired = false;
        _attackFinished = false;

        if (AbilityCastState.Instance != null)
            AbilityCastState.Instance.ClearCast();
    }

    private void PlanEnemyIntents()
    {
        _plannedIntents.Clear();

        for (int i = 0; i < _activeMonsters.Count; i++)
        {
            Monster m = _activeMonsters[i];
            if (m == null || m.IsDead) continue;

            int targetIdx = GetRandomLivingTargetIndex();
            if (targetIdx < 0) continue;

            _plannedIntents.Add(new EnemyIntent
            {
                type = IntentType.Attack,
                enemy = m,
                targetPartyIndex = targetIdx
            });
        }

        OnEnemyIntentsPlanned?.Invoke(new List<EnemyIntent>(_plannedIntents));
        NotifyPartyChanged();
    }

    private void ResetPartyRoundFlags()
    {
        for (int i = 0; i < PartyCount; i++)
            _party[i].hasActedThisRound = false;

        CancelPendingAbility();
        NotifyPartyChanged();
    }

    private int GetFirstAlivePartyIndex()
    {
        for (int i = 0; i < PartyCount; i++)
            if (!_party[i].IsDead) return i;
        return 0;
    }

    private int GetRandomLivingTargetIndex()
    {
        List<int> living = new List<int>(PartyCount);
        for (int i = 0; i < PartyCount; i++)
            if (!_party[i].IsDead) living.Add(i);

        if (living.Count == 0) return -1;
        return living[UnityEngine.Random.Range(0, living.Count)];
    }

    private void SpawnEncounterMonsters()
    {
        _activeMonsters.Clear();

        if (monsterPrefabs == null || monsterPrefabs.Length == 0)
            return;

        int maxSlots = (monsterSpawnPoints != null && monsterSpawnPoints.Length > 0) ? monsterSpawnPoints.Length : 1;

        int count = UnityEngine.Random.Range(minMonstersPerEncounter, maxMonstersPerEncounter + 1);
        count = Mathf.Clamp(count, 1, maxSlots);

        for (int i = 0; i < count; i++)
        {
            GameObject prefab = monsterPrefabs[UnityEngine.Random.Range(0, monsterPrefabs.Length)];
            if (prefab == null) continue;

            Transform spawn = (monsterSpawnPoints != null && i < monsterSpawnPoints.Length) ? monsterSpawnPoints[i] : null;
            Vector3 pos = spawn != null ? spawn.position : Vector3.zero;

            GameObject go = Instantiate(prefab, pos, Quaternion.identity);
            Monster m = go.GetComponentInChildren<Monster>(true);
            if (m != null) _activeMonsters.Add(m);
        }
    }

    private void RemoveMonster(Monster m)
    {
        if (m == null) return;

        _activeMonsters.Remove(m);
        if (m.gameObject != null) Destroy(m.gameObject);

        // If all enemies are dead, trigger the post-battle reward loop.
        if (_activeMonsters.Count == 0)
        {
            StartCoroutine(HandleEncounterVictoryRoutine());
        }
    }


    private IEnumerator HandleEncounterVictoryRoutine()
    {
        if (_postBattleRunning)
            yield break;

        _postBattleRunning = true;

        if (logFlow)
            Debug.Log("[Battle] Encounter cleared. Entering post-battle rewards.", this);

        SetState(BattleState.BattleEnd);

        // Ensure no pending actions linger while the reward panel is open.
        CancelPendingAbility();

        if (stretchController != null)
            stretchController.SetEncounterActive(false);

        // Unpause world visuals between fights so scroll segments (and the walk loop) can show.
        if (scrollingBackground != null)
            scrollingBackground.SetPaused(false);

        // Roll and present post-battle rewards.
        if (enablePostBattleRewards)
        {
            List<ItemOptionSO> pool = postBattleFlow != null ? postBattleFlow.GetItemOptionPool() : null;
            PostBattleRewardPanel panel = postBattleRewardPanel != null ? postBattleRewardPanel : startRewardPanel;

            if (pool != null && pool.Count > 0 && panel != null)
            {
                int min = Mathf.Clamp(postBattleRewardChoicesRange.x, 1, pool.Count);
                int max = Mathf.Clamp(postBattleRewardChoicesRange.y, min, pool.Count);
                int desired = UnityEngine.Random.Range(min, max + 1);

                List<ItemOptionSO> rolled = RollUnique(pool, desired);
                if (includeSkipOptionPostBattle)
                    rolled.Add(BuildRuntimeSkipOption());

                ItemOptionSO chosen = null;
                bool picked = false;

                panel.Show(rolled, opt =>
                {
                    chosen = opt;
                    picked = true;
                });

                yield return new WaitUntil(() => picked);

                panel.Hide();

                if (chosen != null && chosen.item != null && inventory != null)
                    inventory.Add(chosen.item, chosen.quantity);
            }
        }

        // Start the next encounter.
        yield return null;

        _postBattleRunning = false;

        StartBattle();
    }

    private void CleanupExistingEncounter()
    {
        for (int i = 0; i < _activeMonsters.Count; i++)
            if (_activeMonsters[i] != null) Destroy(_activeMonsters[i].gameObject);

        _activeMonsters.Clear();
        _plannedIntents.Clear();
        CancelPendingAbility();
    }

    private Monster TryGetClickedMonster()
    {
        if (_mainCam == null) _mainCam = Camera.main;
        if (_mainCam == null) return null;

        Physics.queriesHitTriggers = true;

        Vector3 world = _mainCam.ScreenToWorldPoint(Input.mousePosition);
        Vector2 p2 = new Vector2(world.x, world.y);

        Collider2D[] hits2D = Physics2D.OverlapPointAll(p2);
        if (hits2D != null && hits2D.Length > 0)
        {
            for (int i = 0; i < hits2D.Length; i++)
            {
                Collider2D c = hits2D[i];
                if (c == null) continue;

                Monster m = c.GetComponentInParent<Monster>();
                if (m != null) return m;
            }
        }

        Ray ray = _mainCam.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits3D = Physics.RaycastAll(ray, 500f, ~0, QueryTriggerInteraction.Collide);

        if (hits3D != null && hits3D.Length > 0)
        {
            float best = float.MaxValue;
            Monster bestMonster = null;

            for (int i = 0; i < hits3D.Length; i++)
            {
                Collider c = hits3D[i].collider;
                if (c == null) continue;

                Monster m = c.GetComponentInParent<Monster>();
                if (m == null) continue;

                if (hits3D[i].distance < best)
                {
                    best = hits3D[i].distance;
                    bestMonster = m;
                }
            }

            return bestMonster;
        }

        return null;
    }

    private bool IsValidPartyIndex(int index) => _party != null && index >= 0 && index < _party.Count;

    private void SetState(BattleState s)
    {
        if (_state == s) return;
        _state = s;
        OnBattleStateChanged?.Invoke(_state);
    }

    private void NotifyPartyChanged() => OnPartyChanged?.Invoke();

    private void SpawnDamageNumber(Vector3 worldPos, int amount)
    {
        if (damageNumberPrefab == null) return;

        Vector3 jitter = new Vector3(
            UnityEngine.Random.Range(-damageNumberRandomJitter.x, damageNumberRandomJitter.x),
            UnityEngine.Random.Range(-damageNumberRandomJitter.y, damageNumberRandomJitter.y),
            UnityEngine.Random.Range(-damageNumberRandomJitter.z, damageNumberRandomJitter.z)
        );

        DamageNumber dn = Instantiate(damageNumberPrefab);
        dn.transform.position = worldPos + damageNumberWorldOffset + jitter;

        TrySetDamageNumberValue(dn, amount);
    }

    private static void TrySetDamageNumberValue(DamageNumber dn, int amount)
    {
        if (dn == null) return;

        string[] names = { "SetValue", "SetAmount", "SetNumber", "SetDamage", "Initialize", "Setup" };

        Type t = dn.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (string methodName in names)
        {
            MethodInfo miInt = t.GetMethod(methodName, flags, null, new[] { typeof(int) }, null);
            if (miInt != null)
            {
                miInt.Invoke(dn, new object[] { amount });
                return;
            }

            MethodInfo miStr = t.GetMethod(methodName, flags, null, new[] { typeof(string) }, null);
            if (miStr != null)
            {
                miStr.Invoke(dn, new object[] { amount.ToString() });
                return;
            }
        }

        dn.gameObject.SendMessage("SetValue", amount, SendMessageOptions.DontRequireReceiver);
    }

    private int GetPartyIndexForHero(HeroStats hero)
    {
        if (hero == null || _party == null) return -1;
        for (int i = 0; i < _party.Count; i++)
            if (_party[i] != null && _party[i].stats == hero) return i;
        return -1;
    }

    public HeroStats GetHeroAtPartyIndex(int index)
    {
        if (!IsValidPartyIndex(index)) return null;
        return _party[index].stats;
    }

    private static List<ItemOptionSO> RollUnique(List<ItemOptionSO> pool, int count)
    {
        List<ItemOptionSO> temp = new List<ItemOptionSO>(pool);
        List<ItemOptionSO> result = new List<ItemOptionSO>(count);

        count = Mathf.Clamp(count, 0, temp.Count);

        for (int i = 0; i < count; i++)
        {
            int swapIndex = UnityEngine.Random.Range(i, temp.Count);
            (temp[i], temp[swapIndex]) = (temp[swapIndex], temp[i]);
            result.Add(temp[i]);
        }

        return result;
    }

    private ItemOptionSO BuildRuntimeSkipOption()
    {
        ItemOptionSO skip = ScriptableObject.CreateInstance<ItemOptionSO>();
        skip.optionName = "Skip";
        skip.description = "Skip this reward and start the battle.";
        skip.pros = Array.Empty<string>();
        skip.cons = Array.Empty<string>();
        skip.item = null;
        skip.quantity = 0;
        skip.icon = null;
        return skip;
    }
}
