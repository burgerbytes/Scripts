using UnityEngine;

/// <summary>
/// Central toggle for dev/debug-only gameplay features.
/// </summary>
public static class GameDebugSettings
{
    /// <summary>
    /// If false, abilities marked as debug-only (AbilityDefinitionSO.isDebugOnly) should be hidden/blocked.
    /// Default false to avoid accidental shipping.
    /// </summary>
    public static bool AllowDebugAbilities = false;

    public static bool IsAbilityAllowed(AbilityDefinitionSO ability)
    {
        if (ability == null) return false;
        if (ability.isDebugOnly && !AllowDebugAbilities) return false;
        return true;
    }
}
