// PATH: Assets/Scripts/UI/PostBattle/PostBattleRewardPanel.cs
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PostBattleRewardPanel : MonoBehaviour
{
    public PlayerInventory playerInventory;
    public ItemSO[] itemsToPickup;

    public void PickupItem(int id)
    {
        playerInventory.Add(itemsToPickup[id]);
    }

    [Header("UI")]
    [SerializeField] private GameObject root; // panel root
    [SerializeField] private Transform optionsContainer;
    [SerializeField] private RewardOptionCard optionCardPrefab;

    private readonly List<RewardOptionCard> _spawned = new List<RewardOptionCard>();
    private Action<ItemOptionSO> _onChosen;
    private bool _choosing;

    public bool IsOpen => root != null && root.activeSelf;

    private void Awake()
    {

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

        root.SetActive(true);

        ClearOptions();

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

            card.BindItem(opt, HandleChosen);
        }
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
}
