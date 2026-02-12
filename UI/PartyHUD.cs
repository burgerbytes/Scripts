// GUID: 5a8a06222baaa2b4883d4bb71239e8a6
////////////////////////////////////////////////////////////
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PartyHUD : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private BattleManager battleManager;
    [SerializeField] private AbilityMenuUI abilityMenu;
    [SerializeField] private HeroStatsPanelUI statsPanel;
    [SerializeField] private QuickAbilityMenuUI quickAbilityMenu;

    [Header("Quick Ability Menu")]
    [Tooltip("If true, the QuickAbilitiesButton will be enabled and will toggle the QuickAbilityMenuUI.\nPortrait click ability menu remains available either way.")]
    [SerializeField] private bool enableQuickAbilityMenu = true;

    [Tooltip("The single button that opens the Quick Ability Menu.")]
    [SerializeField] private Button quickAbilitiesButton;

    [Header("Reelcraft")]
    [SerializeField] private ReelcraftPanelUI reelcraftPanel;

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

    private readonly List<ReelcraftAbilityButtonForwarder> _reelcraftForwarders = new List<ReelcraftAbilityButtonForwarder>();
    private float _lastReelcraftResyncTime = -999f;

    // Prevent double-toggle if you also wire the button in the inspector
    private bool _addedQuickButtonListenerAtRuntime = false;

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

        if (quickAbilityMenu == null)
            quickAbilityMenu = FindFirstObjectByType<QuickAbilityMenuUI>(FindObjectsInactive.Include);

        if (quickAbilitiesButton == null)
        {
            var go = GameObject.Find("QuickAbilitiesButton");
            if (go != null) quickAbilitiesButton = go.GetComponent<Button>();
        }

        EnsureReelcraftPanelRef();

        if (slots == null || slots.Length == 0)
            slots = GetComponentsInChildren<PartyHUDSlot>(true);

        if (statsPanel != null && showStatsOnlyAfterPickAllyClick)
            statsPanel.Hide();

        if (slots != null)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null) continue;
                slots[i].Initialize(OnSlotClicked);
                AssignPortraitToSlot(i);
            }
        }

        CacheReelcraftForwarders();
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

        // Only add runtime listener if the button has no persistent listeners
        _addedQuickButtonListenerAtRuntime = false;
        if (quickAbilitiesButton != null)
        {
            int persistent = quickAbilitiesButton.onClick.GetPersistentEventCount();
            if (persistent == 0)
            {
                quickAbilitiesButton.onClick.AddListener(OnQuickAbilitiesButtonClicked);
                _addedQuickButtonListenerAtRuntime = true;
            }
        }

        if (statsPanel != null && showStatsOnlyAfterPickAllyClick && !_hasShownStatsOnce)
            statsPanel.Hide();

        RefreshAllSlots();
        ForceResyncReelcraftForwarders();
        UpdateQuickAbilitiesButtonInteractable();
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

        if (quickAbilitiesButton != null && _addedQuickButtonListenerAtRuntime)
            quickAbilitiesButton.onClick.RemoveListener(OnQuickAbilitiesButtonClicked);

        _addedQuickButtonListenerAtRuntime = false;
    }

    private void Update()
    {
        // Keep the button state correct even if state events miss a transition frame.
        UpdateQuickAbilitiesButtonInteractable();
    }

    private void OnQuickAbilitiesButtonClicked()
    {
        if (!enableQuickAbilityMenu) return;
        if (quickAbilityMenu == null) return;

        quickAbilityMenu.Toggle();
    }

    private void UpdateQuickAbilitiesButtonInteractable()
    {
        if (quickAbilitiesButton == null) return;

        // If quick menu is not enabled, leave the button interactable as-is (don’t force-disable).
        // This allows you to keep portrait clicks working without affecting button state.
        if (!enableQuickAbilityMenu)
            return;

        if (battleManager == null)
        {
            quickAbilitiesButton.interactable = true;
            return;
        }

        quickAbilitiesButton.interactable = battleManager.IsPlayerPhase;
    }

    // ------------------- existing behavior below -------------------

    private void CacheReelcraftForwarders()
    {
        _reelcraftForwarders.Clear();
        var found = GetComponentsInChildren<ReelcraftAbilityButtonForwarder>(true);
        if (found != null && found.Length > 0)
            _reelcraftForwarders.AddRange(found);
    }

    private void ForceResyncReelcraftForwarders()
    {
        if (Time.unscaledTime - _lastReelcraftResyncTime < 0.25f)
            return;

        _lastReelcraftResyncTime = Time.unscaledTime;

        if (_reelcraftForwarders == null || _reelcraftForwarders.Count == 0)
            CacheReelcraftForwarders();

        for (int i = 0; i < _reelcraftForwarders.Count; i++)
        {
            var fwd = _reelcraftForwarders[i];
            if (fwd == null) continue;
            fwd.ForceResync();
        }
    }

    private void EnsureReelcraftPanelRef()
    {
        if (reelcraftPanel != null) return;
        reelcraftPanel = FindFirstObjectByType<ReelcraftPanelUI>(FindObjectsInactive.Include);
    }

    private void HandleReelPhaseChanged(bool inReelPhase) { }

    private void OnBattleStateChanged(BattleManager.BattleState _)
    {
        RefreshAllSlots();
        UpdateQuickAbilitiesButtonInteractable();
    }

    private void OnActivePartyMemberChanged(int newIndex)
    {
        _selectedIndex = newIndex;
        _panelVisible = true;

        if (statsPanel != null && battleManager != null)
        {
            HeroStats hero = battleManager.GetHeroAtPartyIndex(newIndex);

            if (!showStatsOnlyAfterPickAllyClick || _hasShownStatsOnce)
                statsPanel.ShowForHero(hero);
            else
                statsPanel.SetHero(null);
        }

        RefreshAllSlots();
        UpdateQuickAbilitiesButtonInteractable();
    }

    private void AssignPortraitToSlot(int index)
    {
        if (slots == null || index < 0 || index >= slots.Length) return;
        if (battleManager == null) return;

        HeroStats hero = battleManager.GetHeroAtPartyIndex(index);
        if (hero == null) return;

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
            int incoming = battleManager.GetIncomingDamagePreviewForPartyIndex(i);

            bool isSelected = (i == _selectedIndex);
            slot.Render(snapshot, isSelected, incoming);
        }

        ForceResyncReelcraftForwarders();
    }

    private void OnSlotClicked(int index)
    {
        if (battleManager == null) return;

        if (battleManager.TryHandlePartySlotClickForPendingAbility(index))
        {
            RefreshAllSlots();
            return;
        }

        if (reelcraftPanel != null) reelcraftPanel.Hide();

        battleManager.SetActivePartyMember(index);

        // Clicking portraits should STILL work:
        // - Show stats
        // - Open the legacy ability menu (unchanged behavior)
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

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null) continue;
            slots[i].SetSelected(i == _selectedIndex);
            slots[i].SetActionPanelVisible(_panelVisible && i == _selectedIndex);
        }

        // ✅ Always keep legacy portrait-click menu working.
        if (abilityMenu != null)
        {
            HeroStats hero = battleManager.GetHeroAtPartyIndex(index);
            if (hero != null)
            {
                ClassDefinitionSO classDef = hero.AdvancedClassDef != null ? hero.AdvancedClassDef : hero.BaseClassDef;
                List<AbilityDefinitionSO> abilities = hero.GetUnlockedAbilitiesFromClassDef(classDef);
                abilityMenu.OpenForHero(hero, abilities);
            }
        }

        // Optional: if you want the quick menu to close when switching heroes:
        if (quickAbilityMenu != null)
            quickAbilityMenu.Close();

        RefreshAllSlots();
        UpdateQuickAbilitiesButtonInteractable();
    }

    public RectTransform GetSlotRectTransform(int partyIndex)
    {
        if (slots == null || slots.Length == 0) return null;

        if (partyIndex >= 0 && partyIndex < slots.Length && slots[partyIndex] != null)
        {
            if (slots[partyIndex].PartyIndex == partyIndex)
                return slots[partyIndex].RectTransform;
        }

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
