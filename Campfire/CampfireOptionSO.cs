// PATH: Assets/Scripts/Campfire/CampfireOptionSO.cs
using UnityEngine;

public abstract class CampfireOptionSO : ScriptableObject
{
    public enum OptionType { Item, Perk, Class }

    [Header("UI")]
    public string optionName;
    [TextArea(2, 6)] public string description;
    public Sprite icon;
    public OptionType type;

    [Header("Pros / Cons")]
    [TextArea(1, 3)] public string[] pros;
    [TextArea(1, 3)] public string[] cons;

    /// <summary>
    /// Whether this option can be offered / selected for the given hero right now.
    /// Derived option SOs override this.
    /// </summary>
    public virtual bool IsEligible(HeroStats hero)
    {
        return hero != null;
    }

    /// <summary>
    /// Apply the option's effects to the hero.
    /// </summary>
    public abstract void Apply(HeroStats hero);
}
