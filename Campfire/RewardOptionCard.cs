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

        if (prosText != null)
            prosText.text = FormatList("Pros", _campfireOption.pros);

        if (consText != null)
            consText.text = FormatList("Cons", _campfireOption.cons);

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

        if (prosText != null)
            prosText.text = FormatList("Pros", _itemOption.pros);

        if (consText != null)
            consText.text = FormatList("Cons", _itemOption.cons);

        if (iconImage != null)
        {
            iconImage.sprite = _itemOption.icon != null ? _itemOption.icon : (_itemOption.item != null ? _itemOption.item.icon : null);
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

    private string FormatList(string header, string[] lines)
    {
        if (lines == null || lines.Length == 0)
            return $"<b>{header}:</b>\n<alpha=#88>(none)</alpha>";

        StringBuilder sb = new StringBuilder();
        sb.Append($"<b>{header}:</b>\n");

        for (int i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;

            sb.Append("• ");
            sb.Append(lines[i]);
            sb.Append('\n');
        }

        return sb.ToString().TrimEnd();
    }
}
