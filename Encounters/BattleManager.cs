// GUID: 30f201f35d336bf4d840162cd6fd1fde
////////////////////////////////////////////////////////////
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using System.Runtime.CompilerServices;

public class BattleManager : MonoBehaviour
{
    public static event Action PartyReady;
    public static BattleManager Instance { get; private set; }

    public enum BattleState { Idle, BattleStart, PlayerPhase, EnemyPhase, BattleEnd }
    public enum PlayerActionType { None, Ability1, Ability2 }
    public enum IntentType { Attack, AoEAttack }

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

        // Chosen attack payload (so intent preview + execution stay consistent, and Undo can restore them).
        public int attackIndex;
        public int damage;
        public bool isAoe;

        public bool stunsTarget;
        public int stunPlayerPhases;

        public bool appliesBleed;
        public int bleedStacks;
    }


    // ================= UNDO SAVE STATES =================

    [Serializable]
    private struct ResourcePoolSnapshot
    {
        public long attack;
        public long defense;
        public long magic;
        public long wild;
    }

    [Serializable]
    private struct HeroRuntimeSnapshot
    {
        public int partyIndex;
        public int hp;
        public float stamina;
        public int shield;
        public bool hidden;
        public int bleedStacks;
        public bool hasActedThisRound;
    }

    [Serializable]
    private struct MonsterRuntimeSnapshot
    {
        public int instanceId;
        public bool isActive;
        public int hp;
        public int bleedStacks;
        public Vector3 position;
        public Quaternion rotation;
    }

    [Serializable]
    private struct EnemyIntentSnapshot
    {
        public IntentType type;
        public int enemyInstanceId;
        public int targetPartyIndex;

        public int attackIndex;
        public int damage;
        public bool isAoe;

        public bool stunsTarget;
        public int stunPlayerPhases;

        public bool appliesBleed;
        public int bleedStacks;
    }

    [Serializable]
    private sealed class BattleSaveState
    {
        public List<HeroRuntimeSnapshot> heroes = new List<HeroRuntimeSnapshot>(3);
        public List<MonsterRuntimeSnapshot> monsters = new List<MonsterRuntimeSnapshot>(8);
        public List<EnemyIntentSnapshot> intents = new List<EnemyIntentSnapshot>(8);
        public ResourcePoolSnapshot resources;
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
        public bool IsHidden;

        public bool IsStunned;
        public bool IsTripleBladeEmpowered;
        public bool IsBleeding;

        // UI-only: preview for pending Block cast (shield not yet applied yet)
        public bool HasBlockPreview;
        public int BlockPreviewAmount;

        public float HP01 => MaxHP <= 0 ? 0f : Mathf.Clamp01((float)HP / MaxHP);
        public float Stamina01 => MaxStamina <= 0 ? 0f : Mathf.Clamp01((float)Stamina / MaxStamina);
    }

    [Header("Run / Resources")]
    [SerializeField] private ResourcePool resourcePool;

    [Header("Party (Run Instance)")]
    [SerializeField] private Transform[] partySpawnPoints;
    [SerializeField] private GameObject[] partyMemberPrefabs = new GameObject[3];
    private GameObject[] partyMemberInstances;
    [SerializeField] private Transform partyRoot;
    [SerializeField] private int partySize = 3;

    [Header("Encounter / Spawn")]
    [SerializeField] private GameObject[] monsterPrefabs;
    [SerializeField] private Transform[] monsterSpawnPoints;
    [SerializeField] private int minMonstersPerEncounter = 1;
    [SerializeField] private int maxMonstersPerEncounter = 3;

    [Header("Encounter / Enemy Party Compositions")]
    [Tooltip("If set, ALWAYS use this composition for battles (ignores Enemy Party Pool).")]
    [SerializeField] private EnemyPartyCompositionSO forcedEnemyParty;

    [Tooltip("If Forced Enemy Party is null and this list has entries, BattleManager will choose from here per battle.")]
    [SerializeField] private List<EnemyPartyCompositionSO> enemyPartyPool = new List<EnemyPartyCompositionSO>();

    [Tooltip("If true, pick a random composition from the pool each battle. If false, iterate sequentially (looping).")]
    [SerializeField] private bool randomizeEnemyPartyFromPool = true;

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

    [Header("Post-Battle Chest / Reward Reels")]
    [Tooltip("Panel that shows Small/Large chests and a Skip option.")] 
    [SerializeField] private PostBattleChestPanel postBattleChestPanel;

    [Tooltip("Optional: shown after post-battle rewards so the player can reorganize before the next fight.")] 
    [SerializeField] private PostBattlePrepPanel postBattlePrepPanel;

    [Header("External Systems")]
    [SerializeField] private StretchController stretchController;
    [SerializeField] private ScrollingBackground scrollingBackground;


    [Header("Reels / Spins")]
    [SerializeField] private ReelSpinSystem reelSpinSystem;
    [Header("Input / Targeting")]
    [SerializeField] private bool allowClickToSelectMonsterTarget = true;
    [SerializeField] private bool ignoreClicksOverUI = true;


    [Header("Undo / Confirm UI")]
    [SerializeField] private Button undoButton;
    [SerializeField] private TMP_Text confirmText;


    [Header("Monster Info UI")]
    [Tooltip("Optional. If assigned, BattleManager will populate the Monster Info panel when preview-targeting enemies.")]
    [SerializeField] private MonsterInfoController monsterInfoController;


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
    // Turn this OFF once Block is verified end-to-end.
    [SerializeField] private bool logFlow = true;

    public event Action<BattleState> OnBattleStateChanged;
    public event Action<int> OnActivePartyMemberChanged;
    public event Action OnPartyChanged;
    public event Action<List<EnemyIntent>> OnEnemyIntentsPlanned;
    public event Action OnPendingAbilityCleared;

    public BattleState CurrentState => _state;
    public bool IsPlayerPhase => _state == BattleState.PlayerPhase;
    public bool IsEnemyPhase => _state == BattleState.EnemyPhase;
    public bool IsResolving => _resolving;
    public int PartyCount => _party != null ? _party.Count : 0;
    public int ActivePartyIndex => _activePartyIndex;

    private BattleState _state = BattleState.Idle;

    private readonly List<Monster> _activeMonsters = new List<Monster>();

    // All monsters spawned for the current encounter, including those "killed" (deactivated) so Undo can revive them.
    private readonly List<Monster> _encounterMonsters = new List<Monster>(8);

    // Save states (turn start + each committed ability cast)
    private readonly List<BattleSaveState> _saveStates = new List<BattleSaveState>(16);

    // Party target preview: used for Block-style "click twice to confirm"
    private int _previewPartyTargetIndex = -1;
    // Confirmed party target for ally-targeted defensive abilities (e.g., Aegis)
    private int _selectedPartyTargetIndex = -1;
    private readonly List<EnemyIntent> _plannedIntents = new List<EnemyIntent>();

    // Enemy party selection (per encounter)
    private EnemyPartyCompositionSO _activeEnemyParty;
    private List<ItemOptionSO> _activeLootOverride;
    private int _enemyPartyPoolIndex;
    private EnemyPartyCompositionSO _nextEnemyPartyOverride;

    private List<PartyMemberRuntime> _party = new List<PartyMemberRuntime>(3);
    private int _activePartyIndex = 0;

    private PlayerActionType _pendingAction = PlayerActionType.None;
    private AbilityDefinitionSO _pendingAbility;
    private int _pendingActorIndex = -1;

    private bool _awaitingEnemyTarget = false;
    private bool _awaitingPartyTarget = false; // used for self/ally targeting like Block
    private Monster _selectedEnemyTarget;

    private Monster _previewEnemyTarget = null;

    private bool _resolving;
    // Used to sync damage/removal to the exact impact frame of the caster's animation.
    private bool _impactFired;
    private bool _attackFinished;
    private Camera _mainCam;

    private Coroutine _startBattleRoutine;
    private Coroutine _enemyTurnRoutine;

    private bool _startupRewardHandled;

    private bool _postBattleRunning;

    // IMPORTANT: finds inactive objects too (e.g., UI panels disabled by default)
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


        if (reelSpinSystem == null) reelSpinSystem = FindInSceneIncludingInactive<ReelSpinSystem>();

        // Undo / Confirm UI (optional)
        if (undoButton == null)
        {
            // Prefer an object named "UndoButton" (can be inactive)
            var allButtons = Resources.FindObjectsOfTypeAll<Button>();
            for (int i = 0; i < allButtons.Length; i++)
            {
                var b = allButtons[i];
                if (b == null) continue;
                if (b.gameObject != null && b.gameObject.scene.IsValid() && b.gameObject.name == "UndoButton")
                {
                    undoButton = b;
                    break;
                }
            }
        }

        if (confirmText == null)
        {
            var allText = Resources.FindObjectsOfTypeAll<TMP_Text>();
            for (int i = 0; i < allText.Length; i++)
            {
                var t = allText[i];
                if (t == null) continue;
                if (t.gameObject != null && t.gameObject.scene.IsValid() && t.gameObject.name == "ConfirmText")
                {
                    confirmText = t;
                    break;
                }
            }
        }

        if (undoButton != null)
        {
            undoButton.onClick.RemoveListener(UndoLastSaveState);
            undoButton.onClick.AddListener(UndoLastSaveState);
            undoButton.gameObject.SetActive(false); // disabled by default
        }

        if (confirmText != null)
            confirmText.gameObject.SetActive(false); // disabled by default

        StartNewRun();
    }

    private void Start()
    {
        StartBattle();
    }

    private void Update()
    {
        if (!IsPlayerPhase || _resolving)
            return;

        if (!Input.GetMouseButtonDown(0))
            return;

        // Block clicks through UI if requested.
        if (ignoreClicksOverUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        Monster clicked = TryGetClickedMonster();

        // Clicked empty space (or something non-monster)
        if (clicked == null)
        {
            // If we were casting (enemy target), clicking elsewhere cancels the pending cast.
            if (_awaitingEnemyTarget && _previewEnemyTarget != null)
            {
                if (logFlow) Debug.Log("[Battle][AbilityTarget] Clicked elsewhere -> cancel pending ability.", this);
                ClearEnemyTargetPreview();
                HideConfirmText();
                CancelPendingAbility();
            }
            else if (_awaitingEnemyTarget)
            {
                // If we were awaiting a target but had no preview yet, just clear any lingering preview.
                ClearEnemyTargetPreview();
            }

            // Do not auto-hide Monster Info on empty clicks.
            return;
        }

        // Only respond to active, living encounter monsters
        if (!_activeMonsters.Contains(clicked) || clicked.IsDead)
            return;

        // Always show monster info on click, even when no ability is pending.
        if (monsterInfoController != null)
            monsterInfoController.Show(clicked);

        // If we are currently targeting an enemy for a pending ability, route click into targeting logic.
        if (_awaitingEnemyTarget && allowClickToSelectMonsterTarget)
        {
            SelectEnemyTarget(clicked);
        }
    }


    // Called by AnimatorImpactEvents (Animation Events).
    public void NotifyAttackImpact()
    {
        if (logFlow) Debug.Log("[Battle][AnimEvent] AttackImpact received.");
        _impactFired = true;
    }

    // Optional: call via animation event at the end of the attack animation if desired.
    public void NotifyAttackFinished()
    {
        if (logFlow) Debug.Log("[Battle][AnimEvent] AttackFinished received.");
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
        // 🔗 Wire reels to party (strip + portrait) from spawned heroes
        if (reelSpinSystem != null)
        {
            var heroes = new List<HeroStats>();
            for (int i = 0; i < _party.Count; i++)
            {
                if (_party[i]?.stats != null)
                    heroes.Add(_party[i].stats);
            }

            reelSpinSystem.ConfigureFromParty(heroes);
        }


        _activePartyIndex = GetFirstAlivePartyIndex();
        OnActivePartyMemberChanged?.Invoke(_activePartyIndex);
        NotifyPartyChanged();
        PartyReady?.Invoke();
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
            IsBlocking = shield > 0,
            
           
            IsHidden = hs != null && hs.IsHidden,
            IsStunned = hs != null && hs.IsStunned,
            IsTripleBladeEmpowered = hs != null && hs.IsTripleBladeEmpoweredThisTurn,
            IsBleeding = hs != null && hs.IsBleeding,
HasBlockPreview = (shield <= 0) && (_previewPartyTargetIndex == index) && _awaitingPartyTarget && _pendingAbility != null && _pendingActorIndex == index && _pendingAbility.targetType == AbilityTargetType.Self && _pendingAbility.shieldAmount > 0,
            BlockPreviewAmount = ((_previewPartyTargetIndex == index) && _awaitingPartyTarget && _pendingAbility != null && _pendingActorIndex == index) ? Mathf.Max(0, _pendingAbility.shieldAmount) : 0
        };
    }


    /// <summary>
    /// Sum of raw incoming damage from currently planned enemy intents that target this party index.
    /// Used for HP damage preview segments in PartyHUDSlot.
    /// </summary>
    public int GetIncomingDamagePreviewForPartyIndex(int index)
    {
        if (!IsValidPartyIndex(index)) return 0;

        int total = 0;
        for (int i = 0; i < _plannedIntents.Count; i++)
        {
            var intent = _plannedIntents[i];
            if (intent.enemy == null || intent.enemy.IsDead) continue;
            if (intent.targetPartyIndex != index) continue;

            total += Mathf.Max(0, intent.enemy.GetDamage());
        }
        return total;
    }

    public void SetActivePartyMember(int index)
    {
        if (!IsPlayerPhase) return;
        if (!IsValidPartyIndex(index)) return;

        _activePartyIndex = index;
        OnActivePartyMemberChanged?.Invoke(_activePartyIndex);
        NotifyPartyChanged();
    }

    /// <summary>
    /// When an ability is pending and requires a party target (e.g. Block), this handles clicks on a PartyHUD slot.
    /// Returns true if the click was consumed by targeting logic (so PartyHUD should NOT open menus).
    /// </summary>
    public bool TryHandlePartySlotClickForPendingAbility(int partyIndex)
    {
        if (logFlow)
            Debug.Log($"[Battle][AbilityTarget] Party slot clicked. partyIndex={partyIndex} pendingActorIndex={_pendingActorIndex} awaitingPartyTarget={_awaitingPartyTarget} pendingAbility={(_pendingAbility != null ? _pendingAbility.abilityName : "<null>")}");

        if (!IsPlayerPhase) return false;
        if (_resolving) return true; // consume to prevent UI spam while resolving

        if (_pendingAbility == null) return false;
        if (!_awaitingPartyTarget) return false;

        bool selfOnly = _pendingAbility.targetType == AbilityTargetType.Self;

        // Self-targeted shield (Block): only the caster can be selected.
        if (selfOnly && partyIndex != _pendingActorIndex)
        {
            // If we've already selected a target (preview), clicking anything else cancels.
            if (_previewPartyTargetIndex == _pendingActorIndex)
            {
                if (logFlow) Debug.Log("[Battle][AbilityTarget] Clicked different party slot -> cancel pending ability.", this);
                _previewPartyTargetIndex = -1;
                _selectedPartyTargetIndex = -1;
                HideConfirmText();
                CancelPendingAbility();
                NotifyPartyChanged();
            }
            // Consume the click so PartyHUD doesn't open other menus while casting.
            return true;
        }

        // Two-step confirm:
        // 1) First click on target -> show shield preview + confirm text
        // 2) Second click on SAME target -> commit ability
        if (_previewPartyTargetIndex != partyIndex)
        {
            // If we were already previewing a different party target, clicking a different one cancels (matches enemy targeting behavior).
            if (_previewPartyTargetIndex != -1 && _previewPartyTargetIndex != partyIndex)
            {
                if (logFlow) Debug.Log("[Battle][AbilityTarget] Clicked different party target -> cancel pending ability.", this);
                _previewPartyTargetIndex = -1;
                _selectedPartyTargetIndex = -1;
                HideConfirmText();
                CancelPendingAbility();
                NotifyPartyChanged();
                return true;
            }

            _previewPartyTargetIndex = partyIndex;
            ShowConfirmText();
            NotifyPartyChanged();
            return true;
        }

        if (logFlow) Debug.Log("[Battle][AbilityTarget] Party target clicked again. Committing pending ability.", this);
        _selectedPartyTargetIndex = partyIndex;
        _previewPartyTargetIndex = -1;
        HideConfirmText();
        StartCoroutine(ResolvePendingAbility());
        NotifyPartyChanged();
        return true;
    }

    public void BeginAbilityUseFromMenu(HeroStats hero, AbilityDefinitionSO ability)
    {
        if (logFlow)
            Debug.Log($"[Battle][Ability] BeginAbilityUseFromMenu. hero={(hero != null ? hero.name : "<null>")} ability={(ability != null ? ability.abilityName : "<null>")}");
        if (!IsPlayerPhase || _resolving) return;
        if (hero == null || ability == null) return;

        int actorIndex = GetPartyIndexForHero(hero);
        if (!IsValidPartyIndex(actorIndex)) return;

        PartyMemberRuntime actor = _party[actorIndex];
        if (actor.IsDead) return;

        if (_pendingAction != PlayerActionType.None) return;

        ResourceCost cost = GetEffectiveCost(actor.stats, ability);

        // Per-turn attack limit gate (e.g., Triple Blade / All-In effects)
        if (ability != null && ability.baseDamage > 0 && actor.stats != null && !actor.stats.CanCommitDamageAttackThisTurn())
        {
            if (logFlow) Debug.Log($"[Battle][Ability] Blocked: {actor.name} has reached their attack limit for this turn.", this);
            return;
        }

        if (logFlow)
            Debug.Log($"[Battle][Ability] Pending set. actorIndex={actorIndex} ability={ability.abilityName} targetType={ability.targetType} shieldAmount={ability.shieldAmount} baseDamage={ability.baseDamage} cost={cost}");

        _pendingActorIndex = actorIndex;
        _pendingAbility = ability;
        _selectedEnemyTarget = null;
        _previewPartyTargetIndex = -1;
        _selectedPartyTargetIndex = -1;
        HideConfirmText();
        ClearEnemyTargetPreview();

        if (AbilityCastState.Instance != null)
            AbilityCastState.Instance.BeginCast(hero, ability);

        // Reset animation-sync flags for this cast.
        _impactFired = false;
        _attackFinished = false;

        _pendingAction = PlayerActionType.Ability1;
        if (ability.targetType == AbilityTargetType.Enemy)
        {
            _awaitingEnemyTarget = true;
            ClearEnemyTargetPreview();
            _selectedEnemyTarget = null;
            _previewEnemyTarget = null;
            if (logFlow) Debug.Log($"[Battle][AbilityTarget] Awaiting ENEMY target for {ability.abilityName}");
        }
        else if ((ability.targetType == AbilityTargetType.Self || ability.targetType == AbilityTargetType.Ally) && ability.shieldAmount > 0)
        {
            // Shield-style cast (Block/Aegis): preview on a party member, then require a second click to commit.
            _awaitingEnemyTarget = false;
            _awaitingPartyTarget = true;
            ClearEnemyTargetPreview();
            _selectedEnemyTarget = null;
            _previewEnemyTarget = null;
            if (logFlow)
            {
                string mode = (ability.targetType == AbilityTargetType.Self) ? "SELF" : "ALLY";
                Debug.Log($"[Battle][AbilityTarget] Awaiting {mode} confirm for {ability.abilityName} (shield preview should flash)");
            }
        }
        else
        {
            _awaitingEnemyTarget = false;
            if (logFlow) Debug.Log($"[Battle][Ability] No target required. Resolving immediately for {ability.abilityName}");
            StartCoroutine(ResolvePendingAbility());
        }

        NotifyPartyChanged();
    }

    public void SelectEnemyTarget(Monster target)
    {

        if (logFlow)
            Debug.Log($"[Battle][AbilityTarget] Enemy clicked. target={(target != null ? target.name : "<null>")} awaitingEnemyTarget={_awaitingEnemyTarget}");

        if (!IsPlayerPhase || _resolving) return;
        if (!_awaitingEnemyTarget) return;
        if (target == null) return;
        if (target.IsDead) return;

        // Two-step targeting:
        // 1) First click -> set PREVIEW target (shows damage preview)
        // 2) Second click on the SAME target -> CONFIRM and resolve ability
        if (_previewEnemyTarget != target)
        {
            _previewEnemyTarget = target;
            SetEnemyTargetPreview(target);
            ShowConfirmText();

            if (logFlow)
                Debug.Log($"[Battle][AbilityTarget] Preview target set to {target.name}. Click again to confirm.");
            return;
        }

        // Confirm on second click
        _selectedEnemyTarget = target;
        _awaitingEnemyTarget = false;

        // Clear any yellow damage preview segment now that the ability is being cast.
        ClearEnemyTargetPreview();

        HideConfirmText();

        if (logFlow)
            Debug.Log($"[Battle][AbilityTarget] Target confirmed: {target.name}. Resolving ability.");

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
    /// Queue a specific enemy party to be used for the NEXT battle only.
    /// If null is passed, clears the queued override.
    /// </summary>
    public void QueueNextEnemyParty(EnemyPartyCompositionSO party)
    {
        _nextEnemyPartyOverride = party;
    }

    /// <summary>
    /// Ends the player's turn immediately and begins the enemy phase.
    /// Intended to be called by the End Turn button.
    /// </summary>
    /// 
    public void EndTurn()
    {
        // Only allow during player phase and when not already mid-resolution/enemy turn.
        if (!IsPlayerPhase) return;
        if (_resolving) return;
        if (_enemyTurnRoutine != null) return;

        // Clear resources at end of player turn
        if (resourcePool != null)
            resourcePool.ClearAll();
        
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

        // NOTE: Bleeding now ticks only at the start of the Player Phase (for both heroes and monsters).
        // Do NOT tick monster bleeding here.

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
                int raw = intent.damage;
                if (raw <= 0 && intent.enemy != null) raw = intent.enemy.GetDamage();

                // Conceal / Hidden logic:
                // - Single-target attacks miss hidden heroes entirely.
                // - AoE attacks hit all heroes (including hidden) and break conceal.
                if (intent.type == IntentType.AoEAttack)
                {
                    for (int pi = 0; pi < PartyCount; pi++)
                    {
                        var pm = _party[pi];
                        var hs = pm != null ? pm.stats : null;
                        if (hs == null || pm.IsDead) continue;

                        if (logFlow) Debug.Log($"[Battle][EnemyAtk][AoE] Applying incoming damage. attacker={(intent.enemy != null ? intent.enemy.name : "<null>")} targetIdx={pi} raw={raw} targetShieldBefore={hs.Shield}", this);
                        int dealt = hs.ApplyIncomingDamage(raw);


                        // Optional: stun targets hit by this enemy's default attack.
                        if (intent.stunsTarget)
                            hs.StunForNextPlayerPhases(intent.stunPlayerPhases);

                        if (intent.appliesBleed && intent.bleedStacks > 0)
                            ApplyBleedStacksToHero(hs, intent.bleedStacks);
                        // AoE breaks Conceal/Hidden.
                        if (hs.IsHidden) hs.SetHidden(false);

                        if (pm.avatarGO != null)
                            SpawnDamageNumber(pm.avatarGO.transform.position, dealt);
                    }

                    ApplyPartyHiddenVisuals();
                    return;
                }

                // Single-target attack
                if (targetStats == null) return;

                if (targetStats.IsHidden)
                {
                    if (logFlow) Debug.Log($"[Battle][EnemyAtk] Target is hidden (Conceal). Attack misses. attacker={(intent.enemy != null ? intent.enemy.name : "<null>")} targetIdx={targetIdx}", this);
                    return;
                }

                // Apply damage (HeroStats handles shield+HP).
                if (logFlow) Debug.Log($"[Battle][EnemyAtk] Applying incoming damage. attacker={(intent.enemy != null ? intent.enemy.name : "<null>")} targetIdx={targetIdx} raw={raw} targetShieldBefore={targetStats.Shield}", this);
                int dealtSingle = targetStats.ApplyIncomingDamage(raw);

                // Optional: stun the target hit by this enemy's default attack.
                if (intent.stunsTarget)
                    targetStats.StunForNextPlayerPhases(intent.stunPlayerPhases);

                if (intent.appliesBleed && intent.bleedStacks > 0)
                    ApplyBleedStacksToHero(targetStats, intent.bleedStacks);
                if (logFlow) Debug.Log($"[Battle][EnemyAtk] Damage result. dealtToHp={dealtSingle} targetShieldAfter={targetStats.Shield}", this);

                if (targetGO != null)
                    SpawnDamageNumber(targetGO.transform.position, dealtSingle);
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

        BeginPlayerTurnSaveState();

        // New player turn: reset reel spins.
        if (reelSpinSystem != null)
            reelSpinSystem.BeginTurn();

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
        // Translation-based attack (no animation clips required):
        // 1) Lunge toward target
        // 2) Apply damage at peak
        // 3) Return to start

        if (enemy == null)
            yield break;

        Transform visual = GetEnemyVisualTransform(enemy);

        // If we can't find a visual transform, just apply damage immediately.
        if (visual == null)
        {
            applyDamage?.Invoke();
            yield break;
        }

        Vector3 startPos = visual.position;

        // If no target, just do a small "nudge" forward (or none), still apply damage.
        Vector3 dir = Vector3.right;
        if (target != null)
        {
            Vector3 toTarget = (target.position - startPos);
            if (toTarget.sqrMagnitude > 0.0001f)
                dir = toTarget.normalized;
        }

        Vector3 peakPos = startPos + dir * enemyLungeDistance;

        // Forward
        float t = 0f;
        float forward = Mathf.Max(0.0001f, enemyLungeForwardSeconds);
        while (t < forward)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / forward);
            visual.position = Vector3.Lerp(startPos, peakPos, a);
            yield return null;
        }

        // Peak (impact)
        visual.position = peakPos;
        applyDamage?.Invoke();

        // Hold
        float hold = Mathf.Max(0f, enemyLungeHoldSeconds);
        if (hold > 0f)
            yield return new WaitForSeconds(hold);

        // Back
        t = 0f;
        float back = Mathf.Max(0.0001f, enemyLungeBackSeconds);
        while (t < back)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / back);
            visual.position = Vector3.Lerp(peakPos, startPos, a);
            yield return null;
        }

        visual.position = startPos;
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

        BeginPlayerTurnSaveState();

        // New player turn: reset reel spins.
        if (reelSpinSystem != null)
            reelSpinSystem.BeginTurn();

        _activePartyIndex = GetFirstAlivePartyIndex();
        OnActivePartyMemberChanged?.Invoke(_activePartyIndex);

        NotifyPartyChanged();
    }

    private IEnumerator ResolvePendingAbility()
    {
        if (logFlow)
            Debug.Log($"[Battle][Resolve] ResolvePendingAbility ENTER. pendingAbility={(_pendingAbility != null ? _pendingAbility.abilityName : "<null>")} pendingActorIndex={_pendingActorIndex} selectedEnemyTarget={(_selectedEnemyTarget != null ? _selectedEnemyTarget.name : "<null>")} awaitingEnemyTarget={_awaitingEnemyTarget} awaitingPartyTarget={_awaitingPartyTarget}", this);

        if (_pendingAbility == null || !IsValidPartyIndex(_pendingActorIndex))
        {
            if (logFlow) Debug.Log("[Battle][Resolve] Cancel: pending ability or actor invalid.", this);
            CancelPendingAbility();
            yield break;
        }

        // Capture references up-front so end-of-battle cleanup (or other flows)
        // can't null out _pendingAbility mid-coroutine.
        AbilityDefinitionSO ability = _pendingAbility;
        if (logFlow)
            Debug.Log($"[Battle][Resolve] Confirmed/casting ability: name={ability.name} abilityName={ability.abilityName} targetType={ability.targetType} shieldAmount={ability.shieldAmount} baseDamage={ability.baseDamage}", this);

        PartyMemberRuntime actor = _party[_pendingActorIndex];
        HeroStats actorStats = actor.stats;
        if (actorStats == null || actor.IsDead)
        {
            if (logFlow) Debug.Log("[Battle][Resolve] Cancel: actorStats missing or actor dead.", this);
            CancelPendingAbility();
            yield break;
        }

        Monster enemyTarget = _selectedEnemyTarget;
        if (ability.targetType == AbilityTargetType.Enemy)
        {
            if (enemyTarget == null || enemyTarget.IsDead)
            {
                if (logFlow) Debug.Log("[Battle][Resolve] Abort: Enemy target required but not selected (or dead). Returning to awaiting target.", this);
                _awaitingEnemyTarget = true;
                yield break;
            }
        }

        // Ally-targeted shield abilities (e.g., Aegis) require a valid party target.
        if (ability.targetType == AbilityTargetType.Ally && ability.shieldAmount > 0)
        {
            if (!IsValidPartyIndex(_selectedPartyTargetIndex) || _party[_selectedPartyTargetIndex] == null || _party[_selectedPartyTargetIndex].IsDead)
            {
                if (logFlow) Debug.Log("[Battle][Resolve] Abort: Ally target required but not selected (or dead). Returning to awaiting party target.", this);
                _awaitingPartyTarget = true;
                yield break;
            }
        }

        // Snapshot BEFORE we spend resources / apply effects so Undo restores the pre-cast state.
        PushSaveStateSnapshot();

        ResourceCost cost = GetEffectiveCost(actorStats, ability);
        //int actorPoolId = actorStats.ResourcePool.GetInstanceID();
        int id = RuntimeHelpers.GetHashCode(resourcePool);
        if (resourcePool == null || !resourcePool.TrySpend(cost))
        {
            if (logFlow) Debug.Log($"[Battle][Resolve] Cancel: insufficient resources or missing resourcePool. cost={cost}", this);
            CancelPendingAbility();
            yield break;
        }

        if (logFlow) Debug.Log($"[Battle][Resolve] Resources spent. cost={cost}. Proceeding to apply ability effects.", this);
        // From this point, the cast is committed (resources spent). Block other actions.
        _resolving = true;

        // Trigger caster attack animation for the pending ability.
        // Some abilities may use impact-sync (Animation Event) and others may not.

        Animator anim = actor.animator;
        if (anim == null && actor.avatarGO != null)
            anim = actor.avatarGO.GetComponentInChildren<Animator>(true);

        // Reset flags for this cast (only matters if we choose to wait for impact).
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

                case "Conceal":
                    // Conceal has no damage and no animation for now.
                    useImpactSync = false;
                    stateToPlay = null;
                    if (logFlow) Debug.Log("[Battle][Resolve] Conceal: no animation and no impact sync.", this);
                    break;

                case "Block":
                    // Block has no damage and no animation for now.
                    useImpactSync = false;
                    stateToPlay = null;
                    if (logFlow) Debug.Log("[Battle][Resolve] Block: no animation and no impact sync.", this);
                    break;

                case "Aegis":
                    // Aegis behaves like Block: no damage and no animation for now.
                    useImpactSync = false;
                    stateToPlay = null;
                    if (logFlow) Debug.Log("[Battle][Resolve] Aegis: no animation and no impact sync.", this);
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

            if (!string.IsNullOrWhiteSpace(stateToPlay))
            {
                if (logFlow) Debug.Log($"[Battle][Resolve] Playing animation state '{stateToPlay}'. useImpactSync={useImpactSync}", this);
                anim.Play(stateToPlay, 0, 0f);
            }
            else
            {
                if (logFlow) Debug.Log($"[Battle][Resolve] No animation played for ability '{ability.abilityName}'.", this);
            }
        }
        else
        {
            if (logFlow) Debug.Log("[Battle][Resolve] No animator found on actor; skipping animation.", this);
        }


        if (ability.targetType == AbilityTargetType.Enemy && enemyTarget != null)
        {
            // If this ability uses impact-sync and the caster has an animator playing the attack,
            // wait for the impact frame event before applying damage.
            if (useImpactSync && anim != null)
            {
                if (logFlow) Debug.Log("[Battle][Resolve] Waiting for AttackImpact animation event...", this);

                // Give the animator one frame to enter the state.
                yield return null;

                float elapsed = 0f;
                const float failSafeSeconds = 3.0f; // prevents a soft-lock if the animation event is missing.
                while (!_impactFired && elapsed < failSafeSeconds)
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                if (logFlow) Debug.Log($"[Battle][Resolve] Done waiting for impact. impactFired={_impactFired} elapsed={elapsed:0.000}s", this);
            }

            int dealt = enemyTarget.TakeDamageFromAbility(
                abilityBaseDamage: ability.baseDamage,
                classAttackModifier: actorStats.ClassAttackModifier,
                element: ability.element,
                abilityTags: ability.tags);

            SpawnDamageNumber(enemyTarget.transform.position, dealt);
            actorStats.ApplyOnHitEffectsTo(enemyTarget);


            // Mark that this hero has committed a damaging attack this turn (for per-turn limits).
            if (ability.baseDamage > 0)
                actorStats.RegisterDamageAttackCommitted();

            if (enemyTarget.IsDead)
            {
                actorStats.GainXP(5);
                RemoveMonster(enemyTarget);
                // Enemy intents are locked once planned at the start of the turn.
                // Do not re-plan here, or surviving enemies' targets will change mid-turn.
            }
        }


        if (ability.shieldAmount > 0 && (ability.targetType == AbilityTargetType.Self || ability.targetType == AbilityTargetType.Ally))
        {
            // Defensive shield abilities: Block (Self) and Aegis (Ally)
            HeroStats targetStats = actorStats;
            string targetName = actorStats.name;

            if (ability.targetType == AbilityTargetType.Ally)
            {
                if (IsValidPartyIndex(_selectedPartyTargetIndex) && _party[_selectedPartyTargetIndex] != null)
                {
                    targetStats = _party[_selectedPartyTargetIndex].stats;
                    targetName = _party[_selectedPartyTargetIndex].name;
                }
            }

            if (targetStats != null)
            {
                if (logFlow) Debug.Log($"[Battle][Shield] Applying shield. amount={ability.shieldAmount} target={targetName} shieldBefore={targetStats.Shield}", this);
                targetStats.AddShield(ability.shieldAmount);
                if (logFlow) Debug.Log($"[Battle][Shield] Shield applied. target={targetName} shieldAfter={targetStats.Shield}", this);
            }
        }

        
        // ===== Conceal (Ninja) =====
        // Conceal makes the caster untargetable by single-target enemy attacks until:
        // - they are hit by an AoE attack, OR
        // - they use an ability (except Backstab that kills the target).
        bool wasHiddenBeforeCast = actorStats.IsHidden;

        if (ability.name == "Conceal")
        {
            actorStats.SetHidden(true);
        }
        else if (wasHiddenBeforeCast)
        {
            bool keepHidden = false;

            // Special case: Backstab does NOT break Conceal if it kills the enemy.
            if (ability.name == "Backstab" && ability.targetType == AbilityTargetType.Enemy && enemyTarget != null && enemyTarget.IsDead)
                keepHidden = true;

            if (!keepHidden)
                actorStats.SetHidden(false);
        }

        ApplyPartyHiddenVisuals();

        actor.hasActedThisRound = true;

        _resolving = false;

        if (AbilityCastState.Instance != null)
            AbilityCastState.Instance.ClearCast();

        CancelPendingAbility();
        NotifyPartyChanged();

        // Ability successfully cast -> Undo becomes available for this turn.
        if (_saveStates != null && _saveStates.Count > 1)
            SetUndoButtonEnabled(true);

    }

    private ResourceCost GetEffectiveCost(HeroStats actor, AbilityDefinitionSO ability)
    {
        if (ability == null) return default;
        return ability.cost;
    }

    // ================= TARGET PREVIEW =================

    private void SetEnemyTargetPreview(Monster target)
    {
        // Clear any previous preview
        if (_previewEnemyTarget != null && _previewEnemyTarget != target)
        {
            var oldBar = _previewEnemyTarget.GetComponentInChildren<MonsterHpBar>(true);
            if (oldBar != null) oldBar.ClearPreview();
        }

        _previewEnemyTarget = target;

        if (monsterInfoController != null) monsterInfoController.Show(target);

        if (target == null || _pendingAbility == null) return;
        if (!IsValidPartyIndex(_pendingActorIndex)) return;

        var actor = _party[_pendingActorIndex];
        if (actor == null || actor.stats == null || actor.IsDead) return;

        int predictedDamage = target.CalculateDamageFromAbility(
            abilityBaseDamage: _pendingAbility.baseDamage,
            classAttackModifier: actor.stats.ClassAttackModifier,
            element: _pendingAbility.element,
            abilityTags: _pendingAbility.tags);

        int previewHp = Mathf.Max(0, target.CurrentHp - predictedDamage);

        var bar = target.GetComponentInChildren<MonsterHpBar>(true);
        if (bar != null)
            bar.SetDamagePreview(previewHp);
    }

    private void ClearEnemyTargetPreview()
    {
        if (_previewEnemyTarget != null)
        {
            var bar = _previewEnemyTarget.GetComponentInChildren<MonsterHpBar>(true);
            if (bar != null) bar.ClearPreview();
        }
        _previewEnemyTarget = null;
    }



    private void CancelPendingAbility()
    {
        if (logFlow)
            Debug.Log($"[Battle][Cancel] CancelPendingAbility. pendingAbility={(_pendingAbility != null ? _pendingAbility.abilityName : "<null>")} pendingActorIndex={_pendingActorIndex} awaitingEnemyTarget={_awaitingEnemyTarget} awaitingPartyTarget={_awaitingPartyTarget}", this);
        _pendingAction = PlayerActionType.None;
        _pendingAbility = null;
        _pendingActorIndex = -1;
        _awaitingEnemyTarget = false;
        _awaitingPartyTarget = false;
        _selectedEnemyTarget = null;
        _previewPartyTargetIndex = -1;
        _selectedPartyTargetIndex = -1;
        HideConfirmText();
        ClearEnemyTargetPreview();
        _impactFired = false;
        _attackFinished = false;

        if (AbilityCastState.Instance != null)
            AbilityCastState.Instance.ClearCast();

        OnPendingAbilityCleared?.Invoke();
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

            // Choose an attack from the monster's authored attack list.
            ChooseMonsterAttackForIntent(m,
                out int attackIndex,
                out int damage,
                out bool isAoe,
                out bool stunsTarget,
                out int stunPlayerPhases,
                out bool appliesBleed,
                out int bleedStacks);

            _plannedIntents.Add(new EnemyIntent
            {
                type = isAoe ? IntentType.AoEAttack : IntentType.Attack,
                enemy = m,
                targetPartyIndex = targetIdx,

                attackIndex = attackIndex,
                damage = damage,
                isAoe = isAoe,

                stunsTarget = stunsTarget,
                stunPlayerPhases = stunPlayerPhases,

                appliesBleed = appliesBleed,
                bleedStacks = bleedStacks
            });
        }

        OnEnemyIntentsPlanned?.Invoke(new List<EnemyIntent>(_plannedIntents));
        NotifyPartyChanged();
    }

    private void ChooseMonsterAttackForIntent(Monster m,
        out int attackIndex,
        out int damage,
        out bool isAoe,
        out bool stunsTarget,
        out int stunPlayerPhases,
        out bool appliesBleed,
        out int bleedStacks)
    {
        attackIndex = -1;
        damage = 0;
        isAoe = false;
        stunsTarget = false;
        stunPlayerPhases = 1;
        appliesBleed = false;
        bleedStacks = 0;

        if (m == null) return;

        // Reflection keeps this resilient to small data model changes.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        object attacksObj = null;
        var t = m.GetType();

        var fiAttacks = t.GetField("attacks", flags);
        if (fiAttacks != null)
            attacksObj = fiAttacks.GetValue(m);

        // Support arrays (MonsterAttack[]) primarily.
        System.Array attacksArray = attacksObj as System.Array;
        int count = attacksArray != null ? attacksArray.Length : 0;

        if (count <= 0)
        {
            // Fallback to default attack.
            try { damage = m.GetDamage(); } catch { damage = 0; }

            try
            {
                var pi = t.GetProperty("IsDefaultAttackAoE", flags);
                if (pi != null) isAoe = (bool)pi.GetValue(m, null);
            }
            catch { isAoe = false; }

            try
            {
                stunsTarget = m.DefaultAttackStunsTarget;
                stunPlayerPhases = m.DefaultAttackStunPlayerPhases;
            }
            catch { stunsTarget = false; stunPlayerPhases = 1; }

            return;
        }

        attackIndex = UnityEngine.Random.Range(0, count);
        var atk = attacksArray.GetValue(attackIndex);
        if (atk == null) return;

        var atkType = atk.GetType();
        damage = ReadInt(atk, atkType, "damage", 0);
        isAoe = ReadBool(atk, atkType, "isAoe", false);

        stunsTarget = ReadBool(atk, atkType, "stunsTarget", false);
        stunPlayerPhases = Mathf.Max(1, ReadInt(atk, atkType, "stunPlayerPhases", 1));

        appliesBleed = ReadBool(atk, atkType, "appliesBleed", false);
        if (!appliesBleed) appliesBleed = ReadBool(atk, atkType, "bleedsTarget", false);

        bleedStacks = Mathf.Max(0, ReadInt(atk, atkType, "bleedStacks", 0));
        if (bleedStacks == 0) bleedStacks = Mathf.Max(0, ReadInt(atk, atkType, "bleedAmount", 0));
    }

    private static int ReadInt(object obj, Type t, string name, int fallback)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var fi = t.GetField(name, flags);
        if (fi != null && fi.FieldType == typeof(int)) return (int)fi.GetValue(obj);
        var pi = t.GetProperty(name, flags);
        if (pi != null && pi.PropertyType == typeof(int)) return (int)pi.GetValue(obj, null);
        return fallback;
    }

    private static bool ReadBool(object obj, Type t, string name, bool fallback)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var fi = t.GetField(name, flags);
        if (fi != null && fi.FieldType == typeof(bool)) return (bool)fi.GetValue(obj);
        var pi = t.GetProperty(name, flags);
        if (pi != null && pi.PropertyType == typeof(bool)) return (bool)pi.GetValue(obj, null);
        return fallback;
    }

    private static void ApplyBleedStacksToHero(HeroStats hs, int stacksToAdd)
    {
        if (hs == null || stacksToAdd <= 0) return;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var t = hs.GetType();

        // Prefer an explicit method if your HeroStats provides one.
        var miAdd = t.GetMethod("AddBleedStacks", flags, null, new[] { typeof(int) }, null);
        if (miAdd != null)
        {
            miAdd.Invoke(hs, new object[] { stacksToAdd });
            return;
        }

        var miSet = t.GetMethod("SetBleedStacks", flags, null, new[] { typeof(int) }, null);
        if (miSet != null)
        {
            int current = 0;
            try
            {
                // Property/field common names
                var pi = t.GetProperty("BleedStacks", flags);
                if (pi != null && pi.PropertyType == typeof(int)) current = (int)pi.GetValue(hs, null);
                else
                {
                    var fi = t.GetField("BleedStacks", flags) ?? t.GetField("bleedStacks", flags);
                    if (fi != null && fi.FieldType == typeof(int)) current = (int)fi.GetValue(hs);
                }
            }
            catch { current = 0; }

            miSet.Invoke(hs, new object[] { current + stacksToAdd });
            return;
        }

        // Last resort: write directly to a field/property.
        try
        {
            var pi = t.GetProperty("BleedStacks", flags);
            if (pi != null && pi.CanWrite && pi.PropertyType == typeof(int))
            {
                int cur = (int)(pi.GetValue(hs, null) ?? 0);
                pi.SetValue(hs, cur + stacksToAdd, null);
                return;
            }
        }
        catch { }

        try
        {
            var fi = t.GetField("BleedStacks", flags) ?? t.GetField("bleedStacks", flags);
            if (fi != null && fi.FieldType == typeof(int))
            {
                int cur = (int)(fi.GetValue(hs) ?? 0);
                fi.SetValue(hs, cur + stacksToAdd);
            }
        }
        catch { }
    }

    private void ResetPartyRoundFlags()
    {
        // Bleeding now ticks only at the start of the Player Phase (for both heroes and monsters).
        // Tick monster bleeding first so icons/numbers reflect the post-tick state when the player regains control.
        if (_activeMonsters != null && _activeMonsters.Count > 0)
        {
            for (int mi = 0; mi < _activeMonsters.Count; mi++)
            {
                Monster m = _activeMonsters[mi];
                if (m == null || m.IsDead) continue;

                int bleedDamage = m.TickBleedingAtTurnStart();
                if (bleedDamage > 0)
                {
                    SpawnDamageNumber(m.transform.position, bleedDamage);

                    // If the bleed tick killed the monster, play death effects now.
                    if (m.IsDead)
                        m.PlayDeathEffects();
                }
            }
        }

        for (int i = 0; i < PartyCount; i++)
        {
            HeroStats hs = _party[i].stats;
            if (hs != null)
            {
                // Bleeding ticks once per turn (start of Player Phase).
                int bleedDamage = hs.TickBleedingAtTurnStart();
                if (bleedDamage > 0 && _party[i].avatarGO != null)
                    SpawnDamageNumber(_party[i].avatarGO.transform.position, bleedDamage);

                // Stun/phase statuses consume at the start of the player phase.
                hs.StartPlayerPhaseStatuses();
            }

            // If stunned at the start of player phase, treat as already acted so the UI/input blocks actions.
            _party[i].hasActedThisRound = (hs != null && hs.IsStunned);
        }

        CancelPendingAbility();
        NotifyPartyChanged();

        // Ability successfully cast -> Undo becomes available for this turn.
        if (_saveStates != null && _saveStates.Count > 1)
            SetUndoButtonEnabled(true);

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
        _encounterMonsters.Clear();

        int maxSlots = (monsterSpawnPoints != null && monsterSpawnPoints.Length > 0) ? monsterSpawnPoints.Length : 1;

        // --- Choose active enemy party (optional) ---
        EnemyPartyCompositionSO chosen = null;

        if (_nextEnemyPartyOverride != null)
        {
            chosen = _nextEnemyPartyOverride;
            _nextEnemyPartyOverride = null;
        }
        else if (forcedEnemyParty != null)
        {
            chosen = forcedEnemyParty;
        }
        else if (enemyPartyPool != null && enemyPartyPool.Count > 0)
        {
            if (randomizeEnemyPartyFromPool)
            {
                chosen = enemyPartyPool[UnityEngine.Random.Range(0, enemyPartyPool.Count)];
            }
            else
            {
                if (_enemyPartyPoolIndex < 0) _enemyPartyPoolIndex = 0;
                if (_enemyPartyPoolIndex >= enemyPartyPool.Count) _enemyPartyPoolIndex = 0;

                chosen = enemyPartyPool[_enemyPartyPoolIndex];
                _enemyPartyPoolIndex = (_enemyPartyPoolIndex + 1) % enemyPartyPool.Count;
            }
        }

        _activeEnemyParty = chosen;

        // Cache loot override for this encounter (optional)
        if (_activeEnemyParty != null && _activeEnemyParty.lootTable != null && _activeEnemyParty.lootTable.Count > 0)
            _activeLootOverride = _activeEnemyParty.lootTable;
        else
            _activeLootOverride = null;

        // If we have a chosen party with enemies, spawn exactly those.
        if (_activeEnemyParty != null && _activeEnemyParty.enemies != null && _activeEnemyParty.enemies.Count > 0)
        {
            int spawnCount = Mathf.Clamp(_activeEnemyParty.enemies.Count, 1, maxSlots);

            for (int i = 0; i < spawnCount; i++)
            {
                GameObject prefab = _activeEnemyParty.enemies[i];
                if (prefab == null) continue;

                Transform spawn = (monsterSpawnPoints != null && i < monsterSpawnPoints.Length) ? monsterSpawnPoints[i] : null;
                Vector3 pos = spawn != null ? spawn.position : Vector3.zero;

                GameObject go = Instantiate(prefab, pos, Quaternion.identity);
                Monster m = go.GetComponentInChildren<Monster>(true);
                if (m != null)
                {
                    _activeMonsters.Add(m);
                    if (!_encounterMonsters.Contains(m)) _encounterMonsters.Add(m);
                }
            }

            if (_activeEnemyParty.enemies.Count > maxSlots)
                Debug.LogWarning($"[BattleManager] Enemy party '{_activeEnemyParty.name}' has {_activeEnemyParty.enemies.Count} enemies but only {maxSlots} spawn points. Extra enemies will be ignored.", this);

            return;
        }

        // --- Fallback: your original random spawn behavior ---
        if (monsterPrefabs == null || monsterPrefabs.Length == 0)
            return;

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
            if (m != null)
                {
                    _activeMonsters.Add(m);
                    if (!_encounterMonsters.Contains(m)) _encounterMonsters.Add(m);
                }
        }
    }



    /// <summary>
    /// Enemy intents should not change mid-turn once created.
    /// If an enemy dies, we remove only that enemy's intent(s) (and any null/dead references)
    /// without re-planning or changing the other intents.
    /// </summary>
    private void RemoveEnemyIntentsForMonster(Monster dead)
    {
        if (dead == null) return;
        if (_plannedIntents == null || _plannedIntents.Count == 0) return;

        bool removedAny = false;
        for (int i = _plannedIntents.Count - 1; i >= 0; i--)
        {
            var intent = _plannedIntents[i];
            if (intent.enemy == null || intent.enemy == dead || intent.enemy.IsDead)
            {
                _plannedIntents.RemoveAt(i);
                removedAny = true;
            }
        }

        if (removedAny)
        {
            OnEnemyIntentsPlanned?.Invoke(new List<EnemyIntent>(_plannedIntents));
            NotifyPartyChanged();
        }
    }

    private void RemoveMonster(Monster m)
    {
        if (m == null) return;
        
        // If this monster is currently being inspected, hide the Monster Info panel.
        if (monsterInfoController != null)
            monsterInfoController.HideIfShowing(m);


        // Intents are locked for the turn. If this enemy had a planned intent, remove only its intent(s)
        // without changing the remaining enemies' intents.
        RemoveEnemyIntentsForMonster(m);

        _activeMonsters.Remove(m);

        // Do NOT destroy monsters: we keep them for Undo revives.
        if (m.gameObject != null)
            m.gameObject.SetActive(false);

        // If all enemies are dead, trigger the post-battle reward loop.
        if (_activeMonsters.Count == 0)
        {
            //  Clear resources between battles (immediate)
            if (resourcePool != null)
                resourcePool.ClearAll();
            StartCoroutine(HandleEncounterVictoryRoutine());
        }
    }


    private IEnumerator HandleEncounterVictoryRoutine()
    {
        Debug.Log($"[Battle] Victory detected. Starting post-battle flow. time={Time.time:0.00}", this);

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

        // Award victory gold (from the active enemy party composition, if any).
        HeroStats goldOwner = null;
        if (_party != null && _party.Count > 0)
            goldOwner = _party[0]?.stats;

        if (goldOwner != null && _activeEnemyParty != null && _activeEnemyParty.goldReward > 0)
            goldOwner.AddGold(_activeEnemyParty.goldReward);

        // Use encounter-specific loot if present; else fall back to global pool.
        List<ItemOptionSO> pool =
            (_activeLootOverride != null && _activeLootOverride.Count > 0)
                ? _activeLootOverride
                : (postBattleFlow != null ? postBattleFlow.GetItemOptionPool() : null);

        // ✅ NEW POST-BATTLE FLOW:
        // - Swap reels into Reward Mode (pay gold to spin; 3-in-a-row key payouts)
        // - Offer Small/Large chests (spend keys to open)
        // - Then show the Prep panel (Inventory / Continue)
        if (enablePostBattleRewards && postBattleChestPanel != null && pool != null && pool.Count > 0)
        {
            // 1) Reward reels (optional per enemy party)
            if (reelSpinSystem != null && _activeEnemyParty != null && _activeEnemyParty.rewardReelConfig != null)
                reelSpinSystem.EnterRewardMode(_activeEnemyParty.rewardReelConfig, goldOwner);

            bool done = false;

            int smallCount = _activeEnemyParty != null ? Mathf.Max(0, _activeEnemyParty.smallChestCount) : 0;
            int largeCount = _activeEnemyParty != null ? Mathf.Max(0, _activeEnemyParty.largeChestCount) : 0;

            postBattleChestPanel.Show(
                goldOwner,
                smallCount,
                largeCount,
                pool,
                inventory,
                // Reuse the existing item reward panel for the "choose item" step
                (postBattleRewardPanel != null ? postBattleRewardPanel : startRewardPanel),
                () => done = true
            );

            yield return new WaitUntil(() => done);

            postBattleChestPanel.Hide();

            // Restore combat reels
            if (reelSpinSystem != null)
            {
                // Rebuild from current party so portraits/strips return
                var partyStats = new List<HeroStats>(_party != null ? _party.Count : 0);
                if (_party != null)
                    for (int i = 0; i < _party.Count; i++)
                        if (_party[i]?.stats != null) partyStats.Add(_party[i].stats);

                reelSpinSystem.ExitRewardMode(partyStats);
            }
        }
        else
        {
            // Fallback: original single "choose one" reward behavior
            if (enablePostBattleRewards)
            {
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
        }

        // Prep / inventory reorg panel (optional)
        if (postBattlePrepPanel != null)
        {
            bool cont = false;

            // Ensure the panel object itself is active (Show() only toggles its internal root).
            if (!postBattlePrepPanel.gameObject.activeSelf)
                postBattlePrepPanel.gameObject.SetActive(true);

            int battlesCompleted = stretchController != null ? stretchController.BattlesCompleted : 0;
            int battlesPerStretch = stretchController != null ? stretchController.BattlesPerStretch : 1;

            Debug.Log($"[Battle] Showing PostBattlePrepPanel. battlesCompleted={battlesCompleted} battlesPerStretch={battlesPerStretch} time={Time.time:0.00}", this);

            postBattlePrepPanel.Show(battlesCompleted, battlesPerStretch, () =>
            {
                cont = true;
            });

            yield return new WaitUntil(() => cont);

            // Hide it once continue is pressed so it won't overlap the next encounter UI.
            postBattlePrepPanel.Hide();
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
        _encounterMonsters.Clear();
        _plannedIntents.Clear();

        _activeEnemyParty = null;
        _activeLootOverride = null;

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

        if (s == BattleState.BattleEnd)
            Debug.Log($"[Battle] Battle ended. state={s} time={Time.time:0.00}", this);

        OnBattleStateChanged?.Invoke(_state);
    }

    private void NotifyPartyChanged()
    {
        ApplyPartyHiddenVisuals();
        ApplyMonsterStatusVisuals();
        OnPartyChanged?.Invoke();
    }

    [Header("Conceal / Hidden Visuals")]
    [SerializeField] private Color hiddenTint = new Color(0.65f, 0.65f, 0.65f, 1f);


    [Header("Status Icons (optional)")]
    [SerializeField] private Sprite statusIconHiddenSprite;
    [SerializeField] private Sprite statusIconStunnedSprite;
    [SerializeField] private Sprite statusIconTripleBladeEmpoweredSprite;
    [SerializeField] private Sprite statusIconBleedingSprite;

    private void ApplyPartyHiddenVisuals()
    {
        if (_party == null) return;

        for (int i = 0; i < _party.Count; i++)
        {
            var pm = _party[i];
            if (pm == null || pm.avatarGO == null) continue;

            var hs = pm.stats;
            bool hidden = hs != null && hs.IsHidden;

            // Tint the in-world sprite (prefab) gray when hidden.
            var sr = pm.avatarGO.GetComponentInChildren<SpriteRenderer>(true);
            if (sr != null)
                sr.color = hidden ? hiddenTint : Color.white;

            // Status icon above unit (Hidden / Stunned / Triple Blade Empowered).
            StatusEffectIconController statusIcon = null;

            Transform iconTf = null;

            // Prefer: under HeroStats root (common structure)
            if (hs != null)
            {
                iconTf = hs.transform.Find("_StatusIcon");
                if (iconTf == null)
                    iconTf = hs.transform.Find("__StatusIcon");
            }

            // Fallback: search anywhere under avatarGO
            if (iconTf == null)
            {
                var all = pm.avatarGO.GetComponentsInChildren<Transform>(true);
                for (int t = 0; t < all.Length; t++)
                {
                    if (all[t] != null && all[t].name == "_StatusIcon")
                    {
                        iconTf = all[t];
                        break;
                    }
                }
            }

            if (iconTf != null)
            {
                statusIcon = iconTf.GetComponent<StatusEffectIconController>();
                if (statusIcon == null)
                    statusIcon = iconTf.gameObject.AddComponent<StatusEffectIconController>();
            }

            if (statusIcon != null)
            {
                statusIcon.ConfigureSprites(
                    statusIconHiddenSprite,
                    statusIconStunnedSprite,
                    statusIconTripleBladeEmpoweredSprite,
                    statusIconBleedingSprite
                );

                statusIcon.SetStates(
                    hidden: hidden,
                    stunned: (hs != null && hs.IsStunned),
                    tripleBladeEmpowered: (hs != null && hs.IsTripleBladeEmpoweredThisTurn),
                    bleeding: (hs != null && hs.IsBleeding)
                );

                // Bleed stacks overlay (only shown when Bleeding icon is active).
                statusIcon.SetBleedStacks(hs != null ? hs.BleedStacks : 0);
            }
        }
    }

    private void ApplyMonsterStatusVisuals()
    {
        if (_activeMonsters == null) return;

        for (int i = 0; i < _activeMonsters.Count; i++)
        {
            var m = _activeMonsters[i];
            if (m == null) continue;

            // Find or create a child named "_StatusIcon" under the monster.
            Transform iconTf = m.transform.Find("_StatusIcon");
            if (iconTf == null)
            {
                var go = new GameObject("_StatusIcon");
                go.transform.SetParent(m.transform, false);
                iconTf = go.transform;
                // Default offset above the monster.
                iconTf.localPosition = new Vector3(0f, 1.2f, 0f);
                iconTf.localScale = Vector3.one;
            }

            var ctrl = iconTf.GetComponent<MonsterStatusEffectIconController>();
            if (ctrl == null)
                ctrl = iconTf.gameObject.AddComponent<MonsterStatusEffectIconController>();

            ctrl.Configure(statusIconBleedingSprite);

            int stacks = 0;
            try { stacks = m.BleedStacks; } catch { stacks = 0; }
            ctrl.SetBleedStacks(stacks);
        }
    }

    public void RefreshStatusVisuals()
    {
        ApplyPartyHiddenVisuals();
        ApplyMonsterStatusVisuals();
    }

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


    // ================= UNDO / SAVE STATE HELPERS =================

    private void BeginPlayerTurnSaveState()
    {
        _saveStates.Clear();
        _previewEnemyTarget = null;
        _previewPartyTargetIndex = -1;
        HideConfirmText();
        SetUndoButtonEnabled(false);

        PushSaveStateSnapshot(); // Turn start baseline
    }

    private void PushSaveStateSnapshot()
    {
        var s = new BattleSaveState();

        // Heroes
        for (int i = 0; i < PartyCount; i++)
        {
            var pm = _party[i];
            var hs = pm != null ? pm.stats : null;
            if (hs == null) continue;

            s.heroes.Add(new HeroRuntimeSnapshot
            {
                partyIndex = i,
                hp = hs.CurrentHp,
                stamina = hs.CurrentStamina,
                shield = hs.Shield,
                hidden = hs.IsHidden,
                bleedStacks = hs.BleedStacks,
                hasActedThisRound = pm.hasActedThisRound
            });
        }

        // Resources
        if (resourcePool != null)
        {
            s.resources = new ResourcePoolSnapshot
            {
                attack = resourcePool.Attack,
                defense = resourcePool.Defense,
                magic = resourcePool.Magic,
                wild = resourcePool.Wild
            };
        }

        // Monsters (include inactive so Undo can revive)
        for (int i = 0; i < _encounterMonsters.Count; i++)
        {
            var m = _encounterMonsters[i];
            if (m == null) continue;

            s.monsters.Add(new MonsterRuntimeSnapshot
            {
                instanceId = m.GetInstanceID(),
                isActive = m.gameObject.activeSelf && !m.IsDead,
                hp = m.CurrentHp,
                bleedStacks = m.BleedStacks,
                position = m.transform.position,
                rotation = m.transform.rotation
            });
        }
        // Enemy intents (locked for the turn)
        s.intents.Clear();
        for (int i = 0; i < _plannedIntents.Count; i++)
        {
            var it = _plannedIntents[i];
            if (it.enemy == null) continue;

            s.intents.Add(new EnemyIntentSnapshot
            {
                type = it.type,
                enemyInstanceId = it.enemy.GetInstanceID(),
                targetPartyIndex = it.targetPartyIndex,
                attackIndex = it.attackIndex,
                damage = it.damage,
                isAoe = it.isAoe,
                stunsTarget = it.stunsTarget,
                stunPlayerPhases = it.stunPlayerPhases,
                appliesBleed = it.appliesBleed,
                bleedStacks = it.bleedStacks
            });
        }



        _saveStates.Add(s);
    }

    private void ApplySaveStateSnapshot(BattleSaveState s)
    {
        if (s == null) return;

        // Clear pending cast and previews
        ClearEnemyTargetPreview();
        _previewPartyTargetIndex = -1;
        HideConfirmText();
        CancelPendingAbility();

        // Restore resources
        if (resourcePool != null)
            resourcePool.SetAmounts(s.resources.attack, s.resources.defense, s.resources.magic, s.resources.wild);

        // Restore heroes
        for (int i = 0; i < s.heroes.Count; i++)
        {
            var h = s.heroes[i];
            if (!IsValidPartyIndex(h.partyIndex)) continue;

            var pm = _party[h.partyIndex];
            if (pm == null || pm.stats == null) continue;

            pm.stats.SetRuntimeState(h.hp, h.stamina, h.shield, h.hidden);
            pm.stats.SetBleedStacks(h.bleedStacks);
            pm.hasActedThisRound = h.hasActedThisRound;
        }

        // Restore monsters
        // Build a lookup by instance id
        var map = new Dictionary<int, Monster>(_encounterMonsters.Count);
        for (int i = 0; i < _encounterMonsters.Count; i++)
        {
            var m = _encounterMonsters[i];
            if (m == null) continue;
            map[m.GetInstanceID()] = m;
        }

        _activeMonsters.Clear();
        _encounterMonsters.Clear();

        for (int i = 0; i < s.monsters.Count; i++)
        {
            var ms = s.monsters[i];
            if (!map.TryGetValue(ms.instanceId, out var m) || m == null) continue;

            // Restore transform (helps visuals feel consistent)
            m.transform.position = ms.position;
            m.transform.rotation = ms.rotation;

            if (ms.isActive)
            {
                m.gameObject.SetActive(true);
                m.SetCurrentHp(ms.hp);
                m.SetBleedStacks(ms.bleedStacks);
                if (!m.IsDead)
                    _activeMonsters.Add(m);
            }
            else
            {
                // Keep dead/inactive monsters hidden
                m.SetCurrentHp(ms.hp);
                m.SetBleedStacks(ms.bleedStacks);
                if (m.IsDead || !ms.isActive)
                    m.gameObject.SetActive(false);
            }
        }

        // Restore enemy intents as they were when this snapshot was taken.
        _plannedIntents.Clear();
        if (s.intents != null)
        {
            for (int i = 0; i < s.intents.Count; i++)
            {
                var it = s.intents[i];
                if (!map.TryGetValue(it.enemyInstanceId, out var em) || em == null) continue;
                if (!em.gameObject.activeSelf || em.IsDead) continue;

                _plannedIntents.Add(new EnemyIntent
                {
                    type = it.type,
                    enemy = em,
                    targetPartyIndex = it.targetPartyIndex,
                    attackIndex = it.attackIndex,
                    damage = it.damage,
                    isAoe = it.isAoe,
                    stunsTarget = it.stunsTarget,
                    stunPlayerPhases = it.stunPlayerPhases,
                    appliesBleed = it.appliesBleed,
                    bleedStacks = it.bleedStacks
                });
            }
        }
        OnEnemyIntentsPlanned?.Invoke(new List<EnemyIntent>(_plannedIntents));

        NotifyPartyChanged();
    }

    private void SetUndoButtonEnabled(bool enabled)
    {
        if (undoButton == null) return;
        undoButton.gameObject.SetActive(enabled);
        undoButton.interactable = enabled;
    }

    private void HideConfirmText()
    {
        if (confirmText != null)
            confirmText.gameObject.SetActive(false);
    }

    private void ShowConfirmText()
    {
        if (confirmText != null)
        {
            confirmText.text = "Click target again to confirm";
            confirmText.gameObject.SetActive(true);
        }
    }

    public void UndoLastSaveState()
    {
        if (!IsPlayerPhase || _resolving)
            return;

        if (_saveStates == null || _saveStates.Count <= 1)
        {
            SetUndoButtonEnabled(false);
            return;
        }

        // Remove the most recent snapshot and restore the new last snapshot.
        _saveStates.RemoveAt(_saveStates.Count - 1);

        BattleSaveState s = _saveStates[_saveStates.Count - 1];
        ApplySaveStateSnapshot(s);

        // Disable undo if we're back to turn start baseline.
        if (_saveStates.Count <= 1)
            SetUndoButtonEnabled(false);
    }
    public GameObject GetPartyMemberInstance(int index)
    {
        if (partyMemberInstances == null) return null;
        if (index < 0 || index >= partyMemberInstances.Length) return null;
        return partyMemberInstances[index];
    }
}


////////////////////////////////////////////////////////////

