using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AbilityButtonUI : MonoBehaviour
{
    [Header("Core UI")]
    [SerializeField] private Button button;
    [SerializeField] private Image buttonBackground;      // optional: set this to your button bg Image
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text costText;

    [Header("Cost Sprite Indices (match TMP Sprite Asset table)")]
    [SerializeField] private int attackSpriteIndex = 2;   // sword
    [SerializeField] private int defenseSpriteIndex = 0;  // shield
    [SerializeField] private int magicSpriteIndex = 1;    // diamond
    [SerializeField] private int wildSpriteIndex = 3;     // star

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
    private TopStatusBar resources;

    private System.Action<AbilityButtonUI> onSelected; // menu uses this to enforce single selection
    private System.Action<AbilityDefinitionSO> onClickedConfirm; // optional "confirm" action if you want

    private bool isSelected;

    private bool cachedOriginals;
    private Color originalNameColor;
    private Color originalCostColor;
    private Color originalBgColor;

    public AbilityDefinitionSO Ability => ability;

    public void Bind(
        AbilityDefinitionSO ability,
        TopStatusBar resources,
        System.Action<AbilityButtonUI> onSelectedCallback,
        System.Action<AbilityDefinitionSO> onClickedConfirmCallback = null
    )
    {
        this.ability = ability;
        this.resources = resources;
        this.onSelected = onSelectedCallback;
        this.onClickedConfirm = onClickedConfirmCallback;

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

        // default state
        SetSelected(false);
        RefreshInteractable();
    }

    private void CacheOriginalsIfNeeded()
    {
        if (cachedOriginals) return;

        if (nameText != null) originalNameColor = nameText.color;
        if (costText != null) originalCostColor = costText.color;
        if (buttonBackground != null) originalBgColor = buttonBackground.color;

        // If you left affordable colors as white, inherit originals so we don't override your prefab styling.
        if (affordableTextColor == Color.white && nameText != null) affordableTextColor = originalNameColor;
        if (affordableBackgroundColor == Color.white && buttonBackground != null) affordableBackgroundColor = originalBgColor;

        cachedOriginals = true;
    }

    private void OnClicked()
    {
        // First select/highlight the button in the list
        onSelected?.Invoke(this);

        // Optional: if you later want click to "confirm/cast", you can use this.
        onClickedConfirm?.Invoke(ability);
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;

        // If unaffordable, we keep the gray-out look (unless you want selection to override that)
        bool affordable = IsAffordable();

        // Name text styling
        if (nameText != null)
        {
            if (boldNameWhenSelected)
                nameText.fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;

            // Color priority: unaffordable > selected > normal
            if (grayOutWhenUnaffordable && !affordable)
                nameText.color = unaffordableTextColor;
            else if (selected)
                nameText.color = selectedTextColor;
            else
                nameText.color = affordableTextColor;
        }

        // Cost text color (icons follow vertex color)
        if (costText != null)
        {
            if (grayOutWhenUnaffordable && !affordable)
                costText.color = unaffordableTextColor;
            else
                costText.color = affordableTextColor;
        }

        // Background
        if (buttonBackground != null)
        {
            if (grayOutWhenUnaffordable && !affordable)
                buttonBackground.color = unaffordableBackgroundColor;
            else if (selected && highlightWhenSelected)
                buttonBackground.color = selectedBackgroundColor;
            else
                buttonBackground.color = affordableBackgroundColor;
        }

        if (debugLogs)
            Debug.Log($"[AbilityButtonUI] SetSelected({selected}) '{name}' affordable={affordable}", this);
    }

    public void RefreshInteractable()
    {
        bool affordable = IsAffordable();

        if (button != null)
            button.interactable = affordable;

        // Re-apply visuals (keeps selection + gray-out consistent)
        SetSelected(isSelected);
    }

    private bool IsAffordable()
    {
        if (ability == null || resources == null) return false;

        long cA = AbilityDefSOReader.GetCostAttack(ability);
        long cD = AbilityDefSOReader.GetCostDefense(ability);
        long cM = AbilityDefSOReader.GetCostMagic(ability);
        long cW = AbilityDefSOReader.GetCostWild(ability);

        return
            resources.GetAttack() >= cA &&
            resources.GetDefense() >= cD &&
            resources.GetMagic() >= cM &&
            resources.GetWild() >= cW;
    }

    private string BuildCostString(AbilityDefinitionSO ability)
    {
        if (ability == null) return string.Empty;

        long cA = AbilityDefSOReader.GetCostAttack(ability);
        long cD = AbilityDefSOReader.GetCostDefense(ability);
        long cM = AbilityDefSOReader.GetCostMagic(ability);
        long cW = AbilityDefSOReader.GetCostWild(ability);

        var parts = new List<string>(4);

        if (cA > 0) parts.Add($"{Sprite(attackSpriteIndex)} {cA}");
        if (cD > 0) parts.Add($"{Sprite(defenseSpriteIndex)} {cD}");
        if (cM > 0) parts.Add($"{Sprite(magicSpriteIndex)} {cM}");
        if (cW > 0) parts.Add($"{Sprite(wildSpriteIndex)} {cW}");

        return parts.Count > 0 ? string.Join(separator, parts) : string.Empty;
    }

    private static string Sprite(int index) => $"<sprite index={index}>";
}
