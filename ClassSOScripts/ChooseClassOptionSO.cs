using UnityEngine;

[CreateAssetMenu(menuName = "Campfire/Options/Class/Choose Class")]
public class ChooseClassOptionSO : CampfireOptionSO
{
    [Header("Class to Apply")]
    public ClassDefinitionSO classDef;

    private void OnEnable()
    {
        type = OptionType.Class; // matches your existing CampfireOptionSO field name
    }

    public override bool IsEligible(HeroStats hero)
    {
        if (hero == null || classDef == null) return false;

        // Base class choice
        if (classDef.tier == ClassDefinitionSO.Tier.Base)
            return hero.CanChooseBaseClass();

        // Advanced upgrade choice
        return hero.CanUpgradeTo(classDef);
    }

    public override void Apply(HeroStats hero)
    {
        if (hero == null || classDef == null) return;
        hero.ApplyClassDefinition(classDef);
    }
}
