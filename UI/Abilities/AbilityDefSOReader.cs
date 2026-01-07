using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class AbilityDefSOReader
{
    private static readonly Dictionary<Type, Dictionary<string, MemberInfo>> _memberCache = new();

    // Common member name candidates (fields or properties).
    private static readonly string[] NameCandidates =
    {
        "displayName", "abilityName", "name", "title"
    };

    // Some projects store costs as individual members. (Fallback only.)
    private static readonly string[] AttackCandidates = { "costAttack", "attackCost", "atkCost", "attack", "atk" };
    private static readonly string[] DefenseCandidates = { "costDefense", "defenseCost", "defCost", "defense", "def" };
    private static readonly string[] MagicCandidates = { "costMagic", "magicCost", "magCost", "magic", "mag" };
    private static readonly string[] WildCandidates = { "costWild", "wildCost", "jokerCost", "wild", "joker" };

    // Your project stores costs as: public ResourceCost cost;
    private static readonly string[] CostStructCandidates = { "cost", "resourceCost", "costs" };

    public static string GetDisplayName(ScriptableObject ability)
    {
        if (ability == null) return "(null ability)";

        if (TryGetStringMember(ability, NameCandidates, out string s) && !string.IsNullOrWhiteSpace(s))
            return s;

        return ability.name;
    }

    public static long GetCostAttack(ScriptableObject ability)
    {
        if (TryGetCostStruct(ability, out var c)) return c.attack;
        return GetLongMember(ability, AttackCandidates);
    }

    public static long GetCostDefense(ScriptableObject ability)
    {
        if (TryGetCostStruct(ability, out var c)) return c.defense;
        return GetLongMember(ability, DefenseCandidates);
    }

    public static long GetCostMagic(ScriptableObject ability)
    {
        if (TryGetCostStruct(ability, out var c)) return c.magic;
        return GetLongMember(ability, MagicCandidates);
    }

    public static long GetCostWild(ScriptableObject ability)
    {
        if (TryGetCostStruct(ability, out var c)) return c.wild;
        return GetLongMember(ability, WildCandidates);
    }

    /// <summary>
    /// Returns a compact, UI-friendly cost string that omits any zero-cost resources.
    /// Example: "A: 1" or "A: 1 M: 2". If all costs are 0, returns an empty string.
    /// </summary>
    public static string GetCostStringNonZero(ScriptableObject ability)
    {
        if (ability == null) return string.Empty;

        long cA = GetCostAttack(ability);
        long cD = GetCostDefense(ability);
        long cM = GetCostMagic(ability);
        long cW = GetCostWild(ability);

        var parts = new List<string>(4);

        if (cA > 0) parts.Add($"A: {cA}");
        if (cD > 0) parts.Add($"D: {cD}");
        if (cM > 0) parts.Add($"M: {cM}");
        if (cW > 0) parts.Add($"W: {cW}");

        return parts.Count > 0 ? string.Join(" ", parts) : string.Empty;
    }

    // --- Cost struct handling (ResourceCost) ---

    private static bool TryGetCostStruct(ScriptableObject ability, out ResourceCost cost)
    {
        cost = default;
        if (ability == null) return false;

        // Fast path for your concrete type
        if (ability is AbilityDefinitionSO def)
        {
            cost = def.cost;
            return true;
        }

        // Reflection fallback: look for a member called "cost"/"resourceCost"/"costs" that is ResourceCost
        for (int i = 0; i < CostStructCandidates.Length; i++)
        {
            if (TryGetMemberValue(ability, CostStructCandidates[i], out object raw) && raw is ResourceCost rc)
            {
                cost = rc;
                return true;
            }
        }

        return false;
    }

    // --- Reflection helpers ---

    private static bool TryGetStringMember(ScriptableObject obj, string[] candidates, out string value)
    {
        value = null;
        if (obj == null) return false;

        for (int i = 0; i < candidates.Length; i++)
        {
            if (TryGetMemberValue(obj, candidates[i], out object raw) && raw is string str)
            {
                value = str;
                return true;
            }
        }

        return false;
    }

    private static long GetLongMember(ScriptableObject obj, string[] candidates)
    {
        if (obj == null) return 0;

        for (int i = 0; i < candidates.Length; i++)
        {
            if (!TryGetMemberValue(obj, candidates[i], out object raw) || raw == null)
                continue;

            try
            {
                if (raw is long l) return l;
                if (raw is int ii) return ii;
                if (raw is short ss) return ss;
                if (raw is byte bb) return bb;
                if (raw is float ff) return (long)Mathf.RoundToInt(ff);
                if (raw is double dd) return (long)Math.Round(dd);

                if (raw is string s && long.TryParse(s, out long parsed)) return parsed;
            }
            catch
            {
                // ignore and continue
            }
        }

        return 0;
    }

    private static bool TryGetMemberValue(ScriptableObject obj, string memberName, out object value)
    {
        value = null;
        if (obj == null || string.IsNullOrWhiteSpace(memberName))
            return false;

        Type t = obj.GetType();

        if (!_memberCache.TryGetValue(t, out var map))
        {
            map = BuildMemberMap(t);
            _memberCache[t] = map;
        }

        if (!map.TryGetValue(memberName, out MemberInfo mi) || mi == null)
            return false;

        try
        {
            if (mi is FieldInfo fi) { value = fi.GetValue(obj); return true; }
            if (mi is PropertyInfo pi) { value = pi.GetValue(obj, null); return true; }
        }
        catch { /* ignore */ }

        return false;
    }

    private static Dictionary<string, MemberInfo> BuildMemberMap(Type t)
    {
        var dict = new Dictionary<string, MemberInfo>(StringComparer.OrdinalIgnoreCase);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (var f in t.GetFields(flags))
            if (!dict.ContainsKey(f.Name)) dict[f.Name] = f;

        foreach (var p in t.GetProperties(flags))
            if (!dict.ContainsKey(p.Name)) dict[p.Name] = p;

        return dict;
    }
}
