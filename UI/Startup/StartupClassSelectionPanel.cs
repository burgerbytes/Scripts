// GUID: e60b184de68ccf241b14fb67f0b6851b
////////////////////////////////////////////////////////////
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Startup class selection panel.
///
/// Current requirement:
/// - Only Ally1 is used as a LIVE preview of the current midrow hero.
/// - Ally2/Ally3 will be removed later, so we do NOT maintain their UI here.
/// - ReelSymbols preview already works; we extend the same preview flow to populate:
///     ReelcraftName, ReelcraftDesc, StartAbilityHeader, StartAbilityDropdown, StartAbilityDesc.
/// </summary>
public class StartupClassSelectionPanel : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject root;

    [Header("Confirm")]
    [SerializeField] private Button confirmButton;

    [Header("Ally1 Preview UI (REQUIRED for full preview)")]
    [SerializeField] private TMP_Dropdown ally1ClassDropdown;              // Optional: used if you still want dropdown selection to work
    [SerializeField] private Image ally1Portrait;
    

    [Header("Selected Party Portraits")]
    [Tooltip("Parent container where selected party portraits will be instantiated (left-to-right).")]
    [SerializeField] private Transform selectedPartyPortraitsContainer;

    [Tooltip("Image prefab/template used for each selected party portrait. If this object is part of the scene, keep it disabled and it will be cloned at runtime.")]
    [SerializeField] private Image selectedPartyPortraitPrefab;

    private readonly System.Collections.Generic.List<Image> _selectedPartyPortraitImages = new System.Collections.Generic.List<Image>();
[SerializeField] private TMP_Text ally1Label;

    [Header("Ally1 Reel Symbols Preview")]
    [SerializeField] private Transform ally1ReelSymbolsContainer;
    [SerializeField] private Image reelSymbolIconPrefab;
    [SerializeField] private int maxReelSymbolsToShow = 12;
    [SerializeField] private bool showUniqueReelSymbolsOnly = false;

    [Header("Ally1 Reelcraft Preview")]
    [SerializeField] private TMP_Text ally1ReelcraftName;
    [SerializeField] private TMP_Text ally1ReelcraftDesc;

    [Header("Ally1 Starting Ability Preview")]
    [SerializeField] private TMP_Text ally1StartAbilityHeader;
    [SerializeField] private TMP_Dropdown ally1StartAbilityDropdown;
    [SerializeField] private TMP_Text ally1StartAbilityDesc;

    [Header("Debug")]
    [SerializeField] private bool logFlow = false;

    private GameObject[] _available = Array.Empty<GameObject>();
    private int _partySize = 3;
    private Action<GameObject[]> _onConfirm;

    // symbolId -> prefab index
    private readonly Dictionary<string, int> _symbolIdToPrefabIndex = new Dictionary<string, int>(StringComparer.Ordinal);

    // For cleaning / tracking created reel symbol icons
    private readonly List<Image> _ally1ReelIcons = new List<Image>();

    // Starting ability options for Ally1 dropdown
    private readonly List<AbilityDefinitionSO> _ally1StartAbilityOptions = new List<AbilityDefinitionSO>();

    private void Awake()
    {
        if (confirmButton != null)
        {
            confirmButton.onClick.RemoveAllListeners();
            confirmButton.onClick.AddListener(Confirm);
        }

        if (ally1ClassDropdown != null)
        {
            ally1ClassDropdown.onValueChanged.RemoveAllListeners();
            ally1ClassDropdown.onValueChanged.AddListener(_ =>
            {
                // If the user manually changes the dropdown, refresh Ally1 preview to match
                RefreshAlly1FromSelectedDropdown();
            });
        }

        if (ally1StartAbilityDropdown != null)
        {
            ally1StartAbilityDropdown.onValueChanged.RemoveAllListeners();
            ally1StartAbilityDropdown.onValueChanged.AddListener(_ => CommitAlly1StartingAbilitySelection());
        }
    }

    /// <summary>
    /// Kept for compatibility with StartupClassSelectionBootstrapper.
    /// </summary>
    public void Show(GameObject[] availablePartyPrefabs, int partySize, Action<GameObject[]> onConfirm)
    {
        _available = availablePartyPrefabs ?? Array.Empty<GameObject>();
        _partySize = Mathf.Clamp(partySize, 1, 3);
        _onConfirm = onConfirm;

        StartupPartySelectionData.EnsureCapacity(_partySize);
        StartupPartySelectionData.Clear();
        ClearSelectedPartyPortraits();


        StartupPartySelectionData.Clear();

        if (root != null) root.SetActive(true);
        gameObject.SetActive(true);

        BuildSymbolCacheAndDropdown();

        // Initialize Ally1 preview from dropdown selection (if available)
        RefreshAlly1FromSelectedDropdown();
    }

    public void Hide()
    {
        if (root != null) root.SetActive(false);
        gameObject.SetActive(false);
    }

    /// <summary>
    /// This is the reel-driven entry point.
    /// Call this every time the midrow symbol id changes.
    /// </summary>
    public void PreviewAlly1BySymbolId(string symbolId)
    {
        if (string.IsNullOrEmpty(symbolId)) return;
        if (_available == null || _available.Length == 0) return;

        int idx;
        if (!_symbolIdToPrefabIndex.TryGetValue(symbolId, out idx))
        {
            // Fallback scan: match BaseClassDef.className OR prefab name.
            idx = FindPrefabIndexBySymbolIdScan(symbolId);
            if (idx >= 0) _symbolIdToPrefabIndex[symbolId] = idx;
        }

        if (idx < 0 || idx >= _available.Length)
        {
            if (logFlow) Debug.Log($"[StartupClassSelectionPanel] PreviewAlly1BySymbolId MISS symbolId={symbolId}", this);
            return;
        }

        // Drive dropdown if it exists (keeps any old wiring consistent),
        // but we DO NOT rely on dropdown callbacks for updates.
        if (ally1ClassDropdown != null)
        {
            if (ally1ClassDropdown.options == null || ally1ClassDropdown.options.Count != _available.Length)
            {
                // Safety: rebuild options if needed
                BuildSymbolCacheAndDropdown();
            }

            ally1ClassDropdown.SetValueWithoutNotify(idx);
            ally1ClassDropdown.RefreshShownValue();
        }

        // Update ALL Ally1 preview fields directly
        RefreshAlly1FromPrefab(_available[idx]);
    }

    /// <summary>
    /// Called by StartupReelPartySelectionController when the player locks in a hero via NextHero.
    /// Updates/creates the portrait image for the corresponding party slot.
    /// </summary>
    
    /// <summary>
    /// Called by StartupReelPartySelectionController when the player locks in a hero via NextHero.
    /// Updates/creates the portrait image for the corresponding party slot.
    ///
    /// NOTE: UI Images instantiated from a template inherit that template's anchoredPosition.
    /// We explicitly force the portrait into a deterministic slot position:
    ///   slot 0 => x = -2w
    ///   slot 1 => x = -1w
    ///   slot 2 => x =  0
    /// where w is the portrait width.
    /// </summary>
    public void SetSelectedPartySlotPortrait(int slot, Sprite portrait)
    {
        if (selectedPartyPortraitsContainer == null || selectedPartyPortraitPrefab == null) return;
        if (slot < 0) return;

        // Ensure we have images up to this slot index
        while (_selectedPartyPortraitImages.Count <= slot)
        {
            Image img = Instantiate(selectedPartyPortraitPrefab, selectedPartyPortraitsContainer);
            img.gameObject.SetActive(true);
            _selectedPartyPortraitImages.Add(img);
        }

        var target = _selectedPartyPortraitImages[slot];
        if (target == null) return;

        target.sprite = portrait;
        target.enabled = (portrait != null);

        // Force a stable UI transform position (do NOT inherit prefab offsets)
        ForceSelectedPortraitSlotPosition(target.rectTransform, slot);
    }

    /// <summary>
    /// Clears (removes) the portrait for a specific selected party slot.
    /// Used by the Previous Reel / undo flow.
    /// </summary>
    public void ClearSelectedPartySlotPortrait(int slot)
    {
        if (slot < 0) return;
        if (_selectedPartyPortraitImages == null) return;
        if (slot >= _selectedPartyPortraitImages.Count) return;

        var img = _selectedPartyPortraitImages[slot];
        if (img == null) return;

        // Remove the instance so the next set will recreate and re-position cleanly.
        Destroy(img.gameObject);
        _selectedPartyPortraitImages[slot] = null;
    }


    private void ForceSelectedPortraitSlotPosition(RectTransform rt, int slot)
    {
        if (rt == null) return;

        // Force anchors/pivot so anchoredPosition math is consistent.
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.localScale = Vector3.one;

        float w = GetSelectedPortraitWidth(rt);
        // slot 0: -2w | slot 1: -w | slot 2: 0
        float x = -w * (2 - slot);

        rt.anchoredPosition = new Vector2(x, 0f);

        if (logFlow) Debug.Log($"[StartupClassSelectionPanel] SelectedParty portrait slot={slot} x={x} w={w}", this);
    }

    private float GetSelectedPortraitWidth(RectTransform instanceRt)
    {
        // Prefer the template/prefab width (stable even when instance rect isn't fully resolved yet).
        if (selectedPartyPortraitPrefab != null)
        {
            var prt = selectedPartyPortraitPrefab.rectTransform;
            float pw = prt.sizeDelta.x;
            if (pw > 0.01f) return pw;

            pw = prt.rect.width;
            if (pw > 0.01f) return pw;
        }

        // Fallback to the instance width
        float w = instanceRt.sizeDelta.x;
        if (w > 0.01f) return w;

        w = instanceRt.rect.width;
        if (w > 0.01f) return w;
        // Absolute last resort (prevents NaNs / all portraits at 0)
        return 64f;
    }


    private void ClearSelectedPartyPortraits()
    {
        if (selectedPartyPortraitsContainer == null || selectedPartyPortraitPrefab == null) return;

        // Destroy any runtime-cloned portraits (leave the template alone if it's parented here).
        for (int i = _selectedPartyPortraitImages.Count - 1; i >= 0; i--)
        {
            var img = _selectedPartyPortraitImages[i];
            if (img != null && img.gameObject != selectedPartyPortraitPrefab.gameObject)
                Destroy(img.gameObject);
        }
        _selectedPartyPortraitImages.Clear();

        // If the template lives under the container, keep it disabled.
        if (selectedPartyPortraitPrefab != null)
            selectedPartyPortraitPrefab.gameObject.SetActive(false);
    }


    /// <summary>
    /// Optional compatibility wrapper if older code calls this name.
    /// </summary>
    public void PreviewSlot0BySymbolId(string symbolId) => PreviewAlly1BySymbolId(symbolId);

    private void BuildSymbolCacheAndDropdown()
    {
        _symbolIdToPrefabIndex.Clear();

        // Build dropdown options
        if (ally1ClassDropdown != null)
        {
            ally1ClassDropdown.ClearOptions();
            var opts = new List<TMP_Dropdown.OptionData>();
            for (int i = 0; i < _available.Length; i++)
            {
                var go = _available[i];
                opts.Add(new TMP_Dropdown.OptionData(go != null ? go.name : "<Missing>"));
            }
            ally1ClassDropdown.AddOptions(opts);
            ally1ClassDropdown.value = Mathf.Clamp(ally1ClassDropdown.value, 0, Mathf.Max(0, _available.Length - 1));
            ally1ClassDropdown.RefreshShownValue();
        }

        // Cache: key = BaseClassDef.className (preferred) and prefab name (fallback)
        for (int i = 0; i < _available.Length; i++)
        {
            var go = _available[i];
            if (go == null) continue;

            string key1 = null;
            var hs = go.GetComponentInChildren<HeroStats>(true);
            if (hs != null && hs.BaseClassDef != null && !string.IsNullOrEmpty(hs.BaseClassDef.className))
                key1 = hs.BaseClassDef.className;

            if (!string.IsNullOrEmpty(key1) && !_symbolIdToPrefabIndex.ContainsKey(key1))
                _symbolIdToPrefabIndex.Add(key1, i);

            if (!_symbolIdToPrefabIndex.ContainsKey(go.name))
                _symbolIdToPrefabIndex.Add(go.name, i);
        }
    }

    private int FindPrefabIndexBySymbolIdScan(string symbolId)
    {
        for (int i = 0; i < _available.Length; i++)
        {
            var go = _available[i];
            if (go == null) continue;

            if (go.name == symbolId) return i;

            var hs = go.GetComponentInChildren<HeroStats>(true);
            if (hs != null && hs.BaseClassDef != null && hs.BaseClassDef.className == symbolId)
                return i;
        }
        return -1;
    }

    private void RefreshAlly1FromSelectedDropdown()
    {
        if (ally1ClassDropdown == null) return;
        if (_available == null || _available.Length == 0) return;

        int idx = ally1ClassDropdown.value;
        if (idx < 0 || idx >= _available.Length) return;

        RefreshAlly1FromPrefab(_available[idx]);
    }

    private void RefreshAlly1FromPrefab(GameObject heroPrefab)
    {
        if (heroPrefab == null) return;

        var hs = heroPrefab.GetComponentInChildren<HeroStats>(true);
        var classDef = (hs != null) ? hs.BaseClassDef : null;

        if (logFlow)
        {
            Debug.Log($"[StartupClassSelectionPanel] RefreshAlly1FromPrefab prefab={heroPrefab.name} heroStats={(hs != null)} classDef={(classDef != null ? classDef.className : "NULL")}", this);
            Debug.Log($"[StartupClassSelectionPanel] UI refs: portrait={(ally1Portrait!=null)} label={(ally1Label!=null)} reelcraftName={(ally1ReelcraftName!=null)} reelcraftDesc={(ally1ReelcraftDesc!=null)} startHdr={(ally1StartAbilityHeader!=null)} startDD={(ally1StartAbilityDropdown!=null)} startDesc={(ally1StartAbilityDesc!=null)}", this);
        }

        // Portrait
        if (ally1Portrait != null)
        {
            if (hs != null && hs.Portrait != null)
            {
                ally1Portrait.enabled = true;
                ally1Portrait.sprite = hs.Portrait;
            }
            else
            {
                ally1Portrait.enabled = false;
            }
        }

        // Label (class name)
        if (ally1Label != null)
        {
            ally1Label.text = (classDef != null && !string.IsNullOrEmpty(classDef.className))
                ? classDef.className
                : heroPrefab.name;
        }

        // Reel symbols (this is the part you said is already working, keep it)
        RefreshAlly1ReelSymbols(hs);

        // Reelcraft fields
        if (ally1ReelcraftName != null)
            ally1ReelcraftName.text = (classDef != null) ? classDef.reelcraftName : "";

        if (ally1ReelcraftDesc != null)
            ally1ReelcraftDesc.text = (classDef != null) ? classDef.reelcraftDescription : "";

        // Starting ability header + dropdown + desc
        if (ally1StartAbilityHeader != null)
            ally1StartAbilityHeader.text = "Starting Ability";

        RefreshAlly1StartingAbilityDropdown(classDef);
        CommitAlly1StartingAbilitySelection();
    }

    private void RefreshAlly1ReelSymbols(HeroStats hs)
    {
        if (ally1ReelSymbolsContainer == null || reelSymbolIconPrefab == null) return;

        // Clear existing
        for (int i = _ally1ReelIcons.Count - 1; i >= 0; i--)
        {
            if (_ally1ReelIcons[i] != null)
                Destroy(_ally1ReelIcons[i].gameObject);
        }
        _ally1ReelIcons.Clear();

        if (hs == null || hs.ReelStrip == null || hs.ReelStrip.symbols == null) return;

        int limit = (maxReelSymbolsToShow <= 0) ? int.MaxValue : maxReelSymbolsToShow;
        HashSet<ReelSymbolSO> seen = showUniqueReelSymbolsOnly ? new HashSet<ReelSymbolSO>() : null;

        int shown = 0;
        for (int i = 0; i < hs.ReelStrip.symbols.Count; i++)
        {
            if (shown >= limit) break;

            var sym = hs.ReelStrip.symbols[i];
            if (sym == null || sym.icon == null) continue;

            if (seen != null)
            {
                if (seen.Contains(sym)) continue;
                seen.Add(sym);
            }

            var img = Instantiate(reelSymbolIconPrefab, ally1ReelSymbolsContainer);
            img.sprite = sym.icon;
            img.enabled = true;
            img.preserveAspect = true;
            _ally1ReelIcons.Add(img);
            shown++;
        }
    }

    private void RefreshAlly1StartingAbilityDropdown(ClassDefinitionSO classDef)
    {
        if (ally1StartAbilityDropdown == null) return;

        ally1StartAbilityDropdown.onValueChanged.RemoveAllListeners();

        _ally1StartAbilityOptions.Clear();

        // Gather abilities from classDef using your existing model:
        // - prefer classDef.abilities if present
        // - fallback to classDef.ability1/ability2 if that's how you store them
        var all = new List<AbilityDefinitionSO>();

        if (classDef != null)
        {
            if (classDef.abilities != null && classDef.abilities.Count > 0)
                all.AddRange(classDef.abilities);
            else
            {
                if (classDef.ability1 != null) all.Add(classDef.ability1);
                if (classDef.ability2 != null) all.Add(classDef.ability2);
            }
        }

        // Only show starterChoice abilities if any exist; otherwise show all.
        bool hasStarter = false;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] != null && all[i].starterChoice) { hasStarter = true; break; }
        }

        for (int i = 0; i < all.Count; i++)
        {
            var a = all[i];
            if (a == null) continue;
            if (hasStarter && !a.starterChoice) continue;
            _ally1StartAbilityOptions.Add(a);
        }

        // Build dropdown options
        ally1StartAbilityDropdown.ClearOptions();
        var opts = new List<TMP_Dropdown.OptionData>();
        for (int i = 0; i < _ally1StartAbilityOptions.Count; i++)
        {
            var a = _ally1StartAbilityOptions[i];
            opts.Add(new TMP_Dropdown.OptionData(a != null ? a.abilityName : "<null>"));
        }
        ally1StartAbilityDropdown.AddOptions(opts);

        bool interactable = _ally1StartAbilityOptions.Count > 0;
        ally1StartAbilityDropdown.interactable = interactable;
        ally1StartAbilityDropdown.SetValueWithoutNotify(0);
        ally1StartAbilityDropdown.RefreshShownValue();

        ally1StartAbilityDropdown.onValueChanged.AddListener(_ => CommitAlly1StartingAbilitySelection());
    }

    private void CommitAlly1StartingAbilitySelection()
    {
        AbilityDefinitionSO chosen = null;

        if (ally1StartAbilityDropdown != null && _ally1StartAbilityOptions.Count > 0)
        {
            int idx = ally1StartAbilityDropdown.value;
            if (idx >= 0 && idx < _ally1StartAbilityOptions.Count)
                chosen = _ally1StartAbilityOptions[idx];
        }

        // Store for runtime spawn (if you still use StartupPartySelectionData)
        StartupPartySelectionData.SetStartingAbility(0, chosen);

        if (ally1StartAbilityDesc != null)
            ally1StartAbilityDesc.text = (chosen != null) ? chosen.description : "";
    }

    private void Confirm()
    {
        // Party members are locked in by the NextHero flow.
        // Confirm should start the run with whatever has been chosen so far.
        var chosen = StartupPartySelectionData.GetChosenPartyPrefabs(_partySize);

        // Back-compat fallback: if slot 0 wasn't locked in, use current preview.
        if (chosen != null && chosen.Length > 0 && chosen[0] == null)
            chosen[0] = GetSelectedPrefabFromAlly1();

        Hide();
        _onConfirm?.Invoke(chosen);
    }

    private GameObject GetSelectedPrefabFromAlly1()
    {
        if (_available == null || _available.Length == 0) return null;

        if (ally1ClassDropdown != null)
        {
            int idx = ally1ClassDropdown.value;
            if (idx >= 0 && idx < _available.Length) return _available[idx];
        }

        // Fallback: first prefab
        return _available[0];
    }
}


////////////////////////////////////////////////////////////
