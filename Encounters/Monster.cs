// GUID: ea47960c4ce364a4980645e51f542a03
////////////////////////////////////////////////////////////
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

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

    // ✅ These are required by MonsterHpBar.cs
    public int MaxHp => maxHp;
    public int CurrentHp => _currentHp;
    public int Defense => defense;

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
        OnHpChanged?.Invoke(_currentHp, maxHp);

        return Mathf.Max(0, before - _currentHp);
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

