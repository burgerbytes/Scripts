// PATH: Assets/Scripts/UI/PostBattle/PostBattleRewardPanel.cs
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PostBattleRewardPanel : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject root; // panel root
    [SerializeField] private Transform optionsContainer;
    [SerializeField] private RewardOptionCard optionCardPrefab;

    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text subtitleText;

    [Header("Layout Fix")]
    [Tooltip("If true, this script will ensure OptionsContainer has a VerticalLayoutGroup + ContentSizeFitter at runtime.")]
    [SerializeField] private bool enforceRuntimeLayout = true;

    [Tooltip("Spacing between option cards.")]
    [SerializeField] private float optionSpacing = 10f;

    [Tooltip("Padding (left/right/top/bottom) inside the options container.")]
    [SerializeField] private int optionPadding = 12;

    [Tooltip("Minimum height for each option card (LayoutElement). Helps prevent overlap when prefabs are missing layout components.")]
    [SerializeField] private float optionCardMinHeight = 160f;

    private readonly List<RewardOptionCard> _spawned = new List<RewardOptionCard>();
    private Action<ItemOptionSO> _onChosen;
    private bool _choosing;

    public bool IsOpen => root != null && root.activeSelf;

    private void Awake()
    {
        if (root != null)
            root.SetActive(true);

        if (enforceRuntimeLayout)
            EnsureLayout();
    }

    public void Show(IReadOnlyList<ItemOptionSO> options, Action<ItemOptionSO> onChosen)
    {
        if (root == null || optionsContainer == null || optionCardPrefab == null)
        {
            Debug.LogWarning("[PostBattleRewardPanel] Missing refs (root/optionsContainer/optionCardPrefab).", this);
            onChosen?.Invoke(null);
            return;
        }

        _onChosen = onChosen;
        _choosing = true;

        if (titleText != null)
            titleText.text = "Rewards";

        if (subtitleText != null)
            subtitleText.text = options != null ? "Choose one" : "";

        root.SetActive(true);

        ClearOptions();

        if (enforceRuntimeLayout)
            EnsureLayout();

        if (options == null || options.Count == 0)
        {
            _choosing = false;
            _onChosen?.Invoke(null);
            return;
        }

        for (int i = 0; i < options.Count; i++)
        {
            ItemOptionSO opt = options[i];
            if (opt == null) continue;

            RewardOptionCard card = Instantiate(optionCardPrefab, optionsContainer);
            _spawned.Add(card);

            EnsureCardLayout(card);
            card.BindItem(opt, HandleChosen);
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(optionsContainer as RectTransform);
    }

    public void Hide()
    {
        ClearOptions();
        _onChosen = null;
        _choosing = false;

        if (root != null)
            root.SetActive(false);
    }

    private void HandleChosen(ItemOptionSO chosen)
    {
        if (!_choosing) return;
        _choosing = false;

        // Prevent double-click.
        for (int i = 0; i < _spawned.Count; i++)
        {
            if (_spawned[i] == null) continue;
            var btn = _spawned[i].GetComponentInChildren<Button>();
            if (btn != null) btn.interactable = false;
        }

        _onChosen?.Invoke(chosen);
    }

    private void ClearOptions()
    {
        for (int i = 0; i < _spawned.Count; i++)
        {
            if (_spawned[i] != null)
                Destroy(_spawned[i].gameObject);
        }

        _spawned.Clear();
    }

    private void EnsureLayout()
    {
        if (optionsContainer == null) return;

        var rt = optionsContainer as RectTransform;
        if (rt == null) return;

        var vlg = optionsContainer.GetComponent<VerticalLayoutGroup>();
        if (vlg == null) vlg = optionsContainer.gameObject.AddComponent<VerticalLayoutGroup>();

        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.spacing = optionSpacing;
        vlg.padding = new RectOffset(optionPadding, optionPadding, optionPadding, optionPadding);

        var csf = optionsContainer.GetComponent<ContentSizeFitter>();
        if (csf == null) csf = optionsContainer.gameObject.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Root auto-size to contents (panel is centered and sized to menu content)
        if (root != null)
        {
            var rootRT = root.GetComponent<RectTransform>();
            if (rootRT != null)
            {
                var rootFitter = root.GetComponent<ContentSizeFitter>();
                if (rootFitter == null) rootFitter = root.AddComponent<ContentSizeFitter>();
                rootFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                rootFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
        }
    }

    private void EnsureCardLayout(RewardOptionCard card)
    {
        if (card == null) return;

        var le = card.GetComponent<LayoutElement>();
        if (le == null) le = card.gameObject.AddComponent<LayoutElement>();

        if (optionCardMinHeight > 0f)
            le.minHeight = optionCardMinHeight;
    }
}
