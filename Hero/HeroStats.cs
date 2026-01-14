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
    public int Attack => Mathf.Max(0, Mathf.RoundToInt((attack + attackFlatBonus) * Mathf.Max(0f, attackMultiplier)));
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

    public float ClassAttackModifier => Mathf.Max(0f, attackMultiplier);

    public event Action OnChanged;

    private void Awake()
    {
        currentHp = Mathf.Clamp(currentHp, 0, maxHp);
        currentStamina = Mathf.Clamp(currentStamina, 0f, maxStamina);
        currentShield = Mathf.Max(0, currentShield);


        InitEquipmentWatcher();
        NotifyChanged();
    }

    private void NotifyChanged() => OnChanged?.Invoke();


    // ---------------- Equipment Change Detection ----------------
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

        NotifyChanged();
    }

    public void ClearStartOfBattleStatuses()
    {
        currentShield = 0;
        isHidden = false;
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

    public void GetEquippedItems()
    {
        for (int i = 0; i< equipmentSlots.Length; i++)
        {
            InventorySlot slot = equipmentSlots[i];
            InventoryItem itemInSlot = slot.GetComponentInChildren<InventoryItem>();
            if (itemInSlot != null)
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
            if (currentItem == _lastEquipItems[i]) continue;

            _lastEquipItems[i] = currentItem;

            if (currentItem != null && currentItem.item != null)
            {
                Debug.Log($"[EquipGrid] {name} equipped: {currentItem.item.itemName} (slot {i})");
            }
        }
    }
    
    public void SetEquipGridRoot(Transform root)
    {
        equipGridRoot = root;
    }
}