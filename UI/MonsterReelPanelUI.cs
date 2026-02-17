using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// InfoPanel "Abilities/Reel" tab UI.
///
/// REQUIRED behavior:
/// - When the InfoPanel is opened by clicking a Monster in the world, InfoPanelController stores that Monster instance.
/// - When the Reel/Abilities tab is shown, InfoPanelController calls ShowForMonster(monster).
/// - This script then pulls the ReelStrip from THAT Monster's MonsterReelDefinitionSO (Monster.ReelDefinition)
///   and layers it onto the InfoPanel's 3D reel (Reel3DColumn on "MonsterAbilityReel").
///
/// Notes:
/// - Supports legacy 3-slot UI Images/SpriteRenderers if present (top/mid/bottom).
/// - If the InfoPanel uses only the 3D reel, the legacy slot renderers may not exist (that's OK).
/// </summary>
public class MonsterReelPanelUI : MonoBehaviour
{
    [Header("Reel Root")]
    [Tooltip("Root containing the InfoPanel 3D reel (Reel3DColumn). If null, we try to find a child named 'MonsterAbilityReel'.")]
    [SerializeField, FormerlySerializedAs("slotsParent"), FormerlySerializedAs("slotRoot"), FormerlySerializedAs("monsterAbilityReelRoot")]
    private Transform slotsRoot;

    [Tooltip("Optional: explicitly wire the InfoPanel Reel3DColumn. If null, we'll look under slotsRoot.")]
    [SerializeField] private Reel3DColumn infoPanelReel;

    [Header("Legacy Slot Renderers (Optional)")]
    [Tooltip("Optional: manually wire 3 UI Images for top/mid/bottom icon previews.")]
    [SerializeField] private List<Image> slotImages = new List<Image>();

    [Tooltip("Optional: if using SpriteRenderers for the 3 preview icons.")]
    [SerializeField] private List<SpriteRenderer> slotSpriteRenderers = new List<SpriteRenderer>();

    [Header("Text")]
    [SerializeField] private TMP_Text attackNameText;
    [SerializeField] private TMP_Text attackDescText;

    [Header("Display")]
    [Tooltip("Which preview row is considered the 'mid' row (0=top, 1=mid, 2=bottom).")]
    [SerializeField] private int midRowIndex = 1;

    [Tooltip("Rotate the 2D preview icons visually (does not affect lookup).")]
    [SerializeField] private float iconRotateDegreesCCW = 90f;

    [SerializeField] private bool preserveAspect = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private Monster _currentMonster;

    private void Awake()
    {
        EnsureSlotsRootCached("Awake");
        EnsureInfoPanelReelCached("Awake");
        EnsureLegacySlotsCached("Awake");
        ClearUI();
    }

    private void OnEnable()
    {
        EnsureSlotsRootCached("OnEnable");
        EnsureInfoPanelReelCached("OnEnable");
        EnsureLegacySlotsCached("OnEnable");

        // If the tab gets re-enabled without the controller re-calling us, keep it consistent.
        if (_currentMonster != null)
            ShowForMonster(_currentMonster);
    }

    public void ShowForMonster(Monster monster)
    {
        if (monster == null)
        {
            if (debugLogs) Debug.LogWarning("[MonsterReelPanelUI] ShowForMonster called with null.", this);
            ClearUI();
            return;
        }

        _currentMonster = monster;

        EnsureSlotsRootCached("ShowForMonster");
        EnsureInfoPanelReelCached("ShowForMonster");
        EnsureLegacySlotsCached("ShowForMonster");

        if (infoPanelReel == null)
        {
            Debug.LogError("[MonsterReelPanelUI] No Reel3DColumn found for InfoPanel reel. Ensure 'MonsterAbilityReel' has a Reel3DColumn.", this);
            ClearUI();
            return;
        }

        // ---- THIS IS THE KEY: pull the strip from the SAME Monster instance that opened the panel ----
        ReelStripSO strip = null;
        int[] slotToAttack = null;

        if (monster.ReelDefinition != null)
        {
            strip = monster.ReelDefinition.Strip;
            slotToAttack = monster.ReelDefinition.SlotToAttackIndex;
        }

        if (strip == null)
        {
            // Give a concrete reason + best-effort fallback (but do not silently succeed).
            Debug.LogError($"[MonsterReelPanelUI] Monster '{monster.name}' has no ReelDefinition strip assigned (Monster.ReelDefinition.Strip is null). Assign a MonsterReelDefinitionSO on the Monster component.", monster);
            ClearUI();
            return;
        }

        if (debugLogs)
            Debug.Log($"[MonsterReelPanelUI] Applying strip '{strip.name}' to InfoPanel reel for monster '{monster.name}'.", this);

        // Layer the strip onto the InfoPanel 3D reel.
        infoPanelReel.SetStrip(strip, rebuildNow: true);

        // Optional: update the 3 preview icons if they exist.
        UpdateLegacyPreviewIcons(strip);

        // Update text based on the mid row slot.
        UpdateAttackText(monster, strip, slotToAttack);
    }

    // ------------------------------
    // Core UI updates
    // ------------------------------

    private void UpdateLegacyPreviewIcons(ReelStripSO strip)
    {
        if (strip == null || strip.symbols == null) return;

        // We display the first 3 slots as top/mid/bottom preview.
        // If your 3D reel shows a different window later, we can wire this to the reel's current step.
        int count = strip.symbols.Count;

        for (int i = 0; i < 3; i++)
        {
            var sym = (i >= 0 && i < count) ? strip.symbols[i] : null;
            var sprite = (sym != null) ? sym.icon : null;

            if (slotImages != null && i < slotImages.Count && slotImages[i] != null)
            {
                slotImages[i].sprite = sprite;
                slotImages[i].enabled = (sprite != null);
                slotImages[i].preserveAspect = preserveAspect;

                var rt = slotImages[i].rectTransform;
                if (rt != null)
                    rt.localEulerAngles = new Vector3(0f, 0f, iconRotateDegreesCCW);
            }

            if (slotSpriteRenderers != null && i < slotSpriteRenderers.Count && slotSpriteRenderers[i] != null)
            {
                slotSpriteRenderers[i].sprite = sprite;
                slotSpriteRenderers[i].enabled = (sprite != null);

                // Rotate visually around Z.
                slotSpriteRenderers[i].transform.localEulerAngles = new Vector3(0f, 0f, iconRotateDegreesCCW);
            }
        }
    }

    private void UpdateAttackText(Monster monster, ReelStripSO strip, int[] slotToAttack)
    {
        if (attackNameText == null && attackDescText == null) return;

        string name = "";
        string desc = "";

        int slotIndex = Mathf.Clamp(midRowIndex, 0, 2);
        if (strip != null && strip.symbols != null && strip.symbols.Count > slotIndex)
        {
            // If there is a slot->attack map, use it.
            int attackIdx = -1;
            if (slotToAttack != null && slotToAttack.Length > slotIndex)
                attackIdx = slotToAttack[slotIndex];

            if (attackIdx >= 0 && monster != null && monster.TryGetAttack(attackIdx, out var atk) && atk != null)
            {
                name = string.IsNullOrWhiteSpace(atk.id) ? $"Attack {attackIdx}" : atk.id;
                desc = BuildAttackDescription(atk);
            }
            else
            {
                // Fallback: at least show the symbol id.
                var sym = strip.symbols[slotIndex];
                name = (sym != null && !string.IsNullOrWhiteSpace(sym.id)) ? sym.id : "";
                desc = "";

                if (debugLogs)
                    Debug.LogWarning($"[MonsterReelPanelUI] No valid SlotToAttack mapping for slot {slotIndex} on '{monster.name}'. (attackIdx={attackIdx})", this);
            }
        }

        if (attackNameText != null) attackNameText.text = name;
        if (attackDescText != null) attackDescText.text = desc;
    }

    private static string BuildAttackDescription(Monster.MonsterAttack atk)
    {
        if (atk == null) return "";

        // Keep it compact and game-readable.
        List<string> bits = new List<string>(8);

        if (atk.isSummon)
        {
            string who = (atk.summonPrefab != null) ? atk.summonPrefab.name : "ally";
            bits.Add($"Summons {atk.summonCount}× {who}.");
        }
        else
        {
            bits.Add($"Deals {atk.damage} damage{(atk.isAoe ? " to all allies" : "")}." );
        }

        if (atk.stunsTarget)
            bits.Add($"Stuns for {Mathf.Max(1, atk.stunPlayerPhases)} phase(s)." );

        if (atk.appliesBleed)
            bits.Add($"Applies Bleed {Mathf.Max(1, atk.bleedStacks)}." );

        if (atk.appliesCorrosion)
            bits.Add($"Corrodes {Mathf.Max(1, atk.corrosionIconCount)} reel icon(s)." );

        // speed is more of a designer knob; still useful info.
        bits.Add($"Speed {atk.speed}." );

        return string.Join("\n", bits);
    }

    private void ClearUI()
    {
        if (attackNameText != null) attackNameText.text = "";
        if (attackDescText != null) attackDescText.text = "";

        if (slotImages != null)
        {
            foreach (var img in slotImages)
            {
                if (img == null) continue;
                img.sprite = null;
                img.enabled = false;
            }
        }

        if (slotSpriteRenderers != null)
        {
            foreach (var sr in slotSpriteRenderers)
            {
                if (sr == null) continue;
                sr.sprite = null;
                sr.enabled = false;
            }
        }
    }

    // ------------------------------
    // Caching / discovery
    // ------------------------------

    private void EnsureSlotsRootCached(string reason)
    {
        if (slotsRoot != null) return;

        var direct = transform.Find("MonsterAbilityReel");
        if (direct != null)
        {
            slotsRoot = direct;
            return;
        }

        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t != null && t.name == "MonsterAbilityReel")
            {
                slotsRoot = t;
                break;
            }
        }

        if (debugLogs && slotsRoot == null)
            Debug.LogWarning($"[MonsterReelPanelUI] EnsureSlotsRootCached({reason}): could not find child 'MonsterAbilityReel'.", this);
    }

    private void EnsureInfoPanelReelCached(string reason)
    {
        if (infoPanelReel != null) return;

        if (slotsRoot != null)
        {
            infoPanelReel = slotsRoot.GetComponent<Reel3DColumn>();
            if (infoPanelReel == null)
                infoPanelReel = slotsRoot.GetComponentInChildren<Reel3DColumn>(true);
        }

        if (debugLogs && infoPanelReel == null)
            Debug.LogWarning($"[MonsterReelPanelUI] EnsureInfoPanelReelCached({reason}): no Reel3DColumn found under slotsRoot.", this);
    }

    private void EnsureLegacySlotsCached(string reason)
    {
        // If user wired slots manually, don't stomp it.
        if (slotImages != null && slotImages.Count > 0) return;
        if (slotSpriteRenderers != null && slotSpriteRenderers.Count > 0) return;

        if (slotsRoot == null) return;

        var imgs = slotsRoot.GetComponentsInChildren<Image>(true);
        if (imgs != null && imgs.Length > 0)
        {
            slotImages = new List<Image>(imgs);
            var filtered = slotImages.FindAll(i => i != null && i.name.IndexOf("slot", StringComparison.OrdinalIgnoreCase) >= 0);
            if (filtered.Count >= 3) slotImages = filtered;
            if (slotImages.Count > 3) slotImages = slotImages.GetRange(0, 3);

            if (preserveAspect)
                foreach (var img in slotImages)
                    if (img != null) img.preserveAspect = true;

            if (debugLogs)
                Debug.Log($"[MonsterReelPanelUI] EnsureLegacySlotsCached({reason}) -> UI Images={slotImages.Count}", this);

            return;
        }

        var srs = slotsRoot.GetComponentsInChildren<SpriteRenderer>(true);
        if (srs != null && srs.Length > 0)
        {
            slotSpriteRenderers = new List<SpriteRenderer>(srs);
            var filtered = slotSpriteRenderers.FindAll(sr => sr != null && sr.name.IndexOf("slot", StringComparison.OrdinalIgnoreCase) >= 0);
            if (filtered.Count >= 3) slotSpriteRenderers = filtered;
            if (slotSpriteRenderers.Count > 3) slotSpriteRenderers = slotSpriteRenderers.GetRange(0, 3);

            if (debugLogs)
                Debug.Log($"[MonsterReelPanelUI] EnsureLegacySlotsCached({reason}) -> SpriteRenderers={slotSpriteRenderers.Count}", this);
        }
    }
}
