using System;
using System.Collections.Generic;
using UnityEngine;

public class HeroStats : MonoBehaviour
{
    // ---------------- Passive Ability Event Hub ----------------
    /// <summary>
    /// Event hub for this hero that passive abilities can subscribe to.
    /// </summary>
    public HeroCombatEvents Events { get; private set; }

    [Header("Passive Abilities")]
    [Tooltip("Always-on passives. These are NOT shown in the Ability Menu.")]
    [SerializeField] private List<PassiveAbilitySO> passiveAbilities = new List<PassiveAbilitySO>();

    [Header("Passive Runtime")]
    [Tooltip("If true, logs passive triggers and temporary bonuses.")]
    [SerializeField] private bool logPassives = false;

    // We track what we registered so we can cleanly unregister (and support classDef-based passives too).
    private readonly List<PassiveAbilitySO> _registeredPassives = new List<PassiveAbilitySO>();

    // Temporary combat bonuses (consumed automatically when used)
    [SerializeField] private int bonusDamageNextAttack = 0;

    // ---------------- Progression ----------------
    [Header("Progression")]
    [SerializeField] private int level = 1;
    [SerializeField] private int maxLevel = 5;
    [SerializeField] private int xp = 0;
    [SerializeField] private int xpToNextLevel = 10;

    [Tooltip("If false, XP can still be earned, but LevelUp will NOT occur. Instead, PendingLevelUps increases.")]
    [SerializeField] private bool allowLevelUps = false;

    [Tooltip("How many level-ups are queued to be resolved at the campfire.")]
    [SerializeField] private int pendingLevelUps = 0;

    // ---------------- Core Stats ----------------
    [Header("Core Stats")]
    [SerializeField] private int maxHp = 100;
    [SerializeField] private int attack = 3;
    [SerializeField] private int defense = 0;
    [SerializeField] private int speed = 10;

    // ---------------- Stamina ----------------
    [Header("Stamina")]
    [SerializeField] private int maxStamina = 100;
    [SerializeField] private float staminaRegenPerSecond = 45f;

    [Header("Stamina Costs (Base)")]
    [Tooltip("How much stamina is consumed each time an attack is committed/executed.")]
    [SerializeField] private float staminaCostPerAttack = 10f;

    [Tooltip("How much stamina is drained per second while the player is holding block.")]
    [SerializeField] private float staminaDrainPerSecondBlocking = 22f;

    [Tooltip("One-time stamina cost paid when a monster attack is successfully blocked (impact).")]
    [SerializeField] private float staminaCostOnBlockImpact = 8f;

    // ---------------- Resources ----------------
    [Header("Resources")]
    [SerializeField] private long gold = 0;

    [Tooltip("Persistent keys used for opening post-battle chests.")]
    [SerializeField] private int smallKeys = 0;
    [SerializeField] private int largeKeys = 0;

    // ---------------- Runtime ----------------
    [Header("Runtime")]
    [SerializeField] private int currentHp = 100;
    [SerializeField] private float currentStamina = 100f;

    // ---------------- Combat Runtime Status ----------------
    [Header("Combat Status")]
    [SerializeField] private int currentShield = 0;
    [SerializeField] private bool isHidden = false;

    [Header("Damage-over-time")]
    [Tooltip("Bleeding stacks: each turn lose 1 HP per stack, then stacks reduce by 1.")]
    [SerializeField] private int bleedStacks = 0;

    [Tooltip("The PlayerTurnNumber when Bleed was most recently applied. Used to prevent same-turn ticking.")]
    [SerializeField] private int bleedAppliedOnPlayerTurn = -999;

    [SerializeField] private bool isStunned = false;

    [Tooltip("If > 0, this hero will be stunned for that many upcoming Player Phases (cannot act during those phases).")]
    [SerializeField] private int stunnedPlayerPhasesRemaining = 0;

    [Tooltip("True while Triple Blade's double-damage state is active for this hero this turn (consumed on first damaging attack).")]
    [SerializeField] private bool tripleBladeEmpoweredThisTurn = false;

    // ---------------- Build Modifiers / Perks ----------------
    [Header("Build Modifiers (Campfire choices)")]
    [SerializeField] private bool canBlock = true;

    [Tooltip("Multiplier applied to stamina drain while holding block.")]
    [SerializeField] private float blockHoldDrainMultiplier = 1.0f;

    [Tooltip("Multiplier applied to the one-time stamina cost when a block impact occurs.")]
    [SerializeField] private float blockImpactCostMultiplier = 1.0f;

    [Tooltip("Flat bonus added to Attack.")]
    [SerializeField] private int attackFlatBonus = 0;

    [Tooltip("Multiplier applied to Attack after flat bonus. Also used as ClassAttackModifier for ability damage.")]
    [SerializeField] private float attackMultiplier = 1.0f;

    // ---------------- Per-Turn Combat Limits ----------------
    [Header("Per-Turn Combat Limits (Runtime)")]
    [Tooltip("Multiplier applied only for the current turn (e.g., temporary reel/item buffs). Resets at the start of each player turn.")]
    [SerializeField] private float turnAttackMultiplier = 1.0f;

    [Tooltip("Maximum number of damaging attacks this hero can commit this turn. Resets at the start of each player turn.")]
    [SerializeField] private int maxDamageAttacksThisTurn = int.MaxValue;

    [Tooltip("How many damaging attacks have been committed this turn (runtime).")]
    [SerializeField] private int damageAttacksUsedThisTurn = 0;

    [Tooltip("Self damage taken whenever the hero commits an attack (e.g., Barbed Blade).")]
    [SerializeField] private int selfDamagePerAttack = 0;

    [Tooltip("If > 0, extra poison stacks applied on hit (requires a PoisonReceiver on monster).")]
    [SerializeField] private int poisonStacksOnHit = 0;

    [Tooltip("Poison DPS per stack (requires a PoisonReceiver on monster).")]
    [SerializeField] private int poisonDpsPerStack = 1;

    [Tooltip("Poison duration seconds (requires a PoisonReceiver on monster).")]
    [SerializeField] private float poisonDurationSeconds = 4f;

    // ---------------- Class System (Data-driven) ----------------
    [Header("Class Progression (Data-driven)")]
    [SerializeField] private ClassDefinitionSO baseClassDef;
    [SerializeField] private ClassDefinitionSO advancedClassDef;

    public ClassDefinitionSO BaseClassDef => baseClassDef;
    public ClassDefinitionSO AdvancedClassDef => advancedClassDef;

    // ---------------- Startup / Ability Selection ----------------
    [Header("Startup / Ability Selection")]
    [Tooltip("Optional: selected on the startup class selection panel. If set to a starter-choice ability, this will be the one available at Level 1.\nStarter-choice abilities are ONLY available if they match this selection.")]
    [SerializeField] private AbilityDefinitionSO startingAbilityOverride;

    [Tooltip("Abilities that have been unlocked/earned by this hero and should remain available (even if the hero later changes class definitions).")]
    [SerializeField] private List<AbilityDefinitionSO> permanentlyUnlockedAbilities = new List<AbilityDefinitionSO>();

    [Tooltip("Pending ability choice levels to resolve post-battle. Each level >= 2 adds one entry.")]
    [SerializeField] private List<int> pendingAbilityChoiceLevels = new List<int>();

    public AbilityDefinitionSO StartingAbilityOverride => startingAbilityOverride;

    /// <summary>
    /// Ensures that the hero's permanent unlock list includes any abilities that should have been unlocked
    /// due to the hero's current level (for non-starter abilities), and the chosen starter ability.
    /// This is lazy-synced (called when building menus / checking unlocks) so we don't depend on exact timing of LevelUp events.
    /// </summary>
    private void SyncPermanentUnlocks()
    {
        // Starter selection should always persist
        if (startingAbilityOverride != null && !permanentlyUnlockedAbilities.Contains(startingAbilityOverride))
            permanentlyUnlockedAbilities.Add(startingAbilityOverride);

        // Non-starter abilities should persist once level-gated unlocked.
        // We sync from whichever class defs exist, so changing class defs doesn't "forget" abilities.
        AddEligibleUnlocksFromClassDef(baseClassDef);
        AddEligibleUnlocksFromClassDef(advancedClassDef);
    }

    private void AddEligibleUnlocksFromClassDef(ClassDefinitionSO classDef)
    {
        if (classDef == null) return;
        if (classDef.abilities == null) return;

        for (int i = 0; i < classDef.abilities.Count; i++)
        {
            AbilityDefinitionSO a = classDef.abilities[i];
            if (a == null) continue;

            if (a.starterChoice) continue; // starter-choice is handled by startingAbilityOverride

            int req = Mathf.Max(1, a.unlockAtLevel);
            if (a.unlockAtLevel <= 1 && level >= req && !permanentlyUnlockedAbilities.Contains(a))
                permanentlyUnlockedAbilities.Add(a);
        }
    }


    // ---------------- Ability Choice (Post-Battle) ----------------
    public bool HasPendingAbilityChoices => pendingAbilityChoiceLevels != null && pendingAbilityChoiceLevels.Count > 0;
    public int PendingAbilityChoices => (pendingAbilityChoiceLevels != null) ? pendingAbilityChoiceLevels.Count : 0;
    public int NextPendingAbilityChoiceLevel => HasPendingAbilityChoices ? pendingAbilityChoiceLevels[0] : -1;

    /// <summary>
    /// Returns up to <paramref name="count"/> ability options for the specified unlock level.
    /// Options are pulled from the hero's base class and (if present) advanced class definitions.
    /// Starter-choice abilities are excluded; this panel is for learned abilities.
    /// </summary>
    public List<AbilityDefinitionSO> GetAbilityChoiceOptionsForLevel(int unlockLevel, int count = 2)
    {
        Debug.Log($"[Hero][AbilityUpgrade] GetAbilityChoiceOptionsForLevel hero='{name}' unlockLevel={unlockLevel} count={count}");
        List<AbilityDefinitionSO> options = new List<AbilityDefinitionSO>();
        if (count <= 0) return options;

        AddAbilityOptionsFromClassDef(baseClassDef, unlockLevel, count, options);
        AddAbilityOptionsFromClassDef(advancedClassDef, unlockLevel, count, options);
        Debug.Log($"[Hero][AbilityUpgrade] Options found hero='{name}' unlockLevel={unlockLevel} optionsCount={options.Count} first={(options.Count>0?options[0].abilityName:"<none>")} second={(options.Count>1?options[1].abilityName:"<none>")}");

        return options;
    }

        private IEnumerable<AbilityDefinitionSO> EnumerateClassAbilities(ClassDefinitionSO classDef)
    {
        if (classDef == null) yield break;

        // Legacy 2-slot fields (still used in some data)
        if (classDef.ability1 != null) yield return classDef.ability1;
        if (classDef.ability2 != null) yield return classDef.ability2;

        // New list
        if (classDef.abilities != null)
        {
            for (int i = 0; i < classDef.abilities.Count; i++)
            {
                if (classDef.abilities[i] != null)
                    yield return classDef.abilities[i];
            }
        }
    }

    private void AddAbilityOptionsFromClassDef(ClassDefinitionSO classDef, int unlockLevel, int maxCount, List<AbilityDefinitionSO> options)
    {
        if (classDef == null) return;
        if (options == null) return;

        foreach (AbilityDefinitionSO a in EnumerateClassAbilities(classDef))
        {
            if (options.Count >= maxCount) break;
            if (a == null) continue;

            if (a.starterChoice) continue;

            int req = Mathf.Max(1, a.unlockAtLevel);
            if (req != unlockLevel) continue;

            if (permanentlyUnlockedAbilities.Contains(a)) continue;

            if (!options.Contains(a))
                options.Add(a);
        }
    }

    /// <summary>
    /// Accepts the chosen ability for the next pending choice level (if any), permanently unlocking it.
    /// Returns true if applied.
    /// </summary>
    public bool TryAcceptAbilityChoice(AbilityDefinitionSO chosen)
    {
        if (!HasPendingAbilityChoices)
        {
            Debug.LogWarning($"[Hero][AbilityUpgrade] TryAcceptAbilityChoice called but no pending choices hero='{name}'");
            return false;
        }
        if (chosen == null)
        {
            Debug.LogWarning($"[Hero][AbilityUpgrade] TryAcceptAbilityChoice called with null ability hero='{name}'");
            return false;
        }

        int expectedLevel = pendingAbilityChoiceLevels[0];
        Debug.Log($"[Hero][AbilityUpgrade] Accepting ability choice hero='{name}' expectedUnlockLevel={expectedLevel} chosen='{chosen.abilityName}' unlockAt={chosen.unlockAtLevel}");
        int req = Mathf.Max(1, chosen.unlockAtLevel);
        if (req != expectedLevel) return false;

        if (!permanentlyUnlockedAbilities.Contains(chosen))
            permanentlyUnlockedAbilities.Add(chosen);

        // consume the pending choice
        pendingAbilityChoiceLevels.RemoveAt(0);
        Debug.Log($"[Hero][AbilityUpgrade] Ability permanently unlocked hero='{name}' chosen='{chosen.abilityName}' remainingPending={pendingAbilityChoiceLevels.Count}");

        NotifyChanged();
        return true;
    }

    /// <summary>
    /// Returns true if this hero should be allowed to use the given ability right now,
    /// based on Starter Choice rules + unlock level gating + persistent unlocks.
    ///
    /// Rules:
    /// - Non-starter abilities are unlocked when hero.Level >= ability.unlockAtLevel (and then persist).
    /// - Starter-choice abilities are ONLY available if they match startingAbilityOverride.
    /// </summary>
    public bool IsAbilityUnlocked(AbilityDefinitionSO ability)
    {
        if (ability == null) return false;

        // Keep permanent unlocks in sync before we answer.
        SyncPermanentUnlocks();

        if (ability.starterChoice)
        {
            if (startingAbilityOverride == null) return false;
            return ability == startingAbilityOverride;
        }

        // Non-starter abilities: if level gate is met, they are unlocked (and persisted).
        int req = Mathf.Max(1, ability.unlockAtLevel);
        if (level >= req)
        {
            if (!permanentlyUnlockedAbilities.Contains(ability))
                permanentlyUnlockedAbilities.Add(ability);
            return true;
        }

        // Otherwise, allow if it was previously unlocked/persisted (e.g., from a prior class definition).
        return permanentlyUnlockedAbilities.Contains(ability);
    }

    /// <summary>
    /// Builds the list of abilities that should be shown/usable for this hero.
    ///
    /// Source-of-truth:
    /// - Class definition abilities (for current class actions)
    /// - PLUS any permanently unlocked abilities (so chosen starter abilities don't disappear on later levels/class changes)
    ///
    /// Then we apply IsAbilityUnlocked() for gating.
    /// </summary>
    public List<AbilityDefinitionSO> GetUnlockedAbilitiesFromClassDef(ClassDefinitionSO classDef)
    {
        SyncPermanentUnlocks();

        List<AbilityDefinitionSO> results = new List<AbilityDefinitionSO>();

        // 1) Add abilities from the provided class definition (filtered)
    // Note: supports both legacy (ability1/ability2) and new (abilities list) class data.
    if (classDef != null)
    {
        foreach (AbilityDefinitionSO a in EnumerateClassAbilities(classDef))
        {
            if (a == null) continue;
            if (a.kind == AbilityKind.Passive) continue;
            if (IsAbilityUnlocked(a) && !results.Contains(a))
                results.Add(a);
        }
    }

        // 2) Add any permanently unlocked abilities (filtered) so they persist across level ups/class changes
        for (int i = 0; i < permanentlyUnlockedAbilities.Count; i++)
        {
            AbilityDefinitionSO a = permanentlyUnlockedAbilities[i];
            if (a == null) continue;
            if (a.kind == AbilityKind.Passive) continue;
            if (IsAbilityUnlocked(a) && !results.Contains(a))
                results.Add(a);
        }

        return results;
    }

    // ---------------- Reels / UI (Prefab Data) ----------------
    [Header("Reels / UI (Prefab Data)")]
    [Tooltip("Reel strip used for this hero's reel.")]
    [SerializeField] private ReelStripSO reelStrip;

    [Tooltip("Portrait sprite used for this hero's reel picker button / UI.")]
    [SerializeField] private Sprite portrait;

    // ---------------- Reel Upgrade (Level Up) ----------------
    [Header("Reel Upgrade (Level Up)")]
    [Tooltip("If true, leveling up queues a reel symbol upgrade to be resolved via the Reel Upgrade Minigame.")]
    [SerializeField] private bool upgradeReelOnLevelUp = true;

    [Tooltip("Upgrade mapping rules (e.g., Attack->DoubleAttack, Null->Wild).")]
    [SerializeField] private ReelUpgradeRulesSO reelUpgradeRules;

    [Tooltip("How many reel upgrades are pending for this hero (usually equals number of level-ups gained).")]
    [SerializeField] private int pendingReelUpgrades = 0;

    public ReelStripSO ReelStrip => reelStrip;
    public Sprite Portrait => portrait;

    public int PendingReelUpgrades => pendingReelUpgrades;
    public bool HasPendingReelUpgrades => pendingReelUpgrades > 0;

    // ---------------- Equipment (UI only, no effects yet) ----------------
    [Header("Equipment (UI only, no effects yet)")]
    public int equipmentSlotSize = 1;
    [SerializeField] public InventorySlot[] equipmentSlots = new InventorySlot[1];

    public int EquipmentSlotCount => equipmentSlots != null ? equipmentSlots.Length : 0;

    // ---------------- Equipment Change Events ----------------
    [Header("Equipment Debug Events")]
    [Tooltip("If true, logs whenever an item is placed into an equipment slot under this hero's EquipGrid.")]
    [SerializeField] private bool logOnEquipItemPlaced = true;

    // Cache of last known InventoryItem per equipment slot so we can detect changes.
    private InventoryItem[] _lastEquipmentItems;

    // ---------------- Public Accessors ----------------
    public int Level => level;
    public int XP => xp;
    public int XPToNextLevel => xpToNextLevel;

    public bool AllowLevelUps => allowLevelUps;
    public int PendingLevelUps => pendingLevelUps;

    public int MaxHp => maxHp;
    public int CurrentHp => currentHp;

    // Effective attack with perks/modifiers
    public int Attack => Mathf.Max(0, Mathf.RoundToInt((attack + attackFlatBonus) * Mathf.Max(0f, attackMultiplier) * Mathf.Max(0f, turnAttackMultiplier)));
    public int Defense => defense;
    public int Speed => speed;

    public int MaxStamina => maxStamina;
    public float CurrentStamina => currentStamina;

    public float StaminaCostPerAttack => Mathf.Max(0f, staminaCostPerAttack);
    public long Gold => gold;

    public int SmallKeys => smallKeys;
    public int LargeKeys => largeKeys;

    public bool CanBlock => canBlock;

    public int Shield => currentShield;
    public bool IsHidden => isHidden;

    public int BleedStacks => bleedStacks;
    public bool IsBleeding => bleedStacks > 0;
    public int BleedAppliedOnPlayerTurn => bleedAppliedOnPlayerTurn;

    public bool IsStunned => isStunned;
    public bool IsTripleBladeEmpoweredThisTurn => tripleBladeEmpoweredThisTurn;

    public int BonusDamageNextAttack => Mathf.Max(0, bonusDamageNextAttack);

    public float ClassAttackModifier => Mathf.Max(0f, attackMultiplier) * Mathf.Max(0f, turnAttackMultiplier);

    public event Action OnChanged;

    // ---------------- Ability Per-Turn Limits (Runtime) ----------------
    // Tracks abilities that are marked "usableOncePerTurn" so they can't be used repeatedly in the same player turn.
    private Dictionary<AbilityDefinitionSO, int> _abilityLastUsedOnPlayerTurn;

    private void Awake()
    {
        Events = new HeroCombatEvents(this);

        currentHp = Mathf.Clamp(currentHp, 0, maxHp);
        currentStamina = Mathf.Clamp(currentStamina, 0f, maxStamina);
        currentShield = Mathf.Max(0, currentShield);

        // Ensure reel upgrades are per-hero (do not mutate shared ScriptableObject assets).
        if (Application.isPlaying && reelStrip != null)
            reelStrip = Instantiate(reelStrip);

        InitEquipmentWatcher();      // legacy array watcher (safe to keep)
        RefreshEquipSlotsFromGrid(); // runtime EquipGrid watcher
        NotifyChanged();
    }

    private void OnEnable()
    {
        RegisterPassiveAbilities();
    }

    private void OnDisable()
    {
        UnregisterPassiveAbilities();
    }

    // ---------------- Passive registration helpers ----------------
    private List<PassiveAbilitySO> CollectPassiveAbilityDefs()
    {
        // Passives can be configured either directly on the prefab (HeroStats.passiveAbilities)
        // or on the active ClassDefinitionSO (ClassDefinitionSO.passiveAbilities).
        var results = new List<PassiveAbilitySO>();
        var seen = new HashSet<PassiveAbilitySO>();

        void AddRangeSafe(List<PassiveAbilitySO> list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (p == null) continue;
                if (seen.Add(p)) results.Add(p);
            }
        }

        AddRangeSafe(passiveAbilities);

        var activeDef = GetActiveClassDefinition();
        if (activeDef != null)
            AddRangeSafe(activeDef.passiveAbilities);

        // Also consider the base class definition (if different from active) for safety.
        if (baseClassDef != null && baseClassDef != activeDef)
            AddRangeSafe(baseClassDef.passiveAbilities);

        return results;
    }

    private void RegisterPassiveAbilities()
    {
        UnregisterPassiveAbilities(); // prevent double-register if OnEnable fires multiple times

        var defs = CollectPassiveAbilityDefs();
        if (defs == null || defs.Count == 0)
        {
            if (logPassives) Debug.Log($"[Hero][Passive] No passive abilities configured for hero='{name}'.", this);
            return;
        }

        for (int i = 0; i < defs.Count; i++)
        {
            var p = defs[i];
            if (p == null) continue;

            try
            {
                // Your PassiveAbilitySO is expected to own the subscription logic (Events hub, etc.)
                p.Register(this);

                _registeredPassives.Add(p);

                if (logPassives)
                    Debug.Log($"[Hero][Passive] Registered '{p.abilityName}' for hero='{name}'.", this);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Hero][Passive] Register failed for passive='{p.name}' hero='{name}'. {e}", this);
            }
        }
    }

    private void UnregisterPassiveAbilities()
    {
        if (_registeredPassives.Count == 0) return;

        for (int i = 0; i < _registeredPassives.Count; i++)
        {
            var p = _registeredPassives[i];
            if (p == null) continue;

            try
            {
                p.Unregister(this);

                if (logPassives)
                    Debug.Log($"[Hero][Passive] Unregistered '{p.abilityName}' for hero='{name}'.", this);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Hero][Passive] Unregister failed for passive='{p.name}' hero='{name}'. {e}", this);
            }
        }

        _registeredPassives.Clear();
    }

    /// <summary>
    /// Called by BattleManager when this hero's reel lands on a symbol (including after Reelcraft updates).
    /// This is the main bridge between reels and passive ability triggers.
    /// </summary>
    public void NotifyReelSymbolLanded(ReelSymbolSO symbol, ReelSpinSystem.ResourceType resourceType, int amount, int multiplier)
    {
        // NOTE: This should fire at reel-stop time (not cashout). BattleManager is responsible for calling this.
        string symName = symbol != null ? symbol.name : "NULL";

        // Always log this bridge when passives logging is enabled, so we can diagnose missing procs.
        if (logPassives)
        {
            Debug.Log($"[Hero][PassiveBridge] ReelSymbolLanded hero='{name}' symbol='{symName}' type={resourceType} amount={amount} mult={multiplier} eventsNull={(Events == null)}", this);
        }

        if (Events == null)
        {
            // This is a hard stop for passives; surface loudly even if logPassives is off.
            Debug.LogWarning($"[Hero][PassiveBridge] Events is NULL. Passives will NOT trigger for hero='{name}'. (symbol='{symName}' type={resourceType})", this);
            return;
        }

        Events.RaiseReelSymbolLanded(symbol, resourceType, amount, multiplier);

        if (logPassives)
        {
            Debug.Log($"[Hero][PassiveBridge] Raised OnReelSymbolLanded hero='{name}'", this);
        }
    }

    // -------- Temporary Bonus Damage (Next Attack) --------
    public void AddBonusDamageNextAttack(int amount)
    {
        if (amount <= 0) return;
        bonusDamageNextAttack += amount;
        if (logPassives) Debug.Log($"[Passives] hero='{name}' gained +{amount} bonus damage on next attack. totalNextAttackBonus={bonusDamageNextAttack}", this);
    }

    /// <summary>
    /// Consumes and returns any bonus damage that should apply to a damaging ability.
    /// </summary>
    public int ConsumeBonusDamageNextAttackIfDamaging(AbilityDefinitionSO ability)
    {
        if (ability == null) return 0;
        if (bonusDamageNextAttack <= 0) return 0;

        // Only consume on damaging enemy-targeted abilities.
        bool isDamaging = (ability.baseDamage > 0 && ability.targetType == AbilityTargetType.Enemy) || ability.isDamaging;
        if (!isDamaging) return 0;

        int value = bonusDamageNextAttack;
        bonusDamageNextAttack = 0;

        if (logPassives) Debug.Log($"[Passives] hero='{name}' consumed +{value} bonus damage (next attack).", this);
        return value;
    }

    // ---------------- Ability Per-Turn Limits ----------------
    public bool CanUseAbilityThisTurn(AbilityDefinitionSO ability)
    {
        if (ability == null) return false;
        if (!ability.usableOncePerTurn) return true;

        int turn = (BattleManager.Instance != null) ? BattleManager.Instance.PlayerTurnNumber : 0;
        if (_abilityLastUsedOnPlayerTurn != null && _abilityLastUsedOnPlayerTurn.TryGetValue(ability, out int lastTurn))
            return lastTurn != turn;

        return true;
    }

    public void RegisterAbilityUsedThisTurn(AbilityDefinitionSO ability)
    {
        if (ability == null || !ability.usableOncePerTurn) return;

        int turn = (BattleManager.Instance != null) ? BattleManager.Instance.PlayerTurnNumber : 0;
        _abilityLastUsedOnPlayerTurn ??= new Dictionary<AbilityDefinitionSO, int>(16);
        _abilityLastUsedOnPlayerTurn[ability] = turn;
    }

    private void NotifyChanged() => OnChanged?.Invoke();

    // ---------------- Equipment Change Detection (Legacy InventorySlot[]) ----------------
    private void InitEquipmentWatcher()
    {
        if (equipmentSlots == null)
        {
            _lastEquipmentItems = null;
            return;
        }

        _lastEquipmentItems = new InventoryItem[equipmentSlots.Length];

        // Prime the cache so we only log when something changes after startup.
        for (int i = 0; i < equipmentSlots.Length; i++)
        {
            InventorySlot slot = equipmentSlots[i];
            if (slot == null) continue;
            _lastEquipmentItems[i] = slot.GetComponentInChildren<InventoryItem>();
        }
    }

    // ---------------- Run Lifecycle ----------------
    public void ResetForNewRun()
    {
        level = 1;
        xp = 0;
        xpToNextLevel = 10;
        pendingLevelUps = 0;
        pendingReelUpgrades = 0;

        currentHp = maxHp;
        currentStamina = maxStamina;

        gold = 0;

        // Reset combat state
        currentShield = 0;
        isHidden = false;
        bleedStacks = 0;
        bleedAppliedOnPlayerTurn = -999;

        NotifyChanged();
    }

    public void ClearStartOfBattleStatuses()
    {
        currentShield = 0;
        isHidden = false;
        bleedStacks = 0;
        bleedAppliedOnPlayerTurn = -999;
        NotifyChanged();
    }

    // ---------------- Class Eligibility Helpers ----------------
    public bool CanChooseBaseClass()
    {
        return baseClassDef == null;
    }

    public bool CanUpgradeTo(ClassDefinitionSO candidateAdvanced)
    {
        if (candidateAdvanced == null) return false;
        if (advancedClassDef != null) return false;
        if (baseClassDef == null) return false;

        // If your ClassDefinitionSO has requiredBaseClass, enforce it.
        if (candidateAdvanced.requiredBaseClass != null)
            return candidateAdvanced.requiredBaseClass == baseClassDef;

        // If no requirement specified, allow (you can tighten later).
        return true;
    }

    public void ApplyClassDefinition(ClassDefinitionSO def)
    {
        if (def == null) return;

        if (def.tier == ClassDefinitionSO.Tier.Base)
        {
            if (baseClassDef != null) return;
            baseClassDef = def;
        }
        else
        {
            if (!CanUpgradeTo(def)) return;
            advancedClassDef = def;
        }

        // Apply definition modifiers
        canBlock = def.canBlock;

        attackFlatBonus += def.attackFlatBonus;
        attackMultiplier *= Mathf.Max(0f, def.attackMultiplier);

        staminaCostPerAttack *= Mathf.Max(0f, def.staminaCostPerAttackMultiplier);
        blockHoldDrainMultiplier *= Mathf.Max(0f, def.blockHoldDrainMultiplier);
        blockImpactCostMultiplier *= Mathf.Max(0f, def.blockImpactCostMultiplier);

        NotifyChanged();
    }

    // ---------------- Combat State Helpers ----------------
    public void AddShield(int amount)
    {
        if (amount <= 0) return;
        currentShield += amount;
        NotifyChanged();
    }

    public void SetHidden(bool hidden)
    {
        if (isHidden == hidden) return;
        isHidden = hidden;
        NotifyChanged();
    }

    // ---------------- Bleeding ----------------
    public void AddBleedStacks(int stacks)
    {
        if (stacks <= 0) return;
        bleedStacks = Mathf.Max(0, bleedStacks + stacks);

        // Record the player turn this was applied so we can skip ticking on the same turn.
        if (BattleManager.Instance != null)
            bleedAppliedOnPlayerTurn = BattleManager.Instance.PlayerTurnNumber;

        NotifyChanged();
    }

    public int TickBleedingAtTurnStart()
    {
        // Backwards-compat wrapper. Bleed now ticks at END of the player's turn.
        int turn = (BattleManager.Instance != null) ? BattleManager.Instance.PlayerTurnNumber : 0;
        return TickBleedingAtEndOfPlayerTurn(turn);
    }

    public int TickBleedingAtEndOfPlayerTurn(int currentPlayerTurnNumber)
    {
        if (bleedStacks <= 0)
            return 0;

        // Skip ticking on the same player turn the bleed was applied.
        if (bleedAppliedOnPlayerTurn == currentPlayerTurnNumber)
            return 0;

        int dmg = Mathf.Max(0, bleedStacks);
        int before = currentHp;
        currentHp = Mathf.Max(0, currentHp - dmg);

        // Reduce stacks by 1 each tick.
        bleedStacks = Mathf.Max(0, bleedStacks - 1);

        NotifyChanged();
        return Mathf.Max(0, before - currentHp);
    }

    public bool ClearBleeding()
    {
        if (bleedStacks <= 0)
            return false;

        bleedStacks = 0;
        bleedAppliedOnPlayerTurn = -999;
        NotifyChanged();
        return true;
    }

    // ---------------- Stun ----------------
    public void StartPlayerPhaseStatuses()
    {
        // Clear any leftover immediate stun from last phase.
        isStunned = false;

        // Consume queued stuns (from enemy phase).
        if (stunnedPlayerPhasesRemaining > 0)
        {
            isStunned = true;
            stunnedPlayerPhasesRemaining = Mathf.Max(0, stunnedPlayerPhasesRemaining - 1);
        }

        NotifyChanged();
    }

    public void StunForRemainderOfPlayerPhase()
    {
        if (isStunned) return;
        isStunned = true;
        NotifyChanged();
    }

    public void StunForNextPlayerPhases(int playerPhases = 1)
    {
        if (playerPhases <= 0) return;
        stunnedPlayerPhasesRemaining += playerPhases;
        NotifyChanged();
    }

    public bool ClearStun()
    {
        bool changed = false;

        if (isStunned)
        {
            isStunned = false;
            changed = true;
        }

        if (stunnedPlayerPhasesRemaining > 0)
        {
            stunnedPlayerPhasesRemaining = 0;
            changed = true;
        }

        if (changed)
            NotifyChanged();

        return changed;
    }

    // ---------------- Triple Blade flag ----------------
    public void SetTripleBladeEmpoweredThisTurn(bool empowered)
    {
        if (tripleBladeEmpoweredThisTurn == empowered) return;
        tripleBladeEmpoweredThisTurn = empowered;
        NotifyChanged();
    }

    /// <summary>
    /// Applies incoming damage to shield first, then HP (HP damage still reduced by hero defense).
    /// Returns the HP loss (not including shield absorbed).
    /// </summary>
    public int ApplyIncomingDamage(int amount)
    {
        amount = Mathf.Max(0, amount);
        if (amount == 0) return 0;

        int absorbed = Mathf.Min(currentShield, amount);
        if (absorbed > 0)
        {
            currentShield -= absorbed;
            amount -= absorbed;
        }

        if (amount <= 0)
        {
            NotifyChanged();
            return 0;
        }

        int before = currentHp;
        TakeDamage(amount);
        return Mathf.Max(0, before - currentHp);
    }

    // ---------------- XP / Leveling (GATED) ----------------
    public void GainXP(int amount)
    {
        if (amount <= 0) return;

        xp += amount;

        while (xp >= xpToNextLevel)
        {
            // Max level reached: stop leveling and clamp XP just below the threshold
            // to avoid infinite loops / post-battle softlocks.
            if (level >= maxLevel)
            {
                xp = Mathf.Min(xp, Mathf.Max(0, xpToNextLevel - 1));
                break;
            }

            xp -= xpToNextLevel;

            if (allowLevelUps) LevelUp();
            else pendingLevelUps += 1;
        }

        NotifyChanged();
    }

    public void SetAllowLevelUps(bool allowed)
    {
        allowLevelUps = allowed;
        NotifyChanged();
    }

    public bool SpendOnePendingLevelUp()
    {
        if (pendingLevelUps <= 0) return false;

        pendingLevelUps -= 1;
        // If we've already hit the cap, consume the pending level up to prevent post-battle loops.
        if (level < maxLevel)
            LevelUp();
        NotifyChanged();
        return level < maxLevel;
    }

    // ---------------- Reel Upgrade Minigame Support ----------------
    public bool TryApplyPendingReelUpgradeFromQuadIndex(int quadIndex, out ReelSymbolSO from, out ReelSymbolSO to)
    {
        int _;
        return TryApplyPendingReelUpgradeFromQuadIndex(quadIndex, out from, out to, out _);
    }

    public bool TryApplyPendingReelUpgradeFromQuadIndex(int quadIndex, out ReelSymbolSO from, out ReelSymbolSO to, out int appliedStripIndex)
    {
        from = null;
        to = null;
        appliedStripIndex = -1;

        if (pendingReelUpgrades <= 0) return false;
        if (reelStrip == null || reelStrip.symbols == null || reelStrip.symbols.Count == 0) return false;
        if (reelUpgradeRules == null) return false;

        int n = reelStrip.symbols.Count;
        int startStripIndex = ((quadIndex % n) + n) % n;

        // 1) Prefer upgrading the landed symbol.
        // 2) If not upgradeable, search forward (wrapping) for the nearest upgradeable symbol.
        for (int step = 0; step < n; step++)
        {
            int idx = (startStripIndex + step) % n;
            ReelSymbolSO candidate = reelStrip.symbols[idx];
            if (candidate == null) continue;

            ReelSymbolSO upgrade = reelUpgradeRules.GetUpgradeFor(candidate);
            if (upgrade == null) continue;

            reelStrip.symbols[idx] = upgrade;
            from = candidate;
            to = upgrade;
            appliedStripIndex = idx;

            pendingReelUpgrades -= 1;
            NotifyChanged();
            return true;
        }

        // Nothing on the strip is upgradeable, but we still need to consume the pending upgrade
        // so the post-battle flow cannot loop forever.
        appliedStripIndex = startStripIndex;
        from = reelStrip.symbols[startStripIndex];
        pendingReelUpgrades -= 1;
        NotifyChanged();
        Debug.LogWarning($"[HeroStats] No upgradeable symbols found on reel strip for hero '{name}'. Consuming 1 PendingReelUpgrades to avoid soft-lock.");
        return false;
    }

    private void LevelUp()
    {
        if (level >= maxLevel) return;
        level += 1;

        // Stat growth is defined by the hero's active class (Advanced if present, else Base).
        // If no class is assigned yet, fall back to the previous default growth.
        ClassDefinitionSO growthDef = GetActiveClassDefinition();

        int hpGain = (growthDef != null) ? growthDef.levelUpMaxHp : 10;
        int atkGain = (growthDef != null) ? growthDef.levelUpAttack : 1;
        int defGain = (growthDef != null) ? growthDef.levelUpDefense : 1;

        maxHp += hpGain;
        attack += atkGain;
        defense += defGain;

        currentHp = maxHp;

        maxStamina += 5;
        currentStamina = Mathf.Min(currentStamina, maxStamina);

        xpToNextLevel = Mathf.RoundToInt(xpToNextLevel * 1.25f) + 5;

        // Queue an ability choice for this level (starting at level 2).
        if (level >= 2)
        {
            pendingAbilityChoiceLevels.Add(level);
            Debug.Log($"[Hero][AbilityUpgrade] Queued ability choice hero='{name}' reachedLevel={level} pendingChoices={pendingAbilityChoiceLevels.Count}");
        }

        // Queue a reel upgrade to be resolved via the Reel Upgrade Minigame.
        // Don't queue upgrades past the max level.
        if (level <= maxLevel && upgradeReelOnLevelUp && reelUpgradeRules != null && reelStrip != null)
            pendingReelUpgrades += 1;
    }

    // ---------------- Damage / Resources ----------------
    public void TakeDamage(int amount)
    {
        if (amount <= 0) return;

        int mitigated = Mathf.Max(0, amount - defense);
        currentHp = Mathf.Max(0, currentHp - mitigated);
        NotifyChanged();
    }

    public void Heal(int amount)
    {
        if (amount <= 0) return;
        currentHp = Mathf.Min(maxHp, currentHp + amount);
        NotifyChanged();
    }

    public void AddGold(long amount)
    {
        if (amount <= 0) return;
        gold += amount;
        NotifyChanged();
    }

    public bool TrySpendGold(long amount)
    {
        if (amount <= 0) return true;
        if (gold < amount) return false;
        gold -= amount;
        NotifyChanged();
        return true;
    }

    public void AddSmallKeys(int amount)
    {
        if (amount <= 0) return;
        smallKeys += amount;
        NotifyChanged();
    }

    public void AddLargeKeys(int amount)
    {
        if (amount <= 0) return;
        largeKeys += amount;
        NotifyChanged();
    }

    public bool TrySpendSmallKey(int amount = 1)
    {
        amount = Mathf.Max(1, amount);
        if (smallKeys < amount) return false;
        smallKeys -= amount;
        NotifyChanged();
        return true;
    }

    public bool TrySpendLargeKey(int amount = 1)
    {
        amount = Mathf.Max(1, amount);
        if (largeKeys < amount) return false;
        largeKeys -= amount;
        NotifyChanged();
        return true;
    }

    // ---------------- On-hit effects ----------------
    public void ApplyOnHitEffectsTo(Monster target)
    {
        if (target == null) return;

        if (poisonStacksOnHit > 0)
        {
            PoisonReceiver pr = target.GetComponentInParent<PoisonReceiver>();
            if (pr != null)
                pr.ApplyPoison(poisonStacksOnHit, poisonDpsPerStack, poisonDurationSeconds);
        }
    }

    public ClassDefinitionSO GetActiveClassDefinition()
    {
        if (advancedClassDef != null) return advancedClassDef;
        return baseClassDef;
    }

    /// <summary>
    /// Used by BattleManager Undo. Restores core runtime combat state.
    /// </summary>
    public void SetRuntimeState(int hp, float stamina, int shield, bool hidden)
    {
        currentHp = Mathf.Clamp(hp, 0, maxHp);
        currentShield = Mathf.Max(0, shield);
        isHidden = hidden;
    }

    /// <summary>
    /// Used by BattleManager Undo. Restores bleed stacks directly.
    /// </summary>
    public void SetBleedStacks(int stacks)
    {
        bleedStacks = Mathf.Max(0, stacks);
    }

    /// <summary>
    /// Used by BattleManager Undo. Restores bleed stacks and the "applied on player turn" marker.
    /// This prevents bleed from incorrectly ticking immediately after an Undo.
    /// </summary>
    public void SetBleedStacks(int stacks, int appliedOnPlayerTurn)
    {
        bleedStacks = Mathf.Max(0, stacks);
        bleedAppliedOnPlayerTurn = appliedOnPlayerTurn;
    }

    public void GetEquippedItems()
    {
        if (equipmentSlots == null) return;

        for (int i = 0; i < equipmentSlots.Length; i++)
        {
            InventorySlot slot = equipmentSlots[i];
            if (slot == null) continue;

            InventoryItem itemInSlot = slot.GetComponentInChildren<InventoryItem>();
            if (itemInSlot != null && itemInSlot.item != null)
            {
                Debug.Log($"{itemInSlot.item.itemName}");
            }
        }
    }

    // ---------------- EquipGrid runtime watcher ----------------
    [SerializeField] private Transform equipGridRoot; // assign EquipGrid1
    private InventorySlot[] _equipSlotsRuntime = new InventorySlot[0];
    private InventoryItem[] _lastEquipItems = new InventoryItem[0];
    private int _lastEquipGridChildCount = -1;

    public void RefreshEquipSlotsFromGrid()
    {
        if (equipGridRoot == null)
        {
            // Keep this warning mild; some prefabs may not use the grid.
            _equipSlotsRuntime = new InventorySlot[0];
            _lastEquipItems = new InventoryItem[0];
            _lastEquipGridChildCount = -1;
            return;
        }

        _equipSlotsRuntime = equipGridRoot.GetComponentsInChildren<InventorySlot>(includeInactive: true);
        _lastEquipItems = new InventoryItem[_equipSlotsRuntime.Length];

        for (int i = 0; i < _equipSlotsRuntime.Length; i++)
            _lastEquipItems[i] = _equipSlotsRuntime[i].GetComponentInChildren<InventoryItem>();

        _lastEquipGridChildCount = equipGridRoot.childCount;
    }

    private void LateUpdate()
    {
        if (equipGridRoot == null) return;

        // Auto-refresh if slots are added/removed under EquipGrid1
        if (_equipSlotsRuntime == null || _equipSlotsRuntime.Length == 0 || equipGridRoot.childCount != _lastEquipGridChildCount)
            RefreshEquipSlotsFromGrid();

        for (int i = 0; i < _equipSlotsRuntime.Length; i++)
        {
            var slot = _equipSlotsRuntime[i];
            if (slot == null) continue;

            var currentItem = slot.GetComponentInChildren<InventoryItem>();
            if (i < _lastEquipItems.Length && currentItem == _lastEquipItems[i]) continue;

            if (i < _lastEquipItems.Length)
                _lastEquipItems[i] = currentItem;

            if (currentItem != null && currentItem.item != null)
            {
                if (logOnEquipItemPlaced)
                    Debug.Log($"[HeroStats] {name} equipped: {currentItem.item.itemName} (slot {i})");
            }
        }
    }

    public void SetEquipGridRoot(Transform root)
    {
        equipGridRoot = root;
        RefreshEquipSlotsFromGrid();
    }

    // ---------------- Per-Turn Combat API ----------------
    public void ResetTurnCombatState()
    {
        turnAttackMultiplier = 1.0f;
        maxDamageAttacksThisTurn = int.MaxValue;
        damageAttacksUsedThisTurn = 0;

        // Triple Blade / turn-only flags
        tripleBladeEmpoweredThisTurn = false;

        // Once-per-turn abilities
        if (_abilityLastUsedOnPlayerTurn != null)
            _abilityLastUsedOnPlayerTurn.Clear();
    }

    /// <summary>
    /// Applies a turn-only attack multiplier. Multipliers stack multiplicatively if called multiple times.
    /// </summary>
    public void MultiplyTurnAttack(float multiplier)
    {
        multiplier = Mathf.Max(0f, multiplier);
        turnAttackMultiplier *= multiplier;
    }

    /// <summary>
    /// Constrains how many damaging attacks the hero can commit this turn.
    /// Example: if called with 1, the hero can only commit one damaging attack.
    /// If called multiple times, the tightest constraint wins (min).
    /// </summary>
    public void ConstrainDamageAttacksThisTurn(int maxAttacks)
    {
        maxAttacks = Mathf.Max(0, maxAttacks);
        maxDamageAttacksThisTurn = Mathf.Min(maxDamageAttacksThisTurn, maxAttacks);
    }

    /// <summary>
    /// Returns true if this hero is allowed to commit a damaging attack right now (per-turn constraint).
    /// </summary>
    public bool CanCommitDamageAttackThisTurn()
    {
        return damageAttacksUsedThisTurn < maxDamageAttacksThisTurn;
    }

    /// <summary>
    /// Call this when a damaging attack is actually committed/executed (not when previewing).
    /// </summary>
    public void RegisterDamageAttackCommitted()
    {
        damageAttacksUsedThisTurn++;

        // Triple Blade: after your empowered (double damage) attack, you become stunned for the remainder of the current player phase.
        if (tripleBladeEmpoweredThisTurn)
        {
            tripleBladeEmpoweredThisTurn = false;
            StunForRemainderOfPlayerPhase();
        }
    }

    public int DamageAttacksUsedThisTurn => damageAttacksUsedThisTurn;
    public int MaxDamageAttacksThisTurn => maxDamageAttacksThisTurn;
    public float TurnAttackMultiplier => turnAttackMultiplier;

    // Preferred equipment source-of-truth:
    // Your current equip system parents InventoryItem GameObjects under EquipGridRoot -> InventorySlot.
    // So we detect equipment by scanning InventoryItem children, not by relying on inspector-wired arrays.
    private InventoryItem[] GetEquippedInventoryItems()
    {
        if (equipGridRoot == null) return Array.Empty<InventoryItem>();
        return equipGridRoot.GetComponentsInChildren<InventoryItem>(includeInactive: true);
    }

    // ---------------- Equipment Queries ----------------
    public bool HasEquippedEffect(ItemEffect effect)
    {
        // Preferred: scan actual equipped InventoryItem children under equipGridRoot
        var equipped = GetEquippedInventoryItems();
        for (int i = 0; i < equipped.Length; i++)
        {
            var ii = equipped[i];
            if (ii == null || ii.item == null) continue;

            if (ii.item.effects != null && ii.item.effects.Contains(effect))
                return true;
        }

        // Fallback: legacy inspector-wired equipmentSlots array
        if (equipmentSlots == null) return false;

        for (int i = 0; i < equipmentSlots.Length; i++)
        {
            InventorySlot slot = equipmentSlots[i];
            if (slot == null) continue;

            InventoryItem invItem = slot.GetComponentInChildren<InventoryItem>();
            if (invItem == null || invItem.item == null) continue;

            if (invItem.item.effects != null && invItem.item.effects.Contains(effect))
                return true;
        }

        return false;
    }

    public bool HasEquippedItemName(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return false;

        // Preferred: scan actual equipped InventoryItem children under equipGridRoot
        var equipped = GetEquippedInventoryItems();
        for (int i = 0; i < equipped.Length; i++)
        {
            var ii = equipped[i];
            if (ii == null || ii.item == null) continue;

            if (string.Equals(ii.item.itemName, itemName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Fallback: legacy inspector-wired equipmentSlots array
        if (equipmentSlots == null) return false;

        for (int i = 0; i < equipmentSlots.Length; i++)
        {
            InventorySlot slot = equipmentSlots[i];
            if (slot == null) continue;

            InventoryItem invItem = slot.GetComponentInChildren<InventoryItem>();
            if (invItem == null || invItem.item == null) continue;

            if (string.Equals(invItem.item.itemName, itemName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    // ---------------- Runtime Copy / Evolution Helpers ----------------
    /// <summary>
    /// Replaces this hero's reel strip with a fresh runtime instance cloned from the given template.
    /// Use this when evolving to an advanced class so we don't mutate shared ScriptableObject assets.
    /// </summary>
    public void ReplaceReelStripFromTemplate(ReelStripSO newStripTemplate)
    {
        if (newStripTemplate == null) return;
        reelStrip = Instantiate(newStripTemplate);
        NotifyChanged();
    }

    /// <summary>Overrides the hero portrait used by UI panels (PartyHUD, etc.).</summary>
    public void SetPortrait(Sprite newPortrait)
    {
        portrait = newPortrait;
        NotifyChanged();
    }

    /// <summary>
    /// Copies the complete runtime state from another HeroStats into this instance.
    /// Intended for prefab swaps (e.g., Fighter -> Templar) where we want to preserve progress.
    /// </summary>
    public void CopyRuntimeStateFrom(HeroStats src)
    {
        if (src == null) return;

        // Core progression
        maxLevel = src.maxLevel;
        level = src.level;
        xp = src.xp;
        xpToNextLevel = src.xpToNextLevel;
        allowLevelUps = src.allowLevelUps;
        pendingLevelUps = src.pendingLevelUps;

        // Primary stats
        maxHp = src.maxHp;
        currentHp = src.currentHp;
        attack = src.attack;
        defense = src.defense;

        // Stamina
        maxStamina = src.maxStamina;
        currentStamina = src.currentStamina;
        staminaCostPerAttack = src.staminaCostPerAttack;
        blockHoldDrainMultiplier = src.blockHoldDrainMultiplier;
        blockImpactCostMultiplier = src.blockImpactCostMultiplier;

        // Combat runtime
        currentShield = src.currentShield;
        isHidden = src.isHidden;
        bleedStacks = src.bleedStacks;
        bleedAppliedOnPlayerTurn = src.bleedAppliedOnPlayerTurn;
        isStunned = src.isStunned;

        // Gold/keys/etc.
        gold = src.gold;
        smallKeys = src.smallKeys;
        largeKeys = src.largeKeys;

        // Class modifiers
        canBlock = src.canBlock;
        attackFlatBonus = src.attackFlatBonus;
        attackMultiplier = src.attackMultiplier;

        // Per-turn multipliers/limits (safe to copy)
        turnAttackMultiplier = src.turnAttackMultiplier;
        maxDamageAttacksThisTurn = src.maxDamageAttacksThisTurn;
        damageAttacksUsedThisTurn = src.damageAttacksUsedThisTurn;
        selfDamagePerAttack = src.selfDamagePerAttack;

        poisonStacksOnHit = src.poisonStacksOnHit;
        poisonDpsPerStack = src.poisonDpsPerStack;
        poisonDurationSeconds = src.poisonDurationSeconds;

        tripleBladeEmpoweredThisTurn = src.tripleBladeEmpoweredThisTurn;

        // Class defs
        baseClassDef = src.baseClassDef;
        advancedClassDef = src.advancedClassDef;

        // Abilities / unlocks
        startingAbilityOverride = src.startingAbilityOverride;
        permanentlyUnlockedAbilities = (src.permanentlyUnlockedAbilities != null)
            ? new List<AbilityDefinitionSO>(src.permanentlyUnlockedAbilities)
            : new List<AbilityDefinitionSO>();

        // Reel upgrades + rules
        upgradeReelOnLevelUp = src.upgradeReelOnLevelUp;
        reelUpgradeRules = src.reelUpgradeRules;
        pendingReelUpgrades = src.pendingReelUpgrades;

        // Copy the runtime reel strip (already per-hero)
        if (src.reelStrip != null)
            reelStrip = Instantiate(src.reelStrip);

        // Portrait (optional)
        portrait = src.portrait;

        // Per-turn ability usage tracker is runtime and can be reset safely.
        _abilityLastUsedOnPlayerTurn = null;

        NotifyChanged();
    }

    /// <summary>
    /// Adds abilities from the provided class definition into the permanently-unlocked list.
    /// Used by evolution to ensure the advanced class' abilities show up immediately.
    /// </summary>
    public void ForceUnlockAllAbilitiesFromClassDef(ClassDefinitionSO def, bool includeStarterChoice = true)
    {
        if (def == null || def.abilities == null) return;

        if (permanentlyUnlockedAbilities == null)
            permanentlyUnlockedAbilities = new List<AbilityDefinitionSO>();

        int added = 0;
        for (int i = 0; i < def.abilities.Count; i++)
        {
            var a = def.abilities[i];
            if (a == null) continue;
            if (!includeStarterChoice && a.starterChoice) continue;

            if (!permanentlyUnlockedAbilities.Contains(a))
            {
                permanentlyUnlockedAbilities.Add(a);
                added++;
            }
        }

        Debug.Log($"[Evolution][HeroStats] ForceUnlockAllAbilitiesFromClassDef def='{def.className}' added={added} totalUnlocked={permanentlyUnlockedAbilities.Count}", this);
        NotifyChanged();
    }

    public bool HasAbilityUnlocked(string abilityName)
    {
        if (permanentlyUnlockedAbilities == null) return false;

        for (int i = 0; i < permanentlyUnlockedAbilities.Count; i++)
        {
            var a = permanentlyUnlockedAbilities[i];
            if (a != null && a.abilityName == abilityName) 
                return true;
        }
        return false;
    }
}


