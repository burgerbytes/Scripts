// PATH: Assets/Scripts/Encounters/Monster.cs
// GUID: ea47960c4ce364a4980645e51f542a03
////////////////////////////////////////////////////////////
using System;
using UnityEngine;
using System.Collections; 

public class Monster : MonoBehaviour
{
    [Serializable]
    public class MonsterAttack
    {
        public string id = "Basic";

        [Tooltip("Damage dealt to the hero when this attack lands.")]
        public int damage = 8;

        [Tooltip("Controls how fast the attack telegraph/animation plays. Higher = faster.")]
        public int speed = 10;

        [Tooltip("AoE abilities hit all allies.")]
        public bool isAoe = false;

        [Header("Status Effects")]
        [Tooltip("If true, this attack will stun the target hero (preventing them from acting) for upcoming Player Phases.")]
        public bool stunsTarget = false;

        [Tooltip("How many upcoming Player Phases the target is stunned for (typically 1).")]
        public int stunPlayerPhases = 1;

        [Header("Bleeding")]
        [Tooltip("If true, this attack applies Bleeding stacks to the target hero.")]
        public bool appliesBleed = false;

        [Tooltip("How many Bleeding stacks are applied when this attack lands.")]
        public int bleedStacks = 1;
    }

    public event Action<int, int> OnHpChanged;

    
    public enum MonsterTag
    {
        Beast,
        Inorganic,
        Fire_Elemental,
        Ice_Elemental
    }

    [Header("Info")]
    [Tooltip("Short description shown in the Monster Info panel.")]
    [TextArea(2, 6)]
    [SerializeField] private string description = "";

    [Tooltip("The string that will be displayed in the Monster Name field.")]
    [TextArea(2, 6)]
    [SerializeField] public string DisplayName = "";

    [Tooltip("Tags/properties shown in the Monster Info panel.")]
    [SerializeField] private System.Collections.Generic.List<MonsterTag> tags = new System.Collections.Generic.List<MonsterTag>();

    public string Description => description;
    public System.Collections.Generic.IReadOnlyList<MonsterTag> Tags => tags;

    private bool HasTag(MonsterTag tag)
    {
        return tags != null && tags.Contains(tag);
    }

    private static bool HasAbilityTag(System.Collections.Generic.IReadOnlyList<AbilityTag> abilityTags, AbilityTag tag)
    {
        if (abilityTags == null) return false;
        for (int i = 0; i < abilityTags.Count; i++)
        {
            if (abilityTags[i] == tag)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Applies special rules based on ability tags vs monster tags.
    /// Currently:
    /// - FireElemental abilities deal double damage to Ice_Elemental monsters.
    /// - FireElemental abilities deal zero damage to Fire_Elemental monsters.
    /// </summary>
    private float GetDamageMultiplierForAbilityTags(System.Collections.Generic.IReadOnlyList<AbilityTag> abilityTags)
    {
        if (abilityTags == null || abilityTags.Count == 0)
            return 1f;

        bool isFireElementalAbility = false;
        for (int i = 0; i < abilityTags.Count; i++)
        {
            if (abilityTags[i] == AbilityTag.FireElemental)
            {
                isFireElementalAbility = true;
                break;
            }
        }

        if (!isFireElementalAbility)
            return 1f;

        if (HasTag(MonsterTag.Fire_Elemental))
            return 0f;

        if (HasTag(MonsterTag.Ice_Elemental))
            return 2f;

        return 1f;
    }

    [Header("Core Stats")]
    [SerializeField] private int maxHp = 10;

    [Tooltip("Reduces incoming hero damage. Used in legacy TakeDamage() and in ability formula.")]
    [SerializeField] private int defense = 0;

    [Header("Rewards")]
    [Tooltip("XP awarded to the hero that lands the killing blow.")]
    [SerializeField] private int xpReward = 5;

    [Tooltip("Gold rewarded when this monster dies. If Max > Min, the value is rolled inclusively.")]
    [SerializeField] private Vector2Int goldRewardRange = new Vector2Int(0, 0);

    [Header("Attacks")]
    [SerializeField] private MonsterAttack[] attacks = new MonsterAttack[] { new MonsterAttack() };
    [SerializeField] private int defaultAttackIndex = 0;

    [Header("Elemental Resistances (1.0 = normal)")]
    [SerializeField] private float physicalResistance = 1f;
    [SerializeField] private float electricResistance = 1f;

    [Header("Death FX")]
    [SerializeField] private bool destroyOnDeath = true;
    [SerializeField] private float destroyDelaySeconds = 0.25f;

    [Header("Runtime")]
    [SerializeField] private int _currentHp = 10;



    [Header("Debug")]
    [SerializeField] private bool debugDamageLogs = false;

    [Header("Status: Bleeding")]
    [SerializeField] private int _bleedStacks = 0;

    [Tooltip("The PlayerTurnNumber when Bleed was most recently applied. Used to prevent same-turn ticking.")]
    [SerializeField] private int _bleedAppliedOnPlayerTurn = -999;

    public int BleedStacks => _bleedStacks;
    public bool IsBleeding => _bleedStacks > 0;
    public int BleedAppliedOnPlayerTurn => _bleedAppliedOnPlayerTurn;
    // ✅ These are required by MonsterHpBar.cs
    public int MaxHp => maxHp;
    public int CurrentHp => _currentHp;
    public int Defense => defense;

    // Rewards (used by BattleManager for victory XP/Gold summaries)
    public int XpReward => Mathf.Max(0, xpReward);

    public int RollGoldReward()
    {
        int min = Mathf.Min(goldRewardRange.x, goldRewardRange.y);
        int max = Mathf.Max(goldRewardRange.x, goldRewardRange.y);
        return UnityEngine.Random.Range(min, max + 1);
    }

    public bool IsDead => _currentHp <= 0;

    // True when the monster's default attack is configured as AoE.
    public bool IsDefaultAttackAoE
    {
        get
        {
            var atk = GetDefaultAttack();
            return atk != null && atk.isAoe;
        }
    }

    private BattleManager _battleManager;

    private void Awake()
    {
        _battleManager = BattleManager.Instance != null ? BattleManager.Instance : FindFirstObjectByType<BattleManager>();

        if (_currentHp <= 0) _currentHp = maxHp;
        _currentHp = Mathf.Clamp(_currentHp, 0, maxHp);
        OnHpChanged?.Invoke(_currentHp, maxHp);
    }

    // ✅ Click-to-target: if an ability is pending, this click selects the target and resolves damage.
    private void OnMouseDown()
    {
        // Targeting is handled centrally by BattleManager.Update() raycasts.
        // IMPORTANT: Do NOT forward clicks from here, or a single click will be processed twice
        // (OnMouseDown + BattleManager.Update), causing instant cast instead of preview.
        return;
    }

    private string BuildMonsterDebugString()
    {
        var atk = GetDefaultAttack();
        string atkId = atk != null ? atk.id : "None";
        int atkDmg = atk != null ? atk.damage : 0;
        int atkSpd = atk != null ? atk.speed : 0;

        return
            $"[Monster Debug]\n" +
            $"Name: {gameObject.name}\n" +
            $"HP: {_currentHp}/{maxHp}\n" +
            $"Defense: {defense}\n" +
            $"DefaultAttack: {atkId} (DMG {atkDmg}, SPD {atkSpd})\n" +
            $"Resist (Physical): {physicalResistance:0.###}\n" +
            $"Resist (Electric): {electricResistance:0.###}\n" +
            $"IsDead: {IsDead}\n";
    }

    public int GetSpeed()
    {
        var atk = GetDefaultAttack();
        return atk != null ? atk.speed : 10;
    }

    public int GetDamage()
    {
        var atk = GetDefaultAttack();
        return atk != null ? atk.damage : 0;
    }

    public bool DefaultAttackStunsTarget
    {
        get
        {
            var atk = GetDefaultAttack();
            return atk != null && atk.stunsTarget;
        }
    }

    public int DefaultAttackStunPlayerPhases
    {
        get
        {
            var atk = GetDefaultAttack();
            return atk != null ? Mathf.Max(1, atk.stunPlayerPhases) : 1;
        }
    }

    private MonsterAttack GetDefaultAttack()
    {
        if (attacks == null || attacks.Length == 0) return null;
        int idx = Mathf.Clamp(defaultAttackIndex, 0, attacks.Length - 1);
        return attacks[idx];
    }

    // ✅ Required by MonsterEncounterManager + PoisonReceiver
    public int TakeDamage(int incomingDamage)
    {
        if (_currentHp <= 0)
            return 0;

        int incoming = Mathf.Max(0, incomingDamage);
        if (incoming <= 0)
        {
            OnHpChanged?.Invoke(_currentHp, maxHp);
            return 0;
        }

        int actual = incoming - Mathf.Max(0, defense);
        if (actual < 1) actual = 1;

        return ApplyRawDamage(actual);
    }

    // ✅ Required by MonsterEncounterManager
    public float PlayDeathEffects()
    {
        if (destroyOnDeath)
            StartCoroutine(DeathRoutine());

        return Mathf.Max(0f, destroyDelaySeconds);
    }

    private IEnumerator DeathRoutine()
    {
        if (destroyDelaySeconds > 0f)
            yield return new WaitForSeconds(destroyDelaySeconds);

        Destroy(gameObject);
    }

    public float GetResistance(ElementType element)
    {
        switch (element)
        {
            case ElementType.Electric: return Mathf.Max(0f, electricResistance);
            case ElementType.Physical:
            default: return Mathf.Max(0f, physicalResistance);
        }
    }

    /// <summary>
    /// TotalDamage = ((abilityDamage * classAttackModifier) - enemyDefense) * elementalResistance
    /// Returns HP damage applied.
    /// </summary>

    /// <summary>
    /// Same formula as TakeDamageFromAbility, but DOES NOT apply damage.
    /// Useful for preview/ghost HP targeting UX.
    /// TotalDamage = ((abilityDamage * classAttackModifier) - enemyDefense) * elementalResistance
    /// </summary>
    public int CalculateDamageFromAbility(int abilityBaseDamage, float classAttackModifier, ElementType element, System.Collections.Generic.IReadOnlyList<AbilityTag> abilityTags = null)
    {
        if (_currentHp <= 0)
            return 0;

        float scaled = Mathf.Max(0, abilityBaseDamage) * Mathf.Max(0f, classAttackModifier);
        float afterDef = scaled - Mathf.Max(0, defense);
        float resisted = afterDef * GetResistance(element);

        float specialMult = GetDamageMultiplierForAbilityTags(abilityTags);
        float modified = resisted * specialMult;

        int final = Mathf.RoundToInt(modified);
        if (final < 0) final = 0;

        // Assassinate: if this hit would leave the target at 1 HP (or less), execute.
        // This matches the Ninja Backstab behavior ("kills when they'd have 1 HP after the hit").
        if (final > 0 && HasAbilityTag(abilityTags, AbilityTag.Assassinate))
        {
            int hpAfter = _currentHp - final;
            if (hpAfter <= 1)
                final = _currentHp; // preview lethal
        }

        return final;
    }

    public int TakeDamageFromAbility(int abilityBaseDamage, float classAttackModifier, ElementType element, System.Collections.Generic.IReadOnlyList<AbilityTag> abilityTags = null)
    {
        if (_currentHp <= 0)
            return 0;

        float scaled = Mathf.Max(0, abilityBaseDamage) * Mathf.Max(0f, classAttackModifier);
        float afterDef = scaled - Mathf.Max(0, defense);
        float resisted = afterDef * GetResistance(element);

        float specialMult = GetDamageMultiplierForAbilityTags(abilityTags);
        float modified = resisted * specialMult;

        int final = Mathf.RoundToInt(modified);
        if (final < 0) final = 0;

        // Assassinate: if this hit would leave the target at 1 HP (or less), execute.
        if (final > 0 && HasAbilityTag(abilityTags, AbilityTag.Assassinate))
        {
            int hpAfter = _currentHp - final;
            if (hpAfter <= 1)
                final = _currentHp; // make the hit lethal
        }

        if (final == 0)
        {
            OnHpChanged?.Invoke(_currentHp, maxHp);
            return 0;
        }

        return ApplyRawDamage(final);
    }

    private int ApplyRawDamage(int amount)
    {
        if (amount <= 0)
        {
            OnHpChanged?.Invoke(_currentHp, maxHp);
            return 0;
        }

        int before = _currentHp;
        _currentHp = Mathf.Max(0, _currentHp - amount);

        if (debugDamageLogs)
            Debug.Log($"[Monster] ApplyRawDamage END monster={name} hpAfter={_currentHp}/{maxHp} amount={amount} instance={GetInstanceID()}", this);
        OnHpChanged?.Invoke(_currentHp, maxHp);

        return Mathf.Max(0, before - _currentHp);
    }



    public void AddBleedStacks(int stacks)
    {
        if (stacks <= 0) return;
        _bleedStacks = Mathf.Max(0, _bleedStacks + stacks);

        if (BattleManager.Instance != null)
            _bleedAppliedOnPlayerTurn = BattleManager.Instance.PlayerTurnNumber;
    }

    public void SetBleedStacks(int stacks)
    {
        _bleedStacks = Mathf.Max(0, stacks);
        if (_bleedStacks <= 0)
            _bleedAppliedOnPlayerTurn = -999;
    }

    public void SetBleedStacks(int stacks, int appliedOnPlayerTurn)
    {
        _bleedStacks = Mathf.Max(0, stacks);
        _bleedAppliedOnPlayerTurn = appliedOnPlayerTurn;
        if (_bleedStacks <= 0)
            _bleedAppliedOnPlayerTurn = -999;
    }

    /// <summary>
    /// Called at the start of Enemy Phase. Deals 1 HP per stack, then reduces stacks by 1.
    /// Returns the amount of HP lost from the bleed tick.
    /// </summary>
    public int TickBleedingAtTurnStart()
    {
        // Backwards-compat wrapper. Bleed now ticks at END of the player's turn.
        int turn = (BattleManager.Instance != null) ? BattleManager.Instance.PlayerTurnNumber : 0;
        return TickBleedingAtEndOfPlayerTurn(turn);
    }

    /// <summary>
    /// Ticks Bleeding at the END of the player's turn (starting after the turn it was applied).
    /// Deals HP damage equal to stacks, then reduces stacks by 1.
    /// Returns HP damage applied.
    /// </summary>
    public int TickBleedingAtEndOfPlayerTurn(int currentPlayerTurnNumber)
    {
        if (_bleedStacks <= 0 || IsDead) return 0;

        if (_bleedAppliedOnPlayerTurn == currentPlayerTurnNumber)
            return 0;

        int dmg = Mathf.Max(0, _bleedStacks);
        int dealt = ApplyRawDamage(dmg);

        _bleedStacks = Mathf.Max(0, _bleedStacks - 1);
        if (_bleedStacks <= 0)
            _bleedAppliedOnPlayerTurn = -999;

        return dealt;
    }

    /// <summary>
    /// Used by BattleManager Undo. Sets current HP directly and refreshes UI events.
    /// </summary>
    public void SetCurrentHp(int hp)
    {
        _currentHp = Mathf.Clamp(hp, 0, maxHp);
        OnHpChanged?.Invoke(_currentHp, maxHp);
    }

}




////////////////////////////////////////////////////////////
