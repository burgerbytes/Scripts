using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Ability menu that populates ability buttons and initiates casting via BattleManager.
/// BattleManager is the authority for targeting/cast state.
/// </summary>
public class AbilityMenuUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text headerText;
    [SerializeField] private Transform listParent;
    [SerializeField] private AbilityButtonUI buttonPrefab;

    [Header("Ability Description Panel")]
    [SerializeField] private GameObject abilityDescPanel;
    [SerializeField] private TMP_Text abilityNameText;
    [SerializeField] private TMP_Text abilityDescriptionText;

    [Header("Refs")]
    [SerializeField] private ResourcePool resourcePool;
    [SerializeField] private BattleManager battleManager;
    [SerializeField] private ReelSpinSystem reelSpinSystem;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    private readonly List<AbilityButtonUI> buttons = new();
    private AbilityButtonUI selectedButton;
    private HeroStats currentHero;

    // While an ability is in targeting/cast state, keep the description panel visible.
    private bool _descPinnedUntilCast = false;


    private void Awake()
    {
        if (resourcePool == null)
            resourcePool = ResourcePool.Instance != null ? ResourcePool.Instance : FindFirstObjectByType<ResourcePool>();

        if (battleManager == null)
            battleManager = BattleManager.Instance != null ? BattleManager.Instance : FindFirstObjectByType<BattleManager>();

        if (reelSpinSystem == null)
            reelSpinSystem = FindFirstObjectByType<ReelSpinSystem>();

        HideAbilityDescPanel();

        if (root != null)
            root.SetActive(false);
    }

    private void OnEnable()
{
    if (reelSpinSystem == null)
        reelSpinSystem = FindFirstObjectByType<ReelSpinSystem>();
    if (reelSpinSystem != null)
        reelSpinSystem.OnReelPhaseChanged += HandleReelPhaseChanged;
}

private void OnDisable()
{
    if (reelSpinSystem != null)
        reelSpinSystem.OnReelPhaseChanged -= HandleReelPhaseChanged;
}

private void HandleReelPhaseChanged(bool inReelPhase)
{
    // Ability menu should be hidden during reel phase.
    if (inReelPhase)
        Close();
}

private void Update()
    {
        // If we pinned the description panel during targeting, hide it once the cast state clears.
        if (_descPinnedUntilCast)
        {
            var cast = AbilityCastState.Instance;
            bool hasPending = (cast != null && cast.HasPendingCast);

            if (!hasPending)
            {
                if (debugLogs) Debug.Log("[AbilityMenuUI] Cast state cleared -> hiding AbilityDescPanel.", this);
                _descPinnedUntilCast = false;
                HideAbilityDescPanel();
            }
        }
    }

    public void OpenForHero(HeroStats hero, List<AbilityDefinitionSO> abilities)
    {
        if (resourcePool == null)
            resourcePool = ResourcePool.Instance != null ? ResourcePool.Instance : FindFirstObjectByType<ResourcePool>();

        if (battleManager == null)
            battleManager = BattleManager.Instance != null ? BattleManager.Instance : FindFirstObjectByType<BattleManager>();

        if (reelSpinSystem == null)
            reelSpinSystem = FindFirstObjectByType<ReelSpinSystem>();

        if (debugLogs)
        {
            Debug.Log(
                $"[AbilityMenuUI] OpenForHero called. hero={(hero ? hero.name : "NULL")}, " +
                $"abilitiesCount={(abilities != null ? abilities.Count : -1)}, " +
                $"battleManager={(battleManager ? battleManager.name : "NULL")}, " +
                $"root={(root ? root.name : "NULL")} (activeSelf={(root ? root.activeSelf.ToString() : "n/a")})",
                this);
        }

        if (hero == null) return;
        currentHero = hero;

        if (root == null)
        {
            Debug.LogError("[AbilityMenuUI] OpenForHero: root is NULL. Drag AbilityMenuPanel into AbilityMenuUI.root OR ensure child is named 'AbilityMenuPanel'.", this);
            return;
        }

        root.SetActive(true);
        _descPinnedUntilCast = false;
        HideAbilityDescPanel();

        if (headerText != null)
            headerText.text = $"{hero.name} Abilities";

        if (listParent == null || buttonPrefab == null)
        {
            Debug.LogWarning("[AbilityMenuUI] Missing listParent or buttonPrefab; cannot populate buttons.", this);
            return;
        }

        ClearButtons();
        selectedButton = null;

        if (abilities != null)
        {
            foreach (var ability in abilities)
            {
                if (ability == null) continue;

                var btn = Instantiate(buttonPrefab, listParent);

                btn.Bind(
                    ability,
                    resourcePool,
                    OnAbilityButtonSelected,
                    OnAbilityConfirmed,
                    CanUseAbilityNow
                );

                buttons.Add(btn);
            }
        }

        RefreshAffordability();
    }

    private void OnAbilityButtonSelected(AbilityButtonUI btn)
    {
        if (btn == null) return;

        if (selectedButton != null && selectedButton != btn)
            selectedButton.SetSelected(false);

        selectedButton = btn;
        selectedButton.SetSelected(true);

        if (debugLogs)
            Debug.Log($"[AbilityMenuUI] Selected ability button: {(btn.Ability ? btn.Ability.name : "NULL")}", this);

        _descPinnedUntilCast = false;
        ShowAbilityDescPanel(btn.Ability);
    }

    private void OnAbilityConfirmed(AbilityDefinitionSO ability)
    {
        if (ability == null) return;

        if (debugLogs)
            Debug.Log($"[AbilityMenuUI] Confirmed/begin targeting for ability: {ability.name}", this);

        if (battleManager == null)
            battleManager = BattleManager.Instance != null ? BattleManager.Instance : FindFirstObjectByType<BattleManager>();

        if (reelSpinSystem == null)
            reelSpinSystem = FindFirstObjectByType<ReelSpinSystem>();

        if (battleManager == null || currentHero == null)
        {
            Debug.LogWarning($"[AbilityMenuUI] Cannot begin cast...battleManager={(battleManager ? battleManager.name : "NULL")}, currentHero={(currentHero ? currentHero.name : "NULL")}", this);
            return;
        }

        // ✅ BattleManager is the authority for pending target state.
        battleManager.BeginAbilityUseFromMenu(currentHero, ability);

        if (debugLogs)
            Debug.Log($"[AbilityMenuUI] Sent BeginAbilityUseFromMenu to BattleManager for ability={ability.name} caster={currentHero.name}", this);

        // Keep the description panel up while targeting is active.
        _descPinnedUntilCast = true;
        ShowAbilityDescPanel(ability);

        // Hide the list so the player can click enemies/party members.
        if (root != null) root.SetActive(false);
        selectedButton = null;
    }

    public void Close()
    {
        if (root != null) root.SetActive(false);

        // If we're in targeting/cast, keep description panel visible until cast completes.
        if (!_descPinnedUntilCast)
            HideAbilityDescPanel();

        selectedButton = null;
    }

    public void RefreshAffordability()
    {
        foreach (var b in buttons)
            if (b != null)
                b.RefreshInteractable();
    }

    private bool CanUseAbilityNow(AbilityDefinitionSO ability)
    {
        if (currentHero == null || ability == null)
            return false;

        // Once-per-turn abilities
        if (!currentHero.CanUseAbilityThisTurn(ability))
            return false;

        // Per-turn damaging attack limit (keeps UI consistent with BattleManager's gating)
        if (ability.baseDamage > 0 && !currentHero.CanCommitDamageAttackThisTurn())
            return false;

        // Optional: if hero is stunned, no actions this phase.
        if (currentHero.IsStunned)
            return false;

        // Keep UI consistent with BattleManager gating.
        if (battleManager != null && !battleManager.IsPlayerPhase)
            return false;

        return true;
    }

    private void ClearButtons()
    {
        if (listParent == null)
        {
            buttons.Clear();
            return;
        }

        for (int i = listParent.childCount - 1; i >= 0; i--)
            Destroy(listParent.GetChild(i).gameObject);

        buttons.Clear();
    }

    private void ShowAbilityDescPanel(AbilityDefinitionSO ability)
    {
        if (ability == null)
        {
            HideAbilityDescPanel();
            return;
        }

        if (abilityDescPanel == null)
            return;

        abilityDescPanel.SetActive(true);

        if (abilityNameText != null)
            abilityNameText.text = string.IsNullOrWhiteSpace(ability.abilityName) ? ability.name : ability.abilityName;

        if (abilityDescriptionText != null)
            abilityDescriptionText.text = ability.description ?? string.Empty;
    }

    private void HideAbilityDescPanel()
    {
        if (abilityDescPanel != null)
            abilityDescPanel.SetActive(false);

        if (abilityNameText != null)
            abilityNameText.text = string.Empty;

        if (abilityDescriptionText != null)
            abilityDescriptionText.text = string.Empty;
    }
}




////////////////////////////////////////////////////////////


////////////////////////////////////////////////////////////
