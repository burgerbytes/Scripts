using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using TMPro;

public class MonsterInfoController : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Root panel GameObject (the one you want enabled/disabled).")]
    [SerializeField] private GameObject monsterInfoPanel;

    [SerializeField] private TMP_Text monsterNameText;
    [SerializeField] private TMP_Text monsterStatsText;
    [SerializeField] private TMP_Text monsterDescriptionText;

    [Header("Optional Sorting")]
    [Tooltip("If assigned, we will force this canvas to a low sorting order so other UI (like Ability panel) can draw above it.")]
    [SerializeField] private Canvas monsterCanvas;
    [SerializeField] private int sortingOrder = 0;

    private Monster _currentMonster;

    private void Awake()
    {
        // Initially disabled
        if (monsterInfoPanel != null)
            monsterInfoPanel.SetActive(false);

        if (monsterCanvas != null)
        {
            monsterCanvas.overrideSorting = true;
            monsterCanvas.sortingOrder = sortingOrder;
        }
    }

    public void Show(Monster monster)
    {
        if (monster == null || monster.IsDead)
        {
            Hide();
            return;
        }

        _currentMonster = monster;

        if (monsterInfoPanel != null)
            monsterInfoPanel.SetActive(true);

        if (monsterNameText != null)
            monsterNameText.text = monster.DisplayName;

        if (monsterStatsText != null)
            monsterStatsText.text = BuildStatsText(monster);

        if (monsterDescriptionText != null)
            monsterDescriptionText.text = monster.Description;
    }

    public void Hide()
    {
        _currentMonster = null;

        if (monsterInfoPanel != null)
            monsterInfoPanel.SetActive(false);
    }

    public bool IsShowing(Monster monster)
    {
        return monster != null && _currentMonster == monster && monsterInfoPanel != null && monsterInfoPanel.activeSelf;
    }

    public void HideIfShowing(Monster monster)
    {
        if (IsShowing(monster))
            Hide();
    }

    public void SetSortingOrder(int order)
    {
        sortingOrder = order;
        if (monsterCanvas != null)
        {
            monsterCanvas.overrideSorting = true;
            monsterCanvas.sortingOrder = sortingOrder;
        }
    }

    // =======================
    // Stats formatting helpers
    // =======================

    private string BuildStatsText(Monster monster)
    {
        var sb = new StringBuilder(256);

        // Core stats (always)
        sb.AppendLine($"HP: {monster.CurrentHp}/{monster.MaxHp}");
        sb.AppendLine($"Damage: {monster.GetDamage()}");

        // Tags (optional; pulled via reflection so this script won't break if Monster changes)
        string tagsLine = TryBuildTagsLine(monster);
        if (!string.IsNullOrWhiteSpace(tagsLine))
        {
            sb.AppendLine();
            sb.AppendLine(tagsLine);
        }

        // Resistances (optional; pulled via reflection)
        string resBlock = TryBuildResistanceBlock(monster);
        if (!string.IsNullOrWhiteSpace(resBlock))
        {
            sb.AppendLine();
            sb.Append(resBlock);
        }

        return sb.ToString().TrimEnd();
    }

    private static string TryBuildTagsLine(Monster monster)
    {
        // Expected in Monster.cs:
        // - public IReadOnlyList<MonsterTag> Tags { get; }
        // - OR public List<MonsterTag> Tags
        // - OR serialized field "tags"
        object tagsObj = TryGetMemberValue(monster, "Tags") ?? TryGetMemberValue(monster, "tags");
        if (tagsObj == null) return null;

        // Handle IReadOnlyList / IList / IEnumerable
        if (tagsObj is string s) return string.IsNullOrWhiteSpace(s) ? null : $"Tags: {s}";

        if (tagsObj is IEnumerable enumerable)
        {
            List<string> parts = new List<string>();
            foreach (object item in enumerable)
            {
                if (item == null) continue;
                parts.Add(item.ToString());
            }

            if (parts.Count == 0) return null;
            return "Tags: " + string.Join(", ", parts);
        }

        // Fallback
        return $"Tags: {tagsObj}";
    }

    private static string TryBuildResistanceBlock(Monster monster)
    {
        // Common patterns we support (any subset is fine):
        // - FireResistance / IceResistance / LightningResistance / PoisonResistance (int, %)
        // - fireResistance / iceResistance / lightningResistance / poisonResistance (int, %)
        // - Resistances (Dictionary/struct/etc.) -> we try to render basic entries if enumerable

        // 1) Try explicit per-element ints
        var lines = new List<string>();
        TryAppendResistance(lines, "Fire", TryGetInt(monster, "FireResistance", "fireResistance", "fireResist", "FireResist"));
        TryAppendResistance(lines, "Ice", TryGetInt(monster, "IceResistance", "iceResistance", "iceResist", "IceResist"));
        TryAppendResistance(lines, "Lightning", TryGetInt(monster, "LightningResistance", "lightningResistance", "lightningResist", "LightningResist"));
        TryAppendResistance(lines, "Poison", TryGetInt(monster, "PoisonResistance", "poisonResistance", "poisonResist", "PoisonResist"));

        if (lines.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Resistances:");
            for (int i = 0; i < lines.Count; i++)
                sb.AppendLine(lines[i]);
            return sb.ToString();
        }

        // 2) Try generic container member named Resistances / resistances
        object resObj = TryGetMemberValue(monster, "Resistances") ?? TryGetMemberValue(monster, "resistances");
        if (resObj == null) return null;

        // Dictionary-like
        if (resObj is IDictionary dict)
        {
            List<string> dictLines = new List<string>();
            foreach (DictionaryEntry kv in dict)
            {
                if (kv.Key == null) continue;
                string key = kv.Key.ToString();
                string val = kv.Value != null ? kv.Value.ToString() : "0";
                dictLines.Add($"{key}: {val}");
            }

            if (dictLines.Count == 0) return null;

            var sb = new StringBuilder();
            sb.AppendLine("Resistances:");
            for (int i = 0; i < dictLines.Count; i++)
                sb.AppendLine(dictLines[i]);
            return sb.ToString();
        }

        // Enumerable of entries (KeyValuePair, tuples, etc.)
        if (resObj is IEnumerable enumerable)
        {
            List<string> entryLines = new List<string>();
            foreach (object entry in enumerable)
            {
                if (entry == null) continue;
                entryLines.Add(entry.ToString());
            }

            if (entryLines.Count == 0) return null;

            var sb = new StringBuilder();
            sb.AppendLine("Resistances:");
            for (int i = 0; i < entryLines.Count; i++)
                sb.AppendLine(entryLines[i]);
            return sb.ToString();
        }

        // Fallback
        return $"Resistances:\n{resObj}";
    }

    private static void TryAppendResistance(List<string> lines, string name, int? value)
    {
        if (!value.HasValue) return;
        if (value.Value == 0) return;

        string sign = value.Value > 0 ? "+" : "";
        lines.Add($"{name}: {sign}{value.Value}%");
    }

    private static int? TryGetInt(object obj, params string[] memberNames)
    {
        for (int i = 0; i < memberNames.Length; i++)
        {
            object v = TryGetMemberValue(obj, memberNames[i]);
            if (v == null) continue;

            if (v is int iv) return iv;
            if (v is float fv) return Mathf.RoundToInt(fv);
            if (v is double dv) return (int)Math.Round(dv);

            if (int.TryParse(v.ToString(), out int parsed))
                return parsed;
        }
        return null;
    }

    private static object TryGetMemberValue(object obj, string memberName)
    {
        if (obj == null || string.IsNullOrWhiteSpace(memberName)) return null;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        Type t = obj.GetType();

        // Property
        PropertyInfo p = t.GetProperty(memberName, flags);
        if (p != null)
        {
            try { return p.GetValue(obj, null); }
            catch { /* ignored */ }
        }

        // Field
        FieldInfo f = t.GetField(memberName, flags);
        if (f != null)
        {
            try { return f.GetValue(obj); }
            catch { /* ignored */ }
        }

        return null;
    }
}
