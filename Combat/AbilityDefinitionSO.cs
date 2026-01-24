// GUID: cf56299f5b00af345b24e257cb33b22b
////////////////////////////////////////////////////////////
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
    FireElemental
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

    [Header("Status Effects")]
    public bool inflictsFocusRune = false;
    public bool inflictsBurn = false;
    public bool inflictsFreeze = false;
}


////////////////////////////////////////////////////////////


////////////////////////////////////////////////////////////


