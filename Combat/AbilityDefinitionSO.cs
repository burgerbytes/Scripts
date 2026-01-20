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

[CreateAssetMenu(menuName = "Combat/Ability Definition")]
public class AbilityDefinitionSO : ScriptableObject
{
    [Header("Identity")]
    public string abilityName;
    [TextArea(2, 6)] public string description;
    public Sprite icon;

    [Header("Targeting")]
    public AbilityTargetType targetType = AbilityTargetType.Enemy;

    [Header("Costs")]
    public ResourceCost cost = new ResourceCost(0, 0, 0, 0);

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
}


////////////////////////////////////////////////////////////


////////////////////////////////////////////////////////////


