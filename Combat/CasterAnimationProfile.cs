using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class CasterAnimationProfile : MonoBehaviour
{
    [Header("Default attack animation state (Animator state name)")]
    [SerializeField] private string defaultAttackState = "fighter_basic_attack";

    [Header("Optional per-ability override (match AbilityDefinitionSO.name)")]
    [SerializeField] private List<AbilityStateOverride> perAbilityOverrides = new();

    [Serializable]
    public struct AbilityStateOverride
    {
        public string abilityName;     // e.g. "Slash"
        public string attackStateName; // e.g. "fighter_basic_attack"
    }

    public string GetAttackStateForAbility(string abilityName)
    {
        if (!string.IsNullOrWhiteSpace(abilityName))
        {
            for (int i = 0; i < perAbilityOverrides.Count; i++)
            {
                var entry = perAbilityOverrides[i];
                if (!string.IsNullOrWhiteSpace(entry.abilityName) &&
                    string.Equals(entry.abilityName.Trim(), abilityName.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(entry.attackStateName))
                {
                    return entry.attackStateName.Trim();
                }
            }
        }

        return string.IsNullOrWhiteSpace(defaultAttackState) ? null : defaultAttackState.Trim();
    }
}
