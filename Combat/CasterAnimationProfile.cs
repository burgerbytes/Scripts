using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class CasterAnimationProfile : MonoBehaviour
{
    [Header("Default attack animation state (Animator state name)")]
    [SerializeField] private string defaultAttackState = "fighter_basic_attack";

    [Header("Optional per-animation-key override (preferred)")]
    [Tooltip("Maps an Ability 'animation key' (e.g. 'BasicAttack', 'MageCast') to an animator state. Optionally scope to a class name.")]
    [SerializeField] private List<AnimationKeyStateOverride> perAnimationKeyOverrides = new();

    [Header("Optional per-ability override (match AbilityDefinitionSO.name)")]
    [SerializeField] private List<AbilityStateOverride> perAbilityOverrides = new();

    [Serializable]
    public struct AnimationKeyStateOverride
    {
        [Tooltip("Ability animation key (recommended to be stable and not player-facing).")]
        public string animationKey;    // e.g. "BasicAttack"
        [Tooltip("Optional class name scope. Leave blank to apply to all classes using this profile.")]
        public string className;       // e.g. "Templar" (optional)
        [Tooltip("Animator state name to play.")]
        public string attackStateName; // e.g. "templar_basic_attack"
    }

    [Serializable]
    public struct AbilityStateOverride
    {
        public string abilityName;     // e.g. "Slash"
        public string attackStateName; // e.g. "fighter_basic_attack"
    }

    /// <summary>
    /// Preferred lookup: resolve an animator state from an ability "animation key".
    /// Falls back to (optional) per-ability override and then defaultAttackState.
    /// </summary>
    public string ResolveAttackState(string animationKey, string className = null, string abilityNameFallback = null)
    {
        // 1) animationKey + className (exact class match)
        string key = Normalize(animationKey);
        string cls = Normalize(className);
        if (!string.IsNullOrEmpty(key))
        {
            // exact class match first
            for (int i = 0; i < perAnimationKeyOverrides.Count; i++)
            {
                var entry = perAnimationKeyOverrides[i];
                if (string.IsNullOrWhiteSpace(entry.attackStateName)) continue;

                if (string.Equals(Normalize(entry.animationKey), key, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(cls) &&
                    string.Equals(Normalize(entry.className), cls, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.attackStateName.Trim();
                }
            }

            // then any-class mapping
            for (int i = 0; i < perAnimationKeyOverrides.Count; i++)
            {
                var entry = perAnimationKeyOverrides[i];
                if (string.IsNullOrWhiteSpace(entry.attackStateName)) continue;

                if (string.Equals(Normalize(entry.animationKey), key, StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrWhiteSpace(entry.className))
                {
                    return entry.attackStateName.Trim();
                }
            }
        }

        // 2) legacy: per-ability override
        string abilityName = Normalize(abilityNameFallback);
        if (!string.IsNullOrEmpty(abilityName))
        {
            for (int i = 0; i < perAbilityOverrides.Count; i++)
            {
                var entry = perAbilityOverrides[i];
                if (!string.IsNullOrWhiteSpace(entry.abilityName) &&
                    string.Equals(Normalize(entry.abilityName), abilityName, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(entry.attackStateName))
                {
                    return entry.attackStateName.Trim();
                }
            }
        }

        // 3) default
        return string.IsNullOrWhiteSpace(defaultAttackState) ? null : defaultAttackState.Trim();
    }

    /// <summary>
    /// Backwards-compatible convenience method.
    /// </summary>
    public string GetAttackStateForAbility(string abilityName)
        => ResolveAttackState(animationKey: null, className: null, abilityNameFallback: abilityName);

    private static string Normalize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return s.Trim();
    }
}
