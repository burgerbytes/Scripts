using UnityEngine;

/// <summary>
/// Simple static handoff from the StartupClassSelectionPanel to runtime systems.
/// This avoids changing callback signatures/wiring.
/// </summary>
public static class StartupPartySelectionData
{
    // Slot order matches party slots (0..2).
    private static AbilityDefinitionSO[] _startingAbilities = new AbilityDefinitionSO[3];

    public static void Clear()
    {
        for (int i = 0; i < _startingAbilities.Length; i++)
            _startingAbilities[i] = null;
    }

    public static void SetStartingAbility(int slot, AbilityDefinitionSO ability)
    {
        if (slot < 0 || slot >= _startingAbilities.Length) return;
        _startingAbilities[slot] = ability;
    }

    public static AbilityDefinitionSO GetStartingAbility(int slot)
    {
        if (slot < 0 || slot >= _startingAbilities.Length) return null;
        return _startingAbilities[slot];
    }
}
