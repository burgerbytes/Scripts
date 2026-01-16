using System;
using UnityEngine;

public class HeroStats : MonoBehaviour
{
    [Header("Progression")]
    [SerializeField] private int level = 1;
    [SerializeField] private int xp = 0;
    [SerializeField] private int xpToNextLevel = 10;

    [Tooltip("If false, XP can still be earned, but LevelUp will NOT occur. Instead, PendingLevelUps increases.")]
    [SerializeField] private bool allowLevelUps = false;

    [Tooltip("How many level-ups are queued to be resolved at the campfire.")]
    [SerializeField] private int pendingLevelUps = 0;

    [Header("Core Stats")]
    [SerializeField] private int maxHp = 100;
    [SerializeField] private int attack = 3;
    [SerializeField] private int defense = 0;
    [SerializeField] private int speed = 10;

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

    [Header("Resources")]
    [SerializeField] private long gold = 0;

    [Tooltip("Persistent keys used for opening post-battle chests.")]
    [SerializeField] private int smallKeys = 0;
    [SerializeField] private int largeKeys = 0;

    [Header("Runtime")]
    [SerializeField] private int currentHp = 100;
    [SerializeField] private float currentStamina = 100f;

    // ---------------- Combat Runtime Status (NEW) ----------------
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

    // ✅ NEW: Reel strip + portrait come from the hero prefab
    [Header("Reels / UI (Prefab Data)")]
    [Tooltip("Reel strip used for this hero's reel.")]
    [SerializeField] private ReelStripSO reelStrip;

    [Tooltip("Portrait sprite used for this hero's reel picker button / UI.")]
    [SerializeField] private Sprite portrait;

    public ReelStripSO ReelStrip => reelStrip;
    public Sprite Portrait => portrait;

    // ---------------- Equipment (NEW) ----------------
    [Header("Equipment (UI only, no effects yet)")]
    public int equipmentSlotSize = 1;
    [SerializeField] public InventorySlot[] equipmentSlots = new InventorySlot[1];

    public int EquipmentSlotCount => equipmentSlots != null ? equipmentSlots.Length : 0;

    // ---------------- Equipment Change Events (NEW) ----------------
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

    public float ClassAttackModifier => Mathf.Max(0f, attackMultiplier) * Mathf.Max(0f, turnAttackMultiplier);

    public event Action OnChanged;

    private void Awake()
    {
        currentHp = Mathf.Clamp(currentHp, 0, maxHp);
        currentStamina = Mathf.Clamp(currentStamina, 0f, maxStamina);
        currentShield = Mathf.Max(0, currentShield);

        InitEquipmentWatcher();      // legacy array watcher (safe to keep)
        RefreshEquipSlotsFromGrid(); // runtime EquipGrid watcher
        NotifyChanged();
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

    /// <summary>
    /// Adds Bleeding stacks.
    /// Bleeding deals 1 HP per stack at the start of each Player Phase,
    /// then reduces stacks by 1.
    /// </summary>
    public void AddBleedStacks(int stacks)
    {
        if (stacks <= 0) return;
        bleedStacks = Mathf.Max(0, bleedStacks + stacks);

        // Record the player turn this was applied so we can skip ticking on the same turn.
        if (BattleManager.Instance != null)
            bleedAppliedOnPlayerTurn = BattleManager.Instance.PlayerTurnNumber;

        NotifyChanged();
    }

    /// <summary>
    /// Ticks Bleeding once for this turn.
    /// Returns the HP damage applied.
    /// NOTE: Bleed damage bypasses Shield and Defense (direct HP loss).
    /// </summary>
    public int TickBleedingAtTurnStart()
    {
        // Backwards-compat wrapper. Bleed now ticks at END of the player's turn.
        int turn = (BattleManager.Instance != null) ? BattleManager.Instance.PlayerTurnNumber : 0;
        return TickBleedingAtEndOfPlayerTurn(turn);
    }

    /// <summary>
    /// Ticks Bleeding at the END of the player's turn.
    /// - Deals HP damage equal to current stacks
    /// - Then reduces stacks by 1
    /// - Does NOT tick on the same player turn it was applied
    /// Returns the HP damage applied (bypasses Shield/Defense).
    /// </summary>
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



    // ---------------- Stun ----------------

    /// <summary>
    /// Clears any "remainder of current player phase" stun and consumes any queued stun (from enemy abilities)
    /// so it applies to this player phase.
    /// Call this once at the start of Player Phase.
    /// </summary>
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

    /// <summary>
    /// Stun this hero immediately for the remainder of the current player phase.
    /// </summary>
    public void StunForRemainderOfPlayerPhase()
    {
        if (isStunned) return;
        isStunned = true;
        NotifyChanged();
    }

    /// <summary>
    /// Queue a stun so the hero is unable to act for upcoming Player Phases.
    /// Use this when an enemy stuns the hero during Enemy Phase.
    /// </summary>
    public void StunForNextPlayerPhases(int playerPhases = 1)
    {
        if (playerPhases <= 0) return;
        stunnedPlayerPhasesRemaining += playerPhases;
        NotifyChanged();
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
        LevelUp();
        NotifyChanged();
        return true;
    }

    private void LevelUp()
    {
        level += 1;

        maxHp += 10;
        attack += 1;
        defense += 1;

        currentHp = maxHp;

        maxStamina += 5;
        currentStamina = Mathf.Min(currentStamina, maxStamina);

        xpToNextLevel = Mathf.RoundToInt(xpToNextLevel * 1.25f) + 5;
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

    [SerializeField] private Transform equipGridRoot; // assign EquipGrid1
    private InventorySlot[] _equipSlotsRuntime = new InventorySlot[0];
    private InventoryItem[] _lastEquipItems = new InventoryItem[0];
    private int _lastEquipGridChildCount = -1;

    public void RefreshEquipSlotsFromGrid()
    {
        if (equipGridRoot == null)
        {
            Debug.LogWarning($"[EquipGrid] {name}: equipGridRoot not set.");
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
}

////////////////////////////////////////////////////////////

