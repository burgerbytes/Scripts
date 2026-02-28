////////////////////////////////////////////////////////////
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AbilityButtonUI : MonoBehaviour
{
    [Header("Core UI")]
    [SerializeField] private Button button;
    [SerializeField] private Image buttonBackground;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text costText;

    [Header("Cost Sprite Indices (match TMP Sprite Asset table)")]
    [SerializeField] private int attackSpriteIndex = 2;
    [SerializeField] private int defenseSpriteIndex = 0;
    [SerializeField] private int magicSpriteIndex = 1;
    [SerializeField] private int wildSpriteIndex = 3;

    [Header("Formatting")]
    [SerializeField] private string separator = "  ";

    [Header("Disabled / Gray-out")]
    [SerializeField] private bool grayOutWhenUnaffordable = true;
    [SerializeField] private Color affordableTextColor = Color.white;
    [SerializeField] private Color unaffordableTextColor = new Color(0.6f, 0.6f, 0.6f, 1f);

    [SerializeField] private Color affordableBackgroundColor = Color.white;
    [SerializeField] private Color unaffordableBackgroundColor = new Color(0.35f, 0.35f, 0.35f, 1f);

    [Header("Selection Highlight")]
    [SerializeField] private bool boldNameWhenSelected = true;
    [SerializeField] private bool highlightWhenSelected = true;
    [SerializeField] private Color selectedTextColor = new Color(1f, 0.95f, 0.6f, 1f);
    [SerializeField] private Color selectedBackgroundColor = new Color(0.45f, 0.45f, 0.25f, 1f);

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private AbilityDefinitionSO ability;
    private ResourcePool resourcePool;
    private Func<AbilityDefinitionSO, bool> canUseExtraPredicate;

    // Optional context for dynamic costs (e.g., ramping basic attacks).
    private HeroStats heroContext;

    private System.Action<AbilityButtonUI> onSelected;
    private System.Action<AbilityDefinitionSO> onClickedConfirm;

    private bool isSelected;

    private bool cachedOriginals;
    private Color originalNameColor;
    private Color originalCostColor;
    private Color originalBgColor;

    public AbilityDefinitionSO Ability => ability;

    public void Bind(
        AbilityDefinitionSO ability,
        ResourcePool resourcePool,
        System.Action<AbilityButtonUI> onSelectedCallback,
        System.Action<AbilityDefinitionSO> onClickedConfirmCallback = null,
        Func<AbilityDefinitionSO, bool> canUseExtraPredicate = null,
        HeroStats heroContext = null
    )
    {
        this.ability = ability;
        this.resourcePool = resourcePool;
        this.onSelected = onSelectedCallback;
        this.onClickedConfirm = onClickedConfirmCallback;
        this.canUseExtraPredicate = canUseExtraPredicate;
        this.heroContext = heroContext;

        CacheOriginalsIfNeeded();

        if (nameText != null)
            nameText.text = AbilityDefSOReader.GetDisplayName(ability);

        if (costText != null)
        {
            costText.richText = true;
            costText.text = BuildCostString(ability);
        }

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnClicked);
        }

        SetSelected(false);
        RefreshInteractable();
    }

    private void CacheOriginalsIfNeeded()
    {
        if (cachedOriginals) return;

        if (nameText != null) originalNameColor = nameText.color;
        if (costText != null) originalCostColor = costText.color;
        if (buttonBackground != null) originalBgColor = buttonBackground.color;

        if (affordableTextColor == Color.white && nameText != null) affordableTextColor = originalNameColor;
        if (affordableBackgroundColor == Color.white && buttonBackground != null) affordableBackgroundColor = originalBgColor;

        cachedOriginals = true;
    }

    private void OnClicked()
    {
        // One-step interaction:
        // - Click an ability to begin casting immediately.
        // - UI still highlights and shows the description panel via onSelected.
        if (debugLogs)
            Debug.Log("[AbilityButtonUI] Click -> BeginCast " + (ability != null ? ability.name : "NULL"), this);

        // Ensure selection visuals + description update.
        onSelected?.Invoke(this);

        // Begin cast / targeting flow.
        onClickedConfirm?.Invoke(ability);
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;
        bool usable = IsUsable();

        if (nameText != null)
        {
            if (boldNameWhenSelected)
                nameText.fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;

            if (grayOutWhenUnaffordable && !usable)
                nameText.color = unaffordableTextColor;
            else if (selected)
                nameText.color = selectedTextColor;
            else
                nameText.color = affordableTextColor;
        }

        if (costText != null)
        {
            if (grayOutWhenUnaffordable && !usable)
                costText.color = unaffordableTextColor;
            else
                costText.color = affordableTextColor;
        }

        if (buttonBackground != null)
        {
            if (grayOutWhenUnaffordable && !usable)
                buttonBackground.color = unaffordableBackgroundColor;
            else if (selected && highlightWhenSelected)
                buttonBackground.color = selectedBackgroundColor;
            else
                buttonBackground.color = affordableBackgroundColor;
        }

        if (debugLogs)
            Debug.Log($"[AbilityButtonUI] SetSelected({selected}) '{name}' usable={usable}", this);
    }

    public void RefreshInteractable()
    {
        // Cost can change during the turn (e.g., ramping basic attacks), so refresh it here.
        if (costText != null)
            costText.text = BuildCostString(ability);

        bool usable = IsUsable();

        if (button != null)
            button.interactable = usable;

        SetSelected(isSelected);
    }

    private bool IsUsable()
    {
        if (ability == null || resourcePool == null) return false;

        // Base rule: resource affordability (with dynamic cost support).
        ResourceCost effectiveCost = ComputeEffectiveCost();

        // If we must spend ALL ATK, ensure we have some ATK.
        if (ability.spendAllAttackResources && effectiveCost.attack <= 0)
            return false;

        if (!resourcePool.CanAfford(effectiveCost)) return false;

        // Optional extra rule (e.g., once-per-turn, attack-per-turn limits, etc.)
        if (canUseExtraPredicate != null && !canUseExtraPredicate.Invoke(ability))
            return false;

        return true;
    }

    private ResourceCost ComputeEffectiveCost()
    {
        if (ability == null) return default;

        ResourceCost c = ability.cost;

        // Special: consume-all-ATK abilities (e.g., Fighter "Heavy Strike")
        if (ability.spendAllAttackResources)
        {
            long atk = resourcePool != null ? resourcePool.Attack : 0;
            c.attack = atk;
        }

        // Ramping basic attacks: Slash / Dart / Quick Blade
        if (heroContext != null && HeroStats.IsRampingBasicAttackAbility(ability))
        {
            long add = heroContext.GetRampingBasicAttackAdditionalAtkCost(ability);
            c.attack = System.Math.Max(0L, c.attack) + System.Math.Max(0L, add);
        }

        return c;
    }

    private string BuildCostString(AbilityDefinitionSO ability)
    {
        if (ability == null) return string.Empty;

        ResourceCost effective = ComputeEffectiveCost();

        long cA = effective.attack;
        long cD = effective.defense;
        long cM = effective.magic;
        long cW = effective.wild;

        var parts = new List<string>(4);

        if (ability.spendAllAttackResources) parts.Add($"{Sprite(attackSpriteIndex)} ALL");
        else if (cA > 0) parts.Add($"{Sprite(attackSpriteIndex)} {cA}");
        if (cD > 0) parts.Add($"{Sprite(defenseSpriteIndex)} {cD}");
        if (cM > 0) parts.Add($"{Sprite(magicSpriteIndex)} {cM}");
        if (cW > 0) parts.Add($"{Sprite(wildSpriteIndex)} {cW}");

        return parts.Count > 0 ? string.Join(separator, parts) : string.Empty;
    }

    private static string Sprite(int index) => $"<sprite index={index}>";
}


////////////////////////////////////////////////////////////


////////////////////////////////////////////////////////////
