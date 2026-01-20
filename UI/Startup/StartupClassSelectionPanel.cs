// PATH: Assets/Scripts/UI/Startup/StartupClassSelectionPanel.cs
// GUID: e60b184de68ccf241b14fb67f0b6851b
////////////////////////////////////////////////////////////
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Simple startup party/class selection UI.
///
/// Expected scene wiring (minimal):
/// - A root GameObject for the panel
/// - One TMP_Dropdown per party slot
/// - Optional portrait Images per slot (reads from HeroStats.Portrait on the prefab)
/// - A Confirm/Start button
///
/// The panel does NOT create/destroy heroes. It only returns the selected prefabs.
/// </summary>
public class StartupClassSelectionPanel : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject root;

    [Header("Slot UI")]
    [SerializeField] private TMP_Dropdown[] slotDropdowns = new TMP_Dropdown[3];
    [SerializeField] private Image[] slotPortraits = new Image[3];
    [SerializeField] private TMP_Text[] slotLabels = new TMP_Text[3];

    [Header("Reel Symbols Preview (Optional)")]
    [Tooltip("Assign a container (e.g., HorizontalLayoutGroup) per party slot. The panel will populate it with symbol icons from the selected hero's ReelStrip.")]
    [SerializeField] private Transform[] slotReelSymbolContainers = new Transform[3];
    [Tooltip("UI Image prefab used to render each reel symbol icon.")]
    [SerializeField] private Image reelSymbolIconPrefab;
    [Tooltip("Maximum number of symbol icons to show per slot (helps avoid huge strips overflowing UI). Set to 0 to show ALL symbols in the strip.")]
    [SerializeField] private int maxReelSymbolsToShow = 0;
    [Tooltip("If true, only show one icon per unique symbol (by ScriptableObject reference). If false, show the FULL strip including duplicates.")]
    [SerializeField] private bool showUniqueReelSymbolsOnly = false;

    [Header("Class Info (Optional)")]
    [Tooltip("Optional: per-slot text for the selected class' Reelcraft ability name.")]
    [SerializeField] private TMP_Text[] slotReelcraftNameTexts = new TMP_Text[3];

    [Tooltip("Optional: per-slot text for the selected class' Reelcraft ability description.")]
    [SerializeField] private TMP_Text[] slotReelcraftDescriptionTexts = new TMP_Text[3];

    [Header("Starting Ability Choice (Optional)")]
    [Tooltip("Optional: per-slot dropdown to choose which ability the hero begins with (uses AbilityDefinitionSO.starterChoice).")]
    [SerializeField] private TMP_Dropdown[] slotStartingAbilityDropdowns = new TMP_Dropdown[3];

    [Tooltip("Optional: per-slot text that shows the description of the currently selected starting ability.")]
    [SerializeField] private TMP_Text[] slotStartingAbilityDescriptionTexts = new TMP_Text[3];


    [Header("Buttons")]
    [SerializeField] private Button confirmButton;

    private GameObject[] _available;
    private int _partySize;
    private Action<GameObject[]> _onConfirm;

    private readonly List<Image>[] _slotReelIcons = new List<Image>[3];
    private readonly List<AbilityDefinitionSO>[] _slotStartingAbilityOptions = new List<AbilityDefinitionSO>[3];


    private void Awake()
    {
        for (int i = 0; i < _slotReelIcons.Length; i++)
        {
            if (_slotReelIcons[i] == null) _slotReelIcons[i] = new List<Image>();
        }

        if (confirmButton != null)
        {
            confirmButton.onClick.RemoveListener(Confirm);
            confirmButton.onClick.AddListener(Confirm);
        }

        Hide();
    }

    public void Show(GameObject[] availablePartyPrefabs, int partySize, Action<GameObject[]> onConfirm)
    {
        _available = availablePartyPrefabs ?? Array.Empty<GameObject>();
        _partySize = Mathf.Clamp(partySize, 1, 3);
        StartupPartySelectionData.Clear();
        _onConfirm = onConfirm;

        if (root != null) root.SetActive(true);
        gameObject.SetActive(true);

        // Build dropdown options
        var options = new List<TMP_Dropdown.OptionData>();
        for (int i = 0; i < _available.Length; i++)
        {
            var go = _available[i];
            options.Add(new TMP_Dropdown.OptionData(go != null ? go.name : "<Missing Prefab>"));
        }

        for (int slot = 0; slot < slotDropdowns.Length; slot++)
        {
            var dd = slotDropdowns[slot];
            if (dd == null) continue;

            dd.onValueChanged.RemoveAllListeners();
            dd.ClearOptions();
            dd.AddOptions(options);

            // Disable slots above party size
            bool active = slot < _partySize;
            dd.interactable = active;
            if (slotLabels != null && slot < slotLabels.Length && slotLabels[slot] != null)
                slotLabels[slot].text = active ? $"Reel {slot + 1}" : $"Reel {slot + 1} (unused)";

            int capturedSlot = slot;
            dd.onValueChanged.AddListener(_ => RefreshPortrait(capturedSlot));

            dd.onValueChanged.AddListener(_ => RefreshReelSymbols(capturedSlot));
            dd.onValueChanged.AddListener(_ => RefreshClassInfo(capturedSlot));

            // Default selection: slot index if possible
            dd.value = Mathf.Clamp(slot, 0, Mathf.Max(0, _available.Length - 1));
            dd.RefreshShownValue();

            RefreshPortrait(slot);

            RefreshReelSymbols(slot);
            RefreshClassInfo(slot);
        }

        if (confirmButton != null)
            confirmButton.interactable = _available.Length > 0;
    }

    public void Hide()
    {
        if (root != null) root.SetActive(false);
        // keep component enabled; just hide visuals
    }

    private void RefreshPortrait(int slot)
    {
        if (slotPortraits == null || slot >= slotPortraits.Length) return;
        var img = slotPortraits[slot];
        if (img == null) return;

        var prefab = GetSelectedPrefabForSlot(slot);
        if (prefab == null)
        {
            img.enabled = false;
            return;
        }

        var hs = prefab.GetComponentInChildren<HeroStats>(true);
        if (hs != null && hs.Portrait != null)
        {
            img.enabled = true;
            img.sprite = hs.Portrait;
        }
        else
        {
            img.enabled = false;
        }
    }

    
    private void RefreshReelSymbols(int slot)
    {
        if (slotReelSymbolContainers == null || slot >= slotReelSymbolContainers.Length) return;
        var container = slotReelSymbolContainers[slot];
        if (container == null) return;
        if (reelSymbolIconPrefab == null) return;

        if (_slotReelIcons[slot] == null) _slotReelIcons[slot] = new List<Image>();

        // Clear previous
        for (int i = _slotReelIcons[slot].Count - 1; i >= 0; i--)
        {
            if (_slotReelIcons[slot][i] != null)
                Destroy(_slotReelIcons[slot][i].gameObject);
        }
        _slotReelIcons[slot].Clear();

        var prefab = GetSelectedPrefabForSlot(slot);
        if (prefab == null) return;

        var hs = prefab.GetComponentInChildren<HeroStats>(true);
        if (hs == null || hs.ReelStrip == null || hs.ReelStrip.symbols == null) return;

        var symbols = hs.ReelStrip.symbols;

        // If maxReelSymbolsToShow <= 0, show ALL symbols (including duplicates).
        int limit = maxReelSymbolsToShow <= 0 ? int.MaxValue : Mathf.Clamp(maxReelSymbolsToShow, 1, 9999);

        // Optional: show unique symbols only
        HashSet<ReelSymbolSO> seen = showUniqueReelSymbolsOnly ? new HashSet<ReelSymbolSO>() : null;

        int shown = 0;
        for (int i = 0; i < symbols.Count; i++)
        {
            if (shown >= limit) break;
            var sym = symbols[i];
            if (sym == null || sym.icon == null) continue;

            if (seen != null)
            {
                if (seen.Contains(sym)) continue;
                seen.Add(sym);
            }

            var img = Instantiate(reelSymbolIconPrefab, container);
            img.sprite = sym.icon;
            img.enabled = true;
            img.preserveAspect = true;
            _slotReelIcons[slot].Add(img);
            shown++;
        }
    }

    

    private void RefreshClassInfo(int slot)
    {
        // Reelcraft text
        ClassDefinitionSO classDef = GetSelectedClassDefForSlot(slot);

        if (slotReelcraftNameTexts != null && slot < slotReelcraftNameTexts.Length && slotReelcraftNameTexts[slot] != null)
        {
            slotReelcraftNameTexts[slot].text = (classDef != null && !string.IsNullOrWhiteSpace(classDef.reelcraftName))
                ? classDef.reelcraftName
                : "";
        }

        if (slotReelcraftDescriptionTexts != null && slot < slotReelcraftDescriptionTexts.Length && slotReelcraftDescriptionTexts[slot] != null)
        {
            slotReelcraftDescriptionTexts[slot].text = (classDef != null && !string.IsNullOrWhiteSpace(classDef.reelcraftDescription))
                ? classDef.reelcraftDescription
                : "";
        }

        // Starting ability dropdown
        if (slotStartingAbilityDropdowns == null || slot >= slotStartingAbilityDropdowns.Length || slotStartingAbilityDropdowns[slot] == null)
            return;

        var dd = slotStartingAbilityDropdowns[slot];
        dd.onValueChanged.RemoveAllListeners();

        if (_slotStartingAbilityOptions[slot] == null)
            _slotStartingAbilityOptions[slot] = new List<AbilityDefinitionSO>();
        _slotStartingAbilityOptions[slot].Clear();

        List<AbilityDefinitionSO> all = GetAbilitiesFromClassDef(classDef);
        if (all == null) all = new List<AbilityDefinitionSO>();

        // Prefer starterChoice abilities if any exist; otherwise list all abilities.
        bool hasStarterChoices = false;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] != null && all[i].starterChoice)
            {
                hasStarterChoices = true;
                break;
            }
        }

        for (int i = 0; i < all.Count; i++)
        {
            var a = all[i];
            if (a == null) continue;
            if (hasStarterChoices && !a.starterChoice) continue;
            _slotStartingAbilityOptions[slot].Add(a);
        }

        // Populate dropdown options
        var options = new List<TMP_Dropdown.OptionData>();
        for (int i = 0; i < _slotStartingAbilityOptions[slot].Count; i++)
        {
            var a = _slotStartingAbilityOptions[slot][i];
            options.Add(new TMP_Dropdown.OptionData(a != null ? a.abilityName : "<null>"));
        }

        dd.ClearOptions();
        dd.AddOptions(options);

        bool interactable = (_slotStartingAbilityOptions[slot].Count > 0);
        dd.interactable = interactable;

        // Default selection to first option
        dd.value = 0;
        dd.RefreshShownValue();

        // Commit selection immediately
        CommitStartingAbilitySelection(slot);

        dd.onValueChanged.AddListener(_ => CommitStartingAbilitySelection(slot));
    }

    private void CommitStartingAbilitySelection(int slot)
    {
        if (_slotStartingAbilityOptions == null || slot < 0 || slot >= _slotStartingAbilityOptions.Length) return;

        AbilityDefinitionSO selected = null;
        var opts = _slotStartingAbilityOptions[slot];
        if (opts != null && slotStartingAbilityDropdowns != null && slot < slotStartingAbilityDropdowns.Length && slotStartingAbilityDropdowns[slot] != null)
        {
            int idx = slotStartingAbilityDropdowns[slot].value;
            if (idx >= 0 && idx < opts.Count)
                selected = opts[idx];
        }

        StartupPartySelectionData.SetStartingAbility(slot, selected);

        if (slotStartingAbilityDescriptionTexts != null && slot < slotStartingAbilityDescriptionTexts.Length && slotStartingAbilityDescriptionTexts[slot] != null)
        {
            slotStartingAbilityDescriptionTexts[slot].text = (selected != null) ? selected.description : "";
        }
    }

    private ClassDefinitionSO GetSelectedClassDefForSlot(int slot)
    {
        var prefab = GetSelectedPrefabForSlot(slot);
        if (prefab == null) return null;

        var hs = prefab.GetComponentInChildren<HeroStats>(true);
        if (hs == null) return null;

        // At startup we only care about the base class definition.
        return hs.BaseClassDef;
    }

    private List<AbilityDefinitionSO> GetAbilitiesFromClassDef(ClassDefinitionSO classDef)
    {
        var list = new List<AbilityDefinitionSO>();
        if (classDef == null) return list;

        if (classDef.abilities != null && classDef.abilities.Count > 0)
        {
            list.AddRange(classDef.abilities);
        }
        else
        {
            if (classDef.ability1 != null) list.Add(classDef.ability1);
            if (classDef.ability2 != null) list.Add(classDef.ability2);
        }

        return list;
    }

private GameObject GetSelectedPrefabForSlot(int slot)
    {
        if (_available == null || _available.Length == 0) return null;
        if (slotDropdowns == null || slot >= slotDropdowns.Length || slotDropdowns[slot] == null) return null;

        int idx = slotDropdowns[slot].value;
        if (idx < 0 || idx >= _available.Length) return null;
        return _available[idx];
    }

    private void Confirm()
    {
        if (_available == null || _available.Length == 0) return;

        var chosen = new GameObject[3];
        for (int i = 0; i < 3; i++)
        {
            if (i >= _partySize)
            {
                chosen[i] = null;
                continue;
            }

            chosen[i] = GetSelectedPrefabForSlot(i);
            CommitStartingAbilitySelection(i);
        }

        Hide();
        _onConfirm?.Invoke(chosen);
    }
}
