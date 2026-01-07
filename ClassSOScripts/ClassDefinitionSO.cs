// PATH: Assets/Scripts/Classes/ClassDefinitionSO.cs
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Classes/Class Definition")]
public class ClassDefinitionSO : ScriptableObject
{
    public enum Tier
    {
        Base = 1,
        Advanced = 2
    }

    [Header("Identity")]
    public string className;
    [TextArea(2, 6)] public string description;
    public Sprite icon;

    [Header("Tiering")]
    public Tier tier = Tier.Base;

    [Tooltip("For Advanced classes, which Base class is required?")]
    public ClassDefinitionSO requiredBaseClass;

    [Header("Modifiers")]
    public bool canBlock = true;

    public int attackFlatBonus = 0;
    public float attackMultiplier = 1.0f;

    public float staminaCostPerAttackMultiplier = 1.0f;
    public float blockHoldDrainMultiplier = 1.0f;
    public float blockImpactCostMultiplier = 1.0f;

    [Header("Abilities (legacy 2-slot)")]
    public AbilityDefinitionSO ability1;
    public AbilityDefinitionSO ability2;

    [Header("Abilities (new list)")]
    public List<AbilityDefinitionSO> abilities = new List<AbilityDefinitionSO>();
}
