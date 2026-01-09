using System.Collections.Generic;
using UnityEngine;

public class PartyHUD : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private BattleManager battleManager;
    [SerializeField] private AbilityMenuUI abilityMenu;

    [Header("Slots")]
    [SerializeField] private PartyHUDSlot[] slots;

    [Header("Behavior")]
    [SerializeField] private bool togglePanelWhenClickingSelectedSlot = false;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    private int _selectedIndex = -1;
    private bool _panelVisible = false;

    private void Awake()
    {
        if (battleManager == null)
            battleManager = FindFirstObjectByType<BattleManager>();

        if (abilityMenu == null)
            abilityMenu = FindFirstObjectByType<AbilityMenuUI>();

        if (slots == null || slots.Length == 0)
            slots = GetComponentsInChildren<PartyHUDSlot>(true);

        if (debugLogs)
        {
            Debug.Log(
                $"[PartyHUD] Awake on '{name}'. " +
                $"battleManager={(battleManager ? battleManager.name : "NULL")}, " +
                $"abilityMenu={(abilityMenu ? abilityMenu.name : "NULL")}, " +
                $"slotsCount={(slots != null ? slots.Length : 0)}",
                this);
        }

        // Initialize slots
        if (slots != null)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null) continue;

                slots[i].Initialize(OnSlotClicked);

                // assign portrait immediately
                AssignPortraitToSlot(i);

                if (debugLogs)
                    Debug.Log($"[PartyHUD] Initialized slot index {i} ({slots[i].name})", slots[i]);
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
        RefreshAllSlots();
    }

    private void AssignPortraitToSlot(int index)
    {
        if (slots == null || index < 0 || index >= slots.Length) return;
        if (battleManager == null) return;

        HeroStats hero = battleManager.GetHeroAtPartyIndex(index);
        if (hero == null) return;

        // Resolve class definition safely
        ClassDefinitionSO classDef =
            hero.AdvancedClassDef != null ? hero.AdvancedClassDef : hero.BaseClassDef;

        Sprite portrait = classDef != null ? classDef.portraitSprite : null;

        slots[index].SetPortrait(portrait);
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

        battleManager.SetActivePartyMember(index);

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
