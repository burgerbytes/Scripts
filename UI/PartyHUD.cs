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
                if (debugLogs)
                    Debug.Log($"[PartyHUD] Initialized slot index {i} ({slots[i].name})", slots[i]);
            }
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

        if (battleManager != null)
            battleManager.SetActivePartyMember(index);

        if (togglePanelWhenClickingSelectedSlot && _selectedIndex == index)
            _panelVisible = !_panelVisible;
        else
        {
            _selectedIndex = index;
            _panelVisible = true;
        }

        if (!_panelVisible)
        {
            abilityMenu.Close();
            return;
        }

        HeroStats hero = battleManager.GetHeroAtPartyIndex(index);
        if (hero == null)
        {
            Debug.LogWarning($"[PartyHUD] No hero at party index {index}");
            return;
        }

        // Resolve active class definition without touching private fields
        ClassDefinitionSO classDef = null;
        if (hero.AdvancedClassDef != null) classDef = hero.AdvancedClassDef;
        else classDef = hero.BaseClassDef;

        var abilities = BuildAbilityListFromClassDef(classDef);

        if (debugLogs)
        {
            Debug.Log(
                $"[PartyHUD] Opening ability menu for '{hero.name}'. " +
                $"classDef={(classDef ? classDef.className : "NULL")}, " +
                $"abilitiesCount={abilities.Count}",
                this);
        }

        abilityMenu.OpenForHero(hero, abilities);
    }

    private List<AbilityDefinitionSO> BuildAbilityListFromClassDef(ClassDefinitionSO classDef)
    {
        var results = new List<AbilityDefinitionSO>();
        if (classDef == null) return results;

        // Prefer the new list if it has entries
        if (classDef.abilities != null && classDef.abilities.Count > 0)
        {
            for (int i = 0; i < classDef.abilities.Count; i++)
            {
                var a = classDef.abilities[i];
                if (a != null && !results.Contains(a))
                    results.Add(a);
            }
        }

        // Legacy fallback (or additive if you want both)
        if (classDef.ability1 != null && !results.Contains(classDef.ability1))
            results.Add(classDef.ability1);

        if (classDef.ability2 != null && !results.Contains(classDef.ability2))
            results.Add(classDef.ability2);

        return results;
    }

    // REQUIRED BY EnemyIntentVisualizer
    public RectTransform GetSlotRectTransform(int index)
    {
        if (slots == null || index < 0 || index >= slots.Length)
        {
            Debug.LogWarning($"[PartyHUD] GetSlotRectTransform invalid index {index}", this);
            return null;
        }

        return slots[index].GetComponent<RectTransform>();
    }
}

