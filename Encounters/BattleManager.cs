// Assets/Scripts/Encounters/BattleManager.cs

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;

public class BattleManager : MonoBehaviour
{
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

        // PartyHUDSlot expects this
        public bool IsBlocking;

        // Optional: shield amount
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
    [SerializeField] private ReelSpinSystem reels;

    [Header("External Systems")]
    [SerializeField] private StretchController stretchController;
    [SerializeField] private ScrollingBackground scrollingBackground;
    [SerializeField] private PostBattleFlowController postBattleFlow;

    [Header("Input / Targeting")]
    [SerializeField] private bool allowClickToSelectMonsterTarget = true;
    [SerializeField] private bool ignoreClicksOverUI = true;

    [Header("Debug")]
    [SerializeField] private bool logFlow = false;

    public event Action<BattleState> OnBattleStateChanged;
    public event Action<int> OnActivePartyMemberChanged;
    public event Action OnPartyChanged;
    public event Action<List<EnemyIntent>> OnEnemyIntentsPlanned;

    public BattleState CurrentState => _state;
    public bool IsPlayerPhase => _state == BattleState.PlayerPhase;
    public int PartyCount => _party != null ? _party.Count : 0;
    public int ActivePartyIndex => _activePartyIndex;

    // NEW: expose targeting state (Monster click can call SelectEnemyTarget regardless;
    // this is still useful for debugging / UI)
    public bool IsAwaitingEnemyTarget => _awaitingEnemyTarget;
    public AbilityDefinitionSO PendingAbility => _pendingAbility;
    public int PendingActorIndex => _pendingActorIndex;

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
    private Camera _mainCam;

    private bool _startupRewardHandled;
    private Coroutine _startBattleRoutine;

    private void Awake()
    {
        _mainCam = Camera.main;

        if (resourcePool == null) resourcePool = FindFirstObjectByType<ResourcePool>();
        if (stretchController == null) stretchController = FindFirstObjectByType<StretchController>();
        if (postBattleFlow == null) postBattleFlow = FindFirstObjectByType<PostBattleFlowController>();
        if (inventory == null) inventory = FindFirstObjectByType<PlayerInventory>();
        if (reels == null) reels = FindFirstObjectByType<ReelSpinSystem>();
        if (startRewardPanel == null) startRewardPanel = FindFirstObjectByType<PostBattleRewardPanel>();

        StartNewRun();
    }

    private void OnEnable()
    {
        if (stretchController != null)
            stretchController.OnBattleRequested += HandleBattleRequested;
    }

    private void OnDisable()
    {
        if (stretchController != null)
            stretchController.OnBattleRequested -= HandleBattleRequested;
    }

    private void Start()
    {
        StartBattle();
    }

    private void Update()
    {
        // Keep this as a fallback click path (robust pick code),
        // but Monster.OnMouseDown can also call SelectEnemyTarget directly.
        if (!IsPlayerPhase || !_awaitingEnemyTarget || _resolving || !allowClickToSelectMonsterTarget)
            return;

        if (!Input.GetMouseButtonDown(0))
            return;

        if (ignoreClicksOverUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            if (logFlow) Debug.Log("[Battle] Click ignored because pointer is over UI.");
            return;
        }

        if (logFlow)
            Debug.Log($"[Battle] Click while awaiting target. Pending ability: {(_pendingAbility != null ? _pendingAbility.abilityName : "NULL")}");

        Monster clicked = TryGetClickedMonster();

        if (logFlow)
            Debug.Log($"[Battle] Click hit monster: {(clicked != null ? clicked.name : "NONE")}");

        if (clicked != null && _activeMonsters.Contains(clicked) && !clicked.IsDead)
            SelectEnemyTarget(clicked);
        else if (logFlow && clicked != null)
            Debug.Log($"[Battle] Clicked monster '{clicked.name}' is not active/dead. ActiveCount={_activeMonsters.Count}");
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

    // Required by PartyHUDSlot.cs
    public PartyMemberSnapshot GetPartyMemberSnapshot(int index)
    {
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
        if (!IsPlayerPhase || _resolving) return;
        if (hero == null || ability == null) return;

        int actorIndex = GetPartyIndexForHero(hero);
        if (!IsValidPartyIndex(actorIndex)) return;

        PartyMemberRuntime actor = _party[actorIndex];
        if (actor.IsDead) return;
        if (actor.hasActedThisRound) return;

        if (_pendingAction != PlayerActionType.None) return;

        ResourceCost cost = GetEffectiveCost(actor.stats, ability);
        if (resourcePool == null || !resourcePool.CanAfford(cost)) return;

        _pendingActorIndex = actorIndex;
        _pendingAbility = ability;
        _selectedEnemyTarget = null;

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
        if (logFlow) Debug.Log("[Battle] SelectEnemyTarget called");

        if (!IsPlayerPhase) return;
        if (!_awaitingEnemyTarget || _pendingAbility == null) return;
        if (target == null || target.IsDead) return;

        if (!_activeMonsters.Contains(target)) return;

        _selectedEnemyTarget = target;
        _awaitingEnemyTarget = false;

        StartCoroutine(ResolvePendingAbility());
    }

    private void HandleBattleRequested()
    {
        StartBattle();
    }

    public void StartBattle()
    {
        if (_resolving) return;

        if (_startBattleRoutine != null)
            StopCoroutine(_startBattleRoutine);

        _startBattleRoutine = StartCoroutine(StartBattleRoutine());
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

                if (reels != null)
                    yield return SpinReelsOnce();
            }
        }

        PlanEnemyIntents();

        SetState(BattleState.PlayerPhase);
        _activePartyIndex = GetFirstAlivePartyIndex();
        OnActivePartyMemberChanged?.Invoke(_activePartyIndex);

        NotifyPartyChanged();
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

    private IEnumerator SpinReelsOnce()
    {
        bool done = false;

        void MarkDone(Dictionary<string, ReelSymbolSO> _) { done = true; }

        reels.OnSpinFinished += MarkDone;
        reels.SpinAll();

        yield return new WaitUntil(() => done);

        reels.OnSpinFinished -= MarkDone;
    }

    private IEnumerator ResolvePendingAbility()
    {
        if (_pendingAbility == null || !IsValidPartyIndex(_pendingActorIndex))
        {
            CancelPendingAbility();
            yield break;
        }

        PartyMemberRuntime actor = _party[_pendingActorIndex];
        HeroStats actorStats = actor.stats;
        if (actorStats == null || actor.IsDead)
        {
            CancelPendingAbility();
            yield break;
        }

        Monster enemyTarget = null;
        if (_pendingAbility.targetType == AbilityTargetType.Enemy)
        {
            enemyTarget = _selectedEnemyTarget;
            if (enemyTarget == null || enemyTarget.IsDead)
            {
                CancelPendingAbility();
                yield break;
            }
        }

        ResourceCost cost = GetEffectiveCost(actorStats, _pendingAbility);
        if (resourcePool == null || !resourcePool.TrySpend(cost))
        {
            CancelPendingAbility();
            yield break;
        }

        _resolving = true;

        // SLASH
        if (_pendingAbility.targetType == AbilityTargetType.Enemy && enemyTarget != null)
        {
            int dealt = enemyTarget.TakeDamageFromAbility(
                abilityBaseDamage: _pendingAbility.baseDamage,
                classAttackModifier: actorStats.ClassAttackModifier,
                element: _pendingAbility.element);

            SpawnDamageNumber(enemyTarget.transform.position, dealt);
            actorStats.ApplyOnHitEffectsTo(enemyTarget);

            if (enemyTarget.IsDead)
            {
                actorStats.GainXP(5);
                RemoveMonster(enemyTarget);
                PlanEnemyIntents();
            }
        }

        // BLOCK (shield)
        if (_pendingAbility.targetType == AbilityTargetType.Self && _pendingAbility.shieldAmount > 0)
        {
            actorStats.AddShield(_pendingAbility.shieldAmount);
        }

        actor.hasActedThisRound = true;

        _resolving = false;

        // NEW: ensure pending cast state is cleared once we resolve (success path)
        if (AbilityCastState.Instance != null)
            AbilityCastState.Instance.ClearCast();

        CancelPendingAbility();
        NotifyPartyChanged();
    }

    private ResourceCost GetEffectiveCost(HeroStats actor, AbilityDefinitionSO ability)
    {
        if (ability == null) return default;
        ResourceCost c = ability.cost;

        if (ability.freeIfHidden && actor != null && actor.IsHidden)
            c.attack = 0;

        return c;
    }

    private int GetPartyIndexForHero(HeroStats hero)
    {
        if (hero == null || _party == null) return -1;
        for (int i = 0; i < _party.Count; i++)
            if (_party[i] != null && _party[i].stats == hero) return i;
        return -1;
    }

    private void CancelPendingAbility()
    {
        _pendingAction = PlayerActionType.None;
        _pendingAbility = null;
        _pendingActorIndex = -1;
        _awaitingEnemyTarget = false;
        _selectedEnemyTarget = null;

        // NEW: ensure cast state cannot remain stuck on cancel paths
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

            if (logFlow && m != null)
                Debug.Log($"[Battle] Spawned monster '{m.name}' at {pos}. Layer={m.gameObject.layer}");
        }

        if (logFlow)
            Debug.Log($"[Battle] Active monsters count: {_activeMonsters.Count}");
    }

    private void RemoveMonster(Monster m)
    {
        if (m == null) return;
        _activeMonsters.Remove(m);
        if (m.gameObject != null) Destroy(m.gameObject);
    }

    private void CleanupExistingEncounter()
    {
        for (int i = 0; i < _activeMonsters.Count; i++)
            if (_activeMonsters[i] != null) Destroy(_activeMonsters[i].gameObject);

        _activeMonsters.Clear();
        _plannedIntents.Clear();
    }

    // robust click picking for BOTH 2D and 3D colliders
    private Monster TryGetClickedMonster()
    {
        if (_mainCam == null) _mainCam = Camera.main;
        if (_mainCam == null) return null;

        Physics.queriesHitTriggers = true;

        // --------------- 2D ---------------
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
                if (m != null)
                {
                    if (logFlow) Debug.Log($"[Battle] 2D pick hit '{m.name}' via collider '{c.name}' (layer {c.gameObject.layer}).");
                    return m;
                }
            }
        }

        // --------------- 3D ---------------
        Ray ray = _mainCam.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits3D = Physics.RaycastAll(ray, 500f, ~0, QueryTriggerInteraction.Collide);

        if (hits3D != null && hits3D.Length > 0)
        {
            float best = float.MaxValue;
            Monster bestMonster = null;
            Collider bestCollider = null;

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
                    bestCollider = c;
                }
            }

            if (bestMonster != null)
            {
                if (logFlow && bestCollider != null)
                    Debug.Log($"[Battle] 3D pick hit '{bestMonster.name}' via collider '{bestCollider.name}' (layer {bestCollider.gameObject.layer}).");

                return bestMonster;
            }
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

    // Required by PartyHUD
    public HeroStats GetHeroAtPartyIndex(int index)
    {
        if (!IsValidPartyIndex(index)) return null;
        return _party[index].stats;
    }
}
