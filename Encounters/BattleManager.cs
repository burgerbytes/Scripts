//PATH: Assets/Scripts/Encounters/BattleManager.cs
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
        public bool hasActedThisRound;
    }

    [Serializable]
    private struct MonsterRuntimeSnapshot
    {
        public int instanceId;
        public bool isActive;
        public int hp;
        public Vector3 position;
        public Quaternion rotation;
    }

    [Serializable]
    private struct EnemyIntentSnapshot
    {
        public IntentType type;
        public int enemyInstanceId;
        public int targetPartyIndex;
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
    [Tooltip("Optional. If assigned, BattleManager will populate the Monster Info panel when clicking enemies.")]
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

    // Targeting preview: first click previews damage, second click confirms.
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
        // Allow clicking monsters to open the MonsterInfoPanel even when no ability is pending.
        // If an ability is awaiting an enemy target, we still run the normal two-click confirm targeting flow.
        if (!IsPlayerPhase || _resolving || !allowClickToSelectMonsterTarget)
            return;

        if (!Input.GetMouseButtonDown(0))
            return;

        // When NOT actively targeting an enemy for an ability, respect ignoreClicksOverUI so UI clicks don't select monsters.
        // When targeting, we intentionally ignore this because some setups have full-screen raycast UI that would block all clicks.
        if (ignoreClicksOverUI && !_awaitingEnemyTarget && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        Monster clicked = TryGetClickedMonster();

        // Clicked empty space
        if (clicked == null)
        {
            // If we were in the middle of an enemy-target cast and already previewing, clicking elsewhere cancels.
            if (_awaitingEnemyTarget && _previewEnemyTarget != null)
            {
                if (logFlow) Debug.Log("[Battle][AbilityTarget] Clicked elsewhere -> cancel pending ability.", this);
                ClearEnemyTargetPreview();
                HideConfirmText();
                CancelPendingAbility();
            }
            return;
        }

        if (!_activeMonsters.Contains(clicked) || clicked.IsDead)
            return;

        // Always show/update monster info on click
        if (monsterInfoController != null)
            monsterInfoController.Show(clicked);

        // If we're not targeting an ability, we're done.
        if (!_awaitingEnemyTarget)
            return;

        // If we already selected a preview target, clicking ANY other target cancels the cast.
        if (_previewEnemyTarget != null && clicked != _previewEnemyTarget)
        {
            if (logFlow) Debug.Log("[Battle][AbilityTarget] Clicked different target -> cancel pending ability.", this);
            ClearEnemyTargetPreview();
            HideConfirmText();
            CancelPendingAbility();
            return;
        }

        SelectEnemyTarget(clicked);
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
            IsHidden = hs != null && hs.IsHidden,
            IsBlocking = shield > 0,

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

        // Block: only the caster (pending actor) can be selected.
        if (partyIndex != _pendingActorIndex)
        {
            // If we've already selected a target (preview), clicking anything else cancels.
            if (_previewPartyTargetIndex == _pendingActorIndex)
            {
                if (logFlow) Debug.Log("[Battle][AbilityTarget] Clicked different party slot -> cancel pending ability.", this);
                _previewPartyTargetIndex = -1;
                HideConfirmText();
                CancelPendingAbility();
                NotifyPartyChanged();
            }
            // Consume the click so PartyHUD doesn't open other menus while casting.
            return true;
        }

        // Two-step confirm:
        // 1) First click on caster -> show block preview + confirm text
        // 2) Second click on caster -> commit ability
        if (_previewPartyTargetIndex != partyIndex)
        {
            _previewPartyTargetIndex = partyIndex;
            ShowConfirmText();
            NotifyPartyChanged();
            return true;
        }

        if (logFlow) Debug.Log("[Battle][AbilityTarget] Caster clicked again. Committing pending ability.", this);
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
        if (logFlow)
            Debug.Log($"[Battle][Ability] Pending set. actorIndex={actorIndex} ability={ability.abilityName} targetType={ability.targetType} shieldAmount={ability.shieldAmount} baseDamage={ability.baseDamage} cost={cost}");

        _pendingActorIndex = actorIndex;
        _pendingAbility = ability;
        _selectedEnemyTarget = null;
        _previewPartyTargetIndex = -1;
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
        else if (ability.targetType == AbilityTargetType.Self && ability.shieldAmount > 0)
        {
            // Block-style self cast: preview on the caster, then require a click on the caster to commit.
            _awaitingEnemyTarget = false;
            _awaitingPartyTarget = true;
            ClearEnemyTargetPreview();
            _selectedEnemyTarget = null;
            _previewEnemyTarget = null;
            if (logFlow) Debug.Log($"[Battle][AbilityTarget] Awaiting SELF confirm for {ability.abilityName} (Block preview should flash)");
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

                int raw = intent.enemy.GetDamage();

                // Apply damage (HeroStats handles shield+HP).
                if (logFlow) Debug.Log($"[Battle][EnemyAtk] Applying incoming damage. attacker={intent.enemy.name} targetIdx={targetIdx} raw={raw} targetShieldBefore={targetStats.Shield}", this);
                int dealt = targetStats.ApplyIncomingDamage(raw);
                if (logFlow) Debug.Log($"[Battle][EnemyAtk] Damage result. dealtToHp={dealt} targetShieldAfter={targetStats.Shield}", this);
                if (logFlow) Debug.Log($"[Battle][EnemyAtk] Damage applied. dealtToHP={dealt} targetShieldAfter={targetStats.Shield}", this);

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

                case "Block":
                    // Block has no damage and no animation for now.
                    useImpactSync = false;
                    stateToPlay = null;
                    if (logFlow) Debug.Log("[Battle][Resolve] Block: no animation and no impact sync.", this);
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
                element: ability.element);

            SpawnDamageNumber(enemyTarget.transform.position, dealt);
            actorStats.ApplyOnHitEffectsTo(enemyTarget);

            if (enemyTarget.IsDead)
            {
                actorStats.GainXP(5);
                RemoveMonster(enemyTarget);
                // Enemy intents are locked once planned at the start of the turn.
                // Do not re-plan here, or surviving enemies' targets will change mid-turn.
            }
        }


        if (ability.targetType == AbilityTargetType.Self && ability.shieldAmount > 0)
        {
            if (logFlow) Debug.Log($"[Battle][Block] Applying shield. amount={ability.shieldAmount} actor={actorStats.name} shieldBefore={actorStats.Shield}", this);
            actorStats.AddShield(ability.shieldAmount);
            if (logFlow) Debug.Log($"[Battle][Block] Shield applied. shieldAfter={actorStats.Shield}", this);
        }

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

        if (target == null || _pendingAbility == null) return;
        if (!IsValidPartyIndex(_pendingActorIndex)) return;

        var actor = _party[_pendingActorIndex];
        if (actor == null || actor.stats == null || actor.IsDead) return;

        int predictedDamage = target.CalculateDamageFromAbility(
            abilityBaseDamage: _pendingAbility.baseDamage,
            classAttackModifier: actor.stats.ClassAttackModifier,
            element: _pendingAbility.element);

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
            // Use encounter-specific loot if present; else fall back to global pool.
            List<ItemOptionSO> pool =
                (_activeLootOverride != null && _activeLootOverride.Count > 0)
                    ? _activeLootOverride
                    : (postBattleFlow != null ? postBattleFlow.GetItemOptionPool() : null);

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
                targetPartyIndex = it.targetPartyIndex
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
                if (!m.IsDead)
                    _activeMonsters.Add(m);
            }
            else
            {
                // Keep dead/inactive monsters hidden
                m.SetCurrentHp(ms.hp);
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
                    targetPartyIndex = it.targetPartyIndex
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

}




////////////////////////////////////////////////////////////
// 