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
    }

    public event Action<int, int> OnHpChanged;

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
        // Requires Collider (3D) or Collider2D on this monster (or a child).
        // Always forward clicks to BattleManager; BattleManager will ignore if not currently awaiting a target.
        if (_battleManager == null)
            _battleManager = BattleManager.Instance != null ? BattleManager.Instance : FindFirstObjectByType<BattleManager>();

        if (_battleManager != null)
        {
            Debug.Log($"[Combat Click] Forwarding click to BattleManager.SelectEnemyTarget | Monster={name}", this);
            _battleManager.SelectEnemyTarget(this);
            Debug.Log($"[Combat Click] Called SelectEnemyTarget on BattleManager={_battleManager.name} ({_battleManager.GetInstanceID()}) | Monster={name}", this);
        }
        else
        {
            Debug.LogWarning($"[Combat Click] Could not forward click (BattleManager not found) | Monster={name}", this);
        }

        // Debug-only: print pending cast state if present.
        var state = AbilityCastState.Instance;
        if (state != null && state.HasPendingCast)
        {
            Debug.Log("[Combat Click] Pending ability cast detected!\n" +
                      $"Caster: {(state.CurrentCaster != null ? state.CurrentCaster.name : "NULL")}\n" +
                      $"Ability: {(state.CurrentAbility != null ? state.CurrentAbility.abilityName : "NULL")}\n" +
                      $"Clicked Monster: {name}", this);
        }
        else
        {
            Debug.Log($"[Combat Click] No pending cast | Clicked monster: {name}", this);
        }

        Debug.Log(BuildMonsterDebugString(), this);
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
    public int TakeDamageFromAbility(int abilityBaseDamage, float classAttackModifier, ElementType element)
    {
        if (_currentHp <= 0)
            return 0;

        float scaled = Mathf.Max(0, abilityBaseDamage) * Mathf.Max(0f, classAttackModifier);
        float afterDef = scaled - Mathf.Max(0, defense);
        float resisted = afterDef * GetResistance(element);

        int final = Mathf.RoundToInt(resisted);
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
}


/////////////////////