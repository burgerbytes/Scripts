using System.Collections.Generic;
using UnityEngine;

public class PartyHUD : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private BattleManager battleManager;
    [SerializeField] private AbilityMenuUI abilityMenu;
    [SerializeField] private HeroStatsPanelUI statsPanel;

    [Header("Reel Phase")]
    [SerializeField] private ReelSpinSystem reelSpinSystem;
    [SerializeField] private bool hideMenusDuringReelPhase = true;

    [Header("Slots")]
    [SerializeField] private PartyHUDSlot[] slots;

    [Header("Behavior")]
    [SerializeField] private bool togglePanelWhenClickingSelectedSlot = false;

    [Tooltip("If true, the HeroStatsPanel stays hidden until the player clicks a PickAlly slot.\nThis prevents the panel from auto-appearing during startup / after class selection when BattleManager sets an initial active party member.")]
    [SerializeField] private bool showStatsOnlyAfterPickAllyClick = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    private int _selectedIndex = -1;
    private bool _panelVisible = false;
    private bool _hasShownStatsOnce = false;
    private bool _menusWereHiddenForReelPhase = false;
    private void Awake()
    {
        if (battleManager == null)
            battleManager = FindFirstObjectByType<BattleManager>();

        if (abilityMenu == null)
            abilityMenu = FindFirstObjectByType<AbilityMenuUI>();

        if (statsPanel == null)
            statsPanel = FindFirstObjectByType<HeroStatsPanelUI>();

        if (reelSpinSystem == null)
            reelSpinSystem = FindFirstObjectByType<ReelSpinSystem>();

        if (slots == null || slots.Length == 0)
            slots = GetComponentsInChildren<PartyHUDSlot>(true);

        // Keep the stats panel hidden on boot unless the player explicitly selects a hero.
        if (statsPanel != null && showStatsOnlyAfterPickAllyClick)
            statsPanel.Hide();

        // Initialize slots
        if (slots != null)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null) continue;

                slots[i].Initialize(OnSlotClicked);

                // assign portrait immediately
                AssignPortraitToSlot(i);
            }
        }
    }

    private void OnEnable()
    {
        if (battleManager != null)
        {
            battleManager.OnPartyChanged += RefreshAllSlots;
            battleManager.OnActivePartyMemberChanged += OnActivePartyMemberChanged;
            battleManager.OnBattleStateChanged += OnBattleStateChanged;
        }

        if (reelSpinSystem != null)
            reelSpinSystem.OnReelPhaseChanged += HandleReelPhaseChanged;

        // Class selection -> battle scene transition can re-enable objects; keep stats hidden until user clicks.
        if (statsPanel != null && showStatsOnlyAfterPickAllyClick && !_hasShownStatsOnce)
            statsPanel.Hide();

        RefreshAllSlots();
    }

    private void OnDisable()
    {
        if (battleManager != null)
        {
            battleManager.OnPartyChanged -= RefreshAllSlots;
            battleManager.OnActivePartyMemberChanged -= OnActivePartyMemberChanged;
            battleManager.OnBattleStateChanged -= OnBattleStateChanged;
        }

        if (reelSpinSystem != null)
            reelSpinSystem.OnReelPhaseChanged -= HandleReelPhaseChanged;
    }

    private void HandleReelPhaseChanged(bool inReelPhase)
    {
        if (!hideMenusDuringReelPhase) return;

        if (inReelPhase)
        {
            // Hide menus while the player is interacting with the reels.
            if (abilityMenu != null) abilityMenu.Close();
            if (statsPanel != null) statsPanel.Hide();
            _menusWereHiddenForReelPhase = true;
            return;
        }

        // Reel phase ended -> restore if the player had a hero selected.
        if (!_menusWereHiddenForReelPhase) return;
        _menusWereHiddenForReelPhase = false;

        // Only re-open if the user had previously opened these panels.
        if (_selectedIndex >= 0 && battleManager != null)
        {
            var snap = battleManager.GetPartyMemberSnapshot(_selectedIndex);
            if (!snap.IsDead)
            {
                // Stats panel: respect "show after click" behavior.
				if (statsPanel != null && (!showStatsOnlyAfterPickAllyClick || _hasShownStatsOnce))
				{
					// Keep this consistent with the rest of PartyHUD: Stats panel shows using the HeroStats reference.
					HeroStats hero = battleManager.GetHeroAtPartyIndex(_selectedIndex);
					if (hero != null)
						statsPanel.ShowForHero(hero);
				}

                // Ability menu: only re-open if it was visible before.
                if (abilityMenu != null && _panelVisible)
                    OpenAbilityMenuForSelectedHero();
            }
        }

        RefreshAllSlots();
    }

    private void OpenAbilityMenuForSelectedHero()
    {
        if (battleManager == null || abilityMenu == null) return;
        if (_selectedIndex < 0) return;

		var heroStats = battleManager.GetHeroAtPartyIndex(_selectedIndex);
        if (heroStats == null) return;

        // Abilities are defined on the hero's active class definition (not on HeroStats).
        // Prefer Advanced class if chosen, otherwise fall back to Base.
        ClassDefinitionSO classDef = (heroStats.AdvancedClassDef != null) ? heroStats.AdvancedClassDef : heroStats.BaseClassDef;

        List<AbilityDefinitionSO> abilities = new List<AbilityDefinitionSO>();
        if (classDef != null)
        {
            if (classDef.abilities != null && classDef.abilities.Count > 0)
            {
                abilities.AddRange(classDef.abilities);
            }
            else
            {
                // Legacy 2-slot abilities.
                if (classDef.ability1 != null) abilities.Add(classDef.ability1);
                if (classDef.ability2 != null) abilities.Add(classDef.ability2);
            }
        }

        abilityMenu.OpenForHero(heroStats, abilities);
    }

    private void OnBattleStateChanged(BattleManager.BattleState _)
    {
        // Any state change can affect previews/selection UI.
        RefreshAllSlots();
    }

    private void OnActivePartyMemberChanged(int newIndex)
    {
        // Keep HUD selection in sync with battle manager.
        _selectedIndex = newIndex;
        _panelVisible = true;

        // Update stats only if we're allowed to auto-show, or the player has already shown it at least once.
        if (statsPanel != null && battleManager != null)
        {
            HeroStats hero = battleManager.GetHeroAtPartyIndex(newIndex);

            if (!showStatsOnlyAfterPickAllyClick || _hasShownStatsOnce)
                statsPanel.ShowForHero(hero);
            else
                statsPanel.SetHero(null); // stay hidden until click
        }

        RefreshAllSlots();
    }

    private void AssignPortraitToSlot(int index)
    {
        if (slots == null || index < 0 || index >= slots.Length) return;
        if (battleManager == null) return;

        HeroStats hero = battleManager.GetHeroAtPartyIndex(index);
        if (hero == null) return;

        // ✅ Use HERO prefab portrait, not class portrait
        slots[index].SetPortrait(hero.Portrait);
    }

    private void RefreshAllSlots()
    {
        if (battleManager == null || slots == null) return;

        for (int i = 0; i < slots.Length; i++)
        {
            PartyHUDSlot slot = slots[i];
            if (slot == null) continue;

            var snapshot = battleManager.GetPartyMemberSnapshot(i);

            // Use planned intents for preview while in player phase.
            int incoming = battleManager.GetIncomingDamagePreviewForPartyIndex(i);

            bool isSelected = (i == _selectedIndex);
            slot.Render(snapshot, isSelected, incoming);
        }
    }

    private void OnSlotClicked(int index)
    {
        if (debugLogs)
            Debug.Log($"[PartyHUD] OnSlotClicked(index={index})", this);

        if (battleManager == null || abilityMenu == null)
        {
            Debug.LogWarning("[PartyHUD] Missing BattleManager or AbilityMenuUI reference.");
            return;
        }

        // If we're currently casting a self/ally ability (e.g., Block), allow the click to confirm targeting.
        if (battleManager.TryHandlePartySlotClickForPendingAbility(index))
        {
            RefreshAllSlots();
            return;
        }

        battleManager.SetActivePartyMember(index);

        // Update stats panel to match the clicked hero (and ensure it's visible).
        if (statsPanel != null)
        {
            HeroStats clickedHero = battleManager.GetHeroAtPartyIndex(index);
            statsPanel.ShowForHero(clickedHero);
            _hasShownStatsOnce = true;
        }

        if (togglePanelWhenClickingSelectedSlot && _selectedIndex == index)
            _panelVisible = !_panelVisible;
        else
        {
            _selectedIndex = index;
            _panelVisible = true;
        }

        // Highlight + panel visibility
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null) continue;
            slots[i].SetSelected(i == _selectedIndex);
            slots[i].SetActionPanelVisible(_panelVisible && i == _selectedIndex);
        }

        // Open ability menu for that hero
        HeroStats hero = battleManager.GetHeroAtPartyIndex(index);
        if (hero == null) return;

        ClassDefinitionSO classDef =
            hero.AdvancedClassDef != null ? hero.AdvancedClassDef : hero.BaseClassDef;

        List<AbilityDefinitionSO> abilities = BuildAbilityListFromClassDef(classDef);

        if (debugLogs)
        {
            Debug.Log(
                $"[PartyHUD] Opening ability menu for '{hero.name}'. " +
                $"classDef={(classDef ? classDef.className : "NULL")}, " +
                $"abilitiesCount={abilities.Count}",
                this);
        }

        abilityMenu.OpenForHero(hero, abilities);

        RefreshAllSlots();
    }

    private List<AbilityDefinitionSO> BuildAbilityListFromClassDef(ClassDefinitionSO classDef)
    {
        var results = new List<AbilityDefinitionSO>();
        if (classDef == null) return results;

        if (classDef.abilities != null)
        {
            for (int i = 0; i < classDef.abilities.Count; i++)
            {
                var a = classDef.abilities[i];
                if (a != null) results.Add(a);
            }
        }

        return results;
    }

    /// <summary>
    /// Returns the RectTransform for the PartyHUD slot for the given party index.
    /// Used by systems that want to position UI elements (e.g., enemy intent) relative to a slot.
    /// </summary>
    public RectTransform GetSlotRectTransform(int partyIndex)
    {
        if (slots == null || slots.Length == 0) return null;

        // Fast path: array index matches party index
        if (partyIndex >= 0 && partyIndex < slots.Length && slots[partyIndex] != null)
        {
            // Only accept if the slot's configured PartyIndex matches, otherwise fall through to search.
            if (slots[partyIndex].PartyIndex == partyIndex)
                return slots[partyIndex].RectTransform;
        }

        // Search path: find slot whose configured PartyIndex matches.
        for (int i = 0; i < slots.Length; i++)
        {
            var s = slots[i];
            if (s == null) continue;
            if (s.PartyIndex == partyIndex)
                return s.RectTransform;
        }

        return null;
    }


}

////////////////////////////////////////////////////////////
