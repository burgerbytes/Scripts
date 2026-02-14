// GUID: cf56299f5b00af345b24e257cb33b22b
////////////////////////////////////////////////////////////
using System.Collections.Generic;
using UnityEngine;

public enum AbilityTargetType
{
    Enemy,
    Self,
    Ally
}

public enum AbilityTag
{
    Assassinate,
    Piercing,
    FireElemental,
    Momentum
}

/// <summary>
/// Status effects an ability can remove from a Hero target.
/// (We only include effects that exist in the current runtime status systems.)
/// </summary>
public enum RemovableStatusEffect
{
    Bleeding,
    Stunned
}

/// <summary>
/// High-level ability type.
/// Active abilities appear in the Ability Menu and are clicked to execute.
/// Passive abilities are always-on listeners that react to gameplay events.
/// </summary>
public enum AbilityKind
{
    Active,
    Passive
}

[CreateAssetMenu(menuName = "Combat/Ability Definition")]
public class AbilityDefinitionSO : ScriptableObject
{
    [Header("Identity")]
    public string abilityName;
    [TextArea(2, 6)] public string description;
    public Sprite icon;

    [Header("Animation")]
    [Tooltip("Stable animation key used to map this ability to an Animator state via CasterAnimationProfile (e.g. 'BasicAttack', 'MageCast'). Leave blank to fall back to legacy name-based mapping.")]
    public string animationKey;

    [Header("Type")]
    [Tooltip("Active abilities are executed by the player. Passive abilities are always-on event listeners.")]
    public AbilityKind kind = AbilityKind.Active;
    public bool isDamaging;

    [Header("Targeting")]
    public AbilityTargetType targetType = AbilityTargetType.Enemy;

    [Header("Costs")]
    public ResourceCost cost = new ResourceCost(0, 0, 0, 0);

    [Header("Unlock / Starter")]
    [Tooltip("If true, this ability can be chosen as the hero's starting ability on the Class Selection panel.")]
    public bool starterChoice = false;

    [Tooltip("Minimum hero level required for this ability to appear in the Ability Menu.")]
    [Min(1)] public int unlockAtLevel = 1;


    [Header("Damage / Defense")]
    public int baseDamage = 0;
    public int shieldAmount = 0;
    public ElementType element = ElementType.Physical;

    [Header("Healing")]
    [Tooltip("Flat HP restored to the target (clamped to Max HP).")]
    public int healAmount = 0;

    [Header("Cleansing")]
    [Tooltip("Status effects removed from the target when this ability resolves (Hero targets only).")]
    public List<RemovableStatusEffect> removesStatusEffects = new List<RemovableStatusEffect>();

    [Header("Tags")]
    [Tooltip("Optional tags that can add special rules and synergies.")]
    public List<AbilityTag> tags = new List<AbilityTag>();

    [Header("Special Rules")]

[Tooltip("If true, this ability consumes ALL current Attack resources (ATK) from the shared ResourcePool when cast. The ability is unusable if ATK is 0.")]
public bool spendAllAttackResources = false;

[Tooltip("If spendAllAttackResources is true, this many bonus damage is added per Attack resource consumed.")]
public int bonusDamagePerAttackResource = 2;


    [Tooltip("If true, this ability costs 0 Attack while the user is Hidden.")]
    public bool freeIfHidden = false;

    [Tooltip("If true, using this ability will break Hidden (set Hidden=false) after resolving.")]
    public bool breaksHidden = true;

    [Tooltip("If true, this ability sets the user Hidden=true.")]
    public bool grantsHidden = false;

    [Tooltip("If grantsHidden, this ability also clears existing enemy intents by making them miss (handled at resolution).")]
    public bool makesUntargetable = false;

    [Tooltip("If true, this ability can only be used once per player turn per hero. The UI will gray it out after use.")]
    public bool usableOncePerTurn = false;

    [Header("Momentum")]
    [Tooltip("If true, killing an enemy with this ability triggers an immediate bonus spin on the caster's reel and instantly cashes out ONLY that reel.")]
    public bool momentumOnKill = false;

    
    [Header("Combo")]
    [Tooltip("If true, casting this ability performs one or more bonus one-reel spins (does not consume SpinsRemaining). Each time a spin lands on comboTriggerType, the ability will queue additional casts based on the resource gain. This can chain until comboMaxTotalCasts is reached.")]
    public bool hasCombo = false;

    [Tooltip("If hasCombo: the ability repeats when the bonus spin lands on this resource type.")]
    public ReelSpinSystem.ResourceType comboTriggerType = ReelSpinSystem.ResourceType.Attack;

    [Tooltip("Legacy safety cap for extra casts. If comboMaxTotalCasts is 0, max total casts = 1 + comboMaxExtraCasts.")]
    [Min(0)] public int comboMaxExtraCasts = 8;

    [Tooltip("Safety cap for the total number of casts (including the initial cast) that can occur from combo chaining. Set to 0 to use 1 + comboMaxExtraCasts.")]
    [Min(0)] public int comboMaxTotalCasts = 0;

    [Tooltip("If true, whenever a combo spin lands on comboTriggerType, the NEXT cast will retarget to a random living enemy.")]
    public bool comboRandomizeNextEnemyTargetOnTrigger = false;

    [Tooltip("If true, each extra cast (beyond the initial cast) ALSO grants the same resources from the combo spin and re-fires passive procs as if that symbol landed again. If you enable per-repeat spins (BattleManager logic), this flag is ignored and each repeat uses its own spin result.")]
    public bool comboExtraCastsGrantResourcesAndProcPassives = true;

    [Header("Combo Spin Speed")]
    [Tooltip("Speed multiplier applied to the FIRST combo spin (1 = normal).")]
    [Min(0.1f)] public float comboSpinSpeedMultiplierStart = 1f;

    [Tooltip("Additional speed multiplier added per extra combo spin (linear). Example: start=1, step=0.35 -> 1.00, 1.35, 1.70, ...")]
    [Min(0f)] public float comboSpinSpeedMultiplierStep = 0.35f;

    [Tooltip("Clamp for the combo spin speed multiplier.")]
    [Min(0.1f)] public float comboSpinSpeedMultiplierMax = 3f;

[Header("Status Effects")]
    public bool inflictsFocusRune = false;
    public bool inflictsBurn = false;
    public bool inflictsFreeze = false;


    [Header("Summon (Experimental)")]
    [Tooltip("Optional: if assigned, this ability can summon a monster prefab. (Requires battle-side support for ally summons; currently used for enemy summons.)")]
    public GameObject summonPrefab;

    [Tooltip("How many to summon when this ability resolves (if summonPrefab is assigned).")]
    [Min(1)]
    public int summonCount = 1;
}


////////////////////////////////////////////////////////////

////////////////////////////////////////////////////////////

