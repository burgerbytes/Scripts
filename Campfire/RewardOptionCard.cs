// PATH: Assets/Scripts/Campfire/RewardOptionCard.cs
using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RewardOptionCard : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text descText;
    [SerializeField] private TMP_Text prosText;
    [SerializeField] private TMP_Text consText;
    [SerializeField] private Image iconImage;
    [SerializeField] private Button chooseButton;

    // Campfire option binding (existing)
    private CampfireOptionSO _campfireOption;
    private Action<CampfireOptionSO> _onChooseCampfire;

    // Post-battle item option binding (new)
    private ItemOptionSO _itemOption;
    private Action<ItemOptionSO> _onChooseItem;

    public void Bind(CampfireOptionSO option, Action<CampfireOptionSO> onChoose)
    {
        _campfireOption = option;
        _onChooseCampfire = onChoose;

        _itemOption = null;
        _onChooseItem = null;

        if (_campfireOption == null)
            return;

        if (nameText != null)
            nameText.text = _campfireOption.optionName;

        if (descText != null)
            descText.text = _campfireOption.description;

        // No "Pros"/"Cons" headers; campfire can still show (none) if empty.
        if (prosText != null)
        {
            prosText.gameObject.SetActive(true);
            prosText.text = FormatList(_campfireOption.pros, showNonePlaceholder: true);
        }

        if (consText != null)
        {
            consText.gameObject.SetActive(true);
            consText.text = FormatList(_campfireOption.cons, showNonePlaceholder: true);
        }

        if (iconImage != null)
        {
            iconImage.sprite = _campfireOption.icon;
            iconImage.enabled = _campfireOption.icon != null;
        }

        WireButton(OnChooseClicked);
    }

    public void BindItem(ItemOptionSO option, Action<ItemOptionSO> onChoose)
    {
        _itemOption = option;
        _onChooseItem = onChoose;

        _campfireOption = null;
        _onChooseCampfire = null;

        if (_itemOption == null)
            return;

        if (nameText != null)
            nameText.text = _itemOption.optionName;

        if (descText != null)
            descText.text = _itemOption.description;

        // Item reward behavior:
        // - Hide pros/cons text objects entirely if there is no real content.
        // - Do NOT show "(none)" placeholder.
        if (prosText != null)
        {
            bool hasPros = HasContent(_itemOption.pros);
            prosText.gameObject.SetActive(hasPros);
            if (hasPros)
                prosText.text = FormatList(_itemOption.pros, showNonePlaceholder: false);
        }

        if (consText != null)
        {
            bool hasCons = HasContent(_itemOption.cons);
            consText.gameObject.SetActive(hasCons);
            if (hasCons)
                consText.text = FormatList(_itemOption.cons, showNonePlaceholder: false);
        }

        if (iconImage != null)
        {
            iconImage.sprite = _itemOption.icon != null
                ? _itemOption.icon
                : (_itemOption.item != null ? _itemOption.item.icon : null);

            iconImage.enabled = iconImage.sprite != null;
        }

        WireButton(OnChooseClicked);
    }

    private void WireButton(Action clickHandler)
    {
        if (chooseButton == null) return;

        chooseButton.onClick.RemoveAllListeners();
        chooseButton.onClick.AddListener(() => clickHandler?.Invoke());
    }

    private void OnChooseClicked()
    {
        if (_campfireOption != null)
        {
            _onChooseCampfire?.Invoke(_campfireOption);
            return;
        }

        if (_itemOption != null)
        {
            _onChooseItem?.Invoke(_itemOption);
            return;
        }
    }

    private static bool HasContent(string[] lines)
    {
        if (lines == null || lines.Length == 0) return false;

        for (int i = 0; i < lines.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
                return true;
        }

        return false;
    }

    private string FormatList(string[] lines, bool showNonePlaceholder)
    {
        if (lines == null || lines.Length == 0)
            return showNonePlaceholder ? "<alpha=#88>(none)</alpha>" : string.Empty;

        StringBuilder sb = new StringBuilder();

        for (int i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;

            sb.Append("• ");
            sb.Append(lines[i]);
            sb.Append('\n');
        }

        string result = sb.ToString().TrimEnd();
        if (string.IsNullOrEmpty(result))
            return showNonePlaceholder ? "<alpha=#88>(none)</alpha>" : string.Empty;

        return result;
    }
}
