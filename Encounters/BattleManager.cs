// GUID: 30f201f35d336bf4d840162cd6fd1fde
////////////////////////////////////////////////////////////
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


    // Tracks which hero currently has their casting aura active (while an ability is pending).
    private int _castingAuraPartyIndex = -1;


    private void TriggerHeroHitReaction(PartyMemberRuntime pm)
    {
        if (!enableHeroHitReaction) return;
        if (pm == null) return;

        // 1) Animator flinch (optional if animator missing)
        if (pm.animator != null && !string.IsNullOrEmpty(heroHitTriggerName))
        {
            pm.animator.ResetTrigger(heroHitTriggerName); // helps if multiple hits occur quickly
            pm.animator.SetTrigger(heroHitTriggerName);
        }

        // 2) White flash (optional; requires component on hero prefab)
        if (enableHeroHitFlash && pm.avatarGO != null)
        {
            var flash = pm.avatarGO.GetComponentInChildren<HeroHitFlash>(true);
            if (flash != null)
                flash.Flash();
        }

        // 3) Hit SFX (optional)
        PlayHeroHitSfx();

        if (logHitReaction)
            Debug.Log($"[Battle][HitReaction] hero={pm.name} trigger='{heroHitTriggerName}' flash={enableHeroHitFlash}", pm.avatarGO);
    }

    private void EnsureHeroHitSfxSource()
    {
        if (_heroHitSfxSource != null) return;

        // Create a dedicated 2D audio source for hit SFX so it doesn't interfere with battle music.
        _heroHitSfxSource = gameObject.AddComponent<AudioSource>();
        _heroHitSfxSource.playOnAwake = false;
        _heroHitSfxSource.loop = false;
        _heroHitSfxSource.spatialBlend = 0f; // 2D
        _heroHitSfxSource.volume = 1f;
    }

    private void PlayHeroHitSfx()
    {
        if (heroHitSfx == null) return;

        EnsureHeroHitSfxSource();

        if (randomizeHeroHitPitch)
            _heroHitSfxSource.pitch = UnityEngine.Random.Range(heroHitPitchRange.x, heroHitPitchRange.y);
        else
            _heroHitSfxSource.pitch = 1f;

        _heroHitSfxSource.PlayOneShot(heroHitSfx, heroHitSfxVolume);
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
    
        
        public bool appliesCorrosion;
        public int corrosionIconCount;

        public bool isSummon;
        public int summonCount;
        public int maxSummonsPerBattle;


        public bool isConsume;
        public int consumeVictimInstanceId;
        public int consumeHealAmount;

    }

    private static IntentCategory ComputeIntentCategory(int damage, bool isAoe, bool stunsTarget, bool appliesBleed, bool appliesCorrosion, bool isSummon, bool isConsume)
    {
        if (isSummon) return IntentCategory.Summon;
        if (isConsume) return IntentCategory.SelfBuff;


        bool hasStatus = stunsTarget || appliesBleed || appliesCorrosion;

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

private Coroutine _battleMusicFadeRoutine;

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
        if (!IsInAbilityCastingState)
        {
            // Prefer the unified InfoPanelController (disables reels while open). Fall back to the legacy
            // MonsterInfoController if the unified panel isn't wired yet.
            if (infoPanelController != null)
            {
                string statsText = (monsterInfoController != null) ? monsterInfoController.BuildStatsForPanel(clicked) : null;
                string body = string.IsNullOrWhiteSpace(statsText)
                    ? (clicked.Description ?? "")
                    : (statsText + " " + (clicked.Description ?? ""));

                infoPanelController.ShowMonster(clicked, new InfoPanelData
                {
                    title = clicked.DisplayName,
                    body = body,
                    image = null
                });
            }
            else if (monsterInfoController != null)
            {
                monsterInfoController.Show(clicked);
            }
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

            // Align the hero so that its prefab child 'CenterPoint' sits exactly on the spawn point.
            // This makes partySpawnPoints represent the intended visual center for VFX/UI alignment.
            if (spawn != null)
            {
                AlignHeroToSpawnPointUsingCenterPoint(go, spawn);
            }

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

private bool TryRunLevel5EvolutionNow()
{
    if (_party == null || _party.Count == 0) return false;

    bool any = false;

    for (int i = 0; i < _party.Count; i++)
    {
        var pm = _party[i];
        var hs = pm != null ? pm.stats : null;
        if (hs == null)
        {
            Debug.LogWarning($"[Evolution] TryRunLevel5EvolutionNow partyIndex={i} heroStats=NULL. Skipping.", this);
            continue;
        }

        // Evolve exactly once, when the hero first reaches Level 5+ and has not yet been evolved.
        if (hs.Level < 5) continue;
        if (hs.AdvancedClassDef != null) continue;

        if (!TryGetLevel5EvolutionData(hs,
            out var advancedPrefab,
            out var advancedClassDef,
            out var advancedReelStripTemplate,
            out var advancedPortraitOverride,
            out var advancedWorldSpriteOverride))
        {
            var baseDef = hs.BaseClassDef;
            Debug.LogWarning($"[Evolution] No level5EvolutionMappings entry found for hero='{hs.name}' baseClass='{(baseDef != null ? baseDef.className : "NULL")}'. Skipping evolution.");
            continue;
        }

        var baseClassName = hs.BaseClassDef != null ? hs.BaseClassDef.className : "NULL";
        Debug.Log($"[Evolution] Level 5 evolution triggered for hero='{hs.name}' baseClass='{baseClassName}' -> advanced='{(advancedClassDef != null ? advancedClassDef.className : "NULL")}' prefab='{(advancedPrefab != null ? advancedPrefab.name : "NULL")}'.", this);

        bool ok = EvolvePartyMemberToAdvanced(
            partyIndex: i,
            advancedPrefab: advancedPrefab,
            advancedClassDef: advancedClassDef,
            advancedReelStripTemplate: advancedReelStripTemplate,
            advancedPortraitOverride: advancedPortraitOverride,
            advancedWorldSpriteOverride: advancedWorldSpriteOverride);

        if (!ok)
        {
            Debug.LogError($"[Evolution] Level 5 evolution FAILED for partyIndex={i} hero='{hs.name}'. Continuing run to avoid soft-lock.", this);
            continue;
        }

        any = true;
    }

    return any;
}

    public bool TryGetLevel5EvolutionData(
        HeroStats hero,
        out GameObject advancedPrefab,
        out ClassDefinitionSO advancedClassDef,
        out ReelStripSO advancedReelStripTemplate,
        out Sprite advancedPortraitOverride,
        out Sprite advancedWorldSpriteOverride)
    {
        advancedPrefab = null;
        advancedClassDef = null;
        advancedReelStripTemplate = null;
        advancedPortraitOverride = null;
        advancedWorldSpriteOverride = null;

        if (hero == null)
        {
            Debug.LogWarning("[Evolution][Mapping] hero NULL. Cannot resolve.", this);
            return false;
        }
        if (hero.Level < 5)
        {
            Debug.Log($"[Evolution][Mapping] hero='{hero.name}' level={hero.Level} < 5. Not eligible.", this);
            return false;
        }
        if (hero.AdvancedClassDef != null)
        {
            Debug.Log($"[Evolution][Mapping] hero='{hero.name}' already advanced='{hero.AdvancedClassDef.className}'.", this);
            return false;
        }

        var baseDef = hero.BaseClassDef;
        if (baseDef == null)
        {
            Debug.LogWarning($"[Evolution][Mapping] hero='{hero.name}' BaseClassDef=NULL.", this);
            return false;
        }

        EvolutionMapping match = null;
        if (level5EvolutionMappings != null)
        {
            for (int mi = 0; mi < level5EvolutionMappings.Count; mi++)
            {
                var m = level5EvolutionMappings[mi];
                if (m == null) continue;
                if (m.requiredBaseClass == null) continue;

                if (m.requiredBaseClass == baseDef ||
                    (m.requiredBaseClass != null && baseDef != null &&
                     string.Equals(m.requiredBaseClass.className, baseDef.className, StringComparison.OrdinalIgnoreCase)))
                {
                    match = m;
                    break;
                }
            }
        }

        if (match == null) return false;
        if (match.advancedPrefab == null)
        {
            Debug.LogWarning($"[Evolution][Mapping] hero='{hero.name}' matched base='{baseDef.className}' but advancedPrefab=NULL.", this);
            return false;
        }

        advancedPrefab = match.advancedPrefab;
        advancedClassDef = match.advancedClassDef;
        advancedReelStripTemplate = match.advancedReelStripTemplate;
        advancedPortraitOverride = match.advancedPortraitOverride;
        advancedWorldSpriteOverride = match.advancedWorldSpriteOverride;
        Debug.Log($"[Evolution][Mapping] hero='{hero.name}' base='{baseDef.className}' -> prefab='{advancedPrefab.name}' advClass='{(advancedClassDef != null ? advancedClassDef.className : "NULL")}' strip='{(advancedReelStripTemplate != null ? advancedReelStripTemplate.name : "NULL")}' portrait='{(advancedPortraitOverride != null ? advancedPortraitOverride.name : "NULL")}' worldSprite='{(advancedWorldSpriteOverride != null ? advancedWorldSpriteOverride.name : "NULL")}'", this);
        return true;
    }

    private bool ShouldOfferEvolutionPanel(HeroStats hero)
    {
        if (hero == null) return false;

        // IMPORTANT: only offer evolution when the hero actually reached the evolution threshold.
        // This prevents the evolution panel from popping for non-mapped classes and accidentally
        // running the reel-upgrade minigame early.
        if (!hero.HasPendingEvolution)
        {
            Debug.Log($"[Evolution][Gate] hero='{hero.name}' HasPendingEvolution=false -> false", this);
            return false;
        }

        if (hero.AdvancedClassDef != null)
        {
            Debug.Log($"[Evolution][Gate] hero='{hero.name}' already advanced='{hero.AdvancedClassDef.className}' -> false", this);
            return false;
        }

        bool mappingFound = TryGetLevel5EvolutionData(
            hero,
            out _,
            out _,
            out _,
            out _,
            out _);

        // Legacy fallback: allow Fighter/Ninja evolution even if the mapping list isn't wired
        // or the base class SO reference changed.
        bool legacyFighterNinjaMage =
            hero.BaseClassDef != null &&
            !string.IsNullOrEmpty(hero.BaseClassDef.className) &&
            (
                string.Equals(hero.BaseClassDef.className, "Fighter", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(hero.BaseClassDef.className, "Ninja", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(hero.BaseClassDef.className, "Mage", StringComparison.OrdinalIgnoreCase)
            );

        bool ok = mappingFound || legacyFighterNinjaMage;

        Debug.Log($"[Evolution][Gate] hero='{hero.name}' pending={hero.HasPendingEvolution} mappingFound={mappingFound} legacyFighterNinjaMage={legacyFighterNinjaMage} -> {ok}", this);
        return ok;
    }


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
        List<AbilityDefinitionSO> baseUnlocked = null;
        if (oldStats != null && oldStats.BaseClassDef != null)
            baseUnlocked = oldStats.GetUnlockedAbilitiesFromClassDef(oldStats.BaseClassDef);
        Debug.Log(
            $"[Evolution] Old hero instance='{(m.avatarGO != null ? m.avatarGO.name : "NULL")}' stats='{(oldStats != null ? oldStats.name : "NULL")}' level={(oldStats != null ? oldStats.Level : 0)}",
            this
        );
        Transform parent = (partyRoot != null) ? partyRoot : m.avatarGO.transform.parent;
        Vector3 oldCenterWorld = (oldStats != null ? oldStats.CenterPointWorldPosition : m.avatarGO.transform.position);

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

                // Align the new prefab so its CenterPoint stays where the old hero's CenterPoint was.
        // This prevents evolved prefabs with different CenterPoint local offsets from appearing shifted.
        Vector3 newCenterWorld = newStats.CenterPointWorldPosition;
        Vector3 deltaToMatchCenter = oldCenterWorld - newCenterWorld;
        if (deltaToMatchCenter.sqrMagnitude > 0.000001f)
        {
            newGo.transform.position += deltaToMatchCenter;
            Debug.Log($"[Evolution] CenterPoint align: oldCenter={oldCenterWorld} newCenter={newCenterWorld} delta={deltaToMatchCenter} -> newPos={newGo.transform.position}", this);
        }
        else
        {
            Debug.Log($"[Evolution] CenterPoint align not needed (delta ~ 0). center={newCenterWorld}", this);
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
        if (baseUnlocked != null)
        {
            for (int i = 0; i < baseUnlocked.Count; i++)
            {
                var a = baseUnlocked[i];
                if (a == null) continue;
                newStats.IsAbilityUnlocked(a);
            }
        }
        newStats.MarkEvolutionResolved();


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
            if (intent.enemy == null || intent.enemy.IsDead) continue;// Conceal/Hidden: single-target attacks miss, but AoE still hits.
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
        AbilityCastState.RaiseTargetConfirmed();
        // Resume windup animation (if it was being held).
        ResumePendingWindupHold();
        StartCoroutine(ResolvePendingAbility());
        NotifyPartyChanged();
        return true;
    }


    private void BeginPendingWindupHoldIfNeeded(PartyMemberRuntime actor, AbilityDefinitionSO ability)
    {
        if (!enableWindupHoldWhileTargeting) return;
        if (actor == null || ability == null) return;

        // Only do this for abilities that are awaiting a target.
        if (!(ability.targetType == AbilityTargetType.Enemy ||
              ability.targetType == AbilityTargetType.Ally ||
              ability.targetType == AbilityTargetType.Self))
            return;

        // Abilities that intentionally play no animation should skip.
        if (IsNoAnimAbility(ability)) return;

        Animator anim = actor.animator;
        if (anim == null && actor.avatarGO != null)
            anim = actor.avatarGO.GetComponentInChildren<Animator>(true);
        if (anim == null) return;

        var profile = anim.GetComponentInParent<CasterAnimationProfile>();
        string actorClassName = GetActorClassName(actor.stats);

        string animationKey = ability.GetAnimationKeyString();

        // Resolve animator state to play (same logic as ResolvePendingAbility, but without applying effects).
        string stateToPlay = profile != null
            ? profile.ResolveAttackState(animationKey, actorClassName, abilityNameFallback: ability.name)
            : null;

        if (string.IsNullOrWhiteSpace(stateToPlay) && !string.IsNullOrWhiteSpace(animationKey))
        {
            int hash = Animator.StringToHash(animationKey);
            if (anim.HasState(0, hash))
                stateToPlay = animationKey;
        }

        if (string.IsNullOrWhiteSpace(stateToPlay) && !string.IsNullOrWhiteSpace(actorClassName))
        {
            string classBasic = $"{actorClassName.ToLowerInvariant()}_basic_attack";
            int hash = Animator.StringToHash(classBasic);
            if (anim.HasState(0, hash))
                stateToPlay = classBasic;
        }

        if (string.IsNullOrWhiteSpace(stateToPlay))
            stateToPlay = "fighter_basic_attack";        // Windup hold is always enabled while awaiting a target (data-driven pause point still optional).
        float holdNorm = -1f;
        if (profile != null)
        {
            bool _unusedEnable;
            profile.ResolveWindupHold(animationKey, actorClassName, abilityNameFallback: ability.name, out _unusedEnable, out holdNorm);
        }
        if (holdNorm < 0f) holdNorm = defaultWindupHoldNormalizedTime;
        holdNorm = Mathf.Clamp(holdNorm, 0f, 0.95f);

        // Stop previous hold if any.
        CancelPendingWindupHold(resetAnimatorToDefault: false);

        _windupAnimator = anim;
        _windupStateName = stateToPlay;
        _windupActorIndex = _pendingActorIndex;
        _windupActive = true;

        // Play immediately, then freeze when we reach hold point.
        anim.speed = 1f;
        anim.CrossFadeInFixedTime(stateToPlay, 0.05f, 0, 0f);

        _windupHoldRoutine = StartCoroutine(WindupHoldRoutine(anim, stateToPlay, holdNorm));
    }

    private IEnumerator WindupHoldRoutine(Animator anim, string stateName, float holdNormalizedTime)
    {
        if (anim == null || string.IsNullOrWhiteSpace(stateName))
            yield break;

        int hash = Animator.StringToHash(stateName);

        // Wait until we actually enter the state (or a short timeout).
        float timeout = 0.5f;
        while (timeout > 0f)
        {
            var st = anim.GetCurrentAnimatorStateInfo(0);
            if (st.shortNameHash == hash || st.fullPathHash == hash)
                break;
            timeout -= Time.deltaTime;
            yield return null;
        }

        while (true)
        {
            if (!_windupActive) yield break;
            if (anim == null) yield break;

            var st = anim.GetCurrentAnimatorStateInfo(0);
            float t = st.normalizedTime;
            // normalizedTime can exceed 1 on looping states
            t = t - Mathf.Floor(t);

            if (t >= holdNormalizedTime)
                break;

            yield return null;
        }

        // Freeze at windup hold point.
        if (anim != null)
        {
            // Capture the exact held pose so cancel/reverse can start from THIS frame.
            var st = anim.GetCurrentAnimatorStateInfo(0);
            float t = st.normalizedTime;
            t = t - Mathf.Floor(t);
            _windupHeldNormalizedTime = Mathf.Clamp01(t);

            // Force the animator to the held frame before freezing to avoid a 1-frame overshoot.
            anim.Play(stateName, 0, _windupHeldNormalizedTime);
            anim.Update(0f);

            anim.speed = 0f;
        }
    }

    private void ResumePendingWindupHold()
    {
        if (_windupAnimator != null)
            _windupAnimator.speed = 1f;

        _windupActive = false;

        if (_windupHoldRoutine != null)
        {
            StopCoroutine(_windupHoldRoutine);
            _windupHoldRoutine = null;
        }
    }

    private void CancelPendingWindupHold(bool resetAnimatorToDefault)
    {
        _windupActive = false;

        if (_windupHoldRoutine != null)
        {
            StopCoroutine(_windupHoldRoutine);
            _windupHoldRoutine = null;
        }

        if (_windupAnimator != null)
        {
            _windupAnimator.speed = 1f;
            if (resetAnimatorToDefault)
            {
                // Rebind snaps back to default state (usually Idle) safely without requiring a state name.
                _windupAnimator.Rebind();
                _windupAnimator.Update(0f);
            }
        }

        _windupHeldNormalizedTime = 0f;

        if (_windupReverseRoutine != null)
        {
            StopCoroutine(_windupReverseRoutine);
            _windupReverseRoutine = null;
        }

        _windupAnimator = null;
        _windupStateName = null;
        _windupActorIndex = -1;
    }

    private void ReversePendingWindupToIdle()
    {
        if (_windupAnimator == null || string.IsNullOrWhiteSpace(_windupStateName))
        {
            // Nothing to reverse; just make sure we aren't stuck frozen.
            CancelPendingWindupHold(resetAnimatorToDefault: true);
            return;
        }

        // Stop the hold routine so it doesn't fight us.
        _windupActive = false;
        if (_windupHoldRoutine != null)
        {
            StopCoroutine(_windupHoldRoutine);
            _windupHoldRoutine = null;
        }

        if (_windupReverseRoutine != null)
        {
            StopCoroutine(_windupReverseRoutine);
            _windupReverseRoutine = null;
        }

        float startNorm = _windupHeldNormalizedTime;
        if (startNorm <= 0f && _windupAnimator != null)
        {
            var st = _windupAnimator.GetCurrentAnimatorStateInfo(0);
            float t = st.normalizedTime;
            t = t - Mathf.Floor(t);
            startNorm = Mathf.Clamp01(t);
        }

        Animator anim = _windupAnimator;
        string stateName = _windupStateName;

        // Clear tracking immediately; coroutine has its own copies.
        _windupAnimator = null;
        _windupStateName = null;
        _windupActorIndex = -1;
        _windupHeldNormalizedTime = 0f;

        _windupReverseRoutine = StartCoroutine(ReverseWindupToIdleRoutine_Manual(anim, stateName, startNorm));
    }

    private IEnumerator ReverseWindupToIdleRoutine_Manual(Animator anim, string stateName, float startNormalized)
    {
        if (anim == null || string.IsNullOrWhiteSpace(stateName))
            yield break;

        // Force pose at the starting point (the held frame).
        float t = Mathf.Clamp01(startNormalized);

        // Freeze time; we will drive the pose manually.
        float prevSpeed = anim.speed;
        anim.speed = 0f;

        anim.Play(stateName, 0, t);
        anim.Update(0f);

        // Estimate clip length for consistent reverse speed.
        float clipLen = 0.25f; // fallback
        try
        {
            var clips = anim.GetCurrentAnimatorClipInfo(0);
            if (clips != null && clips.Length > 0 && clips[0].clip != null)
                clipLen = Mathf.Max(0.05f, clips[0].clip.length);
        }
        catch { /* ignore */ }

        while (t > 0f)
        {
            // Step backwards in normalized time.
            t -= Time.deltaTime / clipLen;
            if (t < 0f) t = 0f;

            anim.Play(stateName, 0, t);
            anim.Update(0f);

            yield return null;
        }

        // Restore normal speed and return to default controller state (usually Idle).
        anim.speed = (prevSpeed == 0f) ? 1f : prevSpeed;
        anim.Rebind();
        anim.Update(0f);
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

        // Ensure only one hero shows the casting aura at a time.
        ClearCastingAura();


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

        // Visual feedback on the hero prefab while the ability is pending.
        SetCastingAura(actorIndex, enableHeroCastingAura);


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

            // Start windup immediately while awaiting target.
            BeginPendingWindupHoldIfNeeded(actor, ability);
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

            // Start windup immediately while awaiting target.
            BeginPendingWindupHoldIfNeeded(actor, ability);
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

        AbilityCastState.RaiseTargetConfirmed();
        // Resume windup animation (if it was being held).
        ResumePendingWindupHold();
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
                SpawnDamageNumber(GetHeroCenterWorldPosition(hs, pm.avatarGO.transform), dealt);
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

        if (_plannedIntents.Count == 0)
            PlanEnemyIntents();

        // Snapshot intents so we can safely clear the live list used for UI rendering.
        var intentsToExecute = new List<EnemyIntent>(_plannedIntents);

        // Broadcast the snapshot BEFORE clearing (some listeners depend on the planned list).
        OnEnemyIntentsPlanned?.Invoke(new List<EnemyIntent>(_plannedIntents));

        _plannedIntents.Clear();
        NotifyPartyChanged();

        Debug.Log($"[EnemyPhase] intentsToExecute.Count={intentsToExecute.Count}", this);

        for (int i = 0; i < intentsToExecute.Count; i++)
        {
            var intent = intentsToExecute[i];

            if (intent.enemy == null)
            {
                Debug.LogWarning("[EnemyPhase] intent.enemy is NULL. Skipping intent.", this);
                continue;
            }

            bool summoned = intent.enemy.isSummonedMonster;

            if (intent.enemy.IsDead)
            {
                if (summoned)
                    Debug.LogWarning($"[Summon][EXEC] Summoned enemy '{intent.enemy.name}' is dead. Skipping intent.", intent.enemy);
                continue;
            }

            if (summoned)
                Debug.Log($"[Summon][EXEC] ENTER intent[{i}] enemy={intent.enemy.name} type={intent.type} atkIdx={intent.attackIndex} target={intent.targetPartyIndex} aoe={intent.isAoe}", intent.enemy);

            // Summon intent
            if (intent.type == IntentType.Summon || intent.isSummon)
            {
                if (summoned)
                    Debug.Log($"[Summon][EXEC] (Summoner is summoned) executing SUMMON intent. enemy={intent.enemy.name} atkIdx={intent.attackIndex}", intent.enemy);
                else
                    Debug.Log($"[SUMMON][EXEC] Executing summon intent. Enemy={intent.enemy.name} atkIdx={intent.attackIndex}", intent.enemy);

                ExecuteMonsterSummonIntent(intent);
                yield return new WaitForSeconds(0.15f);
                continue;
            }

            // Consume (self-buff) intent
            if (intent.type == IntentType.SelfBuff || intent.isConsume)
            {
                yield return ExecuteMonsterConsumeIntentRoutine(intent);
                yield return new WaitForSeconds(0.15f);
                continue;
            }

            // Build target list
            List<int> targets = new List<int>();

            if (intent.isAoe)
            {
                for (int p = 0; p < PartyCount; p++)
                {
                    if (!IsValidPartyIndex(p)) continue;
                    var pm = _party[p];
                    if (pm == null || pm.stats == null || pm.IsDead) continue;
                    targets.Add(p);
                }
            }
            else
            {
                int targetIdx = intent.targetPartyIndex;

                // If invalid/dead, choose a fallback living target (DO NOT break the whole enemy phase).
                if (!IsValidPartyIndex(targetIdx) || _party[targetIdx] == null || _party[targetIdx].stats == null || _party[targetIdx].IsDead)
                    targetIdx = GetRandomLivingTargetIndex();

                if (!IsValidPartyIndex(targetIdx) || _party[targetIdx] == null || _party[targetIdx].stats == null || _party[targetIdx].IsDead)
                {
                    if (summoned)
                        Debug.LogWarning($"[Summon][EXEC] Summoned enemy '{intent.enemy.name}' had NO VALID TARGET. originalTarget={intent.targetPartyIndex}. Skipping intent.", intent.enemy);
                    else
                        Debug.LogWarning($"[EnemyPhase] Enemy '{intent.enemy.name}' had NO VALID TARGET. originalTarget={intent.targetPartyIndex}. Skipping intent.", intent.enemy);
                    continue;
                }

                targets.Add(targetIdx);
            }

            if (targets.Count == 0)
            {
                if (summoned)
                    Debug.LogWarning($"[Summon][EXEC] Summoned enemy '{intent.enemy.name}' resolved zero targets. Skipping.", intent.enemy);
                continue;
            }

            // Choose a lunge target transform (use the first target)
            Transform lungeTarget = null;
            var firstHero = _party[targets[0]];
            if (firstHero != null && firstHero.animator != null)
                lungeTarget = firstHero.animator.transform;
            else if (firstHero != null && firstHero.avatarGO != null)
                lungeTarget = firstHero.avatarGO.transform;

            if (lungeTarget == null)
            {
                if (summoned)
                    Debug.LogWarning($"[Summon][EXEC] Summoned enemy '{intent.enemy.name}' has null lunge target transform. Skipping intent.", intent.enemy);
                else
                    Debug.LogWarning($"[EnemyPhase] Enemy '{intent.enemy.name}' has null lunge target transform. Skipping intent.", intent.enemy);
                continue;
            }

            if (summoned)
                Debug.Log($"[Summon][EXEC] START ATTACK enemy={intent.enemy.name} targets={targets.Count}", intent.enemy);

                        // Do the enemy lunge animation, then apply results.
            // Resolve (passive): queue reel spins for heroes that are attacked by this intent (can't yield inside callback).
            var resolveSpinQueue = new List<int>();

            yield return EnemyLungeAttack(intent.enemy, lungeTarget, intent.attackIndex, () =>
            {
                if (summoned)
                    Debug.Log($"[Summon][APPLY] enemy={intent.enemy.name} applying effects to {targets.Count} targets", intent.enemy);

                for (int t = 0; t < targets.Count; t++)
                {
                    int partyIndex = targets[t];
                    if (!IsValidPartyIndex(partyIndex)) continue;

                    var heroPm = _party[partyIndex];
                    if (heroPm == null || heroPm.stats == null || heroPm.IsDead) continue;

                    var hs = heroPm.stats;

                    // Conceal/Hidden: single-target attacks miss; AoE still hits.
                    if (hs.IsHidden && !intent.isAoe)
                    {
                        if (summoned)
                            Debug.Log($"[Summon][APPLY] enemy={intent.enemy.name} MISSED hidden hero partyIndex={partyIndex} hero={hs.name}", hs);
                        continue;
                    }

                    int raw = intent.damage > 0 ? intent.damage : intent.enemy.GetDamage();
                    raw = Mathf.Max(0, raw);

                    if (summoned)
                        Debug.Log($"[Summon][APPLY] enemy={intent.enemy.name} -> hero={hs.name} rawDamage={raw} bleed={intent.appliesBleed} stun={intent.stunsTarget} corrosion={intent.appliesCorrosion}", hs);

                    if (raw > 0)
                    {
                        hs.TakeDamage(raw);
                        TriggerHeroHitReaction(heroPm);
                    }

                    if (intent.appliesBleed && intent.bleedStacks > 0)
                        ApplyBleedStacksToHero(hs, intent.bleedStacks);

                    if (intent.stunsTarget && intent.stunPlayerPhases > 0)
                        hs.StunForNextPlayerPhases(intent.stunPlayerPhases);

                    if (intent.appliesCorrosion && intent.corrosionIconCount > 0 && reelSpinSystem != null)
                    {
                        for (int c = 0; c < intent.corrosionIconCount; c++)
                            reelSpinSystem.ApplyCorrosionToReel(partyIndex);
                    }

                    // Resolve (passive): whenever this hero is attacked by an enemy intent, spin their reel once.
                    if (reelSpinSystem != null && hs.HasAbilityUnlocked("Resolve"))
                    {
                        if (!resolveSpinQueue.Contains(partyIndex))
                            resolveSpinQueue.Add(partyIndex);

                        if (logFlow) Debug.Log($"[Battle][ResolvePassive] Queued Resolve spin. target={hs.name} partyIndex={partyIndex}", hs);
                    }
                }
            });

            // Execute queued Resolve spins AFTER the lunge + damage application completes.
            if (reelSpinSystem != null && resolveSpinQueue.Count > 0)
            {
                for (int r = 0; r < resolveSpinQueue.Count; r++)
                {
                    int ri = resolveSpinQueue[r];
                    if (!IsValidPartyIndex(ri)) continue;

                    var pm = _party[ri];
                    if (pm == null || pm.stats == null || pm.IsDead) continue;

                    yield return StartCoroutine(reelSpinSystem.MomentumSpinAndInstantCollect(ri));
                }
            }
if (summoned)
                Debug.Log($"[Summon][EXEC] FINISHED intent enemy={intent.enemy.name}", intent.enemy);

            // Small pacing delay so multiple enemies don’t feel instantaneous
            yield return new WaitForSeconds(0.12f);

            if (_state == BattleState.BattleEnd) yield break;
            if (IsPartyDefeated())
            {
                Debug.Log("[BattleManager] Party defeated (enemy phase).", this);
                SetState(BattleState.BattleEnd);
                yield break;
            }
        }

        // Plan next-turn intents so the player sees them during the upcoming PlayerPhase.
        // This also ensures newly-summoned monsters get an intent immediately.
        if (_state != BattleState.BattleEnd)
        {
            PlanEnemyIntents();
            Debug.Log($"[EnemyPhase] Planned next-turn intents. count={_plannedIntents.Count}", this);
            OnEnemyIntentsPlanned?.Invoke(new List<EnemyIntent>(_plannedIntents));
            NotifyPartyChanged();
        }

        _enemyTurnRoutine = null;
        if (_state != BattleState.BattleEnd)
            SetState(BattleState.PlayerPhase);
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

        int bonusDamageFromSpentAtk = 0;
        if (ability != null && (ability.spendAllAttackResources || ability.name == "Heavy Strike"))
        {
            // Cost.attack was set to current ResourcePool ATK in GetEffectiveCost().
            long spentAtk = cost.attack;
            // Clamp to int range for damage math.
            long rawBonus = spentAtk * (long)Mathf.Max(0, ability.bonusDamagePerAttackResource);
            if (rawBonus > int.MaxValue) rawBonus = int.MaxValue;
            bonusDamageFromSpentAtk = (int)rawBonus;
            if (logFlow) Debug.Log($"[Battle][HeavyStrike] spendAllAttackResources=true spentAtk={spentAtk} bonusPerAtk={ability.bonusDamagePerAttackResource} bonusDamage={bonusDamageFromSpentAtk}", this);
        }
        // Spend resources (special-case: spend ALL ATK for abilities like Heavy Strike).
        // ResourcePool.TrySpend may treat WILD as a flexible payment source; for "spend all ATK" we must force ATK to zero.
        bool isHeavyStrike = (ability != null) && (ability.spendAllAttackResources || ability.name == "Heavy Strike");
        long heavyStrikeSpentAtk = 0;

        if (isHeavyStrike)
        {
            if (resourcePool == null)
            {
                Debug.Log($"[Battle][HeavyStrike][Cancel] Missing resourcePool.", this);
                CancelPendingAbility();
                yield break;
            }

            long atkBefore = resourcePool.Attack;
            long defBefore = resourcePool.Defense;
            long magBefore = resourcePool.Magic;
            long wildBefore = resourcePool.Wild;

            heavyStrikeSpentAtk = Math.Max(0L, atkBefore);
            if (heavyStrikeSpentAtk <= 0)
            {
                Debug.Log($"[Battle][HeavyStrike][Cancel] No ATK to spend. attack={atkBefore}", this);
                CancelPendingAbility();
                yield break;
            }

            // Force ATK to 0 up-front so it cannot be paid via WILD or left partially unspent.
            resourcePool.SetAmounts(0, defBefore, magBefore, wildBefore);

            // Spend remaining costs (with attack cost zeroed so we don't double-spend).
            var remainingCost = cost;
            remainingCost.attack = 0;

            if (!resourcePool.TrySpend(remainingCost))
            {
                // Revert if spending the remaining cost fails.
                resourcePool.SetAmounts(atkBefore, defBefore, magBefore, wildBefore);
                Debug.Log($"[Battle][HeavyStrike][Cancel] Could not pay remainingCost={remainingCost}. Reverted resources.", this);
                CancelPendingAbility();
                yield break;
            }

            Debug.Log($"[Battle][HeavyStrike][Spend] spentAtk={heavyStrikeSpentAtk} bonusPerAtk={ability.bonusDamagePerAttackResource} bonusDamage={bonusDamageFromSpentAtk} poolAfter(atk={resourcePool.Attack},def={resourcePool.Defense},mag={resourcePool.Magic},wild={resourcePool.Wild})", this);
        }
        else
        {
            if (resourcePool == null || !resourcePool.TrySpend(cost))
            {
                if (logFlow) Debug.Log($"[Battle][Resolve] Cancel: insufficient resources or missing resourcePool. cost={cost}", this);
                CancelPendingAbility();
                yield break;
            }
        }
// Mark once-per-turn ability usage only after the cast is truly committed (cost successfully spent).
        actorStats.RegisterAbilityUsedThisTurn(ability);

        if (logFlow) Debug.Log($"[Battle][Resolve] Resources spent. cost={cost}. Proceeding to apply ability effects.", this);
        _resolving = true;

        // ============================
        // Combo (chaining): handled during damage application so each cast can spin and potentially queue more casts.
        // ============================

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
            // OPTION B (preferred): drive animation from a stable Ability "animation key" instead of the
            // player-facing ability name. This scales cleanly as more classes share abilities.
            //
            // - If the AbilityDefinitionSO has a field/property named "animationKey" (case-insensitive), we'll use it.
            // - Otherwise we fall back to legacy behavior using ability.name/ability.abilityName.
            // - The CasterAnimationProfile can optionally scope a mapping to a className.

            string actorClassName = GetActorClassName(actorStats);
            // Prefer the explicit animation key on the ability asset.
            // Leave blank to fall back to legacy name-based mapping.
            string animationKey = (ability != null)
                ? ability.GetAnimationKeyString()
                : null;

            // Some abilities intentionally play no cast animation.
            if (IsNoAnimAbility(ability))
            {
                useImpactSync = false;
                stateToPlay = null;
                if (logFlow) Debug.Log($"[Battle][Resolve] {ability.abilityName}: no animation and no impact sync.", this);
            }
            else
            {
                stateToPlay = profile != null
                    ? profile.ResolveAttackState(animationKey, actorClassName, abilityNameFallback: ability.name)
                    : null;

                // If a mapping wasn't found but the ability explicitly provided an animationKey,
                // try playing a state with the same name directly. This prevents a missing
                // CasterAnimationProfile mapping from silently falling back to a basic attack.
                if (string.IsNullOrWhiteSpace(stateToPlay) && !string.IsNullOrWhiteSpace(animationKey))
                {
                    int hash = Animator.StringToHash(animationKey);
                    if (anim.HasState(0, hash))
                    {
                        stateToPlay = animationKey;
                        if (logFlow) Debug.Log($"[Battle][Resolve] No profile mapping for animationKey='{animationKey}', but Animator has a state with that name. Using it directly.", this);
                    }
                    else
                    {
                        if (logFlow) Debug.LogWarning($"[Battle][Resolve] No profile mapping for animationKey='{animationKey}', and Animator does not have a state named '{animationKey}'. Falling back.", this);
                    }
                }

                // Next, prefer a class-scoped basic attack instead of always fighter_basic_attack.
                if (string.IsNullOrWhiteSpace(stateToPlay) && !string.IsNullOrWhiteSpace(actorClassName))
                {
                    string classBasic = $"{actorClassName.ToLowerInvariant()}_basic_attack";
                    int hash = Animator.StringToHash(classBasic);
                    if (anim.HasState(0, hash))
                        stateToPlay = classBasic;
                }

                // If we still didn't find anything, retain the prior default behavior.
                if (string.IsNullOrWhiteSpace(stateToPlay))
                    stateToPlay = "fighter_basic_attack";

                useImpactSync = true;
            }

            // If this is a heal/shield targeting Self/Ally, default to syncing the effect
            // to the impact event (if the animation clip has one).
            if ((ability.targetType == AbilityTargetType.Self || ability.targetType == AbilityTargetType.Ally) &&
                (ability.healAmount > 0 || ability.shieldAmount > 0))
            {
                useImpactSync = true;
            }

	        // For combo enemy-target abilities, we play the attack animation PER CAST inside the combo loop.
	        // This avoids the first cast playing once here and then subsequent casts having no animation.
	        bool deferAttackAnimToComboLoop = (ability != null && ability.hasCombo && ability.targetType == AbilityTargetType.Enemy);
	        if (!deferAttackAnimToComboLoop && !string.IsNullOrWhiteSpace(stateToPlay))
	        {
	            if (logFlow) Debug.Log($"[Battle][Resolve] Playing animation state '{stateToPlay}'. useImpactSync={useImpactSync}", this);

	            // If we already started this exact state during target selection (windup hold),
	            // do NOT restart it from time=0 on cast; just continue from the held frame.
	            bool startedDuringTargeting =
	                (_windupAnimator == anim) &&
	                (_windupActorIndex == _pendingActorIndex) &&
	                string.Equals(_windupStateName, stateToPlay, StringComparison.Ordinal);

	            if (startedDuringTargeting)
	            {
	                if (logFlow) Debug.Log($"[Battle][Resolve] Windup hold already started state '{stateToPlay}' during targeting. Continuing without restart.", this);
	                anim.speed = 1f; // ensure unfrozen
	                // Clear windup tracking now that we're committing the cast.
	                CancelPendingWindupHold(resetAnimatorToDefault: false);
	            }
	            else
	            {
	                anim.Play(stateToPlay, 0, 0f);
	            }
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
	            // Wait for impact sync for enemy-target abilities too (even if non-damaging).
	            // For combo abilities, impact sync is handled per-cast inside the combo loop.
	            if (useImpactSync && anim != null && !(ability != null && ability.hasCombo))
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

            // Taunt: force this enemy to target the casting fighter on its next intent, and immediately
            // update any already-planned intent for this enemy so UI + execution reflect the new target.
            if (IsTauntAbility(ability))
            {
                // Force this enemy to target the casting hero (typically the Fighter) immediately.
                int tauntCasterIndex = _pendingActorIndex;
                // _party is a list of PartyMemberRuntime, so we must locate the index by matching stats.
                if (tauntCasterIndex < 0 && actorStats != null && _party != null)
                {
                    for (int i = 0; i < _party.Count; i++)
                    {
                        if (_party[i] != null && _party[i].stats == actorStats)
                        {
                            tauntCasterIndex = i;
                            break;
                        }
                    }
                }

                if (tauntCasterIndex >= 0)
                {
                    enemyTarget.SetForcedTargetPartyIndex(tauntCasterIndex);

                    // If intents were already planned for the upcoming enemy phase, retarget them now
                    // so the UI updates immediately (player sees the taunt right away).
                    if (_plannedIntents.Count == 0)
                        PlanEnemyIntents();

                    RetargetPlannedIntentsForEnemy(enemyTarget, tauntCasterIndex);

                    // Broadcast updated intents for UI listeners.
                    OnEnemyIntentsPlanned?.Invoke(new List<EnemyIntent>(_plannedIntents));
                }

                // Taunt also grants the caster block (shield) even though this is an enemy-target ability.
                if (ability.shieldAmount > 0 && actorStats != null)
                {
                    if (logFlow) Debug.Log($"[Battle][Taunt] Granting block to caster. amount={ability.shieldAmount} caster={actorStats.name} shieldBefore={actorStats.Shield}", this);
                    actorStats.AddShield(ability.shieldAmount);
                    if (logFlow) Debug.Log($"[Battle][Taunt] Block granted. caster={actorStats.name} shieldAfter={actorStats.Shield}", this);
                }

                NotifyPartyChanged();
            }

            // Non-damaging abilities (isDamaging == false) should NEVER apply any damage by default.
            // This makes utility abilities like Taunt/Focus Rune safe even if the caster has high Attack.
            bool doesDamage = (ability != null && ability.isDamaging);

            if (!doesDamage && logFlow)
                Debug.Log($"[Battle][Resolve] Non-damaging ability -> skipping damage application. ability={ability.abilityName}", this);

            int shownDamage = 0;
            int dealt = 0;
            int totalBaseDamage = 0;

            if (doesDamage)
            {
                // Consume "next attack" bonus damage ONCE for the whole ability.
                int passiveBonusOnce = (actorStats != null) ? actorStats.ConsumeBonusDamageNextAttackIfDamaging(ability) : 0;

                // Combo chaining: each cast performs its own bonus one-reel spin (does NOT consume SpinsRemaining).
                // If the spin lands on the trigger type, we queue additional casts based on the resource gain amount.
                // This can chain until a max total cast cap is reached.

                int maxTotalCasts = 1;
                if (ability != null && ability.hasCombo)
                {
                    maxTotalCasts = (ability.comboMaxTotalCasts > 0)
                        ? ability.comboMaxTotalCasts
                        : (1 + Mathf.Max(0, ability.comboMaxExtraCasts));
                }

                int castsRemaining = 1;
                int castsExecuted = 0;

                // Current target can change during combo chaining if the ability requests random retargets.
                Monster currentTarget = enemyTarget;
                bool randomizeNextTarget = false;

                while (castsRemaining > 0)
                {
                    int hitIndex = castsExecuted;
                    castsRemaining--;

	                    // Play the attack animation for EACH combo cast (including the first).
	                    // Restart from time=0 so repeated casts don't get ignored by the Animator.
	                    if (ability != null && ability.hasCombo && anim != null && !string.IsNullOrWhiteSpace(stateToPlay))
	                    {
	                        _impactFired = false;
	                        if (logFlow) Debug.Log($"[Battle][Combo] Playing per-cast animation '{stateToPlay}' hitIndex={hitIndex}.", this);
	                        anim.Play(stateToPlay, 0, 0f);

	                        // Give Animator a frame to evaluate transitions/state.
	                        yield return null;

	                        if (useImpactSync)
	                        {
	                            float elapsed = 0f;
	                            const float failSafeSeconds = 2.0f;
	                            while (!_impactFired && elapsed < failSafeSeconds)
	                            {
	                                elapsed += Time.deltaTime;
	                                yield return null;
	                            }
	                        }
	                    }

                    // Ensure we always have a valid target when chaining.
                    if (currentTarget == null || currentTarget.IsDead)
                        currentTarget = GetRandomLivingEnemy(exclude: null);
                    if (currentTarget == null || currentTarget.IsDead)
                        break;

                    // Each cast's combo spin (including the first cast).
                    if (ability != null && ability.hasCombo && reelSpinSystem != null)
                    {
                        float speedMult = Mathf.Clamp(
                            ability.comboSpinSpeedMultiplierStart + ability.comboSpinSpeedMultiplierStep * hitIndex,
                            0.1f,
                            Mathf.Max(0.1f, ability.comboSpinSpeedMultiplierMax));

                        yield return StartCoroutine(reelSpinSystem.MomentumSpinAndInstantCollect(_pendingActorIndex, speedMult));

                        var spin = reelSpinSystem.LastInstantSpinResult;
                        if (spin.valid && actorStats != null)
                        {
                            // Ensure symbol-landed passives fire for this bonus spin.
                            actorStats.NotifyReelSymbolLanded(spin.symbol, spin.resourceType, spin.amount, spin.multiplier);

                            // Chain: landing on trigger type queues additional casts based on the gained amount.
                            if (spin.resourceType == ability.comboTriggerType)
                            {
                                int extra = Mathf.Max(0, spin.total);
                                if (extra > 0)
                                {
                                    // Cap to max total casts.
                                    int remainingCap = Mathf.Max(0, maxTotalCasts - (castsExecuted + 1) - castsRemaining);
                                    if (remainingCap > 0)
                                        castsRemaining += Mathf.Min(extra, remainingCap);
                                }

                                // If requested, randomize the NEXT target whenever the trigger lands.
                                if (ability.comboRandomizeNextEnemyTargetOnTrigger)
                                    randomizeNextTarget = true;
                            }
                            else
                            {
                                randomizeNextTarget = false;
                            }
                        }
                    }

                    // First cast gets one-time bonuses (spent-ATK bonus, next-attack passive bonus).
                    int passiveBonusThisHit = (castsExecuted == 0) ? passiveBonusOnce : 0;
                    int spentAtkBonusThisHit = (castsExecuted == 0) ? bonusDamageFromSpentAtk : 0;

                    totalBaseDamage =
                        Mathf.Max(0, actorStats.Attack) +
                        Mathf.Max(0, ability.baseDamage) +
                        Mathf.Max(0, passiveBonusThisHit) +
                        Mathf.Max(0, spentAtkBonusThisHit);

                    // Damage numbers should show computed formula damage, not clamped HP lost.
                    var target = currentTarget;

                    shownDamage = target.CalculateDamageFromAbility(
                        abilityBaseDamage: totalBaseDamage,
                        classAttackModifier: 1f,
                        element: ability.element,
                        abilityTags: ability.tags);

                    if (isHeavyStrike)
                    {
                        Debug.Log($"[Battle][HeavyStrike][Damage] caster={actorStats.name} target={(enemyTarget!=null?enemyTarget.name:"<null>")} spentAtk={heavyStrikeSpentAtk} bonusDamage={spentAtkBonusThisHit} totalBaseDamage={totalBaseDamage} shownDamage={shownDamage}", this);
                    }

                    dealt = target.TakeDamageFromAbility(
                        abilityBaseDamage: totalBaseDamage,
                        classAttackModifier: 1f,
                        element: ability.element,
                        abilityTags: ability.tags);

                    if (debugEnemyHpBarDrop && target != null)
                    {
                        Debug.Log($"[Battle][HpBarDrop] After TakeDamageFromAbility target={target.name} dealt={dealt} hpNow={target.CurrentHp}/{target.MaxHp} instance={target.GetInstanceID()}", this);

                        var hpBar = target.GetComponentInChildren<MonsterHpBar>(true);
                        if (hpBar == null)
                        {
                            Debug.LogWarning($"[Battle][HpBarDrop] No MonsterHpBar found under target={target.name} instance={target.GetInstanceID()}", this);
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
                        SpawnDamageNumber(target.transform.position, shownDamage);

                    // Optional monster reaction animations (hit/block) for Animator-driven monsters.
                    var enemyAnim = target != null ? target.GetComponentInChildren<MonsterAnimationDriver>(true) : null;
                    if (enemyAnim != null && !target.IsDead)
                    {
                        if (shownDamage <= 0 || dealt <= 0)
                            enemyAnim.PlayBlock();
                        else
                            enemyAnim.PlayHit();
                    }

                    actorStats.ApplyOnHitEffectsTo(target);

                    if (totalBaseDamage > 0)
                        actorStats.RegisterDamageAttackCommitted();

                    // Bloodlust (passive): whenever this hero deals damage, spin ONLY their reel once and instantly collect that reel's payout.
                    // Uses the same "momentum" spin helper (does not consume spinsRemaining and does not touch normal pending payout state).
                    if (dealt > 0 && actorStats != null && actorStats.HasAbilityUnlocked("Bloodlust") && reelSpinSystem != null)
                    {
                        if (logFlow) Debug.Log($"[Battle][Bloodlust] Triggered. caster={actorStats.name} dealt={dealt} -> reelIndex={_pendingActorIndex}", this);
                        yield return StartCoroutine(reelSpinSystem.MomentumSpinAndInstantCollect(_pendingActorIndex));
                    }

                    // If the enemy died from this hit, handle death once and stop applying further hits.
                    if (target != null && target.IsDead)
                    {
                        int xpAward = (target != null) ? target.XpReward : 5;
                        if (performanceTracker != null)
                            performanceTracker.RecordBaseXpGained(actorStats, xpAward);
                        else
                            actorStats.GainXP(xpAward);

                        // Momentum: if this ability killed the enemy, immediately spin ONLY the caster's reel once and cash it out.
                        if (ability != null && ability.momentumOnKill && reelSpinSystem != null)
                            yield return StartCoroutine(reelSpinSystem.MomentumSpinAndInstantCollect(_pendingActorIndex));

                        HandleMonsterKilled(target);

                        // If we still have casts remaining, pick a new living target and continue.
                        if (castsRemaining > 0 && castsExecuted + 1 < maxTotalCasts)
                        {
                            currentTarget = GetRandomLivingEnemy(exclude: null);
                            if (currentTarget == null) break;
                        }
                        else
                        {
                            break;
                        }
                    }

                    // Apply requested random retargeting for the NEXT cast when the trigger lands.
                    if (randomizeNextTarget && castsRemaining > 0)
                    {
                        currentTarget = GetRandomLivingEnemy(exclude: currentTarget);
                        randomizeNextTarget = false;
                    }
                    castsExecuted++;

                    // Safety: stop if we reached max total casts.
                    if (castsExecuted >= maxTotalCasts)
                        break;

                    // NOTE: Combo chains are bounded by castsRemaining/maxTotalCasts,
                    // so we don't need an additional "handledDeath" early-exit here.
                } // end combo-casts loop
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

                

                // Momentum: if this ability killed the enemy, immediately spin ONLY the caster's reel once and cash it out.
                if (ability != null && ability.momentumOnKill && reelSpinSystem != null)
                    yield return StartCoroutine(reelSpinSystem.MomentumSpinAndInstantCollect(_pendingActorIndex));
                    
                HandleMonsterKilled(enemyTarget);
            }
        }


        // ============================
        // Sabotage (Enemy Ability Debuff)
        // ============================
        // If configured, pick a random enemy attack and mark it sabotaged for the rest of the battle.
        // Whenever the enemy uses that attack, it takes self-damage equal to current sabotage stacks.
        if (ability != null && ability.targetType == AbilityTargetType.Enemy)
        {
            bool doSabotage = false;
            int stacksToApply = 0;
            try { doSabotage = ability.inflictsSabotage; stacksToApply = ability.sabotageStacks; }
            catch { doSabotage = false; stacksToApply = 0; }

            if (doSabotage && enemyTarget != null && !enemyTarget.IsDead)
            {
                int stacks = Mathf.Max(1, stacksToApply);
                int chosenIdx = enemyTarget.ApplySabotageToRandomAttack(stacks);
                if (logFlow)
                    Debug.Log($"[Battle][Sabotage] Applied to monster='{enemyTarget.name}' +{stacks} stacks. chosenAttackIndex={chosenIdx} totalStacks={enemyTarget.SabotageStacks}", this);
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
                    Vector3 pos = GetHeroCenterWorldPosition(targetStats, targetGO != null ? targetGO.transform : (targetStats != null ? targetStats.transform : null));
                    SpawnHealNumber(pos, healed);
                    SpawnHealVfx(GetHeroCenterPointTransform(targetStats, targetStats != null ? targetStats.transform : null));
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

    private static bool IsNoAnimAbility(AbilityDefinitionSO ability)
    {
        if (ability == null) return false;
        string n = null;
        try { n = string.IsNullOrWhiteSpace(ability.name) ? ability.abilityName : ability.name; } catch { n = null; }
        if (string.IsNullOrWhiteSpace(n)) return false;
        n = n.Trim();

        // These are intentionally “instant” (no cast animation / no impact sync).
        return string.Equals(n, "Conceal", StringComparison.OrdinalIgnoreCase)
            || string.Equals(n, "Block", StringComparison.OrdinalIgnoreCase)
            || string.Equals(n, "Aegis", StringComparison.OrdinalIgnoreCase);
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

        int previewBonusFromSpentAtk = 0;
        // Heavy Strike preview: include bonus damage based on CURRENT ATK in the pool (without spending it).
        // This mirrors ResolvePendingAbility logic where spend-all-ATK is forced and bonusDamageFromSpentAtk is added into totalBaseDamage.
        bool previewSpendAllAtk = false;
        try
        {
            previewSpendAllAtk = (_pendingAbility != null && (_pendingAbility.spendAllAttackResources || string.Equals(_pendingAbility.name, "Heavy Strike", StringComparison.OrdinalIgnoreCase)));
        }
        catch { previewSpendAllAtk = false; }

        if (previewSpendAllAtk && resourcePool != null && _pendingAbility != null)
        {
            long atkInPool = Math.Max(0L, resourcePool.Attack);
            int perAtk = Mathf.Max(0, _pendingAbility.bonusDamagePerAttackResource);
            long raw = atkInPool * (long)perAtk;
            if (raw > int.MaxValue) raw = int.MaxValue;
            previewBonusFromSpentAtk = (int)raw;
        }

        int totalBaseDamage =
            Mathf.Max(0, actor.stats.Attack) +
            Mathf.Max(0, _pendingAbility.baseDamage) +
            Mathf.Max(0, previewPassiveBonus) +
            Mathf.Max(0, previewBonusFromSpentAtk);

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

        // Turn off any active casting aura before we wipe pending indices.
        ClearCastingAura();

        // If the player cancels while targeting, play the caster windup back in reverse to idle.
        ReversePendingWindupToIdle();

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

    // ---------------- Casting Aura ----------------
    private void SetCastingAura(int partyIndex, bool enabled)
    {
        if (!enableHeroCastingAura) return;
        if (!IsValidPartyIndex(partyIndex)) return;

        PartyMemberRuntime pm = _party[partyIndex];
        if (pm == null || pm.avatarGO == null) return;

        // Prefer a component on the avatar root; fallback to children.
        var aura = pm.avatarGO.GetComponent<HeroCastingAura>();
        if (aura == null)
            aura = pm.avatarGO.GetComponentInChildren<HeroCastingAura>(true);

        if (aura == null)
        {
            if (logFlow) Debug.Log($"[Battle][Aura] No HeroCastingAura found on avatarGO for partyIndex={partyIndex} ({pm.avatarGO.name}).", this);
            return;
        }

        if (enabled)
        {
            _castingAuraPartyIndex = partyIndex;
            aura.BeginCasting();
        }
        else
        {
            aura.EndCasting();
            if (_castingAuraPartyIndex == partyIndex)
                _castingAuraPartyIndex = -1;
        }
    }

    private void ClearCastingAura()
    {
        if (_castingAuraPartyIndex < 0) return;
        SetCastingAura(_castingAuraPartyIndex, false);
        _castingAuraPartyIndex = -1;
    }


    private static bool IsTauntAbility(AbilityDefinitionSO a)
    {
        if (a == null) return false;
        // Support both the display field and the ScriptableObject asset name.
        return string.Equals(a.abilityName, "Taunt", System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(a.name, "Taunt", System.StringComparison.OrdinalIgnoreCase);
    }

    private void RetargetPlannedIntentsForEnemy(Monster enemy, int newTargetPartyIndex)
    {
        if (enemy == null) return;
        if (_plannedIntents == null || _plannedIntents.Count == 0) return;

        for (int i = 0; i < _plannedIntents.Count; i++)
        {
            var intent = _plannedIntents[i];
            if (intent.enemy != enemy) continue;

            // Only meaningful for single-target attack intents.
            if (intent.isAoe) continue;
            if (intent.type == IntentType.Summon || intent.type == IntentType.SelfBuff) continue;

            intent.targetPartyIndex = newTargetPartyIndex;
            _plannedIntents[i] = intent;
        }
    }

    private void PlanEnemyIntents()
    {
        _plannedIntents.Clear();

        for (int i = 0; i < _activeMonsters.Count; i++)
        {
            Monster m = _activeMonsters[i];
            if (m == null || m.IsDead) continue;

            int targetIdx = -1;

            // Taunt support: if the monster has a forced target, use it (if alive) and clear it immediately.
            if (m != null && m.TryGetForcedTargetPartyIndex(out int forcedIdx))
            {
                bool validForced = IsValidPartyIndex(forcedIdx) && _party != null && forcedIdx < _party.Count && _party[forcedIdx] != null && !_party[forcedIdx].IsDead;
                if (validForced)
                    targetIdx = forcedIdx;

                // One-shot: clear regardless so stale taunts don't persist.
                m.ClearForcedTargetPartyIndex();
            }

            if (targetIdx < 0)
                targetIdx = GetRandomLivingTargetIndex();

            if (targetIdx < 0) continue;

            ChooseMonsterAttackForIntent(m,
                out int attackIndex,
                out int damage,
                out bool isAoe,
                out bool stunsTarget,
                out int stunPlayerPhases,
                out bool appliesBleed,
                out int bleedStacks,
                out bool appliesCorrosion,
                out int corrosionIconCount,
                out bool isSummon,
                out int summonCount,
                out int maxSummonsPerBattle,
                out bool isConsume);
            Debug.Log(
                $"[SUMMON][PLAN] Monster={m.name} " +
                $"atkIdx={attackIndex} isSummon={isSummon} " +
                $"summonCount={summonCount} maxPerBattle={maxSummonsPerBattle}",
                m
            );

            _plannedIntents.Add(new EnemyIntent
            {
                type = isSummon ? IntentType.Summon : (isConsume ? IntentType.SelfBuff : (isAoe ? IntentType.AoEAttack : IntentType.Attack)),
                category = ComputeIntentCategory(damage, isAoe, stunsTarget, appliesBleed, appliesCorrosion, isSummon, isConsume),
                enemy = m,
                targetPartyIndex = isConsume ? -1 : targetIdx,

                attackIndex = attackIndex,
                damage = damage,
                isAoe = isAoe,

                stunsTarget = stunsTarget,
                stunPlayerPhases = stunPlayerPhases,

                appliesBleed = appliesBleed,
                bleedStacks = bleedStacks,

                appliesCorrosion = appliesCorrosion,
                corrosionIconCount = corrosionIconCount,

                isSummon = isSummon,
                summonCount = summonCount,
                maxSummonsPerBattle = maxSummonsPerBattle,

                isConsume = isConsume,
                consumeVictimInstanceId = 0,
                consumeHealAmount = 0
            });



        }

        OnEnemyIntentsPlanned?.Invoke(new List<EnemyIntent>(_plannedIntents));
        NotifyPartyChanged();
        Debug.Log($"[SUMMON][PLAN] PlanEnemyIntents END. _plannedIntents.Count={_plannedIntents.Count}", this);

    }

    private void ChooseMonsterAttackForIntent(Monster m,
        out int attackIndex,
        out int damage,
        out bool isAoe,
        out bool stunsTarget,
        out int stunPlayerPhases,
        out bool appliesBleed,
        out int bleedStacks,
        out bool appliesCorrosion,
        out int corrosionIconCount,
        out bool isSummon,
        out int summonCount,
        out int maxSummonsPerBattle,
        out bool isConsume)
    {
        attackIndex = -1;
        damage = 0;
        isAoe = false;
        stunsTarget = false;
        stunPlayerPhases = 1;
        appliesBleed = false;
        bleedStacks = 0;
        appliesCorrosion = false;
        corrosionIconCount = 1;

        isSummon = false;
        summonCount = 1;
        maxSummonsPerBattle = 1;

        isConsume = false;

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

        // Pick an attack. If we roll a summon attack that has no remaining uses, re-roll a few times.
        object atk = null;
        Type atkType = null;

        const int MAX_REROLL_ATTEMPTS = 8;
        int attempts = 0;

        while (attempts < MAX_REROLL_ATTEMPTS)
        {
            attempts++;
            attackIndex = UnityEngine.Random.Range(0, count);
            atk = attacksArray.GetValue(attackIndex);
            if (atk == null) continue;

            atkType = atk.GetType();

            bool candidateIsSummon = ReadBool(atk, atkType, "isSummon", false);
            bool candidateIsConsume = ReadBool(atk, atkType, "isConsume", false);

            // Sacrifice gating:
            // If the rolled ability requires a Pawn sacrifice but there are no Pawn allies available,
            // use the authored backupAbilityId if provided; otherwise reroll.
            bool candidateIsSacrifice = ReadBool(atk, atkType, "isSacrifice", false) || candidateIsConsume;
            if (candidateIsSacrifice)
            {
                bool onlySummoned = candidateIsConsume ? ReadBool(atk, atkType, "consumeOnlySummoned", true) : false;
                if (!HasEligiblePawnSacrificeTarget(m, onlySummoned))
                {
                    string backupId = ReadString(atk, atkType, "backupAbilityId", "");
                    int backupIdx = FindAttackIndexById(attacksArray, backupId);

                    if (backupIdx >= 0)
                    {
                        var backupAtk = attacksArray.GetValue(backupIdx);
                        if (backupAtk != null)
                        {
                            var backupType = backupAtk.GetType();
                            bool backupIsSummon = ReadBool(backupAtk, backupType, "isSummon", false);

                            // If the backup is a summon, ensure it has remaining uses.
                            if (!backupIsSummon || m.CanUseSummonAttack(backupIdx, ReadInt(backupAtk, backupType, "maxSummonsPerBattle", 1)))
                            {
                                attackIndex = backupIdx;
                                atk = backupAtk;
                                atkType = backupType;
                                break;
                            }
                        }
                    }

                    // No valid backup found -> reroll
                    atk = null;
                    atkType = null;
                    continue;
                }
            }

            if (!candidateIsSummon)
                break;

            int candidateMax = ReadInt(atk, atkType, "maxSummonsPerBattle", 1);
            if (m.CanUseSummonAttack(attackIndex, candidateMax))
                break;

            atk = null;
            atkType = null;
        }

        if (atk == null || atkType == null) return;
        damage = ReadInt(atk, atkType, "damage", 0);
        isAoe = ReadBool(atk, atkType, "isAoe", false);

        stunsTarget = ReadBool(atk, atkType, "stunsTarget", false);
        stunPlayerPhases = Mathf.Max(1, ReadInt(atk, atkType, "stunPlayerPhases", 1));

        appliesBleed = ReadBool(atk, atkType, "appliesBleed", false);
        if (!appliesBleed) appliesBleed = ReadBool(atk, atkType, "bleedsTarget", false);

        bleedStacks = Mathf.Max(0, ReadInt(atk, atkType, "bleedStacks", 0));
        if (bleedStacks == 0) bleedStacks = Mathf.Max(0, ReadInt(atk, atkType, "bleedAmount", 0));

        appliesCorrosion = ReadBool(atk, atkType, "appliesCorrosion", false);
        if (!appliesCorrosion) appliesCorrosion = ReadBool(atk, atkType, "corrodesReel", false);

        corrosionIconCount = Mathf.Max(1, ReadInt(atk, atkType, "corrosionIconCount", 1));

        // Summon support (optional attack behavior).
        isSummon = ReadBool(atk, atkType, "isSummon", false);
        isConsume = ReadBool(atk, atkType, "isConsume", false);
        summonCount = Mathf.Max(1, ReadInt(atk, atkType, "summonCount", 1));
        maxSummonsPerBattle = ReadInt(atk, atkType, "maxSummonsPerBattle", 1);

        if (isSummon)
        {
            // Summon attacks don't deal damage by default; they are their own intent category.
            damage = 0;
            isAoe = false;

            stunsTarget = false;
            stunPlayerPhases = 1;
            appliesBleed = false;
            bleedStacks = 0;
            appliesCorrosion = false;
            corrosionIconCount = 1;
            Debug.Log(
                $"[SUMMON][CHOOSE] Monster={m.name} selected SUMMON attack " +
                $"atkIdx={attackIndex} count={summonCount} max={maxSummonsPerBattle}",
                m
            );
        }

        // Consume support (optional attack behavior).
        if (isConsume)
        {
            // Consume is a self-buff; it does not deal damage directly.
            damage = 0;
            isAoe = false;

            stunsTarget = false;
            stunPlayerPhases = 1;
            appliesBleed = false;
            bleedStacks = 0;
            appliesCorrosion = false;
            corrosionIconCount = 1;

            // Ensure this isn't treated as a summon.
            isSummon = false;
            summonCount = 0;
            maxSummonsPerBattle = 0;
        }

        if (corrosionIconCount == 1) corrosionIconCount = Mathf.Max(1, ReadInt(atk, atkType, "corrosionCount", 1));
        if (corrosionIconCount == 1) corrosionIconCount = Mathf.Max(1, ReadInt(atk, atkType, "corrodeCount", 1));
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
    private bool HasEligiblePawnSacrificeTarget(Monster caster, bool onlySummoned)
    {
        if (caster == null) return false;
        if (_activeMonsters == null) return false;

        for (int i = 0; i < _activeMonsters.Count; i++)
        {
            var m = _activeMonsters[i];
            if (m == null || m == caster || m.IsDead) continue;
            if (!m.IsPawn) continue;
            if (onlySummoned && !m.isSummonedMonster) continue;
            return true;
        }
        return false;
    }

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
private IEnumerator ExecuteMonsterConsumeIntentRoutine(EnemyIntent intent)
{
    if (intent.enemy == null) yield break;

    // Pull authored consume settings from the attack definition.
    bool onlySummoned = true;
    float mult = 1f;
    bool canOverheal = false;

    if (intent.enemy.TryGetAttack(intent.attackIndex, out var atk) && atk != null)
    {
        onlySummoned = atk.consumeOnlySummoned;
        mult = Mathf.Max(0f, atk.consumeHealMultiplier);
        canOverheal = atk.consumeCanOverheal;
    }

    Monster victim = ChooseConsumeVictim(intent.enemy, onlySummoned);
    if (victim == null)
        yield break;

    // VISUALS:
    // - Caster plays CAST (not Spell).
    // - A separate SpellEffect prefab spawns on the victim and plays SPELL.
    // - Gameplay effects resolve AFTER the spell visual completes.
    MonsterAnimationDriver casterAnim = intent.enemy.GetComponentInChildren<MonsterAnimationDriver>(true);
    if (casterAnim != null)
    {
        casterAnim.ResetCastRelease();
        casterAnim.PlayCast();

        // Prefer an animation event for release timing (cast->spell handoff). Falls back to a short delay.
        if (casterAnim.waitForCastReleaseEvent)
        {
            float elapsed = 0f;
            const float failSafeSeconds = 2.0f;
            while (!casterAnim.CastReleaseFired && elapsed < failSafeSeconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }
        else
        {
            // Small delay so the Cast trigger is visually perceived before the effect spawns.
            yield return new WaitForSeconds(0.1f);
        }
    }
    else
    {
        // No animation driver; keep legacy behavior for gameplay, and just delay slightly for readability.
        yield return new WaitForSeconds(0.05f);
    }

    // Spawn Spell effect on the victim (CenterPoint if present).
    yield return SpawnSpellEffectOnTargetRoutine(victim);

    // GAMEPLAY RESOLUTION (unchanged from prior behavior)
    int healAmount = Mathf.RoundToInt(victim.MaxHp * mult);

    // Kill the victim (treat as lethal damage).
    int lethalIncoming = victim.CurrentHp + Mathf.Max(0, victim.Defense) + 9999;
    victim.TakeDamage(lethalIncoming);

    if (victim.IsDead)
        HandleMonsterKilled(victim);

    // Heal the caster.
    intent.enemy.Heal(healAmount, canOverheal);

    NotifyPartyChanged();
}

// Legacy entry point kept for safety; any older call sites still compile.
private void ExecuteMonsterConsumeIntent(EnemyIntent intent)
{
    StartCoroutine(ExecuteMonsterConsumeIntentRoutine(intent));
}

[Header("BoD Spell Spawner")]
[SerializeField] private float spellEffectVerticalOffset = 0.5f;
private IEnumerator SpawnSpellEffectOnTargetRoutine(Monster target)
{
    if (spellEffectPrefab == null || target == null)
        yield break;

    Transform anchor = GetMonsterCenterPointTransform(target.transform);
    Vector3 pos = (anchor != null ? anchor.position : target.transform.position) + Vector3.up * spellEffectVerticalOffset;

    // Parent to the anchor if available so it follows motion.
    Transform parent = anchor != null ? anchor : null;

    GameObject go = Instantiate(spellEffectPrefab, pos, Quaternion.identity, parent);

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

// =======================
    // Summon support (Monsters)
    // =======================
    private void ExecuteMonsterSummonIntent(EnemyIntent intent)
    {
        if (intent.enemy == null) return;

        if (!TryGetSummonAttackData(intent.enemy, intent.attackIndex, out GameObject prefab, out int count, out int maxPerBattle))
            return;

        if (!intent.enemy.CanUseSummonAttack(intent.attackIndex, maxPerBattle))
            return;

        // NOTE:
        // Summon intents bypass the normal "lunge + attack" execution path (which triggers animations).
        // Fire the authored animation cue (Attack/Spell/Cast) here so Summon attacks can animate.
        PlayMonsterAnimationCue(intent.enemy, intent.attackIndex);

        int spawnCount = Mathf.Max(1, count);

        for (int i = 0; i < spawnCount; i++)
        {
            Debug.Log(
                $"[SUMMON][SPAWN] Spawning {spawnCount} monster(s) " +
                $"for {intent.enemy.name}",
                intent.enemy
            );

            if (_activeMonsters.Count >= Mathf.Max(1, maxActiveEnemiesOnScreen))
            {
                EnqueueSummonedEnemy(prefab);
                continue;
            }

            SpawnSummonedEnemy(prefab);
        }

        intent.enemy.RegisterSummonAttackUse(intent.attackIndex);
        NotifyPartyChanged();
    }

    private void EnqueueSummonedEnemy(GameObject prefab)
    {
        if (prefab == null) return;
        _summonedEnemyQueue.Enqueue(prefab);
        NotifyEnemySummonQueueChanged();
        Debug.Log($"[SUMMON][QUEUE] Enqueued summon prefab='{prefab.name}'. queueCount={_summonedEnemyQueue.Count}", this);
    }

    private void TrySpawnQueuedSummonsToFillCap()
    {
        int cap = Mathf.Max(1, maxActiveEnemiesOnScreen);
        bool spawnedAny = false;

        while (_activeMonsters.Count < cap && _summonedEnemyQueue.Count > 0)
        {
            GameObject prefab = _summonedEnemyQueue.Dequeue();
            NotifyEnemySummonQueueChanged();

            if (prefab == null) continue;

            Debug.Log($"[SUMMON][QUEUE] Dequeued summon prefab='{prefab.name}'. remaining={_summonedEnemyQueue.Count}", this);
            SpawnSummonedEnemy(prefab);
            spawnedAny = true;
        }

        if (spawnedAny)
            NotifyPartyChanged();
    }

    private void SpawnSummonedEnemy(GameObject prefab)
    {
        if (prefab == null) return;

        Vector3 pos = GetSummonSpawnPosition();
        GameObject go = Instantiate(prefab, pos, Quaternion.identity);
        // If the monster prefab defines a visual CenterPoint, align it to the intended summon position.
        AlignMonsterToWorldPositionUsingCenterPoint(go, pos);

        Monster summoned = go.GetComponentInChildren<Monster>(true);
        if (summoned == null)
        {
            Debug.LogWarning($"[BattleManager][Summon] Summon prefab '{prefab.name}' did not have a Monster component in children.", this);
            Destroy(go);
            return;
        }

        summoned.gameObject.SetActive(true);
        summoned.ResetSummonTrackingForBattle();

        summoned.isSummonedMonster = true;
        Debug.Log($"[SUMMON][SPAWN] Marked summoned monster as isSummonedMonster=true name={summoned.name}", summoned);
        _activeMonsters.Add(summoned);
        if (!_encounterMonsters.Contains(summoned)) _encounterMonsters.Add(summoned);

        // If the summon spawns dead for some reason, remove it.
        if (summoned.IsDead)
        {
            summoned.gameObject.SetActive(false);
            _activeMonsters.Remove(summoned);
        }
    }

    private void NotifyEnemySummonQueueChanged()
    {
        OnEnemySummonQueueChanged?.Invoke(EnemySummonQueueCount);
    }

    private bool TryGetSummonAttackData(Monster m, int attackIndex, out GameObject summonPrefab, out int summonCount, out int maxSummonsPerBattle)
    {
        Debug.Log(
            $"[SUMMON][DATA] Reading summon data. " +
            $"Monster={m.name} atkIdx={attackIndex}",
            m
        );

        summonPrefab = null;
        summonCount = 1;
        maxSummonsPerBattle = 1;

        if (m == null) return false;
        if (attackIndex < 0) return false;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var t = m.GetType();

        var fiAttacks = t.GetField("attacks", flags);
        if (fiAttacks == null) return false;

        var attacksObj = fiAttacks.GetValue(m);
        var attacksArray = attacksObj as Array;
        if (attacksArray == null) return false;
        if (attackIndex >= attacksArray.Length) return false;

        var atk = attacksArray.GetValue(attackIndex);
        if (atk == null) return false;

        var atkType = atk.GetType();
        bool isSummon = ReadBool(atk, atkType, "isSummon", false);
        if (!isSummon) return false;

        // Prefab field/property
        var fiPrefab = atkType.GetField("summonPrefab", flags);
        if (fiPrefab != null && typeof(GameObject).IsAssignableFrom(fiPrefab.FieldType))
            summonPrefab = fiPrefab.GetValue(atk) as GameObject;

        var piPrefab = atkType.GetProperty("summonPrefab", flags);
        if (summonPrefab == null && piPrefab != null && typeof(GameObject).IsAssignableFrom(piPrefab.PropertyType))
            summonPrefab = piPrefab.GetValue(atk, null) as GameObject;

        if (summonPrefab == null)
        {
            Debug.LogWarning($"[BattleManager][Summon] Monster '{m.name}' used a summon attack but summonPrefab was null (attackIndex={attackIndex}).", this);
            return false;
        }

        summonCount = Mathf.Max(1, ReadInt(atk, atkType, "summonCount", 1));
        maxSummonsPerBattle = ReadInt(atk, atkType, "maxSummonsPerBattle", 1);

        return true;
    }

    private Vector3 GetSummonSpawnPosition()
    {
        if (monsterSpawnPoints != null && monsterSpawnPoints.Length > 0)
        {
            // Pick the first spawn point that isn't already occupied by a live monster.
            for (int i = 0; i < monsterSpawnPoints.Length; i++)
            {
                var sp = monsterSpawnPoints[i];
                if (sp == null) continue;

                bool occupied = false;
                for (int j = 0; j < _activeMonsters.Count; j++)
                {
                    var m = _activeMonsters[j];
                    if (m == null || m.IsDead) continue;

                    // IMPORTANT:
                    // Monsters may be CenterPoint-aligned, meaning the monster root transform.position
                    // will NOT equal the spawn point position. Use the monster's CenterPoint (if present)
                    // when determining whether a spawn point is occupied.
                    Vector3 monsterPos = GetMonsterWorldPositionForSpawnOccupancy(m);
                    if (Vector3.SqrMagnitude(monsterPos - sp.position) < 0.01f)
                    {
                        occupied = true;
                        break;
                    }
                }

                if (!occupied)
                    return sp.position;
            }

            var last = monsterSpawnPoints[monsterSpawnPoints.Length - 1];
            if (last != null)
                return last.position + new Vector3(UnityEngine.Random.Range(-0.35f, 0.35f), 0f, UnityEngine.Random.Range(-0.35f, 0.35f));
        }

        return Vector3.zero;
    }

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


    private bool IsValidPartyIndex(int index) => _party != null && index >= 0 && index < _party.Count;



// ---------------- Battle Music ----------------
private void EnsureBattleMusicSource()
{
    if (battleMusicSource == null)
    {
        battleMusicSource = GetComponent<AudioSource>();
        if (battleMusicSource == null)
            battleMusicSource = gameObject.AddComponent<AudioSource>();
    }

    battleMusicSource.playOnAwake = false;
    battleMusicSource.spatialBlend = 0f; // 2D
    battleMusicSource.loop = loopBattleMusic;
    battleMusicSource.volume = battleMusicVolume;

    if (battleMusicClip != null && battleMusicSource.clip != battleMusicClip)
        battleMusicSource.clip = battleMusicClip;
}

private void StartBattleMusic()
{
    if (battleMusicClip == null && battleMusicSource == null) return;

    EnsureBattleMusicSource();

    if (battleMusicSource.clip == null) return;

    // If it's already playing, don't restart it.
    if (battleMusicSource.isPlaying)
    {
        // Ensure volume/loop reflect current inspector values.
        battleMusicSource.loop = loopBattleMusic;
        battleMusicSource.volume = battleMusicVolume;
        return;
    }

    // Fade-in or instant play.
    if (battleMusicFadeSeconds <= 0f)
    {
        battleMusicSource.volume = battleMusicVolume;
        battleMusicSource.Play();
        return;
    }

    if (_battleMusicFadeRoutine != null)
        StopCoroutine(_battleMusicFadeRoutine);

    _battleMusicFadeRoutine = StartCoroutine(FadeMusicRoutine(battleMusicSource, 0f, battleMusicVolume, battleMusicFadeSeconds, playIfStopped: true));
}

private void StopBattleMusic()
{
    if (battleMusicSource == null) return;

    if (!battleMusicSource.isPlaying)
        return;

    if (battleMusicFadeSeconds <= 0f)
    {
        battleMusicSource.Stop();
        return;
    }

    if (_battleMusicFadeRoutine != null)
        StopCoroutine(_battleMusicFadeRoutine);

    _battleMusicFadeRoutine = StartCoroutine(FadeMusicRoutine(battleMusicSource, battleMusicSource.volume, 0f, battleMusicFadeSeconds, playIfStopped: false, stopAtEnd: true));
}

private IEnumerator FadeMusicRoutine(AudioSource src, float from, float to, float duration, bool playIfStopped, bool stopAtEnd = false)
{
    if (src == null) yield break;

    if (playIfStopped && !src.isPlaying)
        src.Play();

    float t = 0f;
    duration = Mathf.Max(0.0001f, duration);

    // Set the starting volume explicitly.
    src.volume = from;

    while (t < duration)
    {
        t += Time.deltaTime;
        float a = Mathf.Clamp01(t / duration);
        src.volume = Mathf.Lerp(from, to, a);
        yield return null;
    }

    src.volume = to;

    if (stopAtEnd && Mathf.Approximately(to, 0f))
        src.Stop();
}

    private void SetState(BattleState s)
    {
        if (_state == s) return;
        _state = s;

        // Battle music: start when battle begins, stop when battle ends.
        if (s == BattleState.BattleStart)
            StartBattleMusic();
        else if (s == BattleState.BattleEnd)
            StopBattleMusic();

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

            
            
            // Status icons: support multiple simultaneous effects by creating one child icon per effect under _StatusIcon.
            // (Legacy versions used a single StatusEffectIconController on the root, which could only show one sprite at a time.)

            // Status icons should be positioned relative to the hero prefab's CenterPoint (if present).
            // This avoids variance from differing sprite pivots/bounds between heroes.
            Transform centerTf = GetHeroCenterPointTransform(hs, pm.avatarGO.transform);
            Transform desiredParent = (centerTf != null) ? centerTf : pm.avatarGO.transform;

            Transform iconTf = null;

            // First try: look for an existing "_StatusIcon" under the preferred anchor (CenterPoint/root),
            // then fall back to the HeroStats root (legacy setups).
            if (desiredParent != null)
            {
                iconTf = desiredParent.Find("_StatusIcon");
                if (iconTf == null)
                    iconTf = desiredParent.Find("__StatusIcon");
            }

            if (iconTf == null && hs != null)
            {
                iconTf = hs.transform.Find("_StatusIcon");
                if (iconTf == null)
                    iconTf = hs.transform.Find("__StatusIcon");
            }

            if (iconTf == null)
            {
                // Broad fallback: find any existing "_StatusIcon" anywhere under the avatar GO.
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
                go.transform.SetParent(desiredParent != null ? desiredParent : pm.avatarGO.transform, false);
                iconTf = go.transform;
            }
            else
            {
                // Ensure the anchor is parented to the desired parent so offsets are relative to CenterPoint.
                if (desiredParent != null && iconTf.parent != desiredParent)
                    iconTf.SetParent(desiredParent, true);
            }

            // Normalize placement relative to CenterPoint (or root if CenterPoint is missing).
            if (iconTf != null)
            {
                iconTf.localPosition = statusIconLocalOffset;
                iconTf.localScale = Vector3.one * statusIconScale;

                // Root should never render a sprite (children do the rendering).
                var rootSr = iconTf.GetComponent<SpriteRenderer>();
                if (rootSr != null) rootSr.enabled = false;

                int corrosionCount = (reelSpinSystem != null) ? reelSpinSystem.GetCorrosionCountForReel(i) : 0;
                RefreshHeroStatusIcons(iconTf, hs, corrosionCount);
                LayoutHeroStatusIcons(iconTf);
            }
        }
    }



/// <summary>
/// Ensures hero status icons are shown simultaneously by maintaining one child icon GameObject per status.
/// The root is expected to be the "_StatusIcon" transform (anchored under the hero CenterPoint).
/// </summary>
private void RefreshHeroStatusIcons(Transform statusIconRoot, HeroStats hs, int corrosionCount)
{
    if (statusIconRoot == null) return;

    bool hidden = hs != null && hs.IsHidden;
    bool stunned = hs != null && hs.IsStunned;
    bool triple = hs != null && hs.IsTripleBladeEmpoweredThisTurn;

    int attackBoost = (hs != null) ? hs.BonusDamageNextAttack : 0;
    bool attackBoostActive = attackBoost > 0;
    bool bleeding = hs != null && hs.IsBleeding;
    int bleedStacks = (hs != null) ? hs.BleedStacks : 0;

    bool corrosion = corrosionCount > 0;
    int corrosionStacks = Mathf.Max(0, corrosionCount);

    // Disable any legacy root-level "Stacks" label; stacks now live under the Bleeding icon.
    var legacyStacks = statusIconRoot.Find("Stacks");
    if (legacyStacks != null)
        legacyStacks.gameObject.SetActive(false);

    EnsureHeroStatusIcon(statusIconRoot, "Hidden", statusIconHiddenSprite, hidden);
    EnsureHeroStatusIcon(statusIconRoot, "Stunned", statusIconStunnedSprite, stunned);
    EnsureHeroStatusIcon(statusIconRoot, "TripleBlade", statusIconTripleBladeEmpoweredSprite, triple);

    var attackBoostIcon = EnsureHeroStatusIcon(statusIconRoot, "AttackBoost", statusIconAttackBoostSprite, attackBoostActive);
    if (attackBoostIcon != null)
        EnsureHeroStatusStacks(attackBoostIcon, attackBoostActive ? attackBoost : 0);

    var corrosionIcon = EnsureHeroStatusIcon(statusIconRoot, "Corrosion", statusIconCorrosionSprite, corrosion);
    if (corrosionIcon != null)
        EnsureHeroStatusStacks(corrosionIcon, corrosion ? corrosionStacks : 0);

    var bleedIcon = EnsureHeroStatusIcon(statusIconRoot, "Bleeding", statusIconBleedingSprite, bleeding);
    if (bleedIcon != null)
        EnsureHeroStatusStacks(bleedIcon, bleeding ? bleedStacks : 0);
}

private Transform EnsureHeroStatusIcon(Transform root, string childName, Sprite sprite, bool active)
{
    if (root == null) return null;

    // Don't create/show an icon if no sprite is assigned.
    bool shouldShow = active && sprite != null;

    Transform tf = root.Find(childName);
    if (tf == null)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(root, false);
        tf = go.transform;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 50;
    }

    // Keep transforms stable; layout will position in a row.
    tf.localPosition = Vector3.zero;
    tf.localRotation = Quaternion.identity;
    tf.localScale = Vector3.one;

    var iconSr = tf.GetComponent<SpriteRenderer>();
    if (iconSr == null) iconSr = tf.gameObject.AddComponent<SpriteRenderer>();
    iconSr.sprite = sprite;
    iconSr.enabled = shouldShow;

    if (tf.gameObject.activeSelf != shouldShow)
        tf.gameObject.SetActive(shouldShow);

    return tf;
}

private void EnsureHeroStatusStacks(Transform iconTf, int stacks)
{
    if (iconTf == null) return;

    Transform stacksTf = iconTf.Find("Stacks");
    TextMeshPro tmp = null;

    if (stacksTf == null)
    {
        var go = new GameObject("Stacks");
        go.transform.SetParent(iconTf, false);
        stacksTf = go.transform;

        tmp = go.AddComponent<TextMeshPro>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = false;

        var mr = tmp.GetComponent<MeshRenderer>();
        if (mr != null) mr.sortingOrder = 51;
    }
    else
    {
        tmp = stacksTf.GetComponent<TextMeshPro>();
        if (tmp == null) tmp = stacksTf.GetComponentInChildren<TextMeshPro>(true);
    }

    if (stacksTf != null)
    {
        stacksTf.localPosition = statusStackTextLocalOffset;
        stacksTf.localScale = Vector3.one * statusStackTextScale;
    }

    if (tmp != null)
    {
        bool show = stacks > 0;
        tmp.text = show ? stacks.ToString() : "";
        tmp.enabled = show;

        if (statusStackTextFontSize > 0f)
            tmp.fontSize = statusStackTextFontSize;
    }
}
/// <summary>
/// Layout hero status icons in a centered horizontal row and apply stack-count text tuning.
/// Icons are expected to be SpriteRenderer children under the _StatusIcon root.
/// The bleed stack label is expected to be a child named "Stacks" (TMP_Text) under _StatusIcon (legacy setup).
/// </summary>
private void LayoutHeroStatusIcons(Transform statusIconRoot)
{
    if (statusIconRoot == null) return;

    // Collect active icon children (SpriteRenderer) excluding the stack label object.
    List<Transform> icons = new List<Transform>(8);

    for (int i = 0; i < statusIconRoot.childCount; i++)
    {
        Transform child = statusIconRoot.GetChild(i);
        if (child == null || !child.gameObject.activeSelf) continue;

        // Exclude the legacy stack label container if it's directly under the root.
        if (string.Equals(child.name, "Stacks", StringComparison.OrdinalIgnoreCase))
            continue;

        // Only layout actual icon sprites.
        var sr = child.GetComponent<SpriteRenderer>();
        if (sr != null)
            icons.Add(child);
    }

    // Centered horizontal row.
    int count = icons.Count;
    if (count > 0)
    {
        float startX = -(count - 1) * 0.5f * statusIconHorizontalSpacing;
        for (int i = 0; i < count; i++)
        {
            float x = startX + i * statusIconHorizontalSpacing;
            icons[i].localPosition = new Vector3(x, 0f, 0f);
        }
    }

    // Apply stack-count tuning if a TMP label exists (common legacy: "_StatusIcon/Stacks").
    Transform stacksTf = statusIconRoot.Find("Stacks");
    if (stacksTf != null)
    {
        stacksTf.localPosition = statusStackTextLocalOffset;
        stacksTf.localScale = Vector3.one * statusStackTextScale;

        TMP_Text tmp = stacksTf.GetComponent<TMP_Text>();
        if (tmp == null)
            tmp = stacksTf.GetComponentInChildren<TMP_Text>(true);

        if (tmp != null && statusStackTextFontSize > 0f)
            tmp.fontSize = statusStackTextFontSize;
    }

    // Also, if any icon has its own embedded TMP count label, apply the same tuning there too.
    for (int i = 0; i < icons.Count; i++)
    {
        var tmp = icons[i].GetComponentInChildren<TMP_Text>(true);
        if (tmp == null) continue;

        // Try to move a child named "Stacks"/"Count" if present, otherwise leave as-is.
        Transform labelTf = icons[i].Find("Stacks");
        if (labelTf == null) labelTf = icons[i].Find("Count");
        if (labelTf != null)
        {
            labelTf.localPosition = statusStackTextLocalOffset;
            labelTf.localScale = Vector3.one * statusStackTextScale;
        }

        if (statusStackTextFontSize > 0f)
            tmp.fontSize = statusStackTextFontSize;
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
                                             statusIconStasisSprite,
                                             statusIconSabotagedSprite);
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

            ctrl.Configure(statusIconBleedingSprite, statusIconSabotagedSprite);

            int stacks = 0;
            try { stacks = m.BleedStacks; } catch { stacks = 0; }
            ctrl.SetBleedStacks(stacks);

            int sab = 0;
            try { sab = m.SabotageStacks; } catch { sab = 0; }
            ctrl.SetSabotageStacks(sab);
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
                bleedStacks = it.bleedStacks,
                appliesCorrosion = it.appliesCorrosion,
                    corrosionIconCount = Mathf.Max(1, it.corrosionIconCount),
                    isSummon = it.isSummon,
                    summonCount = Mathf.Max(1, it.summonCount),
                    maxSummonsPerBattle = it.maxSummonsPerBattle
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
        _summonedEnemyQueue.Clear();
        NotifyEnemySummonQueueChanged();

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
                    category = ComputeIntentCategory(it.damage, it.isAoe, it.stunsTarget, it.appliesBleed, it.appliesCorrosion, it.isSummon, it.isConsume),
                    enemy = em,
                    targetPartyIndex = it.targetPartyIndex,
                    attackIndex = it.attackIndex,
                    damage = it.damage,
                    isAoe = it.isAoe,
                    stunsTarget = it.stunsTarget,
                    stunPlayerPhases = it.stunPlayerPhases,
                    appliesBleed = it.appliesBleed,
                    bleedStacks = it.bleedStacks,
                    appliesCorrosion = it.appliesCorrosion
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
    private static HeroStats[] BuildPartyStatsArray(List<PartyMemberRuntime> party)
    {
        // IMPORTANT: Preserve party indices so dropdown selections map back to _party reliably.
        // (We may have null slots if a party member is missing.)
        if (party == null || party.Count == 0) return System.Array.Empty<HeroStats>();

        var arr = new HeroStats[party.Count];
        for (int i = 0; i < party.Count; i++)
            arr[i] = party[i] != null ? party[i].stats : null;

        return arr;
    }

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
    public Transform GetSelectedEnemyVisualTransform()
    {
        if (_selectedEnemyTarget == null)
            return null;

        // If your monsters have a CenterPoint transform, prefer that:
        var center = _selectedEnemyTarget.transform.Find("CenterPoint");
        if (center != null)
            return center;

        return _selectedEnemyTarget.transform;
    }
}


////////////////////////////////////////////////////////////