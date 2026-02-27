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

public partial class BattleManager : MonoBehaviour
{
    private const string MAGE_DART_ABILITY_NAME = "Dart";

    public  int PlayerTurnNumber;
    public static event Action PartyReady;
    public static BattleManager Instance { get; private set; }

    private bool _runStarted;
    private bool _startHasRun;

    public enum BattleState { Idle, BattleStart, PlayerPhase, EnemyPhase, BattleEnd }
    public enum PlayerActionType { None, Ability1, Ability2 }
    public enum IntentType { Attack, AoEAttack, Summon, SelfBuff }

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

    [Header("VFX / Casting Aura")]
    [Tooltip("If true, when a hero ability is selected for casting, the hero prefab can show a casting aura (via a HeroCastingAura component on the hero avatar prefab).")]
    [SerializeField] private bool enableHeroCastingAura = true;


    [Header("Ability Windup Hold")]
    [Tooltip("If true, when selecting an ability that requires a target, we immediately play the hero's cast/attack animation and freeze it at a class-scoped windup point until the target is confirmed.")]
    [SerializeField] private bool enableWindupHoldWhileTargeting = true;

    [Tooltip("Default normalized time (0..1) to freeze at when a profile override enables windup hold but does not specify a time.")]
    [SerializeField, Range(0f, 0.95f)] private float defaultWindupHoldNormalizedTime = 0.35f;


    [Header("VFX / Hit Reaction")]
    [Tooltip("If true, when a hero takes damage, we trigger a hit reaction (Animator trigger + optional white flash).")]
    [SerializeField] private bool enableHeroHitReaction = true;

    [Tooltip("Animator trigger used to play the hero flinch/hit reaction.")]
    [SerializeField] private string heroHitTriggerName = "Hit";

    [Tooltip("If true, also triggers a white flash on the hero (requires a HeroHitFlash component on the hero prefab).")]
    [SerializeField] private bool enableHeroHitFlash = true;

    [Header("Audio / Hero Hit SFX")]
    [Tooltip("Optional SFX played when a hero takes damage (on the same frame as the hit reaction).")]
    [SerializeField] private AudioClip heroHitSfx;

    [SerializeField] [Range(0f, 1f)] private float heroHitSfxVolume = 0.85f;

    [Tooltip("If true, randomizes pitch slightly for variation.")]
    [SerializeField] private bool randomizeHeroHitPitch = true;

    [SerializeField] private Vector2 heroHitPitchRange = new Vector2(0.95f, 1.05f);

    [SerializeField] private bool logHitReaction = false;

    private AudioSource _heroHitSfxSource;

    // =============================
    // Victory Jingle (Hero-specific)
    // =============================
    [Header("Audio / Victory Jingle")]
    [SerializeField] private bool playVictoryJingle = true;

    [Tooltip("Optional AudioSource to play the victory jingle. If null, one will be created at runtime.")]
    [SerializeField] private AudioSource victoryJingleSource;

    [Tooltip("Fallback jingle used if we can't determine a hero-specific clip.")]
    [SerializeField] private AudioClip defaultVictoryJingle;

    [SerializeField] [Range(0f, 1f)] private float victoryJingleVolume = 0.9f;

    [Tooltip("If true, randomizes pitch slightly for variation.")]
    [SerializeField] private bool randomizeVictoryJinglePitch = false;

    [SerializeField] private Vector2 victoryJinglePitchRange = new Vector2(0.98f, 1.02f);

    [Tooltip("Extra logging for diagnosing jingle start/stop timing.")]
    [SerializeField] private bool victoryJingleDebugLogs = true;

    // Last hero recorded as having killed a monster (used to select the victory jingle).
    private HeroStats _victoryKillerHero;



    
    // Prevent duplicate jingle playback if victory routine triggers more than once.
    private bool _victoryJinglePlayedThisEncounter;
// Tracks which hero currently has their casting aura active (while an ability is pending).
    private int _castingAuraPartyIndex = -1;






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
    
        
        public bool appliesCorrosion;
        public int corrosionIconCount;

        public bool isSummon;
        public int summonCount;
        public int maxSummonsPerBattle;


        public bool isConsume;
        public int consumeVictimInstanceId;
        public int consumeHealAmount;

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
        public bool appliesCorrosion;
        public int corrosionIconCount;

        public bool isSummon;
        public int summonCount;
        public int maxSummonsPerBattle;

        public bool isConsume;
        public int consumeVictimInstanceId;
        public int consumeHealAmount;
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




    [Tooltip("If true, the shared ResourcePool is cleared to 0 at the end of each player turn.")]
    [SerializeField] private bool clearResourcesAtEndOfPlayerTurn = false;

    [Header("Audio / Music")]
    [Tooltip("Optional audio source used for battle music. If null, BattleManager will create one at runtime.")]
    [SerializeField] private AudioSource battleMusicSource;

    [Tooltip("Music clip to play for battles (e.g., Area 1 battle theme).")]
    [SerializeField] private AudioClip battleMusicClip;

    [Range(0f, 1f)]
    [SerializeField] private float battleMusicVolume = 0.7f;

    [Tooltip("Fade in/out duration in seconds. Set to 0 for instant.")]
    [SerializeField] private float battleMusicFadeSeconds = 0.5f;

    [Tooltip("If true, music loops while the battle is active.")]
    [SerializeField] private bool loopBattleMusic = true;


    [Header("Audio / Music (Hero Stems)")]
    [Tooltip("If true, battle music will be layered from per-hero stems (ClassDefinitionSO.battleMusicStemClip) instead of a single battleMusicClip.")]
    [SerializeField] private bool useHeroBattleMusicStems = true;

    [Tooltip("Optional parent transform where hero stem AudioSources will be created. If null, BattleManager will use its own GameObject.")]
    [SerializeField] private Transform battleMusicStemRoot;

    [Tooltip("Fade out duration for a single hero stem when that hero dies.")]
    [SerializeField] private float heroStemFadeOutSeconds = 0.35f;

    [Tooltip("If true, automatically scales each stem volume down when multiple stems are active to avoid clipping.")]
    [SerializeField] private bool normalizeStemVolume = true;

private Coroutine _battleMusicFadeRoutine;


    // Hero stem music runtime
    private readonly Dictionary<HeroStats, AudioSource> _heroMusicStemSources = new Dictionary<HeroStats, AudioSource>(8);
    private readonly HashSet<HeroStats> _heroStemStoppedForDead = new HashSet<HeroStats>();
    private Coroutine _heroStemFadeAllRoutine;
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

    [Header("Spell Effect VFX")]
    [Tooltip("Prefab spawned on the target when a monster uses a spell-style ability (e.g., Consume). Should contain a SpriteRenderer+Animator and SpellEffectEntity.")]
    [SerializeField] private GameObject spellEffectPrefab;




    [Header("Hero Ability VFX")]
    [Tooltip("Prefab spawned on the target when the Mage uses Dart. Should contain a SpriteRenderer+Animator and SpellEffectEntity (same structure as monster spellEffectPrefab).")]
    [SerializeField] private GameObject mageDartEffectPrefab;

    [Tooltip("Vertical offset applied when spawning the Dart effect on the target.")]
    [SerializeField] private float mageDartEffectVerticalOffset = 0.5f;

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

    // Tracks the most recent spin's symbols list so we can avoid double-proccing when
    // OnCurrentLandedChanged fires immediately after OnSpinLanded for the same spin.
    [Header("Input / Targeting")]
    [SerializeField] private bool allowClickToSelectMonsterTarget = true;

    [Header("Info Panel Hold")]
    [Tooltip("Seconds the player must hold the mouse/touch on a monster to open the InfoPanel (prevents accidental opens when selecting targets).")]
    [SerializeField] private float infoPanelHoldSeconds = 0.35f;

    [Tooltip("Pixels the pointer can drift while holding before we cancel the hold (treat it as a drag).")]
    [SerializeField] private float infoPanelHoldMoveThresholdPx = 18f;
    [Tooltip("If true, clicking hero world sprites (their prefab colliders) can be used to select ALLY/SELF targets while an ability is awaiting a party target.")]
    [SerializeField] private bool allowClickHeroSpritesToTargetAllies = true;
    [SerializeField] private bool ignoreClicksOverUI = true;

    [Header("Undo / Confirm UI")]
    [SerializeField] private Button undoButton;
    [SerializeField] private TMP_Text confirmText;

    [Header("Monster Info UI")]
    [Tooltip("Optional. If assigned, BattleManager will populate the Monster Info panel when preview-targeting enemies.")]
    [SerializeField] private MonsterInfoController monsterInfoController;

    
    [SerializeField] private InfoPanelController infoPanelController;
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

    [Header("Enemy Spawn Limit")]
    [Tooltip("Maximum number of enemy monsters that can be active on-screen at once. Summons beyond this are queued.")]
    [SerializeField] private int maxActiveEnemiesOnScreen = 3;

    // Summoned enemies beyond the cap are queued and spawned immediately when a slot frees up.
    private readonly Queue<GameObject> _summonedEnemyQueue = new Queue<GameObject>();

    /// <summary>Raised whenever the summon queue size changes.</summary>
    public event Action<int> OnEnemySummonQueueChanged;

    public int EnemySummonQueueCount => _summonedEnemyQueue != null ? _summonedEnemyQueue.Count : 0;

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


// InfoPanel hold runtime (monster inspection)
private bool _monsterInfoHoldArmed = false;
private bool _monsterInfoHoldOpened = false;
private float _monsterInfoHoldDownTime = 0f;
private Vector3 _monsterInfoHoldDownPos;
private Monster _monsterInfoHoldCandidate;


    private Monster _previewEnemyTarget = null;

    private bool _resolving;
    private bool _impactFired;
    private bool _attackFinished;


    // Windup hold (targeting) runtime
    private Coroutine _windupHoldRoutine;
    private Animator _windupAnimator;
    private string _windupStateName;
    private int _windupActorIndex = -1;
        private bool _windupActive;
    private float _windupHeldNormalizedTime = 0f;
    private Coroutine _windupReverseRoutine;

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
        {
            reelSpinSystem.OnCurrentLandedChanged += HandleCurrentLandedChanged;
            reelSpinSystem.OnSpinLanded += HandleSpinLandedBattle;
            reelSpinSystem.OnCorrosionChanged += HandleCorrosionChanged;
        }

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
        // Cashout/Stop button is used again (end reel phase / collect payout).
        // Keep BattleManager's listener (locks ability selection) without disabling other listeners.
        if (stopSpinningButton != null)
        {
            stopSpinningButton.gameObject.SetActive(true);
            stopSpinningButton.onClick.RemoveListener(OnStopSpinningPressed);
            stopSpinningButton.onClick.AddListener(OnStopSpinningPressed);
        }
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


// Resolve CLICK + HOLD to open monster info panels.
if (_monsterInfoHoldArmed)
{
    // Released -> clear.
    if (!Input.GetMouseButton(0))
    {
        _monsterInfoHoldArmed = false;
        _monsterInfoHoldOpened = false;
        _monsterInfoHoldCandidate = null;
    }
    else
    {
        // If the player drags, cancel the hold.
        if ((Input.mousePosition - _monsterInfoHoldDownPos).sqrMagnitude > (infoPanelHoldMoveThresholdPx * infoPanelHoldMoveThresholdPx))
        {
            _monsterInfoHoldArmed = false;
            _monsterInfoHoldOpened = false;
            _monsterInfoHoldCandidate = null;
        }
        else if (!_monsterInfoHoldOpened && (Time.unscaledTime - _monsterInfoHoldDownTime) >= infoPanelHoldSeconds)
        {
            Monster m = _monsterInfoHoldCandidate;

            // Only show if it's still valid.
            if (m != null && _activeMonsters.Contains(m) && !m.IsDead && !IsInAbilityCastingState)
            {
                if (infoPanelController != null)
                {
                    string statsText = (monsterInfoController != null) ? monsterInfoController.BuildStatsForPanel(m) : null;
                    string body = string.IsNullOrWhiteSpace(statsText)
                        ? (m.Description ?? "")
                        : (statsText + " " + (m.Description ?? ""));

                    infoPanelController.ShowMonster(m, new InfoPanelData
                    {
                        title = m.DisplayName,
                        body = body,
                        image = null
                    });
                }
                else if (monsterInfoController != null)
                {
                    monsterInfoController.Show(m);
                }
            }

            _monsterInfoHoldOpened = true;
        }
    }
}

        if (!Input.GetMouseButtonDown(0))
            return;

        if (ignoreClicksOverUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        Monster clicked = TryGetClickedMonster();

        if (clicked == null)
        {
            // Allow clicking hero world sprites (their prefab colliders) to select ally/self targets.
            // This prevents "clicked elsewhere -> cancel" when the player is actually clicking the ally to heal/shield.
            int clickedPartyIndex = TryGetClickedPartyMemberIndex();
            if (clickedPartyIndex >= 0)
            {
                if (_awaitingPartyTarget)
                {
                    TryHandlePartySlotClickForPendingAbility(clickedPartyIndex);
                    return;
                }
            }

            // Clicking anything that is NOT a valid target should cancel the pending ability.
            // This covers both enemy-targeting and party-targeting abilities.
            if (_awaitingEnemyTarget || _awaitingPartyTarget)
            {
                if (logFlow) Debug.Log("[Battle][AbilityTarget] Clicked elsewhere -> cancel pending ability.", this);
                ClearEnemyTargetPreview();
                HideConfirmText();
                CancelPendingAbility();
                return;
            }

            // If we were hovering an enemy preview, clear it.
            if (_awaitingEnemyTarget)
                ClearEnemyTargetPreview();

            return;
        }
        if (!_activeMonsters.Contains(clicked) || clicked.IsDead) return;

        // If we're in the middle of casting/targeting an ability, a monster click should be treated as
        // target selection (if enabled), NOT as an info-panel request.
        if (_awaitingEnemyTarget && allowClickToSelectMonsterTarget)
        {
            SelectEnemyTarget(clicked);
            return;
        }


// Guard: do not open info panels while an ability is pending/targeting.
// InfoPanel now requires CLICK + HOLD (prevents accidental opens when selecting monsters).
if (!IsInAbilityCastingState)
{
    _monsterInfoHoldArmed = true;
    _monsterInfoHoldOpened = false;
    _monsterInfoHoldDownTime = Time.unscaledTime;
    _monsterInfoHoldDownPos = Input.mousePosition;
    _monsterInfoHoldCandidate = clicked;
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
    /// <summary>
    /// Used by animation-event receivers (e.g., AnimatorImpactEvents) to choose the correct impact SFX
    /// without requiring per-attack wiring. Returns true when the currently resolving/pending ability is considered magic.
    /// Heuristic: checks ability tags first, then falls back to element name (non-Physical/non-None treated as magic).
    /// </summary>
    public bool IsCurrentImpactMagic()
    {
        AbilityDefinitionSO a = _pendingAbility;
        if (a == null) return false;

        // 1) Tags-based check (most explicit). Works whether tags are strings or enums.
        try
        {
            if (a.tags != null)
            {
                foreach (var t in a.tags)
                {
                    if (t == null) continue;
                    string ts = t.ToString();
                    if (string.IsNullOrEmpty(ts)) continue;

                    // Explicit magic-ish tags
                    if (ts.IndexOf("magic", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    if (ts.IndexOf("spell", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    if (ts.IndexOf("arcane", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    if (ts.IndexOf("element", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    if (ts.IndexOf("holy", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;

                    // Explicit melee/physical-ish tags
                    if (ts.IndexOf("melee", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
                    if (ts.IndexOf("physical", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
                }
            }
        }
        catch { /* ignore tag iteration issues */ }

        // 2) Element-based fallback. Treat anything other than 'Physical'/'None'/'Neutral' as magic.
        string eName = (a.element != null) ? a.element.ToString() : "";
        if (string.IsNullOrEmpty(eName)) return false;

        if (eName.Equals("Physical", System.StringComparison.OrdinalIgnoreCase)) return false;
        if (eName.Equals("None", System.StringComparison.OrdinalIgnoreCase)) return false;
        if (eName.Equals("Neutral", System.StringComparison.OrdinalIgnoreCase)) return false;

        return true;
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



    // ---------------- Party Lookup / Evolution ----------------

[Header("Level 5 Evolution")]
[Tooltip("Optional mappings from a Base class to the Advanced prefab/defs used when a hero reaches Level 5. " +
         "If multiple entries match, the first match is used. If no entry matches, evolution is skipped (and the run continues).")]
[SerializeField] private List<EvolutionMapping> level5EvolutionMappings = new List<EvolutionMapping>();

[Serializable]
private class EvolutionMapping
{
    [Header("Match")]
    public ClassDefinitionSO requiredBaseClass;

    [Header("Evolve To")]
    public GameObject advancedPrefab;
    public ClassDefinitionSO advancedClassDef;
    public ReelStripSO advancedReelStripTemplate;
    public Sprite advancedPortraitOverride;
    public Sprite advancedWorldSpriteOverride;
}






    /// <summary>
    /// Swaps a party member's prefab at runtime (e.g., Fighter -> Templar) while preserving all HeroStats progress.
    /// This is called after the Level 5 reel-evolution minigame finishes.
    /// </summary>

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


    public void SetActivePartyMember(int index)
    {
        if (!IsPlayerPhase) return;
        if (!IsValidPartyIndex(index)) return;

        _activePartyIndex = index;
        OnActivePartyMemberChanged?.Invoke(_activePartyIndex);
        NotifyPartyChanged();
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

        if (clearResourcesAtEndOfPlayerTurn && resourcePool != null)
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




    private bool IsPartyDefeated()
    {
        for (int i = 0; i < PartyCount; i++)
        {
            if (_party[i] != null && !_party[i].IsDead)
                return false;
        }
        return true;
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


    // =======================
    // Monster animation cue support (Attack / Spell / Cast)
    // =======================
    private Monster.MonsterAnimCue GetMonsterAnimCueSafe(Monster enemy, int attackIndex)
    {
        if (enemy == null) return Monster.MonsterAnimCue.Attack;

        try
        {
            if (enemy.TryGetAttack(attackIndex, out Monster.MonsterAttack atk) && atk != null)
                return atk.animationCue;
        }
        catch { }

        return Monster.MonsterAnimCue.Attack;
    }

    private void PlayMonsterAnimationCue(Monster enemy, int attackIndex)
    {
        if (enemy == null) return;

        MonsterAnimationDriver animDriver = enemy.GetComponentInChildren<MonsterAnimationDriver>(true);
        if (animDriver == null) return;

        var cue = GetMonsterAnimCueSafe(enemy, attackIndex);

        switch (cue)
        {
            case Monster.MonsterAnimCue.Cast:
                animDriver.PlayCast();
                break;
            case Monster.MonsterAnimCue.Spell:
                animDriver.PlaySpell();
                break;
            default:
                animDriver.PlayAttackForAttackIndex(attackIndex);
                break;
        }
    }


    private IEnumerator EnemyLungeAttack(Monster enemy, Transform target, int attackIndex, Action applyDamage)
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

        // Optional animated monster driver (e.g., Skeleton).
        // If present, we drive walk/attack/idle via Animator while still using the existing lunge translation.
        MonsterAnimationDriver animDriver = enemy.GetComponentInChildren<MonsterAnimationDriver>(true);
        if (animDriver != null)
        {
            animDriver.PlayWalk();
        }


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

        // If this monster has an Animator-driven attack, trigger it now and (optionally) wait for an impact event.
        // If the authored attack uses Spell/Cast cues, we fire those triggers instead and skip the impact-event wait.
        if (animDriver != null)
        {
            var cue = GetMonsterAnimCueSafe(enemy, attackIndex);

            if (cue == Monster.MonsterAnimCue.Attack)
            {
                if (animDriver.waitForAttackImpactEvent) animDriver.ResetAttackImpact();
                animDriver.PlayAttackForAttackIndex(attackIndex);

                if (animDriver.waitForAttackImpactEvent)
                {
                    float elapsedImpact = 0f;
                    const float impactFailSafeSeconds = 2.0f;
                    while (!animDriver.AttackImpactFired && elapsedImpact < impactFailSafeSeconds)
                    {
                        elapsedImpact += Time.deltaTime;
                        yield return null;
                    }
                }
            }
            else
            {
                // Spell/Cast: just fire the trigger. (If you want precise timing, use animation events later.)
                PlayMonsterAnimationCue(enemy, attackIndex);
            }
        }


        
        // Sabotage: if this attack is sabotaged, the monster takes self-damage now.
        // If it dies from this self-damage, the attack is cancelled.
        if (enemy != null && !enemy.IsDead)
        {
            int selfDamage = 0;
            try { selfDamage = enemy.GetSabotageSelfDamageForAttackIndex(attackIndex); }
            catch { selfDamage = 0; }

            if (selfDamage > 0)
            {
                int dealt = 0;
                try { dealt = enemy.TakeTrueDamage(selfDamage); }
                catch { dealt = 0; }

                if (dealt > 0)
                    SpawnDamageNumber(visual.position, dealt);

                if (enemy.IsDead)
                {
                    HandleMonsterKilled(enemy);
                    yield break;
                }
            }
        }

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

        if (animDriver != null)
            animDriver.PlayIdle();
    }

    private IEnumerator StartBattleRoutine()
    {
        CleanupExistingEncounter();
        SetState(BattleState.BattleStart);

        

        _victoryKillerHero = null;
        _victoryJinglePlayedThisEncounter = false;
ResetPartyRoundFlags();
        if (reelSpinSystem != null)
        {
            reelSpinSystem.ResetBattleSubstitutionState();
            reelSpinSystem.ResetBattleCorrosionState();

            // Spins are per-battle: initialize spinsRemaining from inspector value at encounter start.
            reelSpinSystem.BeginBattle();
        }

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



    private ResourceCost GetEffectiveCost(HeroStats actor, AbilityDefinitionSO ability)
    {
        if (ability == null) return default;

        // NOTE: Costs are normally static (from the asset).
        // Some abilities have dynamic costs based on current resource amounts.
        ResourceCost c = ability.cost;

        // Heavy Strike-style: spend ALL current ATK resources when cast.
        // This keeps the behavior data-driven via the AbilityDefinitionSO flag.
        if (ability != null && (ability.spendAllAttackResources || ability.name == "Heavy Strike"))
        {
            long atk = (resourcePool != null) ? resourcePool.Attack : 0;
            c.attack = Math.Max(0L, atk);
        }

        return c;
    }

    // ============================
    // Ability Animation Key Helpers
    // ============================
    private static string GetActorClassName(HeroStats actorStats)
    {
        // Prefer the current advanced class, otherwise base class.
        try
        {
            if (actorStats != null)
            {
                if (actorStats.AdvancedClassDef != null && !string.IsNullOrWhiteSpace(actorStats.AdvancedClassDef.className))
                    return actorStats.AdvancedClassDef.className.Trim();

                if (actorStats.BaseClassDef != null && !string.IsNullOrWhiteSpace(actorStats.BaseClassDef.className))
                    return actorStats.BaseClassDef.className.Trim();
            }
        }
        catch { /* ignore */ }

        return null;
    }









    // ---------------- Casting Aura ----------------







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


    private static string ReadString(object obj, Type t, string name, string fallback)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var fi = t.GetField(name, flags);
        if (fi != null && fi.FieldType == typeof(string)) return (string)fi.GetValue(obj);
        var pi = t.GetProperty(name, flags);
        if (pi != null && pi.PropertyType == typeof(string)) return (string)pi.GetValue(obj, null);
        return fallback;
    }

    private static int FindAttackIndexById(System.Array attacksArray, string abilityId)
    {
        if (attacksArray == null) return -1;
        if (string.IsNullOrWhiteSpace(abilityId)) return -1;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        string needle = abilityId.Trim();

        for (int i = 0; i < attacksArray.Length; i++)
        {
            var atk = attacksArray.GetValue(i);
            if (atk == null) continue;

            var t = atk.GetType();
            var fi = t.GetField("id", flags);
            if (fi != null && fi.FieldType == typeof(string))
            {
                var id = (string)fi.GetValue(atk);
                if (string.Equals(id, needle, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            var pi = t.GetProperty("id", flags);
            if (pi != null && pi.PropertyType == typeof(string))
            {
                var id = (string)pi.GetValue(atk, null);
                if (string.Equals(id, needle, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }

        return -1;
    }



    
    // =======================
    // Consume support (Monsters)
    // =======================

    private Monster ChooseConsumeVictim(Monster caster, bool onlySummoned)
    {
        if (caster == null) return null;
        if (_activeMonsters == null) return null;

        Monster best = null;
        int bestHp = int.MaxValue;

        // Prefer lowest current HP (keeps behavior predictable and prevents the boss from always eating the biggest body).
        for (int i = 0; i < _activeMonsters.Count; i++)
        {
            var m = _activeMonsters[i];
            if (m == null || m == caster || m.IsDead) continue;
            if (!m.IsPawn) continue;
            if (onlySummoned && !m.isSummonedMonster) continue;

            int hp = m.CurrentHp;
            if (hp < bestHp)
            {
                bestHp = hp;
                best = m;
            }
        }

        // If onlySummoned=true and we found none, return null.
        if (best != null) return best;

        return null;
    }

// Legacy entry point kept for safety; any older call sites still compile.

[Header("BoD Spell Spawner")]
[SerializeField] private float spellEffectVerticalOffset = 0.5f;

// =======================
    // Summon support (Monsters)
    // =======================







    /// <summary>
    /// Returns the world position to use when checking whether a monster occupies a spawn point.
    /// If the monster prefab uses a child named 'CenterPoint' for alignment, we use that position;
    /// otherwise we fall back to the monster root transform position.
    /// </summary>
    private static Vector3 GetMonsterWorldPositionForSpawnOccupancy(Monster m)
    {
        if (m == null) return Vector3.zero;

        // Prefer a CenterPoint child if present.
        Transform t = m.transform;
        Transform cp = t.Find("CenterPoint");
        if (cp != null) return cp.position;

        return t.position;
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



    private void SpawnEncounterMonsters()
    {
        _activeMonsters.Clear();
        _encounterMonsters.Clear();
        _summonedEnemyQueue.Clear();
        NotifyEnemySummonQueueChanged();

        int maxSlots = (monsterSpawnPoints != null && monsterSpawnPoints.Length > 0) ? monsterSpawnPoints.Length : 1;
        maxSlots = Mathf.Clamp(maxSlots, 1, Mathf.Max(1, maxActiveEnemiesOnScreen));

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
            // Progression gating: EnemyPartyCompositionSO is eligible ONLY for a single fight number (1-based).
            // We treat the current fight number as: "number of battles already completed in this stretch" + 1.
            int fightNumber = 1;
            if (stretchController != null)
                fightNumber = Mathf.Max(1, stretchController.BattlesCompleted + 1);

            // Build eligible pool for this fight.
            List<EnemyPartyCompositionSO> eligible = new List<EnemyPartyCompositionSO>(enemyPartyPool.Count);
            for (int i = 0; i < enemyPartyPool.Count; i++)
            {
                var p = enemyPartyPool[i];
                if (p == null) continue;
                if (p.IsEligibleForFight(fightNumber))
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

                Debug.LogWarning($"[BattleManager] No EnemyPartyCompositionSO matched fightNumber={fightNumber}. Falling back to ungated pool selection.", this);
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
                // If the monster prefab defines a visual CenterPoint, align it to the spawn point (like heroes).
                if (spawn != null)
                {
                    AlignMonsterToSpawnPointUsingCenterPoint(go, spawn);
                }
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

        // If we have queued summons waiting for a slot, spawn them immediately.
        TrySpawnQueuedSummonsToFillCap();

        if (_activeMonsters.Count == 0)
        {
            // Only end the encounter if no more enemies are active AND none are waiting in the summon queue.
            if (EnemySummonQueueCount > 0)
                return;

            if (resourcePool != null)
                resourcePool.ClearAll();
            StartCoroutine(HandleEncounterVictoryRoutine());
        }
    }

    public void HandleMonsterKilled(Monster m)
    {
        if (m == null) return;

        if (!_activeMonsters.Contains(m))
            return;

        // 🔊 Play death SFX BEFORE deactivation
        var sfx = m.GetComponent<MonsterSFX>();
        if (sfx != null)
            sfx.PlayDeathSFX();

        // Optional animated death (e.g., Skeleton). If present, play the death animation before deactivating.
        var animDriver = m.GetComponentInChildren<MonsterAnimationDriver>(true);
        if (animDriver != null && animDriver.useDeathAnimation)
        {
            StartCoroutine(HandleMonsterKilledAnimatedRoutine(m, animDriver));
            return;
        }

        RemoveMonster(m);
    }

    private IEnumerator HandleMonsterKilledAnimatedRoutine(Monster m, MonsterAnimationDriver animDriver)
    {
        if (m == null)
            yield break;

        // Monster might get killed multiple times by chained effects; guard.
        if (!_activeMonsters.Contains(m))
            yield break;

        if (animDriver != null)
            animDriver.PlayDeath();

        float wait = (animDriver != null) ? Mathf.Max(0f, animDriver.deathDurationSeconds) : 0f;
        if (wait > 0f)
            yield return new WaitForSeconds(wait);

        // If the battle ended during the wait, still clean up the monster safely.
        if (_activeMonsters.Contains(m))
            RemoveMonster(m);
    }

    private IEnumerator HandleEncounterVictoryRoutine()
    {
        
        Debug.Log("[BattleManager] HandleEncounterVictoryRoutine (EVOLUTION-FIRST BUILD)", this);
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

        // Play hero-specific victory jingle (waits in realtime so it won't be cut short by pauses).
        StartCoroutine(PlayVictoryJingleRoutine());

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
                if (logFlow)
                    Debug.Log($"[PostBattle][Results] Continue clicked. heroes={(heroes != null ? heroes.Count : 0)}", this);
                if (heroes != null)
                {
                    for (int hi = 0; hi < heroes.Count; hi++)
                        if (heroes[hi] != null)
                            heroes[hi].SetAllowLevelUps(true);
                }

                performanceTracker.ApplySummaries(summaries);

                // Resolve any level-ups that were queued while leveling was disabled.
                if (heroes != null)
                {
                    for (int hi = 0; hi < heroes.Count; hi++)
                    {
                        var h = heroes[hi];
                        if (h == null) continue;
                        if (logFlow)
                            Debug.Log($"[PostBattle][Results] Before pending resolve hero='{h.name}' level={h.Level} pendingLevelUps={h.PendingLevelUps} allowLevelUps={h.AllowLevelUps}", this);
                        while (h.PendingLevelUps > 0)
                            h.SpendOnePendingLevelUp();
                        if (logFlow)
                            Debug.Log($"[PostBattle][Results] After pending resolve hero='{h.name}' level={h.Level} pendingLevelUps={h.PendingLevelUps} allowLevelUps={h.AllowLevelUps}", this);
                    }
                }
                resultsDone = true;
            });
            yield return new WaitUntil(() => resultsDone);

            postBattleResultsPanel.Hide();

        }

        // Level 5 evolution: show the evolution panel immediately after Results -> Continue,
        // then resume the normal flow (ability upgrades, rewards, etc.).
        bool evolutionShown = false;
        if (_party != null && _party.Count > 0)
        {
            Debug.Log($"[Evolution][Flow] Checking evolution panel eligibility. partyCount={_party.Count} postBattleReelUpgradeMinigamePanel={(postBattleReelUpgradeMinigamePanel != null ? postBattleReelUpgradeMinigamePanel.name : "NULL")}", this);
            if (postBattleReelUpgradeMinigamePanel != null)
            {
                for (int i = 0; i < _party.Count; i++)
                {
                    HeroStats hs = _party[i] != null ? _party[i].stats : null;
                    if (hs == null)
                    {
                        Debug.LogWarning($"[Evolution][Flow] PartyIndex={i} heroStats=NULL. Skipping.", this);
                        continue;
                    }

                    bool offer = ShouldOfferEvolutionPanel(hs);
                    Debug.Log($"[Evolution][Flow] PartyIndex={i} hero='{hs.name}' level={hs.Level} baseClass='{(hs.BaseClassDef != null ? hs.BaseClassDef.className : "NULL")}' advanced='{(hs.AdvancedClassDef != null ? hs.AdvancedClassDef.className : "NULL")}' offerPanel={offer}", this);
                    if (!offer) continue;

                    if (!postBattleReelUpgradeMinigamePanel.gameObject.activeSelf)
                        postBattleReelUpgradeMinigamePanel.gameObject.SetActive(true);

                    bool done = false;
                    Debug.Log($"[Evolution][Flow] Showing evolution panel for hero='{hs.name}' partyIndex={i}", this);
                    postBattleReelUpgradeMinigamePanel.Show(hs, () => done = true);
                    yield return new WaitUntil(() => done);
                    postBattleReelUpgradeMinigamePanel.Hide();

                    evolutionShown = true;
                }
            }
            else if (TryRunLevel5EvolutionNow())
            {
                evolutionShown = true;
            }

            if (evolutionShown)
                Debug.Log("[Evolution] Level 5 evolution complete. Resuming normal post-battle flow.", this);
        }

        // Ability choice (starts at level 2). Resolve AFTER reel upgrades so the hero stays consistent with existing flow.
        if (_party != null && _party.Count > 0)
        {
            if (evolutionShown)
            {
                Debug.Log("[PostBattle][AbilityUpgrade] Skipping ability upgrade panel because evolution was shown this battle.", this);
            }
            else if (postBattleAbilityUpgradePanel != null)
            {
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
                            hs.TryConsumeNextPendingAbilityChoiceWithoutSelection();
                            continue;
                        }

                        if (!postBattleAbilityUpgradePanel.gameObject.activeSelf)
                            postBattleAbilityUpgradePanel.gameObject.SetActive(true);

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
        _summonedEnemyQueue.Clear();
        NotifyEnemySummonQueueChanged();
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



    private int TryGetClickedPartyMemberIndex()
    {
        if (!allowClickHeroSpritesToTargetAllies)
            return -1;

        if (_mainCam == null) _mainCam = Camera.main;
        if (_mainCam == null) return -1;

        Physics.queriesHitTriggers = true;

        Vector3 world = _mainCam.ScreenToWorldPoint(Input.mousePosition);
        Vector2 p2 = new Vector2(world.x, world.y);

        // 2D colliders (common for sprite heroes)
        Collider2D[] hits2D = Physics2D.OverlapPointAll(p2);
        if (hits2D != null && hits2D.Length > 0)
        {
            for (int i = 0; i < hits2D.Length; i++)
            {
                Collider2D c = hits2D[i];
                if (c == null) continue;

                HeroStats hs = null;

                var receiver = c.GetComponentInParent<HeroTargetClickReceiver>();
                if (receiver != null) hs = receiver.HeroStats;

                if (hs == null) hs = c.GetComponentInParent<HeroStats>();
                if (hs == null) continue;

                int idx = GetPartyIndexForHeroStats(hs);
                if (IsValidPartyIndex(idx)) return idx;
            }
        }

        // 3D colliders (if you ever swap to 3D hitboxes)
        Ray ray = _mainCam.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits3D = Physics.RaycastAll(ray, 500f, ~0, QueryTriggerInteraction.Collide);
        if (hits3D != null && hits3D.Length > 0)
        {
            float best = float.MaxValue;
            int bestIdx = -1;

            for (int i = 0; i < hits3D.Length; i++)
            {
                Collider c = hits3D[i].collider;
                if (c == null) continue;

                HeroStats hs = null;

                var receiver = c.GetComponentInParent<HeroTargetClickReceiver>();
                if (receiver != null) hs = receiver.HeroStats;

                if (hs == null) hs = c.GetComponentInParent<HeroStats>();
                if (hs == null) continue;

                int idx = GetPartyIndexForHeroStats(hs);
                if (!IsValidPartyIndex(idx)) continue;

                if (hits3D[i].distance < best)
                {
                    best = hits3D[i].distance;
                    bestIdx = idx;
                }
            }

            if (bestIdx >= 0) return bestIdx;
        }

        return -1;
    }






    

// ---------------- Battle Music (Hero Stems) ----------------








private void CheckAndHandleNewlyDeadHeroesForStems()
{
    if (!useHeroBattleMusicStems) return;
    if (_party == null) return;

    for (int i = 0; i < _party.Count; i++)
    {
        var pm = _party[i];
        if (pm == null || pm.stats == null) continue;

        // We only want to stop stems once, at the moment they first become dead.
        if (pm.IsDead)
            FadeOutHeroStemIfNeeded(pm.stats);
    }
}

private void SetState(BattleState s)
    {
        if (_state == s) return;
        _state = s;

        // Battle music: start when battle begins, stop when battle ends.
        if (s == BattleState.BattleStart)
            StartBattleMusicForEncounter();
        else if (s == BattleState.BattleEnd)
            StopAllBattleMusic();
        if (s == BattleState.BattleEnd)
            Debug.Log($"[Battle] Battle ended. state={s} time={Time.time:0.00}", this);

        // Undo any per-battle reel symbol mutations (e.g., corrosion converting landed tokens to NULL).
        if (s == BattleState.BattleEnd && reelSpinSystem != null)
            reelSpinSystem.RestoreReelsAfterBattle();


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
    [SerializeField] private Sprite statusIconCorrosionSprite;
    [SerializeField] private Sprite statusIconAttackBoostSprite;
    [SerializeField] private Sprite statusIconSabotagedSprite;

    [Header("Status Icon Layout")]
    [SerializeField] private Vector3 statusIconLocalOffset = new Vector3(0f, 1.2f, 0f);
    [SerializeField] private float statusIconScale = 0.8f;
    [SerializeField] private float statusIconHorizontalSpacing = 0.35f;

    [Header("Status Stack Count Layout")]
    [SerializeField] private Vector3 statusStackTextLocalOffset = new Vector3(0.22f, -0.18f, 0f);
    [SerializeField] private float statusStackTextScale = 1.0f;
    [SerializeField] private float statusStackTextFontSize = 2.5f;




/// <summary>
/// Ensures hero status icons are shown simultaneously by maintaining one child icon GameObject per status.
/// The root is expected to be the "_StatusIcon" transform (anchored under the hero CenterPoint).
/// </summary>


/// <summary>
/// Layout hero status icons in a centered horizontal row and apply stack-count text tuning.
/// Icons are expected to be SpriteRenderer children under the _StatusIcon root.
/// The bleed stack label is expected to be a child named "Stacks" (TMP_Text) under _StatusIcon (legacy setup).
/// </summary>






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

    public void SpawnIgnitionBlastVfx(Transform targetRoot)
    {
        if (healVfxSpawner == null || targetRoot == null)
            return;

        healVfxSpawner.PlayIgnitionBlastVfx(targetRoot);
    }

    /// <summary>
    /// Repositions a newly-instantiated hero so that its CenterPoint aligns with the given spawn point.
    /// This keeps visual anchors consistent across different sprite pivots/silhouettes.
    /// </summary>
    private void AlignHeroToSpawnPointUsingCenterPoint(GameObject heroGO, Transform spawnPoint)
    {
        if (heroGO == null || spawnPoint == null) return;

        Transform root = heroGO.transform;
        HeroStats hs = heroGO.GetComponentInChildren<HeroStats>(true);

        Transform centerTf = GetHeroCenterPointTransform(hs, root);
        if (centerTf == null) return;

        Vector3 delta = spawnPoint.position - centerTf.position;
        root.position += delta;
    }


    // ---------------- MONSTER CENTERPOINT HELPERS ----------------
    // Monsters can optionally define a child transform named "CenterPoint" to represent their visual anchor.
    // When present, we align that CenterPoint to the spawn position (matching hero spawn behavior).
    private void AlignMonsterToSpawnPointUsingCenterPoint(GameObject monsterGO, Transform spawnPoint)
    {
        if (monsterGO == null || spawnPoint == null) return;
        AlignMonsterToWorldPositionUsingCenterPoint(monsterGO, spawnPoint.position);
    }

    private void AlignMonsterToWorldPositionUsingCenterPoint(GameObject monsterGO, Vector3 desiredCenterWorld)
    {
        if (monsterGO == null) return;

        Transform root = monsterGO.transform;
        Transform centerTf = GetMonsterCenterPointTransform(root);
        if (centerTf == null) return; // No CenterPoint -> spawn as usual.

        Vector3 delta = desiredCenterWorld - centerTf.position;
        root.position += delta;
    }

    private static Transform GetMonsterCenterPointTransform(Transform fallbackRoot)
    {
        if (fallbackRoot == null) return null;
        if (fallbackRoot.name == "CenterPoint") return fallbackRoot;
        return FindChildRecursive(fallbackRoot, "CenterPoint");
    }



    // ---------------- HERO CENTERPOINT HELPERS ----------------
    // Many VFX/status visuals should be anchored to a hero's sprite center,
    // not necessarily the hero GameObject's root transform.
    private static Transform GetHeroCenterPointTransform(HeroStats hs, Transform fallbackRoot)
    {
        if (hs != null && hs.CenterPointTransform != null)
            return hs.CenterPointTransform;

        if (fallbackRoot == null) return null;

        if (fallbackRoot.name == "CenterPoint")
            return fallbackRoot;

        Transform found = FindChildRecursive(fallbackRoot, "CenterPoint");
        return found != null ? found : fallbackRoot;
    }

    private static Vector3 GetHeroCenterWorldPosition(HeroStats hs, Transform fallbackRoot)
    {
        Transform t = GetHeroCenterPointTransform(hs, fallbackRoot);
        return t != null ? t.position : Vector3.zero;
    }

    private static Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null) return null;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform c = root.GetChild(i);
            if (c == null) continue;
            if (c.name == childName) return c;

            Transform nested = FindChildRecursive(c, childName);
            if (nested != null) return nested;
        }
        return null;
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





    

    

    // -----------------------------
    // Victory Jingle Helpers
    // -----------------------------


    private const string VictoryJingleChildName = "VictoryJingle_AudioSource";


/// <summary>
    /// Adds Magic resource directly to the battle resource pool (does nothing if resourcePool is missing).
    /// Used by effects like Arcane Transmutation granting an immediate MAG point.
    /// </summary>
    public void AddMagicResource(long amount)
    {
        if (amount <= 0) return;
        if (resourcePool != null)
            resourcePool.Add(0, 0, amount, 0);
    }


    private void GetActiveSigils(out bool flameSigilActive, out bool waterSigilActive)
    {
        flameSigilActive = false;
        waterSigilActive = false;

        if (_party == null || _party.Count == 0) return;

        for (int i = 0; i < _party.Count; i++)
        {
            var heroStats = _party[i]?.stats;
            if (heroStats == null) continue;

            flameSigilActive |= heroStats.HasAbilityUnlocked("Flame Sigil");
            waterSigilActive |= heroStats.HasAbilityUnlocked("Water Sigil");
        }
    }

    /// <summary>
    /// Shared implementation used by BOTH: reel-landed MAGIC procs and external MAGIC sources (eg. Transmute).
    /// Returns true if anything changed that should refresh UI.
    /// </summary>
    private bool ProcSigilsOnFocusedMonsters(bool flameSigilActive, bool waterSigilActive, string sourceTag)
    {
        if (!flameSigilActive && !waterSigilActive) return false;
        if (_activeMonsters == null || _activeMonsters.Count == 0) return false;

        bool uiDirty = false;

        // NOTE: Sigil procs can KILL monsters (Ignition/Stasis bomb), which removes them from _activeMonsters.
        // Iterate a snapshot to avoid "Collection was modified" exceptions.
        var monstersSnapshot = new List<Monster>(_activeMonsters);

        for (int mi = 0; mi < monstersSnapshot.Count; mi++)
        {
            var enemyMonster = monstersSnapshot[mi];
            if (enemyMonster == null) continue;
            if (!enemyMonster.HasFocusRune) continue;

            DimScreenTemporarily(0.5f);

            if (flameSigilActive)
            {
                int beforeIgn = enemyMonster.IgnitionStacks;
                int capIgn = enemyMonster.maxIgnitionStacks;

                // Capture transform now; VFX spawner may choose to spawn world-space for blasts so it still plays if the monster is destroyed.
                Transform enemyRoot = enemyMonster.transform;

                if (healVfxSpawner != null)
                {
                    if (beforeIgn + 1 < capIgn) healVfxSpawner.PlayBRVfx(enemyRoot);
                    else healVfxSpawner.PlayIgnitionBlastVfx(enemyRoot);
                }

                Debug.Log($"[Battle][Sigil] Flame Sigil proc ({sourceTag}) -> AddIgnition(+1) target='{enemyMonster.name}' before={beforeIgn} cap={capIgn}", this);
                bool triggerBomb = enemyMonster.AddIgnition(1);

                if (triggerBomb && healVfxSpawner != null)
                    healVfxSpawner.PlayIgnitionBlastVfx(enemyRoot);

                Debug.Log($"[Battle][Sigil] Flame Sigil done ({sourceTag}) target='{enemyMonster.name}' after={enemyMonster.IgnitionStacks} dead={enemyMonster.IsDead}", this);
                uiDirty = true;
            }

            if (waterSigilActive)
            {
                if (healVfxSpawner != null) healVfxSpawner.PlayBRVfx(enemyMonster.transform);
                int beforeSta = enemyMonster.StasisStacks;
                int capSta = enemyMonster.maxStasisStacks;
                if (logFlow) Debug.Log($"[Battle][Sigil] Water Sigil proc ({sourceTag}) -> AddStasis(+1) target='{enemyMonster.name}' before={beforeSta} cap={capSta}", this);
                enemyMonster.AddStasis(1);
                if (logFlow) Debug.Log($"[Battle][Sigil] Water Sigil done ({sourceTag}) target='{enemyMonster.name}' after={enemyMonster.StasisStacks} dead={enemyMonster.IsDead}", this);
                uiDirty = true;
            }
        }

        return uiDirty;
    }

    /// <summary>
    /// Procs all currently-implemented Sigil passives as if a MAGIC symbol had landed.
    /// (Currently: Flame Sigil + Water Sigil) and respects Focus Rune on monsters.
    ///
    /// This is intentionally public so Reelcraft effects like Arcane Transmutation can trigger sigils
    /// without requiring a reel spin/cashout.
    /// </summary>
    public void ProcSigilsFromExternalMagicSource()
    {
        GetActiveSigils(out bool flameSigilActive, out bool waterSigilActive);

        bool uiDirty = ProcSigilsOnFocusedMonsters(flameSigilActive, waterSigilActive, "external");
        if (uiDirty)
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


    /// <summary>
    /// Reel corrosion is tracked in ReelSpinSystem. When corrosion count changes, refresh party status icon visuals
    /// so the Corrosion status icon (and stacks) appears above the affected hero immediately.
    /// </summary>
    private void HandleCorrosionChanged(int partyIndex, int newCount)
    {
        // We currently render corrosion as a status icon above heroes; this refresh handles both add/remove.
        ApplyPartyHiddenVisuals();
    }


    private void OnDestroy()
    {
        if (reelSpinSystem != null)
        {
            reelSpinSystem.OnCurrentLandedChanged -= HandleCurrentLandedChanged;
            reelSpinSystem.OnSpinLanded -= HandleSpinLandedBattle;
            reelSpinSystem.OnCorrosionChanged -= HandleCorrosionChanged;
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

        // Remember the frame this spin landed so we can avoid double-proccing on the immediately-following
        // OnCurrentLandedChanged that is emitted from the same spin (same frame).
        _lastSpinLandedFrame = Time.frameCount;

        _spinResolvedAndLocked = false;
        if (logFlow) Debug.Log($"[Battle][SpinLanded] Reset _spinResolvedAndLocked=false. symbols={info.symbols.Count} A={info.attackCount} D={info.defendCount} M={info.magicCount} W={info.wildCount}", this);

        if (_party == null || _party.Count == 0) return;

        int count = Mathf.Min(_party.Count, info.symbols.Count);

        // ANY-hero checks (OR accumulate)
        bool flameSigilActive = false;
        bool waterSigilActive = false;
        bool uiDirty = false;

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
                    uiDirty = true;
                }
            }
            else if (rt == ReelSpinSystem.ResourceType.Defend)
            {
                if (hero.HasAbilityUnlocked("Iron Guard"))
                {
                    DimScreenTemporarily(0.5f);
                    if (healVfxSpawner != null) healVfxSpawner.PlayBRVfx(hero.transform);
                    hero.AddShield(Mathf.Max(1, amount));
                    uiDirty = true;
                }
            }
            else if (rt == ReelSpinSystem.ResourceType.Magic)
            {
                // MAGIC symbol landed -> proc Sigils (Flame/Water) against Focus-Rune targets.
                uiDirty |= ProcSigilsOnFocusedMonsters(flameSigilActive, waterSigilActive, "spin");
            }
        }
        if (uiDirty)
            NotifyPartyChanged();
    }


    private void HandleCurrentLandedChanged(ReelSpinSystem.SpinLandedInfo info)
    {
        if (reelSpinSystem == null) return;
        if (info.symbols == null || info.symbols.Count == 0) return;
        if (_party == null || _party.Count == 0) return;

        var multipliers = reelSpinSystem.CurrentLandedMultipliers;

        int count = Mathf.Min(_party.Count, info.symbols.Count);

        // A normal spin triggers BOTH OnSpinLanded and OnCurrentLandedChanged back-to-back in the SAME frame.
        // Reelcraft edits (nudges/pushes), momentum spins, etc. typically trigger ONLY OnCurrentLandedChanged (or do so in a later frame).
        bool fromSameSpin = (Time.frameCount == _lastSpinLandedFrame);
if (logPassiveBridge)
            Debug.Log($"[Battle][PassiveBridge] CurrentLandedChanged symbols={info.symbols.Count} partyCount={_party.Count} fromSameSpin={fromSameSpin}", this);

        // If this was a Reelcraft edit, we want battle-only passives (Battle Rhythm/Iron Guard/Sigils) to proc too.
        bool flameSigilActive = false;
        bool waterSigilActive = false;
        bool uiDirty = false;

        if (!fromSameSpin)
        {
            for (int i = 0; i < count; i++)
            {
                var heroStats = _party[i]?.stats;
                if (heroStats == null) continue;

                flameSigilActive |= heroStats.HasAbilityUnlocked("Flame Sigil");
                waterSigilActive |= heroStats.HasAbilityUnlocked("Water Sigil");
            }
        }

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

            // Always notify the hero passive system.
            hero.NotifyReelSymbolLanded(sym, rt, amount, mult);

            // For Reelcraft edits, also apply the battle-only hooks that previously only ran on OnSpinLanded.
            if (!fromSameSpin)
            {
                if (rt == ReelSpinSystem.ResourceType.Attack)
                {
                    if (hero.HasAbilityUnlocked("Battle Rhythm"))
                    {
                        DimScreenTemporarily(0.5f);
                        if (healVfxSpawner != null) healVfxSpawner.PlayBRVfx(hero.transform);
                        hero.AddBonusDamageNextAttack(Mathf.Max(1, amount));
                        uiDirty = true;
                    }
                }
                else if (rt == ReelSpinSystem.ResourceType.Defend)
                {
                    if (hero.HasAbilityUnlocked("Iron Guard"))
                    {
                        DimScreenTemporarily(0.5f);
                        if (healVfxSpawner != null) healVfxSpawner.PlayBRVfx(hero.transform);
                        hero.AddShield(Mathf.Max(1, amount));
                        uiDirty = true;
                    }
                }
                else if (rt == ReelSpinSystem.ResourceType.Magic)
                {
                    if (!flameSigilActive && !waterSigilActive) continue;
                    if (_activeMonsters != null)
                    {
                        // NOTE: Sigil procs can KILL monsters (Ignition/Stasis bomb), which removes them from _activeMonsters.
                        // Iterate a snapshot to avoid "Collection was modified" exceptions.
                        var monstersSnapshot = new List<Monster>(_activeMonsters);
                        for (int mi = 0; mi < monstersSnapshot.Count; mi++)
                        {
                            var enemyMonster = monstersSnapshot[mi];
                            if (enemyMonster == null) continue;
                            if (!enemyMonster.HasFocusRune) continue;

                            DimScreenTemporarily(0.5f);

                            if (flameSigilActive)
                            {
                                if (healVfxSpawner != null) healVfxSpawner.PlayBRVfx(enemyMonster.transform);
                                int beforeIgn = enemyMonster.IgnitionStacks;
                            int capIgn = enemyMonster.maxIgnitionStacks;
                            if (logFlow) Debug.Log($"[Battle][Sigil] Flame Sigil proc -> AddIgnition(+1) target='{enemyMonster.name}' before={beforeIgn} cap={capIgn}", this);
                            enemyMonster.AddIgnition(1);
                            if (logFlow) Debug.Log($"[Battle][Sigil] Flame Sigil done target='{enemyMonster.name}' after={enemyMonster.IgnitionStacks} dead={enemyMonster.IsDead}", this);
                                uiDirty = true;
                            }

                            if (waterSigilActive)
                            {
                                if (healVfxSpawner != null) healVfxSpawner.PlayBRVfx(enemyMonster.transform);
                                int beforeSta = enemyMonster.StasisStacks;
                            int capSta = enemyMonster.maxStasisStacks;
                            if (logFlow) Debug.Log($"[Battle][Sigil] Water Sigil proc -> AddStasis(+1) target='{enemyMonster.name}' before={beforeSta} cap={capSta}", this);
                            enemyMonster.AddStasis(1);
                            if (logFlow) Debug.Log($"[Battle][Sigil] Water Sigil done target='{enemyMonster.name}' after={enemyMonster.StasisStacks} dead={enemyMonster.IsDead}", this);
                                uiDirty = true;
                        }
                    }
                    }
                }
            }
        }
        if (uiDirty)
            NotifyPartyChanged();
    }


    private int _lastSpinLandedFrame = -1;

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

        if (logFlow) Debug.Log("[Battle][SubstitutionHook] Installing CanApplySubstitutionForReelIndex delegate (unlock-only; ReelSpinSystem gates by first cashout this battle).", this);

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

            // IMPORTANT: After the reel-phase/player-phase merge, some scenes/prefabs only wire the Stop button
            // to BattleManager (and ReelSpinSystem.stopSpinningButton may be null). In that case, clicking Stop
            // would never reach ReelSpinSystem, and Substitution (NULL->WILD) would never run.
            //
            // So: if we have a ReelSpinSystem reference, forward the click.
            if (reelSpinSystem != null)
            {
                if (logFlow) Debug.Log("[Battle][StopPressed] Forwarding Stop to ReelSpinSystem.StopSpinningAndCollect().", this);
                reelSpinSystem.StopSpinningAndCollect();
            }
        }
    // --- Rewards / Party helpers ---

    private HeroStats GetPartyGoldReceiver()
    {
        // Gold receiver / reward-mode owner: for now use party slot 0 if available.
        if (_party != null && _party.Count > 0 && _party[0] != null)
            return _party[0].stats;
        return null;
    }
    public bool IsInAbilityCastingState
    {
        get
        {
            return _resolving
                || _pendingAbility != null
                || _awaitingEnemyTarget
                || _awaitingPartyTarget;
        }
    }

    // ---------------- Combo Targeting Helpers ----------------
    private Monster GetRandomLivingEnemy(Monster exclude)
    {
        if (_activeMonsters == null || _activeMonsters.Count == 0)
            return null;

        // Build a small candidate list of living enemies.
        List<Monster> candidates = null;
        for (int i = 0; i < _activeMonsters.Count; i++)
        {
            var m = _activeMonsters[i];
            if (m == null) continue;
            if (m.IsDead) continue;
            if (!m.gameObject.activeInHierarchy) continue;
            if (exclude != null && m == exclude) continue;

            if (candidates == null) candidates = new List<Monster>(8);
            candidates.Add(m);
        }

        // If we excluded the only living enemy, fall back to allowing it.
        if (candidates == null || candidates.Count == 0)
        {
            for (int i = 0; i < _activeMonsters.Count; i++)
            {
                var m = _activeMonsters[i];
                if (m == null) continue;
                if (m.IsDead) continue;
                if (!m.gameObject.activeInHierarchy) continue;
                return m;
            }
            return null;
        }

        int pick = UnityEngine.Random.Range(0, candidates.Count);
        return candidates[pick];
    }

    // ---------------- Teleport Support ----------------


    // ============================
    // Hero Ability VFX helpers
    // ============================
    private bool IsMageDartAbility(AbilityDefinitionSO ability, HeroStats caster)
    {
        if (ability == null) return false;

        // Match both the asset name and the player-facing abilityName for safety.
        bool nameMatch =
            string.Equals(ability.abilityName, MAGE_DART_ABILITY_NAME, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ability.name, MAGE_DART_ABILITY_NAME, StringComparison.OrdinalIgnoreCase);

        if (!nameMatch) return false;

        // Optional class gate (keeps future non-mage "Dart" abilities from triggering this VFX).
        string cls = GetActorClassName(caster);
        if (!string.IsNullOrWhiteSpace(cls) &&
            cls.IndexOf("mage", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        return true;
    }

    private IEnumerator SpawnSpellEffectPrefabOnMonsterRoutine(GameObject prefab, Monster target, float verticalOffset)
    {
        if (prefab == null || target == null)
            yield break;

        Transform anchor = GetMonsterCenterPointTransform(target.transform);
        Vector3 pos = (anchor != null ? anchor.position : target.transform.position) + Vector3.up * verticalOffset;

        // Parent to the anchor if available so it follows motion.
        Transform parent = anchor != null ? anchor : null;

        GameObject go = Instantiate(prefab, pos, Quaternion.identity, parent);

        SpellEffectEntity effect = go.GetComponentInChildren<SpellEffectEntity>(true);
        if (effect == null)
        {
            // No controller; destroy with a conservative fallback so we don't leak objects.
            Destroy(go, 2.0f);
            yield break;
        }

        bool finished = false;
        effect.Play(() => finished = true);

        float elapsed = 0f;
        const float failSafeSeconds = 5.0f;
        while (!finished && elapsed < failSafeSeconds)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

}
