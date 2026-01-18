using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Idle Wasteland/Reels/Reel Upgrade Rules", fileName = "ReelUpgradeRules_")]
public class ReelUpgradeRulesSO : ScriptableObject
{
    [Serializable]
    public class Rule
    {
        [Tooltip("Symbol that will be upgraded when landed on.")]
        public ReelSymbolSO from;

        [Tooltip("Symbol to replace 'from' with.")]
        public ReelSymbolSO to;
    }

    [SerializeField] private List<Rule> rules = new List<Rule>();

    /// <summary>
    /// Returns the upgrade symbol for the given symbol, or null if no rule exists.
    /// </summary>
    public ReelSymbolSO GetUpgradeFor(ReelSymbolSO from)
    {
        if (from == null || rules == null) return null;

        for (int i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            if (r == null) continue;
            if (r.from == from)
                return r.to;
        }

        return null;
    }


    /// <summary>
    /// Convenience wrapper for code paths that prefer a Try* pattern.
    /// </summary>
    public bool TryGetUpgrade(ReelSymbolSO from, out ReelSymbolSO to)
    {
        to = GetUpgradeFor(from);
        return to != null;
    }

}