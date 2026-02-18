using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// InfoPanel "Abilities" tab UI.
///
/// REQUIRED behavior (preserved):
/// - InfoPanelController opens the panel from a clicked Monster instance.
/// - When this tab is shown, controller calls ShowForMonster(monster).
/// - This script pulls the ReelStrip from THAT monster (Monster.ReelDefinition.Strip).
///
/// Display modes:
/// - Icon List (preferred for clarity): lays out all strip icons as clickable buttons.
/// - 3D Reel (optional): keeps prior functionality if you still want the reel visible.
/// </summary>
public class MonsterReelPanelUI : MonoBehaviour
{
    [Header("Reel Root (optional)")]
    [Tooltip("Root containing the InfoPanel 3D reel (Reel3DColumn). If null, we try to find a child named 'MonsterAbilityReel'.")]
    [SerializeField, FormerlySerializedAs("slotsParent"), FormerlySerializedAs("slotRoot"), FormerlySerializedAs("monsterAbilityReelRoot")]
    private Transform slotsRoot;

    [Tooltip("Optional: explicitly wire the InfoPanel Reel3DColumn. If null, we'll look under slotsRoot.")]
    [SerializeField] private Reel3DColumn infoPanelReel;

    [Header("Icon List (recommended)")]
    [Tooltip("If true, we lay out the monster strip as a row/grid of clickable icons.")]
    [SerializeField] private bool useIconList = true;

    [Tooltip("Parent transform that will hold the instantiated icon buttons (e.g., a HorizontalLayoutGroup).")]
    [SerializeField] private Transform iconListRoot;

    [Tooltip("Optional prefab for an icon button. Should have Button + Image on the root (or an Image on a child).")]
    [SerializeField] private Button iconButtonPrefab;

    [Tooltip("If no prefab is provided, we create a simple Button+Image with this size.")]
    [SerializeField] private Vector2 fallbackIconSize = new Vector2(48f, 48f);

    [Tooltip("When using the icon list, hide the 3D reel root for clarity.")]
    [SerializeField] private bool hide3DReelWhenUsingIconList = true;

    [Tooltip("Visual nudge on the selected icon.")]
    [SerializeField] private float selectedScale = 1.15f;

    [Header("Legacy Slot Renderers (Optional)")]
    [Tooltip("Optional: manually wire 3 UI Images for top/mid/bottom icon previews.")]
    [SerializeField] private List<Image> slotImages = new List<Image>();

    [Tooltip("Optional: if using SpriteRenderers for the 3 preview icons.")]
    [SerializeField] private List<SpriteRenderer> slotSpriteRenderers = new List<SpriteRenderer>();

    [Header("Text")]
    [SerializeField] private TMP_Text attackNameText;
    [SerializeField] private TMP_Text attackDescText;

    [Header("Display")]
    [Tooltip("Default selected slot when showing a monster (0-based index into strip.symbols).")]
    [SerializeField] private int defaultSelectedSlotIndex = 0;

    [Tooltip("Rotate the 2D preview icons visually (does not affect lookup).")]
    [SerializeField] private float iconRotateDegreesCCW = 0f;

    [SerializeField] private bool preserveAspect = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private Monster _currentMonster;
    private ReelStripSO _currentStrip;
    private int[] _currentSlotToAttack;

    private readonly List<Button> _spawnedIconButtons = new List<Button>();
    private int _selectedSlotIndex = -1;

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

        if (_currentMonster != null)
            ShowForMonster(_currentMonster);
    }

    private void OnDisable()
    {
        // Prevent leaks / double listeners if tab toggles.
        ClearSpawnedIconButtons();
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

        // ---- KEY: pull strip from the SAME Monster instance that opened the panel ----
        ReelStripSO strip = null;
        int[] slotToAttack = null;

        if (monster.ReelDefinition != null)
        {
            strip = monster.ReelDefinition.Strip;
            slotToAttack = monster.ReelDefinition.SlotToAttackIndex;
        }

        if (strip == null)
        {
            Debug.LogError($"[MonsterReelPanelUI] Monster '{monster.name}' has no ReelDefinition strip assigned (Monster.ReelDefinition.Strip is null). Assign a MonsterReelDefinitionSO on the Monster component.", monster);
            ClearUI();
            return;
        }

        _currentStrip = strip;
        _currentSlotToAttack = slotToAttack;

        if (debugLogs)
            Debug.Log($"[MonsterReelPanelUI] ShowForMonster '{monster.name}' strip='{strip.name}' symbols={strip.symbols?.Count ?? 0} mode={(useIconList ? "IconList" : "3DReel")}", this);

        // Optional: keep the 3D reel in sync (but we may hide it).
        if (infoPanelReel != null)
        {
            infoPanelReel.SetStrip(strip, rebuildNow: true);

            if (hide3DReelWhenUsingIconList && useIconList && slotsRoot != null)
                slotsRoot.gameObject.SetActive(false);
            else if (slotsRoot != null)
                slotsRoot.gameObject.SetActive(true);
        }
        else
        {
            // If you truly have no reel in this tab anymore, that's fine.
            if (slotsRoot != null)
                slotsRoot.gameObject.SetActive(!useIconList);
        }

        // Optional: update the 3 preview icons if they exist.
        UpdateLegacyPreviewIcons(strip);

        // Icon list is the new primary UI.
        if (useIconList)
        {
            BuildIconList(strip);
            SelectSlot(Mathf.Clamp(defaultSelectedSlotIndex, 0, (strip.symbols?.Count ?? 1) - 1));
        }
        else
        {
            // Fallback to default behavior if list not used.
            SelectSlot(Mathf.Clamp(defaultSelectedSlotIndex, 0, (strip.symbols?.Count ?? 1) - 1));
        }
    }

    // ------------------------------
    // Icon List
    // ------------------------------

    private void BuildIconList(ReelStripSO strip)
    {
        ClearSpawnedIconButtons();

        if (iconListRoot == null)
        {
            Debug.LogError("[MonsterReelPanelUI] useIconList is enabled, but iconListRoot is not assigned.", this);
            return;
        }

        if (strip == null || strip.symbols == null)
            return;

        for (int i = 0; i < strip.symbols.Count; i++)
        {
            var sym = strip.symbols[i];
            var sprite = sym != null ? sym.icon : null;

            Button btn = null;
            Image img = null;

            if (iconButtonPrefab != null)
            {
                btn = Instantiate(iconButtonPrefab, iconListRoot);
                img = btn.GetComponent<Image>();
                if (img == null)
                    img = btn.GetComponentInChildren<Image>(true);
            }
            else
            {
                var go = new GameObject($"ReelIcon_{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                go.transform.SetParent(iconListRoot, false);

                var rt = (RectTransform)go.transform;
                rt.sizeDelta = fallbackIconSize;

                img = go.GetComponent<Image>();
                btn = go.GetComponent<Button>();
            }

            if (btn == null)
                continue;

            // Configure image
            if (img != null)
            {
                img.sprite = sprite;
                img.enabled = (sprite != null);
                img.preserveAspect = preserveAspect;

                var rt = img.rectTransform;
                if (rt != null)
                    rt.localEulerAngles = new Vector3(0f, 0f, iconRotateDegreesCCW);
            }

            int capturedIndex = i;
            btn.onClick.AddListener(() => SelectSlot(capturedIndex));

            _spawnedIconButtons.Add(btn);
        }
    }

    private void ClearSpawnedIconButtons()
    {
        for (int i = 0; i < _spawnedIconButtons.Count; i++)
        {
            var b = _spawnedIconButtons[i];
            if (b == null) continue;

            b.onClick.RemoveAllListeners();

            // Only destroy spawned instances.
            if (Application.isPlaying)
                Destroy(b.gameObject);
            else
                DestroyImmediate(b.gameObject);
        }
        _spawnedIconButtons.Clear();
        _selectedSlotIndex = -1;
    }

    private void SelectSlot(int slotIndex)
    {
        if (_currentStrip == null || _currentStrip.symbols == null || _currentStrip.symbols.Count == 0)
        {
            ClearUI();
            return;
        }

        slotIndex = Mathf.Clamp(slotIndex, 0, _currentStrip.symbols.Count - 1);
        _selectedSlotIndex = slotIndex;

        UpdateSelectionVisuals();
        UpdateAttackText(_currentMonster, _currentStrip, _currentSlotToAttack, slotIndex);

        if (debugLogs)
            Debug.Log($"[MonsterReelPanelUI] Selected slot {slotIndex} sym='{(_currentStrip.symbols[slotIndex] != null ? _currentStrip.symbols[slotIndex].id : "NULL")}'", this);
    }

    private void UpdateSelectionVisuals()
    {
        for (int i = 0; i < _spawnedIconButtons.Count; i++)
        {
            var btn = _spawnedIconButtons[i];
            if (btn == null) continue;

            float s = (i == _selectedSlotIndex) ? selectedScale : 1f;
            btn.transform.localScale = new Vector3(s, s, 1f);
        }
    }

    // ------------------------------
    // Legacy preview (optional)
    // ------------------------------

    private void UpdateLegacyPreviewIcons(ReelStripSO strip)
    {
        if (strip == null || strip.symbols == null) return;

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
                slotSpriteRenderers[i].transform.localEulerAngles = new Vector3(0f, 0f, iconRotateDegreesCCW);
            }
        }
    }

    // ------------------------------
    // Text
    // ------------------------------

    private void UpdateAttackText(Monster monster, ReelStripSO strip, int[] slotToAttack, int slotIndex)
    {
        if (attackNameText == null && attackDescText == null) return;

        string name = "";
        string desc = "";

        if (strip != null && strip.symbols != null && strip.symbols.Count > slotIndex)
        {
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
                var sym = strip.symbols[slotIndex];
                name = (sym != null && !string.IsNullOrWhiteSpace(sym.id)) ? sym.id : "";
                desc = "";

                if (debugLogs && monster != null)
                    Debug.LogWarning($"[MonsterReelPanelUI] No valid SlotToAttack mapping for slot {slotIndex} on '{monster.name}'. (attackIdx={attackIdx})", this);
            }
        }

        if (attackNameText != null) attackNameText.text = name;
        if (attackDescText != null) attackDescText.text = desc;
    }

    private static string BuildAttackDescription(Monster.MonsterAttack atk)
    {
        if (atk == null) return "";

        List<string> bits = new List<string>(8);

        if (atk.isSummon)
        {
            string who = (atk.summonPrefab != null) ? atk.summonPrefab.name : "ally";
            bits.Add($"Summons {atk.summonCount}× {who}.");
        }
        else
        {
            bits.Add($"Deals {atk.damage} damage{(atk.isAoe ? " to all allies" : "")}.");
        }

        if (atk.stunsTarget)
            bits.Add($"Stuns for {Mathf.Max(1, atk.stunPlayerPhases)} phase(s).");

        if (atk.appliesBleed)
            bits.Add($"Applies Bleed {Mathf.Max(1, atk.bleedStacks)}.");

        if (atk.appliesCorrosion)
            bits.Add($"Corrodes {Mathf.Max(1, atk.corrosionIconCount)} reel icon(s).");

        bits.Add($"Speed {atk.speed}.");

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
            var filtered = slotSpriteRenderers.FindAll(s => s != null && s.name.IndexOf("slot", StringComparison.OrdinalIgnoreCase) >= 0);
            if (filtered.Count >= 3) slotSpriteRenderers = filtered;
            if (slotSpriteRenderers.Count > 3) slotSpriteRenderers = slotSpriteRenderers.GetRange(0, 3);

            if (debugLogs)
                Debug.Log($"[MonsterReelPanelUI] EnsureLegacySlotsCached({reason}) -> SpriteRenderers={slotSpriteRenderers.Count}", this);
        }
    }
}
