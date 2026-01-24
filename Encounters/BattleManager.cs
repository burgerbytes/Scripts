using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using System.Runtime.CompilerServices;

// Project specific namespaces
using SlotsAndSorcery.VFX;

public class BattleManager : MonoBehaviour
{
    public  int PlayerTurnNumber;
    public static event Action PartyReady;
    public static BattleManager Instance { get; private set; }

    private bool _runStarted;
    private bool _startHasRun;

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
        public IntentCategory category;
        public Monster enemy;
        public int targetPartyIndex;

        public int attackIndex;
        public int damage;
        public bool isAoe;

        public bool stunsTarget;
        public int stunPlayerPhases;

        public bool appliesBleed;
        public int bleedStacks;
    }

    private static IntentCategory ComputeIntentCategory(int damage, bool isAoe, bool stunsTarget, bool appliesBleed)
    {
        bool hasStatus = stunsTarget || appliesBleed;

        if (isAoe)
        {
            if (damage > 0) return IntentCategory.StatusAndAoe;
            return IntentCategory.Aoe;
        }

        if (damage > 0)
        {
            return hasStatus ? IntentCategory.DamageAndStatus : IntentCategory.Normal;
        }

        return hasStatus ? IntentCategory.StatusDebuffOnly : IntentCategory.Normal;
    }

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

    [Header("Heal VFX Spawner")]
    [SerializeField] private HealVFXSpawner healVfxSpawner;

    [Tooltip("World offset applied when spawning the heal VFX.")]
    [SerializeField] private Vector3 healVfxWorldOffset = new Vector3(0f, 1.2f, 0f);

    [Tooltip("Fallback destroy time if the prefab has no ParticleSystems or duration can't be computed.")]
    [SerializeField] private float healVfxFallbackDestroySeconds = 2.0f;

    [Tooltip("If Damage Number Prefab is not assigned, BattleManager will spawn a simple TextMeshPro damage number in world-space.")]
    [SerializeField] private bool enableRuntimeDamageNumbers = true;

    [Header("Target Indicators")]
    [Tooltip("Optional. If assigned, BattleManager will spawn one indicator per monster at runtime (no prefab edits needed).")]
    [SerializeField] private TargetIndicatorUI enemyTargetIndicatorPrefab;

    [Tooltip("Anchored offset applied to the spawned indicator relative to its parent (typically the monster HP bar UI).")]
    [SerializeField] private Vector2 enemyTargetIndicatorOffset = new Vector2(-40f, 0f);

    [Tooltip("Uniform scale applied to the spawned indicator.")]
    [SerializeField] private float enemyTargetIndicatorScale = 1f;

    [SerializeField] private float runtimeDamageNumberLifetime = 0.75f;
    [SerializeField] private float runtimeDamageNumberRiseDistance = 0.8f;
    [SerializeField] private float runtimeDamageNumberFontSize = 3.5f;

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

    [Header("Post-Battle Results")]
    [Tooltip("Optional: shown immediately after victory to summarize gold / XP gained.")]
    [SerializeField] private PostBattleResultsPanel postBattleResultsPanel;

    [Header("Post-Battle Reel Upgrade Minigame")]
    [Tooltip("Optional: shown after Results (and before reward reels) to let the player spin to upgrade a reel symbol for each level up.")]
    [SerializeField] private PostBattleReelUpgradeMinigamePanel postBattleReelUpgradeMinigamePanel;

    [Header("Post-Battle Ability Upgrade")]
    [Tooltip("Optional: shown after Reel Upgrade Minigame to let the player choose one of two abilities to permanently unlock for each level gained (starting at level 2).")]
    [SerializeField] private PostBattleAbilityUpgradePanel postBattleAbilityUpgradePanel;

    [Header("Post-Battle Rewards Table")]
    [Tooltip("Optional: shown after Results/Ability choice. Lets the player choose ONE reward type (Reelforging or Treasure Reels).")]
    [SerializeField] private RewardsTablePanel rewardsTablePanel;

    [Tooltip("Optional: tracks in-battle performance for bonus XP awards.")]
    [SerializeField] private BattlePerformanceTracker performanceTracker;

    [Tooltip("Optional: shown after post-battle rewards so the player can reorganize before the next fight.")]
    [SerializeField] private PostBattlePrepPanel postBattlePrepPanel;

    [Header("External Systems")]
    [SerializeField] private StretchController stretchController;
    [SerializeField] private ScrollingBackground scrollingBackground;

    [Header("Reels / Spins")]
    [SerializeField] private ReelSpinSystem reelSpinSystem;
    [Tooltip("Log passive bridge events (symbol landed notifications).")]
    [SerializeField] private bool logPassiveBridge = true;
    [SerializeField] private Button stopSpinningButton;
    [SerializeField] private bool _spinResolvedAndLocked;

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

    [Header("VFX")]
    [SerializeField] private ScreenDimmer screenDimmer;

    [Header("Passive Effects")]
    [NonSerialized] public int BonusDamageNextDamagingAbility = 0;

    [Header("Debug")]
    [SerializeField] private bool logFlow = true;
    [Tooltip("Logs where the enemy HP bar should decrease after damage is applied.")]
    [SerializeField] private bool debugEnemyHpBarDrop = true;

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

    // Exposed for UI (PartyHUD target indicators, etc.)
    public bool IsAwaitingEnemyTarget => _awaitingEnemyTarget;
    public Monster PreviewEnemyTarget => _previewEnemyTarget;
    public bool IsAwaitingPartyTarget => _awaitingPartyTarget;
    public int PreviewPartyTargetIndex => _previewPartyTargetIndex;

    private BattleState _state = BattleState.Idle;

    private readonly List<Monster> _activeMonsters = new List<Monster>();

    // Runtime-spawned / cached target indicators (one per monster).
    private readonly Dictionary<Monster, TargetIndicatorUI> _enemyTargetIndicators = new Dictionary<Monster, TargetIndicatorUI>(16);
    private readonly HashSet<Monster> _spawnedEnemyTargetIndicators = new HashSet<Monster>();

    private readonly List<Monster> _encounterMonsters = new List<Monster>(8);

    private readonly List<BattleSaveState> _saveStates = new List<BattleSaveState>(16);

    private int _previewPartyTargetIndex = -1;
    private int _selectedPartyTargetIndex = -1;
    private readonly List<EnemyIntent> _plannedIntents = new List<EnemyIntent>();

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
    private bool _impactFired;
    private bool _attackFinished;
    private Camera _mainCam;

    private Coroutine _startBattleRoutine;
    private Coroutine _enemyTurnRoutine;

    private bool _startupRewardHandled;

    private bool _postBattleRunning;

    [Header("Target Indicator")]
    public TargetIndicatorUI indicatorPrefab;
    public Vector2 indicatorOffset;
    public float indicatorScale;

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

    public int PartySize => partySize;

    public void SetPartyMemberPrefabs(GameObject[] chosen)
    {
        if (chosen == null) chosen = Array.Empty<GameObject>();
        var normalized = new GameObject[3];
        for (int i = 0; i < 3; i++)
            normalized[i] = i < chosen.Length ? chosen[i] : null;

        partyMemberPrefabs = normalized;

        if (_startHasRun && !_runStarted && ArePartyPrefabsReady())
            BeginRunAndBattle();
    }

    private void Awake()
    {

        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[BattleManager] Duplicate instance detected. Existing={Instance.name} ({Instance.GetInstanceID()}), New={name} ({GetInstanceID()}). Using the new instance.", this);
        }
        Instance = this;
        _mainCam = Camera.main;

        if (resourcePool == null) resourcePool = FindInSceneIncludingInactive<ResourcePool>();
        if (stretchController == null) stretchController = FindInSceneIncludingInactive<StretchController>();
        if (postBattleFlow == null) postBattleFlow = FindInSceneIncludingInactive<PostBattleFlowController>();
        if (inventory == null) inventory = FindInSceneIncludingInactive<PlayerInventory>();
        if (startRewardPanel == null) startRewardPanel = FindInSceneIncludingInactive<PostBattleRewardPanel>();
        if (postBattleRewardPanel == null) postBattleRewardPanel = startRewardPanel;
        if (postBattleResultsPanel == null) postBattleResultsPanel = FindInSceneIncludingInactive<PostBattleResultsPanel>();
        if (postBattleReelUpgradeMinigamePanel == null) postBattleReelUpgradeMinigamePanel = FindInSceneIncludingInactive<PostBattleReelUpgradeMinigamePanel>();
        if (performanceTracker == null) performanceTracker = FindInSceneIncludingInactive<BattlePerformanceTracker>();

        if (performanceTracker == null)
        {
            performanceTracker = GetComponent<BattlePerformanceTracker>();
            if (performanceTracker == null)
                performanceTracker = gameObject.AddComponent<BattlePerformanceTracker>();
        }

        if (reelSpinSystem == null) reelSpinSystem = FindInSceneIncludingInactive<ReelSpinSystem>();
        if (reelSpinSystem != null)
            reelSpinSystem.OnCurrentLandedChanged += HandleCurrentLandedChanged;
        if (reelSpinSystem != null)
            reelSpinSystem.OnSpinLanded += HandleSpinLandedBattle;

        if (undoButton == null)
        {
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
        if (stopSpinningButton != null) stopSpinningButton.onClick.AddListener(OnStopSpinningPressed);
    }

    private void Start()
    {
        _startHasRun = true;
        if (!_runStarted)
        {
            if (!ArePartyPrefabsReady())
            {
                Debug.LogWarning("[BattleManager] Party prefabs not set yet. Waiting for class selection UI to provide partyMemberPrefabs.");
                return;
            }

            BeginRunAndBattle();
        }
    }

    private void BeginRunAndBattle()
    {
        if (_runStarted) return;

        _runStarted = true;
        StartNewRun();
        StartBattle();
    }

    private bool ArePartyPrefabsReady()
    {
        int count = Mathf.Clamp(partySize, 1, 3);
        if (partyMemberPrefabs == null || partyMemberPrefabs.Length < count)
            return false;
        for (int i = 0; i < count; i++)
        {
            if (partyMemberPrefabs[i] == null)
                return false;
        }
        return true;
    }

    private void Update()
    {
        if (!IsPlayerPhase || _resolving)
            return;

        if (!Input.GetMouseButtonDown(0))
            return;

        if (ignoreClicksOverUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        Monster clicked = TryGetClickedMonster();

        if (clicked == null)
        {
            if (_awaitingEnemyTarget && _previewEnemyTarget != null)
            {
                if (logFlow) Debug.Log("[Battle][AbilityTarget] Clicked elsewhere -> cancel pending ability.", this);
                ClearEnemyTargetPreview();
                HideConfirmText();
                CancelPendingAbility();
            }
            else if (_awaitingEnemyTarget)
            {
                ClearEnemyTargetPreview();
            }

            return;
        }

        if (!_activeMonsters.Contains(clicked) || clicked.IsDead)
            return;

        if (monsterInfoController != null)
            monsterInfoController.Show(clicked);

        if (_awaitingEnemyTarget && allowClickToSelectMonsterTarget)
        {
            SelectEnemyTarget(clicked);
        }
    }

    public void NotifyAttackImpact()
    {
        if (logFlow) Debug.Log("[Battle][AnimEvent] AttackImpact received.");
        _impactFired = true;
    }

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
            {
                m.stats.ResetForNewRun();
            }

            _party.Add(m);
        }
        
        // Startup selection data is one-shot.
        StartupPartySelectionData.Clear();

if (reelSpinSystem != null)
        {
            var heroes = new List<HeroStats>();
            for (int i = 0; i < _party.Count; i++)
            {
                if (_party[i]?.stats != null)
                    heroes.Add(_party[i].stats);
            }

            reelSpinSystem.ConfigureFromParty(heroes);
        ConfigureReelSpinSystemCashoutHooks();
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


    // ---------------- Party Lookup / Evolution ----------------
    public int GetPartyIndexForHeroStats(HeroStats hero)
    {
        if (hero == null || _party == null) return -1;
        for (int i = 0; i < _party.Count; i++)
        {
            if (_party[i] != null && _party[i].stats == hero)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Swaps a party member's prefab at runtime (e.g., Fighter -> Templar) while preserving all HeroStats progress.
    /// This is called after the Level 5 reel-evolution minigame finishes.
    /// </summary>
    public bool EvolvePartyMemberToAdvanced(
        int partyIndex,
        GameObject advancedPrefab,
        ClassDefinitionSO advancedClassDef,
        ReelStripSO advancedReelStripTemplate,
        Sprite advancedPortraitOverride,
        Sprite advancedWorldSpriteOverride)
    {
        Debug.Log(
            $"[Evolution] BattleManager.EvolvePartyMemberToAdvanced BEGIN partyIndex={partyIndex} advancedPrefab='{(advancedPrefab != null ? advancedPrefab.name : "NULL")}' " +
            $"advancedClassDef='{(advancedClassDef != null ? advancedClassDef.className : "NULL")}' advancedStrip='{(advancedReelStripTemplate != null ? advancedReelStripTemplate.name : "NULL")}' " +
            $"portraitOverride='{(advancedPortraitOverride != null ? advancedPortraitOverride.name : "NULL")}' worldSpriteOverride='{(advancedWorldSpriteOverride != null ? advancedWorldSpriteOverride.name : "NULL")}'",
            this
        );

        if (!IsValidPartyIndex(partyIndex))
        {
            Debug.LogError($"[BattleManager] EvolvePartyMemberToAdvanced invalid partyIndex={partyIndex}");
            return false;
        }

        if (advancedPrefab == null)
        {
            Debug.LogError("[BattleManager] EvolvePartyMemberToAdvanced advancedPrefab is NULL.");
            return false;
        }

        PartyMemberRuntime m = _party[partyIndex];
        if (m == null || m.avatarGO == null || m.stats == null)
        {
            Debug.LogError($"[BattleManager] EvolvePartyMemberToAdvanced partyIndex={partyIndex} missing avatar/stats.");
            return false;
        }

        HeroStats oldStats = m.stats;
        Debug.Log(
            $"[Evolution] Old hero instance='{(m.avatarGO != null ? m.avatarGO.name : "NULL")}' stats='{(oldStats != null ? oldStats.name : "NULL")}' level={(oldStats != null ? oldStats.Level : 0)}",
            this
        );
        Transform parent = (partyRoot != null) ? partyRoot : m.avatarGO.transform.parent;

        Vector3 pos = m.avatarGO.transform.position;
        Quaternion rot = m.avatarGO.transform.rotation;

        GameObject newGo = Instantiate(advancedPrefab, pos, rot, parent);
        Debug.Log($"[Evolution] Instantiated new advanced prefab GO='{newGo.name}'", this);
        HeroStats newStats = newGo.GetComponentInChildren<HeroStats>(true);
        Animator newAnim = newGo.GetComponentInChildren<Animator>(true);

        if (newStats == null)
        {
            Debug.LogError($"[BattleManager] Advanced prefab '{advancedPrefab.name}' has no HeroStats component.");
            Destroy(newGo);
            return false;
        }

        // Preserve all runtime progress from the old instance.
        Debug.Log("[Evolution] Copying runtime state oldStats -> newStats", this);
        newStats.CopyRuntimeStateFrom(oldStats);

        // Apply advanced class definition (if not already present).
        if (advancedClassDef != null && newStats.AdvancedClassDef == null)
        {
            Debug.Log($"[Evolution] Applying advanced class def '{advancedClassDef.className}'", this);
            newStats.ApplyClassDefinition(advancedClassDef);
        }
        else
        {
            Debug.Log($"[Evolution] Skipping ApplyClassDefinition (advancedClassDef NULL or already set). currentAdvanced='{(newStats.AdvancedClassDef != null ? newStats.AdvancedClassDef.className : "NULL")}'", this);
        }

        // Swap reel strip to advanced template (if provided).
        if (advancedReelStripTemplate != null)
        {
            Debug.Log($"[Evolution] Replacing reel strip from template '{advancedReelStripTemplate.name}'", this);
            newStats.ReplaceReelStripFromTemplate(advancedReelStripTemplate);
        }
        else
        {
            Debug.Log("[Evolution] No advancedReelStripTemplate provided. Leaving current reel strip as-is.", this);
        }

        // Override portrait (optional).
        if (advancedPortraitOverride != null)
        {
            Debug.Log($"[Evolution] Setting portrait override '{advancedPortraitOverride.name}'", this);
            newStats.SetPortrait(advancedPortraitOverride);
        }
        else
        {
            Debug.Log("[Evolution] No portrait override provided. Leaving portrait as-is.", this);
        }


        // Override world sprite (optional) - useful during early prefab setup.
        if (advancedWorldSpriteOverride != null)
        {
            var srs = newGo.GetComponentsInChildren<SpriteRenderer>(true);
            int changed = 0;
            for (int i = 0; i < srs.Length; i++)
            {
                if (srs[i] == null) continue;
                srs[i].sprite = advancedWorldSpriteOverride;
                changed++;
            }
            Debug.Log($"[Evolution] Applied world sprite override '{advancedWorldSpriteOverride.name}'. spriteRenderersChanged={changed}", this);
        }
        else
        {
            Debug.Log("[Evolution] No world sprite override provided. Leaving SpriteRenderer sprites as-is.", this);
        }

        // Ensure advanced class abilities are available immediately.
        if (advancedClassDef != null)
            newStats.ForceUnlockAllAbilitiesFromClassDef(advancedClassDef, includeStarterChoice: true);


        // Destroy old avatar
        Debug.Log($"[Evolution] Destroying old avatar GO='{m.avatarGO.name}'", this);
        Destroy(m.avatarGO);

        // Update runtime party entry
        m.avatarGO = newGo;
        m.animator = newAnim;
        m.stats = newStats;
        _party[partyIndex] = m;

        // Reconfigure reels to reference the new HeroStats instances.
        if (reelSpinSystem != null)
        {
            Debug.Log("[Evolution] Reconfiguring ReelSpinSystem from updated party", this);
            var heroes = new List<HeroStats>(_party.Count);
            for (int i = 0; i < _party.Count; i++)
                if (_party[i] != null && _party[i].stats != null)
                    heroes.Add(_party[i].stats);

            reelSpinSystem.ConfigureFromParty(heroes);
            Debug.Log($"[Evolution] ReelSpinSystem.ConfigureFromParty done. heroes={heroes.Count}", this);
        }
        else
        {
            Debug.Log("[Evolution] reelSpinSystem is NULL. Skipping reel reconfigure.", this);
        }

        NotifyPartyChanged();

        Debug.Log("[Evolution] NotifyPartyChanged called.", this);

        Debug.Log($"[BattleManager] Evolved partyIndex={partyIndex} '{oldStats.name}' -> prefab='{advancedPrefab.name}' class='{(advancedClassDef != null ? advancedClassDef.className : "NULL")}'.");
        return true;
    }

    /// <summary>
    /// Enables/disables the instantiated party avatar GameObjects (the in-world ally sprites).
    /// Used by post-battle panels that should not show the full party lineup.
    /// </summary>
    public void SetPartyAvatarsActive(bool active)
    {
        if (_party == null) return;
        for (int i = 0; i < _party.Count; i++)
        {
            var pm = _party[i];
            if (pm != null && pm.avatarGO != null)
                pm.avatarGO.SetActive(active);
        }
    }

    public int GetIncomingDamagePreviewForPartyIndex(int index)
    {
        if (!IsValidPartyIndex(index)) return 0;

        var hs = _party[index].stats;
        if (hs == null || hs.CurrentHp <= 0) return 0;

        // Predict HP loss by simulating how shields + defense will reduce incoming damage.
        int predictedHpLoss = 0;
        int remainingShield = Mathf.Max(0, hs.Shield);
        int defense = Mathf.Max(0, hs.Defense);

        for (int i = 0; i < _plannedIntents.Count; i++)
        {
            var intent = _plannedIntents[i];
            if (intent.enemy == null || intent.enemy.IsDead) continue;
            // Conceal/Hidden: single-target attacks miss, but AoE still hits.
            // Mirror the runtime resolution rules (see EnemyAttack resolution).
            bool hitsThisHero = intent.isAoe || intent.targetPartyIndex == index;
            if (!hitsThisHero) continue;

            if (hs.IsHidden && !intent.isAoe)
                continue;

            int raw = intent.damage > 0 ? intent.damage : intent.enemy.GetDamage();
            raw = Mathf.Max(0, raw);
            if (raw <= 0) continue;

            // Shield absorbs first (shared across all hits in the preview).
            int absorbed = Mathf.Min(remainingShield, raw);
            remainingShield -= absorbed;
            int afterShield = raw - absorbed;

            // Defense mitigation happens per-hit (matches HeroStats.TakeDamage()).
            int hpLoss = Mathf.Max(0, afterShield - defense);
            predictedHpLoss += hpLoss;
        }

        // Add bleed tick preview (applies at start of the player's turn).
        try
        {
            if (hs.IsBleeding)
            {
                int stacks = hs.BleedStacks;
                int appliedTurn = hs.BleedAppliedOnPlayerTurn;
                if (stacks > 0 && appliedTurn != PlayerTurnNumber)
                {
                    int raw = stacks;
                    int hpLoss = Mathf.Max(0, raw - defense);
                    predictedHpLoss += hpLoss;
                }
            }
        }
        catch { }

        return Mathf.Max(0, predictedHpLoss);
    }

    public void SetActivePartyMember(int index)
    {
        if (!IsPlayerPhase) return;
        if (!IsValidPartyIndex(index)) return;

        _activePartyIndex = index;
        OnActivePartyMemberChanged?.Invoke(_activePartyIndex);
        NotifyPartyChanged();
    }

    public bool TryHandlePartySlotClickForPendingAbility(int partyIndex)
    {
        if (logFlow)
            Debug.Log($"[Battle][AbilityTarget] Party slot clicked. partyIndex={partyIndex} pendingActorIndex={_pendingActorIndex} awaitingPartyTarget={_awaitingPartyTarget} pendingAbility={(_pendingAbility != null ? _pendingAbility.abilityName : "<null>")}");

        if (!IsPlayerPhase) return false;
        if (_resolving) return true; // consume to prevent UI spam while resolving

        if (_pendingAbility == null) return false;
        if (!_awaitingPartyTarget) return false;

        bool selfOnly = _pendingAbility.targetType == AbilityTargetType.Self;

        if (selfOnly && partyIndex != _pendingActorIndex)
        {
            if (_previewPartyTargetIndex == _pendingActorIndex)
            {
                if (logFlow) Debug.Log("[Battle][AbilityTarget] Clicked different party slot -> cancel pending ability.", this);
                _previewPartyTargetIndex = -1;
                _selectedPartyTargetIndex = -1;
                HideConfirmText();
                CancelPendingAbility();
                NotifyPartyChanged();
            }
            return true;
        }

        if (_previewPartyTargetIndex != partyIndex)
        {
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


        // Ability unlock rules (Starter Choice / level unlock).
        HeroStats gateHero = actor.stats != null ? actor.stats : hero;
        if (gateHero != null && !gateHero.IsAbilityUnlocked(ability))
        {
            if (logFlow) Debug.Log($"[Battle][Ability] Blocked: {actor.name} tried to use locked ability '{ability.abilityName}'.", this);
            return;
        }

        if (_pendingAction != PlayerActionType.None) return;

        ResourceCost cost = GetEffectiveCost(actor.stats, ability);

        if (ability != null && ability.baseDamage > 0 && actor.stats != null && !actor.stats.CanCommitDamageAttackThisTurn())
        {
            if (logFlow) Debug.Log($"[Battle][Ability] Blocked: {actor.name} has reached their attack limit for this turn.", this);
            return;
        }

        // Once-per-turn abilities (per hero)
        if (actor.stats != null && !actor.stats.CanUseAbilityThisTurn(ability))
        {
            if (logFlow) Debug.Log($"[Battle][Ability] Blocked: {actor.name} already used '{ability.abilityName}' this player turn.", this);
            return;
        }

        if (logFlow)
            Debug.Log($"[Battle][Ability] Pending set. actorIndex={actorIndex} ability={ability.abilityName} targetType={ability.targetType} shieldAmount={ability.shieldAmount} healAmount={ability.healAmount} baseDamage={ability.baseDamage} cost={cost}");

        _pendingActorIndex = actorIndex;
        _pendingAbility = ability;
        _selectedEnemyTarget = null;
        _previewPartyTargetIndex = -1;
        _selectedPartyTargetIndex = -1;
        HideConfirmText();
        ClearEnemyTargetPreview();

        if (AbilityCastState.Instance != null)
            AbilityCastState.Instance.BeginCast(hero, ability);

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
        else if ((ability.targetType == AbilityTargetType.Self || ability.targetType == AbilityTargetType.Ally) && (ability.shieldAmount > 0 || ability.healAmount > 0))
        {
            _awaitingEnemyTarget = false;
            _awaitingPartyTarget = true;
            ClearEnemyTargetPreview();
            _selectedEnemyTarget = null;
            _previewEnemyTarget = null;
            if (logFlow)
            {
                string mode = (ability.targetType == AbilityTargetType.Self) ? "SELF" : "ALLY";
                Debug.Log($"[Battle][AbilityTarget] Awaiting {mode} confirm for {ability.abilityName} (ally/self ability)");
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

        if (_previewEnemyTarget != target)
        {
            // IMPORTANT: do NOT set _previewEnemyTarget here; SetEnemyTargetPreview() needs the old value
            // so it can clear the previous target's preview correctly.
            SetEnemyTargetPreview(target);
            ShowConfirmText();

            if (logFlow)
                Debug.Log($"[Battle][AbilityTarget] Preview target set to {target.name}. Click again to confirm.");
            return;
        }

        _selectedEnemyTarget = target;
        _awaitingEnemyTarget = false;

        ClearEnemyTargetPreview();

        HideConfirmText();

        if (logFlow)
            Debug.Log($"[Battle][AbilityTarget] Target confirmed: {target.name}. Resolving ability.");

        StartCoroutine(ResolvePendingAbility());

    }

    public void StartBattle()
    {
        if (_resolving) return;

        PlayerTurnNumber = 0;

        if (_startBattleRoutine != null)
            StopCoroutine(_startBattleRoutine);

        _startBattleRoutine = StartCoroutine(StartBattleRoutine());
    }

    public void QueueNextEnemyParty(EnemyPartyCompositionSO party)
    {
        _nextEnemyPartyOverride = party;
    }

    public void EndTurn()
    {
        if (!IsPlayerPhase) return;
        if (_resolving) return;
        if (_enemyTurnRoutine != null) return;

        if (resourcePool != null)
            resourcePool.ClearAll();

        TickBleedingAtEndOfPlayerTurn();

        // Ninja Reelcraft: Twofold Shadow should only persist for the current turn.
        // Clear the temporary doubled-icon visuals so they do not carry into the next turn.
        if (reelSpinSystem != null)
            reelSpinSystem.ClearAllTemporaryDoubles();

        if (_state == BattleState.BattleEnd)
            return;

        if (_activeMonsters == null || _activeMonsters.Count == 0) return;

        _enemyTurnRoutine = StartCoroutine(EnemyPhaseRoutine());
    }

    private void TickBleedingAtEndOfPlayerTurn()
    {
        if (_party == null) return;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        for (int i = 0; i < _party.Count; i++)
        {
            var pm = _party[i];
            var hs = pm != null ? pm.stats : null;
            if (hs == null || pm.IsDead) continue;

            int stacks = 0;
            try { stacks = hs.BleedStacks; } catch { stacks = 0; }
            if (stacks <= 0) continue;

            int appliedTurn = -999;
            try
            {
                var pi = hs.GetType().GetProperty("BleedAppliedOnPlayerTurn", flags);
                if (pi != null && pi.PropertyType == typeof(int))
                    appliedTurn = (int)pi.GetValue(hs, null);
                else
                {
                    var fi = hs.GetType().GetField("BleedAppliedOnPlayerTurn", flags) ?? hs.GetType().GetField("bleedAppliedOnPlayerTurn", flags);
                    if (fi != null && fi.FieldType == typeof(int))
                        appliedTurn = (int)fi.GetValue(hs);
                }
            }
            catch { appliedTurn = -999; }

            if (appliedTurn == PlayerTurnNumber)
                continue;

            int dealt = 0;
            try
            {
                var mi = hs.GetType().GetMethod("TickBleedingAtEndOfPlayerTurn", flags, null, Type.EmptyTypes, null);
                if (mi != null && mi.ReturnType == typeof(int))
                {
                    dealt = (int)mi.Invoke(hs, null);
                }
                else
                {
                    var mi2 = hs.GetType().GetMethod("TickBleedingAtTurnStart", flags, null, Type.EmptyTypes, null);
                    if (mi2 != null && mi2.ReturnType == typeof(int))
                        dealt = (int)mi2.Invoke(hs, null);
                    else
                        dealt = 0;
                }
            }
            catch { dealt = 0; }

            if (dealt > 0 && pm.avatarGO != null)
                SpawnDamageNumber(pm.avatarGO.transform.position, dealt);
        }

        if (IsPartyDefeated())
        {
            Debug.Log("[BattleManager] Party defeated (bleed tick).", this);
            SetState(BattleState.BattleEnd);
        }

        NotifyPartyChanged();
    }

    private IEnumerator EnemyPhaseRoutine()
    {
        SetState(BattleState.EnemyPhase);

        CancelPendingAbility();

        if (_plannedIntents.Count == 0) PlanEnemyIntents();

        var intentsToExecute = new List<EnemyIntent>(_plannedIntents);

        _plannedIntents.Clear();
        OnEnemyIntentsPlanned?.Invoke(new List<EnemyIntent>(_plannedIntents));
        NotifyPartyChanged();

        for (int i = 0; i < intentsToExecute.Count; i++)
        {
            var intent = intentsToExecute[i];
            if (intent.enemy == null || intent.enemy.IsDead) continue;

            int targetIdx = intent.targetPartyIndex;

            if (!IsValidPartyIndex(targetIdx) || _party[targetIdx].IsDead)
                targetIdx = GetRandomLivingTargetIndex();

            if (!IsValidPartyIndex(targetIdx)) break;

            HeroStats targetStats = _party[targetIdx].stats;
            GameObject targetGO = _party[targetIdx].avatarGO;

            Transform targetTf = targetGO != null ? targetGO.transform : (targetStats != null ? targetStats.transform : null);

            yield return EnemyLungeAttack(intent.enemy, targetTf, () =>
            {
                int raw = intent.damage;
                if (raw <= 0 && intent.enemy != null) raw = intent.enemy.GetDamage();

                if (intent.isAoe || intent.type == IntentType.AoEAttack)
                {
                    for (int pi = 0; pi < PartyCount; pi++)
                    {
                        var pm = _party[pi];
                        var hs = pm != null ? pm.stats : null;
                        if (hs == null || pm.IsDead) continue;

                        if (logFlow) Debug.Log($"[Battle][EnemyAtk][AoE] Applying incoming damage. attacker={(intent.enemy != null ? intent.enemy.name : "<null>")} targetIdx={pi} raw={raw} targetShieldBefore={hs.Shield}", this);

                        int hpBefore = hs.CurrentHp;
                        int shieldBefore = hs.Shield;

                        int hpDealt = hs.ApplyIncomingDamage(raw);

                        // Show total damage applied (shield removed + HP lost), not just HP lost.
                        int shieldLost = Mathf.Max(0, shieldBefore - hs.Shield);
                        int hpLost = Mathf.Max(0, hpBefore - hs.CurrentHp);
                        int totalDamageShown = shieldLost + hpLost;

                        if (performanceTracker != null)
                            performanceTracker.RecordDamageTaken(hs, hpDealt);

                        if (intent.stunsTarget)
                            hs.StunForNextPlayerPhases(intent.stunPlayerPhases);

                        if (intent.appliesBleed && intent.bleedStacks > 0)
                            ApplyBleedStacksToHero(hs, intent.bleedStacks);
                        if (hs.IsHidden) hs.SetHidden(false);

                        if (pm.avatarGO != null)
                            SpawnDamageNumber(pm.avatarGO.transform.position, totalDamageShown);
                    }

                    ApplyPartyHiddenVisuals();
                    return;
                }

                if (targetStats == null) return;

                if (targetStats.IsHidden && !intent.isAoe)
                {
                    if (logFlow) Debug.Log($"[Battle][EnemyAtk] Target is hidden (Conceal). Attack misses. attacker={(intent.enemy != null ? intent.enemy.name : "<null>")} targetIdx={targetIdx}", this);
                    return;
                }

                if (logFlow) Debug.Log($"[Battle][EnemyAtk] Applying incoming damage. attacker={(intent.enemy != null ? intent.enemy.name : "<null>")} targetIdx={targetIdx} raw={raw} targetShieldBefore={targetStats.Shield}", this);

                int hpBeforeSingle = targetStats.CurrentHp;
                int shieldBeforeSingle = targetStats.Shield;

                int dealtSingle = targetStats.ApplyIncomingDamage(raw);

                int shieldLostSingle = Mathf.Max(0, shieldBeforeSingle - targetStats.Shield);
                int hpLostSingle = Mathf.Max(0, hpBeforeSingle - targetStats.CurrentHp);
                int totalDamageShownSingle = shieldLostSingle + hpLostSingle;

                if (performanceTracker != null)
                    performanceTracker.RecordDamageTaken(targetStats, dealtSingle);

                if (intent.stunsTarget)
                    targetStats.StunForNextPlayerPhases(intent.stunPlayerPhases);

                if (intent.appliesBleed && intent.bleedStacks > 0)
                    ApplyBleedStacksToHero(targetStats, intent.bleedStacks);
                if (logFlow) Debug.Log($"[Battle][EnemyAtk] Damage result. dealtToHp={dealtSingle} targetShieldAfter={targetStats.Shield}", this);

                if (targetGO != null)
                    SpawnDamageNumber(targetGO.transform.position, totalDamageShownSingle);
            });
            NotifyPartyChanged();

            if (IsPartyDefeated())
            {
                Debug.Log("[BattleManager] Party defeated (enemy phase).", this);
                SetState(BattleState.BattleEnd);
                _enemyTurnRoutine = null;
                yield break;
            }
        }

        ResetPartyRoundFlags();
        PlanEnemyIntents();

        SetState(BattleState.PlayerPhase);

        PlayerTurnNumber++;

        BeginPlayerTurnSaveState();

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

        var sr = enemy.GetComponentInChildren<SpriteRenderer>(true);
        if (sr != null) return sr.transform;

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

        if (enemy == null)
            yield break;

        Transform visual = GetEnemyVisualTransform(enemy);

        if (visual == null)
        {
            applyDamage?.Invoke();
            yield break;
        }

        Vector3 startPos = visual.position;

        Vector3 dir = Vector3.right;
        if (target != null)
        {
            Vector3 toTarget = (target.position - startPos);
            if (toTarget.sqrMagnitude > 0.0001f)
                dir = toTarget.normalized;
        }

        Vector3 peakPos = startPos + dir * enemyLungeDistance;

        float t = 0f;
        float forward = Mathf.Max(0.0001f, enemyLungeForwardSeconds);
        while (t < forward)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / forward);
            visual.position = Vector3.Lerp(startPos, peakPos, a);
            yield return null;
        }

        visual.position = peakPos;
        applyDamage?.Invoke();

        float hold = Mathf.Max(0f, enemyLungeHoldSeconds);
        if (hold > 0f)
            yield return new WaitForSeconds(hold);

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

        // Ensure any per-battle-only statuses (e.g., Conceal/Hidden) are cleared before a new encounter begins.
        if (_party != null)
        {
            for (int i = 0; i < _party.Count; i++)
            {
                var hs = _party[i] != null ? _party[i].stats : null;
                if (hs != null) hs.ClearStartOfBattleStatuses();
            }
        }
        ApplyPartyHiddenVisuals();
        SpawnEncounterMonsters();

        if (performanceTracker != null)
        {
            var heroes = new List<HeroStats>(_party != null ? _party.Count : 0);
            if (_party != null)
                for (int i = 0; i < _party.Count; i++)
                    if (_party[i] != null && _party[i].stats != null)
                        heroes.Add(_party[i].stats);
            performanceTracker.BeginBattle(heroes);
        }

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

        PlayerTurnNumber++;

        BeginPlayerTurnSaveState();

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

        AbilityDefinitionSO ability = _pendingAbility;
        if (ability == null)
        {
            if (logFlow) Debug.Log("[Battle][Resolve] Cancel: ability is null.", this);
            CancelPendingAbility();
            yield break;
        }

        if (logFlow)
            Debug.Log($"[Battle][Resolve] Confirmed/casting ability: name={ability.name} abilityName={ability.abilityName} targetType={ability.targetType} shieldAmount={ability.shieldAmount} baseDamage={ability.baseDamage} isDamaging={ability.isDamaging} inflictsFocusRune={ability.inflictsFocusRune}", this);

        PartyMemberRuntime actor = _party[_pendingActorIndex];
        HeroStats actorStats = actor.stats;
        if (actorStats == null || actor.IsDead)
        {
            if (logFlow) Debug.Log("[Battle][Resolve] Cancel: actorStats missing or actor dead.", this);
            CancelPendingAbility();
            yield break;
        }

        if (performanceTracker != null)
            performanceTracker.RecordAbilityUse(actorStats, ability);

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

        if (ability.targetType == AbilityTargetType.Ally && ability.shieldAmount > 0)
        {
            if (!IsValidPartyIndex(_selectedPartyTargetIndex) || _party[_selectedPartyTargetIndex] == null || _party[_selectedPartyTargetIndex].IsDead)
            {
                if (logFlow) Debug.Log("[Battle][Resolve] Abort: Ally target required but not selected (or dead). Returning to awaiting party target.", this);
                _awaitingPartyTarget = true;
                yield break;
            }
        }

        PushSaveStateSnapshot();

        ResourceCost cost = GetEffectiveCost(actorStats, ability);
        if (resourcePool == null || !resourcePool.TrySpend(cost))
        {
            if (logFlow) Debug.Log($"[Battle][Resolve] Cancel: insufficient resources or missing resourcePool. cost={cost}", this);
            CancelPendingAbility();
            yield break;
        }

        // Mark once-per-turn ability usage only after the cast is truly committed (cost successfully spent).
        actorStats.RegisterAbilityUsedThisTurn(ability);

        if (logFlow) Debug.Log($"[Battle][Resolve] Resources spent. cost={cost}. Proceeding to apply ability effects.", this);
        _resolving = true;

        Animator anim = actor.animator;
        if (anim == null && actor.avatarGO != null)
            anim = actor.avatarGO.GetComponentInChildren<Animator>(true);

        _impactFired = false;
        _attackFinished = false;
        bool useImpactSync = false;
        string stateToPlay = null;

        if (anim != null)
        {
            var profile = anim.GetComponentInParent<CasterAnimationProfile>();

            switch (ability.name)
            {
                case "Slash":
                    useImpactSync = true;
                    stateToPlay = profile != null ? profile.GetAttackStateForAbility("Slash") : null;
                    if (string.IsNullOrWhiteSpace(stateToPlay))
                        stateToPlay = "fighter_basic_attack";
                    break;

                case "Pyre":
                    useImpactSync = true;
                    stateToPlay = profile != null ? profile.GetAttackStateForAbility("Pyre") : null;
                    if (string.IsNullOrWhiteSpace(stateToPlay))
                        stateToPlay = "mage_basic_attack";
                    break;

                case "Heal":
                    useImpactSync = true;
                    stateToPlay = profile != null ? profile.GetAttackStateForAbility("Heal") : null;
                    if (string.IsNullOrWhiteSpace(stateToPlay))
                        stateToPlay = profile != null ? profile.GetAttackStateForAbility("Pyre") : null;
                    if (string.IsNullOrWhiteSpace(stateToPlay))
                        stateToPlay = "mage_basic_attack";
                    break;

                case "First Aid":
                    useImpactSync = true;
                    stateToPlay = profile != null ? profile.GetAttackStateForAbility("First Aid") : null;
                    if (string.IsNullOrWhiteSpace(stateToPlay))
                        stateToPlay = "fighter_magic_ability";
                    break;

                case "Backstab":
                    useImpactSync = true;
                    stateToPlay = profile != null ? profile.GetAttackStateForAbility("Backstab") : null;
                    if (string.IsNullOrWhiteSpace(stateToPlay))
                        stateToPlay = "ninja_backstab";
                    break;

                case "Conceal":
                    useImpactSync = false;
                    stateToPlay = null;
                    if (logFlow) Debug.Log("[Battle][Resolve] Conceal: no animation and no impact sync.", this);
                    break;

                case "Block":
                    useImpactSync = false;
                    stateToPlay = null;
                    if (logFlow) Debug.Log("[Battle][Resolve] Block: no animation and no impact sync.", this);
                    break;

                case "Aegis":
                    useImpactSync = false;
                    stateToPlay = null;
                    if (logFlow) Debug.Log("[Battle][Resolve] Aegis: no animation and no impact sync.", this);
                    break;

                // Templar Abilities
                case "Righteous Cut":
                    useImpactSync = true;
                    stateToPlay = profile != null ? profile.GetAttackStateForAbility("Righteous Cut") : null;
                    if (string.IsNullOrWhiteSpace(stateToPlay))
                        stateToPlay = "templar_basic_attack";
                    break;

                case "Verdict & Execution":
                    useImpactSync = true;
                    stateToPlay = profile != null ? profile.GetAttackStateForAbility("Verdict & Execution") : null;
                    if (string.IsNullOrWhiteSpace(stateToPlay))
                        stateToPlay = "templar_strong_attack";
                    break;

                case "Stay the Sentence":
                    useImpactSync = true;
                    stateToPlay = profile != null ? profile.GetAttackStateForAbility("Stay the Sentence") : null;
                    if (string.IsNullOrWhiteSpace(stateToPlay))
                        stateToPlay = "templar_magic_ability";
                    break;

                default:
                    useImpactSync = false;
                    stateToPlay = profile != null ? profile.GetAttackStateForAbility(ability.name) : null;
                    if (string.IsNullOrWhiteSpace(stateToPlay))
                        stateToPlay = "fighter_basic_attack";
                    break;
            }

            // If this is a heal/shield targeting Self/Ally, default to syncing the effect
            // to the impact event (if the animation clip has one).
            if ((ability.targetType == AbilityTargetType.Self || ability.targetType == AbilityTargetType.Ally) &&
                (ability.healAmount > 0 || ability.shieldAmount > 0))
            {
                useImpactSync = true;
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

        // Support ability impact sync (heal/shield)
        bool isSupportAbility =
            (ability.targetType == AbilityTargetType.Self || ability.targetType == AbilityTargetType.Ally) &&
            (ability.healAmount > 0 || ability.shieldAmount > 0);

        if (isSupportAbility && useImpactSync && anim != null)
        {
            if (logFlow) Debug.Log("[Battle][Resolve] Support ability: waiting for AttackImpact animation event...", this);

            yield return null;

            float elapsed = 0f;
            const float failSafeSeconds = 3.0f;
            while (!_impactFired && elapsed < failSafeSeconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (logFlow) Debug.Log($"[Battle][Resolve] Support impact wait finished. impactFired={_impactFired} elapsed={elapsed:0.000}s", this);
        }

        // ============================
        // Enemy-target abilities
        // ============================
        if (ability.targetType == AbilityTargetType.Enemy && enemyTarget != null)
        {
            // Wait for impact sync for enemy-target abilities too (even if non-damaging)
            if (useImpactSync && anim != null)
            {
                if (logFlow) Debug.Log("[Battle][Resolve] Waiting for AttackImpact animation event...", this);

                yield return null;

                float elapsed = 0f;
                const float failSafeSeconds = 3.0f;
                while (!_impactFired && elapsed < failSafeSeconds)
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                if (logFlow) Debug.Log($"[Battle][Resolve] Done waiting for impact. impactFired={_impactFired} elapsed={elapsed:0.000}s", this);
            }

            bool doesDamage = ability.isDamaging;

            int shownDamage = 0;
            int dealt = 0;
            int totalBaseDamage = 0;

            if (doesDamage)
            {
                int passiveBonus = (actorStats != null) ? actorStats.ConsumeBonusDamageNextAttackIfDamaging(ability) : 0;

                totalBaseDamage =
                    Mathf.Max(0, actorStats.Attack) +
                    Mathf.Max(0, ability.baseDamage) +
                    Mathf.Max(0, passiveBonus);

                // Damage numbers should show computed formula damage, not clamped HP lost.
                shownDamage = enemyTarget.CalculateDamageFromAbility(
                    abilityBaseDamage: totalBaseDamage,
                    classAttackModifier: 1f,
                    element: ability.element,
                    abilityTags: ability.tags);

                dealt = enemyTarget.TakeDamageFromAbility(
                    abilityBaseDamage: totalBaseDamage,
                    classAttackModifier: 1f,
                    element: ability.element,
                    abilityTags: ability.tags);

                if (debugEnemyHpBarDrop && enemyTarget != null)
                {
                    Debug.Log($"[Battle][HpBarDrop] After TakeDamageFromAbility target={enemyTarget.name} dealt={dealt} hpNow={enemyTarget.CurrentHp}/{enemyTarget.MaxHp} instance={enemyTarget.GetInstanceID()}", this);

                    var hpBar = enemyTarget.GetComponentInChildren<MonsterHpBar>(true);
                    if (hpBar == null)
                    {
                        Debug.LogWarning($"[Battle][HpBarDrop] No MonsterHpBar found under target={enemyTarget.name} instance={enemyTarget.GetInstanceID()}", this);
                    }
                    else
                    {
                        Debug.Log($"[Battle][HpBarDrop] Found hpBar={hpBar.name} barInstance={hpBar.GetInstanceID()} barBoundMonster={(hpBar != null ? (hpBar.GetComponentInParent<Monster>() != null ? hpBar.GetComponentInParent<Monster>().GetInstanceID().ToString() : "none") : "none")}", this);

                        hpBar.ForceDebugDumpVisual("BattleManager BEFORE ClearPreview/Refresh");
                        hpBar.ClearPreview();

                        hpBar.ForceDebugDumpVisual("BattleManager AFTER ClearPreview");
                        hpBar.RefreshNow("BattleManager post-damage");

                        hpBar.ForceDebugDumpVisual("BattleManager AFTER RefreshNow");
                    }
                }

                if (performanceTracker != null)
                    performanceTracker.RecordDamageDealt(actorStats, dealt);

                if (shownDamage > 0)
                    SpawnDamageNumber(enemyTarget.transform.position, shownDamage);

                actorStats.ApplyOnHitEffectsTo(enemyTarget);

                if (totalBaseDamage > 0)
                    actorStats.RegisterDamageAttackCommitted();
            }
            else
            {
                if (logFlow) Debug.Log($"[Battle][Resolve] Non-damaging enemy ability '{ability.abilityName}': skipping damage math.", this);
            }

            // ---------------- Status Infliction (Monster) ----------------
            if (ability.inflictsFocusRune && enemyTarget != null && !enemyTarget.IsDead)
            {
                if (logFlow) Debug.Log($"[Battle][Status] Applying FocusRune via ability='{ability.abilityName}' to monster='{enemyTarget.name}'", this);
                enemyTarget.SetFocusRune(true);
            }

            // Death check ALWAYS (not gated)
            if (enemyTarget.IsDead)
            {
                int xpAward = (enemyTarget != null) ? enemyTarget.XpReward : 5;
                if (performanceTracker != null)
                    performanceTracker.RecordBaseXpGained(actorStats, xpAward);
                else
                    actorStats.GainXP(xpAward);

                RemoveMonster(enemyTarget);
            }
        }

        // ============================
        // Shield (Self/Ally)
        // ============================
        if (ability.shieldAmount > 0 && (ability.targetType == AbilityTargetType.Self || ability.targetType == AbilityTargetType.Ally))
        {
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

        // ============================
        // Heal (Self/Ally)
        // ============================
        if (ability.healAmount > 0 && (ability.targetType == AbilityTargetType.Self || ability.targetType == AbilityTargetType.Ally))
        {
            HeroStats targetStats = actorStats;
            GameObject targetGO = actor != null ? actor.avatarGO : null;
            string targetName = actorStats != null ? actorStats.name : "<null>";

            if (ability.targetType == AbilityTargetType.Ally)
            {
                if (IsValidPartyIndex(_selectedPartyTargetIndex) && _party[_selectedPartyTargetIndex] != null)
                {
                    targetStats = _party[_selectedPartyTargetIndex].stats;
                    targetGO = _party[_selectedPartyTargetIndex].avatarGO;
                    targetName = _party[_selectedPartyTargetIndex].name;
                }
            }

            if (targetStats != null)
            {
                int before = targetStats.CurrentHp;
                targetStats.Heal(ability.healAmount);
                int healed = Mathf.Max(0, targetStats.CurrentHp - before);

                if (logFlow) Debug.Log($"[Battle][Heal] Applied. amount={ability.healAmount} healed={healed} target={targetName} hpNow={targetStats.CurrentHp}/{targetStats.MaxHp}", this);

                if (healed > 0)
                {
                    Vector3 pos = (targetGO != null) ? targetGO.transform.position : (targetStats != null ? targetStats.transform.position : Vector3.zero);
                    SpawnHealNumber(pos, healed);
                    SpawnHealVfx(targetStats.transform);
                }
            }
        }

        // ---------------- Status Cleansing (Bleeding / Stunned) ----------------
        if (ability.targetType == AbilityTargetType.Self || ability.targetType == AbilityTargetType.Ally)
        {
            bool hasConfiguredCleansing = (ability.removesStatusEffects != null && ability.removesStatusEffects.Count > 0);
            bool isFirstAid = (ability.name == "First Aid" || ability.abilityName == "First Aid");

            if (hasConfiguredCleansing || isFirstAid)
            {
                HeroStats cleanseTargetStats = actorStats;
                GameObject cleanseTargetGO = actor != null ? actor.avatarGO : null;
                string cleanseTargetName = actor != null ? actor.name : (actorStats != null ? actorStats.name : "<null>");

                if (ability.targetType == AbilityTargetType.Ally)
                {
                    if (IsValidPartyIndex(_selectedPartyTargetIndex) && _party[_selectedPartyTargetIndex] != null)
                    {
                        cleanseTargetStats = _party[_selectedPartyTargetIndex].stats;
                        cleanseTargetGO = _party[_selectedPartyTargetIndex].avatarGO;
                        cleanseTargetName = _party[_selectedPartyTargetIndex].name;
                    }
                }

                ApplyStatusCleansingToHero(ability, cleanseTargetStats, cleanseTargetName, cleanseTargetGO, forceBleedForFirstAid: isFirstAid);
            }
        }

        bool wasHiddenBeforeCast = actorStats.IsHidden;

        if (ability.name == "Conceal")
        {
            actorStats.SetHidden(true);
        }
        else if (wasHiddenBeforeCast)
        {
            bool keepHidden = false;

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

        if (_saveStates != null && _saveStates.Count > 1)
            SetUndoButtonEnabled(true);
    }

    private void ApplyStatusCleansingToHero(
        AbilityDefinitionSO ability,
        HeroStats targetStats,
        string targetName,
        GameObject targetGO,
        bool forceBleedForFirstAid)
    {
        if (ability == null || targetStats == null) return;

        bool clearBleed = forceBleedForFirstAid;
        bool clearStun = false;

        var list = ability.removesStatusEffects;
        if (list != null)
        {
            for (int i = 0; i < list.Count; i++)
            {
                switch (list[i])
                {
                    case RemovableStatusEffect.Bleeding:
                        clearBleed = true;
                        break;
                    case RemovableStatusEffect.Stunned:
                        clearStun = true;
                        break;
                }
            }
        }

        int removedCount = 0;

        if (clearBleed)
        {
            bool removed = false;
            try { removed = targetStats.ClearBleeding(); } catch { removed = false; }
            if (removed) removedCount++;
            if (logFlow && removed) Debug.Log($"[Battle][Cleanse] Removed BLEEDING from {targetName} via {ability.abilityName}", this);
        }

        if (clearStun)
        {
            bool removed = false;
            try { removed = targetStats.ClearStun(); } catch { removed = false; }
            if (removed) removedCount++;
            if (logFlow && removed) Debug.Log($"[Battle][Cleanse] Removed STUNNED from {targetName} via {ability.abilityName}", this);
        }

        if (removedCount > 0)
            NotifyPartyChanged();
    }

    private ResourceCost GetEffectiveCost(HeroStats actor, AbilityDefinitionSO ability)
    {
        if (ability == null) return default;
        return ability.cost;
    }

    private void SetEnemyTargetPreview(Monster target)
    {
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

        // NEW: Non-damaging abilities should show 0 predicted damage (no preview drop).
        // Also: don't preview-consume or include "next attack" bonus for non-damaging abilities.
        if (_pendingAbility.targetType == AbilityTargetType.Enemy && !_pendingAbility.isDamaging)
        {
            var bar0 = target.GetComponentInChildren<MonsterHpBar>(true);
            if (bar0 != null)
                bar0.SetDamagePreview(target.CurrentHp); // no change

            UpdateEnemyTargetIndicators();
            NotifyPartyChanged();
            return;
        }

        int previewPassiveBonus = 0;
        if (actor.stats != null && _pendingAbility != null && _pendingAbility.targetType == AbilityTargetType.Enemy)
        {
            // Preview should include the "next attack" bonus even when baseDamage is 0,
            // because your runtime damage model is: Attack + baseDamage (+ bonus).
            // BUT only if the ability is damaging (handled above).
            int baseNoBonus = Mathf.Max(0, actor.stats.Attack) + Mathf.Max(0, _pendingAbility.baseDamage);
            if (baseNoBonus > 0)
                previewPassiveBonus = actor.stats.BonusDamageNextAttack;
        }

        int totalBaseDamage =
            Mathf.Max(0, actor.stats.Attack) +
            Mathf.Max(0, _pendingAbility.baseDamage) +
            Mathf.Max(0, previewPassiveBonus);

        int predictedDamage = 0;

        // Optional micro-optimization: if total base is 0, skip CalculateDamageFromAbility.
        if (totalBaseDamage > 0)
        {
            predictedDamage = target.CalculateDamageFromAbility(
                abilityBaseDamage: totalBaseDamage,
                classAttackModifier: 1f,
                element: _pendingAbility.element,
                abilityTags: _pendingAbility.tags);
        }

        int previewHp = Mathf.Max(0, target.CurrentHp - predictedDamage);

        var bar = target.GetComponentInChildren<MonsterHpBar>(true);
        if (bar != null)
            bar.SetDamagePreview(previewHp);

        UpdateEnemyTargetIndicators();
        NotifyPartyChanged(); // lets PartyHUD refresh ally target indicators
    }

    private void ClearEnemyTargetPreview()
    {
        if (_previewEnemyTarget != null)
        {
            var bar = _previewEnemyTarget.GetComponentInChildren<MonsterHpBar>(true);
            if (bar != null) bar.ClearPreview();
        }
        _previewEnemyTarget = null;

        UpdateEnemyTargetIndicators();
        NotifyPartyChanged();
    }

    private TargetIndicatorUI GetOrCreateEnemyTargetIndicator(Monster m)
    {
        if (m == null) return null;

        if (_enemyTargetIndicators.TryGetValue(m, out var cached) && cached != null)
            return cached;

        // If the prefab already has an indicator wired, use it.
        var existing = m.GetComponentInChildren<TargetIndicatorUI>(true);
        if (existing != null)
        {
            _enemyTargetIndicators[m] = existing;
            return existing;
        }

        // Option A: Spawn at runtime if a prefab is provided.
        if (enemyTargetIndicatorPrefab == null)
            return null;

        RectTransform parent = null;

        // Prefer attaching to the HP bar object so offsets are intuitive.
        var hpBar = m.GetComponentInChildren<MonsterHpBar>(true);
        if (hpBar != null)
        {
            parent = hpBar.GetComponent<RectTransform>();
            if (parent == null)
                parent = hpBar.transform.parent as RectTransform;
        }

        // Fallback: any canvas under the monster.
        if (parent == null)
        {
            var canvas = m.GetComponentInChildren<Canvas>(true);
            if (canvas != null)
                parent = canvas.transform as RectTransform;
        }

        if (parent == null)
            return null;

        TargetIndicatorUI spawned = Instantiate(enemyTargetIndicatorPrefab, parent);
        spawned.name = "TargetIndicator";
        spawned.transform.SetAsLastSibling();
        spawned.Configure(enemyTargetIndicatorOffset, enemyTargetIndicatorScale);
        spawned.SetVisible(false);

        _enemyTargetIndicators[m] = spawned;
        _spawnedEnemyTargetIndicators.Add(m);
        return spawned;
    }

    private void RemoveEnemyTargetIndicatorForMonster(Monster m)
    {
        if (m == null) return;
        if (_enemyTargetIndicators == null) return;

        if (_enemyTargetIndicators.TryGetValue(m, out var indicator))
        {
            _enemyTargetIndicators.Remove(m);
            if (_spawnedEnemyTargetIndicators.Contains(m))
            {
                _spawnedEnemyTargetIndicators.Remove(m);
                if (indicator != null && indicator.gameObject != null)
                    Destroy(indicator.gameObject);
            }
        }
    }

    private void CleanupEnemyTargetIndicators()
    {
        if (_enemyTargetIndicators == null || _enemyTargetIndicators.Count == 0)
            return;

        foreach (var kvp in _enemyTargetIndicators)
        {
            if (!_spawnedEnemyTargetIndicators.Contains(kvp.Key))
                continue;

            var indicator = kvp.Value;
            if (indicator != null && indicator.gameObject != null)
                Destroy(indicator.gameObject);
        }
        _enemyTargetIndicators.Clear();
        _spawnedEnemyTargetIndicators.Clear();
    }

    private void UpdateEnemyTargetIndicators()
    {
        // Optional, purely visual.
        // Show indicator only while awaiting an enemy target, and only on the current preview target.
        bool shouldShow = _awaitingEnemyTarget && _previewEnemyTarget != null;

        for (int i = 0; i < _activeMonsters.Count; i++)
        {
            Monster m = _activeMonsters[i];
            if (m == null) continue;

            var indicator = GetOrCreateEnemyTargetIndicator(m);
            if (indicator == null) continue;

            indicator.SetVisible(shouldShow && m == _previewEnemyTarget);
        }
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
        UpdateEnemyTargetIndicators();
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
                category = ComputeIntentCategory(damage, isAoe, stunsTarget, appliesBleed),
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

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        object attacksObj = null;
        var t = m.GetType();

        var fiAttacks = t.GetField("attacks", flags);
        if (fiAttacks != null)
            attacksObj = fiAttacks.GetValue(m);

        System.Array attacksArray = attacksObj as System.Array;
        int count = attacksArray != null ? attacksArray.Length : 0;

        if (count <= 0)
        {
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
                hs.StartPlayerPhaseStatuses();
            }

            _party[i].hasActedThisRound = (hs != null && hs.IsStunned);
        }

        CancelPendingAbility();
        NotifyPartyChanged();

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
            // Progression gating: EnemyPartyCompositionSO is eligible ONLY for a single fight index (0-based).
            // We treat the current fight index as: "number of battles already completed in this stretch".
            int fightIndex = 0;
            if (stretchController != null)
                fightIndex = Mathf.Max(0, stretchController.BattlesCompleted);

            // Build eligible pool for this fight.
            List<EnemyPartyCompositionSO> eligible = new List<EnemyPartyCompositionSO>(enemyPartyPool.Count);
            for (int i = 0; i < enemyPartyPool.Count; i++)
            {
                var p = enemyPartyPool[i];
                if (p == null) continue;
                if (p.IsEligibleForFight(fightIndex))
                    eligible.Add(p);
            }

            // If authoring forgot to create an eligible party for this fight, fall back to the entire pool
            // (otherwise the encounter would silently spawn random monsters).
            if (eligible.Count == 0)
            {
                for (int i = 0; i < enemyPartyPool.Count; i++)
                {
                    var p = enemyPartyPool[i];
                    if (p != null) eligible.Add(p);
                }

                Debug.LogWarning($"[BattleManager] No EnemyPartyCompositionSO matched fightIndex={fightIndex}. Falling back to ungated pool selection.", this);
            }

            if (eligible.Count > 0)
            {
                if (randomizeEnemyPartyFromPool)
                {
                    // Weighted random by selectionWeight (<=0 means "never", unless all are <=0).
                    float totalW = 0f;
                    for (int i = 0; i < eligible.Count; i++)
                        totalW += Mathf.Max(0f, eligible[i] != null ? eligible[i].selectionWeight : 0f);

                    if (totalW <= 0f)
                    {
                        chosen = eligible[UnityEngine.Random.Range(0, eligible.Count)];
                    }
                    else
                    {
                        float r = UnityEngine.Random.Range(0f, totalW);
                        float acc = 0f;
                        for (int i = 0; i < eligible.Count; i++)
                        {
                            float w = Mathf.Max(0f, eligible[i].selectionWeight);
                            acc += w;
                            if (r <= acc)
                            {
                                chosen = eligible[i];
                                break;
                            }
                        }
                        if (chosen == null)
                            chosen = eligible[eligible.Count - 1];
                    }
                }
                else
                {
                    // Deterministic cycle through eligible parties.
                    if (_enemyPartyPoolIndex < 0) _enemyPartyPoolIndex = 0;
                    if (_enemyPartyPoolIndex >= eligible.Count) _enemyPartyPoolIndex = 0;

                    chosen = eligible[_enemyPartyPoolIndex];
                    _enemyPartyPoolIndex = (_enemyPartyPoolIndex + 1) % Mathf.Max(1, eligible.Count);
                }
            }
        }

        _activeEnemyParty = chosen;

        if (_activeEnemyParty != null && _activeEnemyParty.lootTable != null && _activeEnemyParty.lootTable.Count > 0)
            _activeLootOverride = _activeEnemyParty.lootTable;
        else
            _activeLootOverride = null;

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

        RemoveEnemyTargetIndicatorForMonster(m);

        if (monsterInfoController != null)
            monsterInfoController.HideIfShowing(m);

        RemoveEnemyIntentsForMonster(m);

        _activeMonsters.Remove(m);

        if (m.gameObject != null)
            m.gameObject.SetActive(false);

        if (_activeMonsters.Count == 0)
        {
            if (resourcePool != null)
                resourcePool.ClearAll();
            StartCoroutine(HandleEncounterVictoryRoutine());
        }
    }

    public void HandleMonsterKilled(Monster m)
    {
        if (m == null) return;

        // Prevent double-processing if something calls this twice.
        if (!_activeMonsters.Contains(m))
            return;

        // Optionally play death FX here if you want.
        // (Your RemoveMonster currently just deactivates.)
        // m.PlayDeathEffects();

        RemoveMonster(m);
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

        // Clear per-battle-only statuses so they don't persist into post-battle panels or the next encounter.
        if (_party != null)
        {
            for (int i = 0; i < _party.Count; i++)
            {
                var hs = _party[i] != null ? _party[i].stats : null;
                if (hs != null) hs.ClearStartOfBattleStatuses();
            }
        }
        ApplyPartyHiddenVisuals();

        CancelPendingAbility();

        if (stretchController != null)
            stretchController.SetEncounterActive(false);

        if (scrollingBackground != null)
            scrollingBackground.SetPaused(false);

        HeroStats goldOwner = null;
        if (_party != null && _party.Count > 0)
            goldOwner = _party[0]?.stats;

        if (goldOwner != null && _activeEnemyParty != null && _activeEnemyParty.goldReward > 0)
            goldOwner.AddGold(_activeEnemyParty.goldReward);

        if (postBattleResultsPanel != null && performanceTracker != null)
        {
            if (!postBattleResultsPanel.gameObject.activeSelf)
                postBattleResultsPanel.gameObject.SetActive(true);

            var heroes = new List<HeroStats>(_party != null ? _party.Count : 0);
            if (_party != null)
                for (int i = 0; i < _party.Count; i++)
                    if (_party[i] != null && _party[i].stats != null)
                        heroes.Add(_party[i].stats);

            long goldGained = (_activeEnemyParty != null) ? _activeEnemyParty.goldReward : 0;
            var summaries = performanceTracker.ComputeSummaries(heroes);

            bool resultsDone = false;
            postBattleResultsPanel.Show(goldGained, summaries, () =>
            {
                if (heroes != null)
                {
                    for (int hi = 0; hi < heroes.Count; hi++)
                        if (heroes[hi] != null)
                            heroes[hi].SetAllowLevelUps(true);
                }

                performanceTracker.ApplySummaries(summaries);
                resultsDone = true;
            });
            yield return new WaitUntil(() => resultsDone);

            postBattleResultsPanel.Hide();
        }

        // Ability choice (starts at level 2). Resolve AFTER reel upgrades so the hero stays consistent with existing flow.
        if (_party != null && _party.Count > 0)
        {
            if (postBattleAbilityUpgradePanel != null)
            {
                postBattleAbilityUpgradePanel.gameObject.SetActive(true);

                for (int i = 0; i < _party.Count; i++)
                {
                    HeroStats hs = _party[i] != null ? _party[i].stats : null;
                    if (hs == null) continue;

                    while (hs.HasPendingAbilityChoices)
                    {
                        Debug.Log($"[PostBattle][AbilityUpgrade] Pending choices for hero='{hs.name}' pendingCount={hs.PendingAbilityChoices} nextUnlockLevel={hs.NextPendingAbilityChoiceLevel}");

                        // If no options exist for this level (misconfigured ability data), consume it to avoid soft-lock.
                        int unlockLevel = hs.NextPendingAbilityChoiceLevel;
                        List<AbilityDefinitionSO> options = hs.GetAbilityChoiceOptionsForLevel(unlockLevel, 2);
                        if (options == null || options.Count == 0)
                        {
                            Debug.LogWarning($"[PostBattle][AbilityUpgrade] No ability options for hero='{hs.name}' unlockLevel={unlockLevel}. Consuming pending choice to avoid soft-lock.");
                            continue;
                        }

                        bool done = false;
                        Debug.Log($"[PostBattle][AbilityUpgrade] Showing panel for hero='{hs.name}' unlockLevel={unlockLevel} options={options.Count}");
                        postBattleAbilityUpgradePanel.Show(hs, () => done = true);
                        yield return new WaitUntil(() => done);
                        Debug.Log($"[PostBattle][AbilityUpgrade] Panel completed for hero='{hs.name}' unlockLevel={unlockLevel} remainingPending={hs.PendingAbilityChoices}");
                        postBattleAbilityUpgradePanel.Hide();
                    }
                }
            }
            else
            {
                // Safety: if the panel isn't wired, consume pending choices so the run can continue.
                Debug.LogWarning("[PostBattle][AbilityUpgrade] postBattleAbilityUpgradePanel is not assigned in BattleManager inspector. Skipping/consuming pending ability choices.");
                for (int i = 0; i < _party.Count; i++)
                {
                    HeroStats hs = _party[i] != null ? _party[i].stats : null;
                    if (hs == null) continue;
                }
            }
        }

        // --- Rewards choice (choose ONE): Reelforging OR Treasure Reels ---
        List<ItemOptionSO> pool =
            (_activeLootOverride != null && _activeLootOverride.Count > 0)
                ? _activeLootOverride
                : (postBattleFlow != null ? postBattleFlow.GetItemOptionPool() : null);

        RewardsTablePanel.RewardsTableChoice rewardChoice = RewardsTablePanel.RewardsTableChoice.Skip;
        int selectedReelforgeHeroIndex = -1;

        if (rewardsTablePanel != null)
        {
            if (!rewardsTablePanel.gameObject.activeSelf)
                rewardsTablePanel.gameObject.SetActive(true);

            bool chosen = false;
                        // Build HeroStats[] for the rewards table (panel works with hero stats, not PartyMemberRuntime).
            var partyStatsArr = BuildPartyStatsArray(_party);
            rewardsTablePanel.Show(partyStatsArr, (choice, heroIdx) =>
            {
                rewardChoice = choice;
                selectedReelforgeHeroIndex = heroIdx;
                chosen = true;
            });
            yield return new WaitUntil(() => chosen);

            rewardsTablePanel.Hide();
        }
        else
        {
            // If the table isn't wired, fall back to the old behavior: Treasure Reels (if enabled) else skip.
            rewardChoice = enablePostBattleRewards ? RewardsTablePanel.RewardsTableChoice.TreasureReels : RewardsTablePanel.RewardsTableChoice.Skip;
        }

        if (rewardChoice == RewardsTablePanel.RewardsTableChoice.Reelforging)
        {
            // Reelforging: grant exactly ONE reel upgrade, applied to the hero selected on the RewardsTablePanel.
            HeroStats reelforgeHero = null;

            if (_party != null && selectedReelforgeHeroIndex >= 0 && selectedReelforgeHeroIndex < _party.Count)
                reelforgeHero = _party[selectedReelforgeHeroIndex] != null ? _party[selectedReelforgeHeroIndex].stats : null;

            // Fallback (shouldn't happen if dropdown is populated correctly)
            if (reelforgeHero == null)
                reelforgeHero = GetPartyGoldReceiver();

            if (postBattleReelUpgradeMinigamePanel != null && reelforgeHero != null)
            {
                if (!postBattleReelUpgradeMinigamePanel.gameObject.activeSelf)
                    postBattleReelUpgradeMinigamePanel.gameObject.SetActive(true);

                reelforgeHero.AddPendingReelUpgrades(1);

                while (reelforgeHero.PendingReelUpgrades > 0)
                {
                    bool done = false;
                    postBattleReelUpgradeMinigamePanel.Show(reelforgeHero, () => done = true);
                    yield return new WaitUntil(() => done);
                    postBattleReelUpgradeMinigamePanel.Hide();
                }
                // IMPORTANT: The upgrade panel updates the hero's reel strip data, but the in-battle reels
                // may still be showing a cached strip. Reconfigure the ReelSpinSystem so the upgrade is visible.
                if (reelSpinSystem != null)
                {
                    var partyStats = new List<HeroStats>(_party != null ? _party.Count : 0);
                    if (_party != null)
                        for (int i = 0; i < _party.Count; i++)
                            if (_party[i]?.stats != null) partyStats.Add(_party[i].stats);

                    reelSpinSystem.ConfigureFromParty(partyStats);
                }
            }
            else
            {
                Debug.LogWarning("[PostBattle][RewardsTable] Reelforging chosen but ReelUpgradeMinigamePanel or selected hero is missing. Skipping reel upgrade.");
            }
        }
else if (rewardChoice == RewardsTablePanel.RewardsTableChoice.TreasureReels)
        {
            if (enablePostBattleRewards && postBattleChestPanel != null && pool != null && pool.Count > 0)
            {
                if (reelSpinSystem != null && _activeEnemyParty != null && _activeEnemyParty.rewardReelConfig != null)
                    reelSpinSystem.EnterRewardMode(_activeEnemyParty.rewardReelConfig, GetPartyGoldReceiver());

                bool done = false;

                int smallCount = _activeEnemyParty != null ? Mathf.Max(0, _activeEnemyParty.smallChestCount) : 0;
                int largeCount = _activeEnemyParty != null ? Mathf.Max(0, _activeEnemyParty.largeChestCount) : 0;

                postBattleChestPanel.Show(
                    GetPartyGoldReceiver(),
                    smallCount,
                    largeCount,
                    pool,
                    inventory,
                    (postBattleRewardPanel != null ? postBattleRewardPanel : startRewardPanel),
                    () => done = true
                );

                yield return new WaitUntil(() => done);

                postBattleChestPanel.Hide();

                if (reelSpinSystem != null)
                {
                    var partyStats = new List<HeroStats>(_party != null ? _party.Count : 0);
                    if (_party != null)
                        for (int i = 0; i < _party.Count; i++)
                            if (_party[i]?.stats != null) partyStats.Add(_party[i].stats);

                    reelSpinSystem.ExitRewardMode(partyStats);
                }
            }
            else
            {
                // If Treasure Reels is chosen but the panel/pool isn't wired, we simply skip rewards.
                if (enablePostBattleRewards)
                    Debug.LogWarning("[PostBattle][RewardsTable] Treasure Reels chosen but postBattleChestPanel or item pool is missing. Skipping treasure rewards.");
            }
        }

        if (postBattlePrepPanel != null)

        {
            bool cont = false;

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

            postBattlePrepPanel.Hide();
        }
        yield return null;

        _postBattleRunning = false;

        StartBattle();
    }

    private void CleanupExistingEncounter()
    {
        CleanupEnemyTargetIndicators();

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
    [SerializeField] private Sprite statusIconFocusRuneSprite;
    [SerializeField] private Sprite statusIconIgnitionSprite;
    [SerializeField] private Sprite statusIconStasisSprite;

    private void ApplyPartyHiddenVisuals()
    {
        if (_party == null) return;

        for (int i = 0; i < _party.Count; i++)
        {
            var pm = _party[i];
            if (pm == null || pm.avatarGO == null) continue;

            var hs = pm.stats;
            bool hidden = hs != null && hs.IsHidden;

            var sr = pm.avatarGO.GetComponentInChildren<SpriteRenderer>(true);
            if (sr != null)
                sr.color = hidden ? hiddenTint : Color.white;

            StatusEffectIconController statusIcon = null;

            Transform iconTf = null;

            if (hs != null)
            {
                iconTf = hs.transform.Find("_StatusIcon");
                if (iconTf == null)
                    iconTf = hs.transform.Find("__StatusIcon");
            }

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

            // If the ally prefab doesn't include a status icon anchor, create one at runtime.
            if (iconTf == null)
            {
                var go = new GameObject("_StatusIcon");
                go.transform.SetParent(pm.avatarGO.transform, false);
                iconTf = go.transform;
                iconTf.localPosition = new Vector3(0f, 1.2f, 0f);
                iconTf.localScale = Vector3.one;
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

            // Preferred: show status icons under the monster's HP bar.
            var hpBar = m.GetComponentInChildren<MonsterHpBar>(true);
            if (hpBar != null)
            {
                hpBar.ConfigureStatusSprites(statusIconBleedingSprite, 
                                             statusIconFocusRuneSprite,
                                             statusIconIgnitionSprite,
                                             statusIconStasisSprite);
                // MonsterHpBar subscribes to status changes and will refresh automatically,
                // but do an initial refresh so newly-spawned monsters show correct icons immediately.
                // (The call above already refreshes.)
                continue;
            }

            // Fallback (legacy): world-space icon above the monster.
            Transform iconTf = m.transform.Find("_StatusIcon");
            if (iconTf == null)
            {
                var go = new GameObject("_StatusIcon");
                go.transform.SetParent(m.transform, false);
                iconTf = go.transform;
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
        if (amount == 0) return;

        Vector3 jitter = new Vector3(
            UnityEngine.Random.Range(-damageNumberRandomJitter.x, damageNumberRandomJitter.x),
            UnityEngine.Random.Range(-damageNumberRandomJitter.y, damageNumberRandomJitter.y),
            UnityEngine.Random.Range(-damageNumberRandomJitter.z, damageNumberRandomJitter.z)
        );

        Vector3 spawnPos = worldPos + damageNumberWorldOffset + jitter;

        if (damageNumberPrefab != null)
        {
            DamageNumber dn = Instantiate(damageNumberPrefab);
            dn.transform.position = spawnPos;
            TrySetDamageNumberValue(dn, amount);
            return;
        }

        if (!enableRuntimeDamageNumbers)
            return;

        var go = new GameObject($"DamageNumber_{amount}");
        go.transform.position = spawnPos;

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = amount.ToString();
        tmp.fontSize = runtimeDamageNumberFontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.color = Color.white;

        var runtime = go.AddComponent<RuntimeDamageNumber>();
        runtime.Initialize(Camera.main, runtimeDamageNumberLifetime, runtimeDamageNumberRiseDistance);
    }

    private void SpawnHealNumber(Vector3 worldPos, int amount)
    {
        if (amount <= 0) return;

        Vector3 jitter = new Vector3(
            UnityEngine.Random.Range(-damageNumberRandomJitter.x, damageNumberRandomJitter.x),
            UnityEngine.Random.Range(-damageNumberRandomJitter.y, damageNumberRandomJitter.y),
            UnityEngine.Random.Range(-damageNumberRandomJitter.z, damageNumberRandomJitter.z)
        );

        Vector3 spawnPos = worldPos + damageNumberWorldOffset + jitter;
        string txt = $"+{amount}";

        if (damageNumberPrefab != null)
        {
            DamageNumber dn = Instantiate(damageNumberPrefab);
            dn.transform.position = spawnPos;

            // Best-effort: set the value via existing init methods then override the displayed text.
            TrySetDamageNumberValue(dn, amount);
            TrySetDamageNumberTextAndColor(dn, txt, Color.green);
            return;
        }

        if (!enableRuntimeDamageNumbers)
            return;

        var go = new GameObject($"HealNumber_{amount}");
        go.transform.position = spawnPos;

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = txt;
        tmp.fontSize = runtimeDamageNumberFontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.color = Color.green;

        var runtime = go.AddComponent<RuntimeDamageNumber>();
        runtime.Initialize(Camera.main, runtimeDamageNumberLifetime, runtimeDamageNumberRiseDistance);
    }

    private void SpawnHealVfx(Transform targetRoot)
    {
        if (healVfxSpawner == null || targetRoot == null)
            return;

        healVfxSpawner.PlayHealVfx(targetRoot);
    }
    private void SpawnBrVfx(Transform targetRoot)
    {
        if (healVfxSpawner == null || targetRoot == null)
            return;

        healVfxSpawner.PlayBRVfx(targetRoot);
    }

    private static float ComputeParticleLifetimeSeconds(GameObject root, float fallbackSeconds)
    {
        if (root == null) return fallbackSeconds;

        var systems = root.GetComponentsInChildren<ParticleSystem>(true);
        if (systems == null || systems.Length == 0)
            return fallbackSeconds;

        float maxEnd = 0f;
        for (int i = 0; i < systems.Length; i++)
        {
            var ps = systems[i];
            var main = ps.main;

            float duration = main.duration;

            float startDelay = 0f;
            var delay = main.startDelay;
            if (delay.mode == ParticleSystemCurveMode.Constant) startDelay = delay.constant;
            else if (delay.mode == ParticleSystemCurveMode.TwoConstants) startDelay = delay.constantMax;

            float lifetime = 0f;
            var lt = main.startLifetime;
            if (lt.mode == ParticleSystemCurveMode.Constant) lifetime = lt.constant;
            else if (lt.mode == ParticleSystemCurveMode.TwoConstants) lifetime = lt.constantMax;

            float end = startDelay + duration + lifetime;
            if (end > maxEnd) maxEnd = end;
        }

        // small padding so the fade completes
        return Mathf.Max(fallbackSeconds, maxEnd + 0.15f);
    }

    private static void TrySetDamageNumberValue(DamageNumber dn, int amount)
    {
        if (dn == null) return;

        string[] names = { "Init", "SetValue", "SetAmount", "SetNumber", "SetDamage", "Initialize", "Setup" };

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

        TMP_Text tmp = dn.GetComponent<TMP_Text>();
        if (tmp == null) tmp = dn.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null)
        {
            tmp.text = amount.ToString();
            return;
        }

        dn.gameObject.SendMessage("SetValue", amount, SendMessageOptions.DontRequireReceiver);
    }

    private static void TrySetDamageNumberTextAndColor(DamageNumber dn, string textValue, Color color)
    {
        if (dn == null) return;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        try
        {
            // Most common: private TMP_Text text;
            var f = dn.GetType().GetField("text", flags);
            if (f != null)
            {
                var tmp = f.GetValue(dn) as TMP_Text;
                if (tmp != null)
                {
                    tmp.text = textValue;
                    tmp.color = color;
                    return;
                }
            }

            // Fallback: search any TMP_Text on the object.
            var any = dn.GetComponent<TMP_Text>();
            if (any == null) any = dn.GetComponentInChildren<TMP_Text>(true);
            if (any != null)
            {
                any.text = textValue;
                any.color = color;
            }
        }
        catch
        {
            // best-effort only
        }
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

        ClearEnemyTargetPreview();
        _previewPartyTargetIndex = -1;
        HideConfirmText();
        CancelPendingAbility();

        if (resourcePool != null)
            resourcePool.SetAmounts(s.resources.attack, s.resources.defense, s.resources.magic, s.resources.wild);

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
                m.SetCurrentHp(ms.hp);
                m.SetBleedStacks(ms.bleedStacks);
                if (m.IsDead || !ms.isActive)
                    m.gameObject.SetActive(false);
            }
        }

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
                    category = ComputeIntentCategory(it.damage, it.isAoe, it.stunsTarget, it.appliesBleed),
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

        _saveStates.RemoveAt(_saveStates.Count - 1);

        BattleSaveState s = _saveStates[_saveStates.Count - 1];
        ApplySaveStateSnapshot(s);

        if (_saveStates.Count <= 1)
            SetUndoButtonEnabled(false);
    }

    private void OnDestroy()
    {
        if (reelSpinSystem != null)
        {
            reelSpinSystem.OnCurrentLandedChanged -= HandleCurrentLandedChanged;
            reelSpinSystem.OnSpinLanded -= HandleSpinLandedBattle;
        }
    }


    /// <summary>
    /// Battle-only hook: fires ONLY when a spin lands (not on Reelcraft edits).
    /// If the Fighter's own reel lands an ATK symbol on the midrow, log a debug message.
    /// Mapping is index-based: party[0] -> reel[0], etc.
    /// </summary>
    private void HandleSpinLandedBattle(ReelSpinSystem.SpinLandedInfo info)
    {
        if (reelSpinSystem == null) return;
        if (info.symbols == null || info.symbols.Count == 0) return;

        _spinResolvedAndLocked = false;
        if (logFlow) Debug.Log($"[Battle][SpinLanded] Reset _spinResolvedAndLocked=false. symbols={info.symbols.Count} A={info.attackCount} D={info.defendCount} M={info.magicCount} W={info.wildCount}", this);

        if (_party == null || _party.Count == 0) return;

        int count = Mathf.Min(_party.Count, info.symbols.Count);

        // ANY-hero checks (OR accumulate)
        bool flameSigilActive = false;
        bool waterSigilActive = false;

        for (int i = 0; i < count; i++)
        {
            var heroStats = _party[i]?.stats;
            if (heroStats == null) continue;

            flameSigilActive |= heroStats.HasAbilityUnlocked("Flame Sigil");
            waterSigilActive |= heroStats.HasAbilityUnlocked("Water Sigil");
        }

        for (int i = 0; i < count; i++)
        {
            var hero = _party[i]?.stats;
            if (hero == null) continue;

            var sym = info.symbols[i];
            if (sym == null) continue;

            if (!reelSpinSystem.TryMapSymbolPublic(sym, out var rt, out int amount))
                continue;

            if (rt == ReelSpinSystem.ResourceType.Attack)
            {
                if (hero.HasAbilityUnlocked("Battle Rhythm"))
                {
                    DimScreenTemporarily(0.5f);
                    if (healVfxSpawner != null) healVfxSpawner.PlayBRVfx(hero.transform);
                    hero.AddBonusDamageNextAttack(Mathf.Max(1, amount));
                }
            }
            else if (rt == ReelSpinSystem.ResourceType.Defend)
            {
                if (hero.HasAbilityUnlocked("Iron Guard"))
                {
                    DimScreenTemporarily(0.5f);
                    if (healVfxSpawner != null) healVfxSpawner.PlayBRVfx(hero.transform);
                    hero.AddShield(Mathf.Max(1, amount));
                }
            }
            else if (rt == ReelSpinSystem.ResourceType.Magic)
            {
                // Only do work if any sigil exists
                if (!flameSigilActive && !waterSigilActive) continue;
                if (_activeMonsters != null)
                {
                    foreach (var enemyMonster in _activeMonsters)
                    {
                        if (enemyMonster == null) continue;
                        if (!enemyMonster.HasFocusRune) continue;

                        DimScreenTemporarily(0.5f);

                        if (flameSigilActive)
                        {
                            if (healVfxSpawner != null) healVfxSpawner.PlayBRVfx(enemyMonster.transform);
                            enemyMonster.AddIgnition(1);
                        }

                        if (waterSigilActive)
                        {
                            if (healVfxSpawner != null) healVfxSpawner.PlayBRVfx(enemyMonster.transform);
                            enemyMonster.AddStasis(1);
                        }
                    }
                }
            }
        }
    }


    private void HandleCurrentLandedChanged(ReelSpinSystem.SpinLandedInfo info)
    {
        if (reelSpinSystem == null) return;
        if (info.symbols == null || info.symbols.Count == 0) return;
        if (_party == null || _party.Count == 0) return;

        var multipliers = reelSpinSystem.CurrentLandedMultipliers;

        int count = Mathf.Min(_party.Count, info.symbols.Count);

        if (logPassiveBridge)
            Debug.Log($"[Battle][PassiveBridge] CurrentLandedChanged symbols={info.symbols.Count} partyCount={_party.Count}", this);

        for (int i = 0; i < count; i++)
        {
            var hero = _party[i]?.stats;
            if (hero == null) continue;

            var sym = info.symbols[i];
            if (sym == null) continue;

            if (!reelSpinSystem.TryMapSymbolPublic(sym, out var rt, out int amount))
                continue;

            int mult = 1;
            if (multipliers != null && i < multipliers.Count)
                mult = Mathf.Max(1, multipliers[i]);

            if (logPassiveBridge)
                Debug.Log($"[Battle][PassiveBridge] SymbolLanded partyIndex={i} hero='{hero.name}' symbol='{sym.name}' type={rt} amount={amount} mult={mult}", this);

            if (rt == ReelSpinSystem.ResourceType.Attack && logPassiveBridge)
                Debug.Log($"[Battle][PassiveBridge] ATK symbol landed hero='{hero.name}' symbol='{sym.name}' amount={amount} mult={mult}", this);

            hero.NotifyReelSymbolLanded(sym, rt, amount, mult);
        }
    }

    private Coroutine _dimRoutine;

    private void DimScreenTemporarily(float duration)
    {
        if (_dimRoutine != null)
            StopCoroutine(_dimRoutine);

        _dimRoutine = StartCoroutine(DimRoutine(duration));
    }

    private IEnumerator DimRoutine(float duration)
    {
        screenDimmer.DimScreenTo(0.8f);

        yield return new WaitForSeconds(duration);

        // UNDIM
        screenDimmer.DimScreenTo(0.0f);

        _dimRoutine = null;
    }

    

    private void ConfigureReelSpinSystemCashoutHooks()
    {
        if (reelSpinSystem == null) return;

        if (logFlow) Debug.Log("[Battle][SubstitutionHook] Installing CanApplySubstitutionForReelIndex delegate.", this);

        // Gate Substitution per reel index based on each hero's unlock.
        reelSpinSystem.CanApplySubstitutionForReelIndex = (reelIndex) =>
        {
            if (logFlow) Debug.Log($"[Battle][SubstitutionHook] Query reelIndex={reelIndex} partyCount={(_party != null ? _party.Count : 0)}", this);
            if (_party == null) return false;
            if (reelIndex < 0 || reelIndex >= _party.Count) return false;
            var hero = _party[reelIndex]?.stats;
            if (hero == null) return false;
            bool unlocked = hero.HasAbilityUnlocked("Substitution");
            if (logFlow) Debug.Log($"[Battle][SubstitutionHook] reelIndex={reelIndex} hero={(hero!=null?hero.name:"null")} unlocked={unlocked}", this);
            return unlocked;
        };
    }

    private void OnStopSpinningPressed()
        {
            if (logFlow) Debug.Log($"[Battle][StopPressed] Click. _spinResolvedAndLocked={_spinResolvedAndLocked}", this);

            // Defensive: prevent re-trigger spam
            if (_spinResolvedAndLocked)
            {
                if (logFlow) Debug.Log("[Battle][StopPressed] Ignored (already locked for this spin).", this);
                return;
            }

            _spinResolvedAndLocked = true;

            // NOTE: Actual NULL->WILD substitution happens inside ReelSpinSystem.StopSpinningAndCollect(),
            // before CollectPendingPayout(), gated by CanApplySubstitutionForReelIndex.
            if (logFlow) Debug.Log("[Battle][StopPressed] Locked=true. Waiting for ReelSpinSystem cashout to apply substitution + payout.", this);
        }
    // --- Rewards / Party helpers ---
    private static HeroStats[] BuildPartyStatsArray(List<PartyMemberRuntime> party)
    {
        if (party == null || party.Count == 0) return System.Array.Empty<HeroStats>();

        var list = new List<HeroStats>(party.Count);
        for (int i = 0; i < party.Count; i++)
        {
            var hs = party[i] != null ? party[i].stats : null;
            if (hs != null) list.Add(hs);
        }
        return list.ToArray();
    }

    private HeroStats GetPartyGoldReceiver()
    {
        // Gold receiver / reward-mode owner: for now use party slot 0 if available.
        if (_party != null && _party.Count > 0 && _party[0] != null)
            return _party[0].stats;
        return null;
    }

}

////////////////////////////////////////////////////////////
