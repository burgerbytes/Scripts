// PATH: Assets/Scripts/Combat/AbilityDefinitionSO.cs
using UnityEngine;

public enum AbilityTargetType
{
    Enemy,
    Self,
    Ally
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

    [Header("Special Rules")]
    [Tooltip("If true, this ability costs 0 Attack while the user is Hidden.")]
    public bool freeIfHidden = false;

    [Tooltip("If true, using this ability will break Hidden (set Hidden=false) after resolving.")]
    public bool breaksHidden = true;

    [Tooltip("If true, this ability sets the user Hidden=true.")]
    public bool grantsHidden = false;

    [Tooltip("If grantsHidden, this ability also clears existing enemy intents by making them miss (handled at resolution).")]
    public bool makesUntargetable = false;
}
