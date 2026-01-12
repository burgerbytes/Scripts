using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Post-battle chest selection UI.
/// - Shows X Small chests and Y Large chests, plus a Skip button.
/// - Chests are "covered" (unknown contents) until opened.
/// - Opening a chest spends the corresponding key and then grants a random item from the provided pool.
/// </summary>
public class PostBattleChestPanel : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject root;

    [Header("Small Chests")]
    [SerializeField] private Transform smallChestContainer;
    [SerializeField] private Button smallChestButtonPrefab;
    [SerializeField] private Sprite smallChestIcon;

    [Header("Large Chests")]
    [SerializeField] private Transform largeChestContainer;
    [SerializeField] private Button largeChestButtonPrefab;
    [SerializeField] private Sprite largeChestIcon;

    [Header("Footer")]
    [SerializeField] private Button skipButton;
    [SerializeField] private TMP_Text infoText;

    private readonly List<Button> _spawned = new List<Button>();
    private Action _onDone;

    private HeroStats _hero;
    private PlayerInventory _inventory;
    private IReadOnlyList<ItemOptionSO> _pool;

    private int _smallRemaining;
    private int _largeRemaining;

    public bool IsOpen => root != null && root.activeSelf;

    private void Awake()
    {
        if (root != null) root.SetActive(false);

        if (skipButton != null)
        {
            skipButton.onClick.RemoveAllListeners();
            skipButton.onClick.AddListener(Finish);
        }
    }

    public void Show(
        HeroStats hero,
        int smallChestCount,
        int largeChestCount,
        IReadOnlyList<ItemOptionSO> rewardPool,
        PlayerInventory inventory,
        PostBattleRewardPanel unusedChoicePanel,
        Action onDone)
    {
        _hero = hero;
        _inventory = inventory;
        _pool = rewardPool;
        _onDone = onDone;

        _smallRemaining = Mathf.Max(0, smallChestCount);
        _largeRemaining = Mathf.Max(0, largeChestCount);

        ClearSpawned();

        if (root != null) root.SetActive(true);

        SpawnChests();
        RefreshInfo();
    }

    public void Hide()
    {
        ClearSpawned();
        _onDone = null;
        _hero = null;
        _inventory = null;
        _pool = null;

        if (root != null) root.SetActive(false);
    }

    private void SpawnChests()
    {
        // Small
        for (int i = 0; i < _smallRemaining; i++)
        {
            var btn = Instantiate(smallChestButtonPrefab, smallChestContainer);
            _spawned.Add(btn);

            var img = btn.GetComponentInChildren<Image>();
            if (img != null && smallChestIcon != null)
                img.sprite = smallChestIcon;

            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => TryOpenSmall(btn));
        }

        // Large
        for (int i = 0; i < _largeRemaining; i++)
        {
            var btn = Instantiate(largeChestButtonPrefab, largeChestContainer);
            _spawned.Add(btn);

            var img = btn.GetComponentInChildren<Image>();
            if (img != null && largeChestIcon != null)
                img.sprite = largeChestIcon;

            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => TryOpenLarge(btn));
        }
    }

    private void TryOpenSmall(Button btn)
    {
        if (_hero == null) return;

        if (!_hero.TrySpendSmallKey(1))
        {
            RefreshInfo("Need a Small Key.");
            return;
        }

        _smallRemaining = Mathf.Max(0, _smallRemaining - 1);
        DisableButton(btn);

        GrantRandomItemReward();
        RefreshInfo();
        CheckAutoFinish();
    }

    private void TryOpenLarge(Button btn)
    {
        if (_hero == null) return;

        if (!_hero.TrySpendLargeKey(1))
        {
            RefreshInfo("Need a Large Key.");
            return;
        }

        _largeRemaining = Mathf.Max(0, _largeRemaining - 1);
        DisableButton(btn);

        GrantRandomItemReward();
        RefreshInfo();
        CheckAutoFinish();
    }

    private void GrantRandomItemReward()
    {
        if (_pool == null || _pool.Count == 0) return;
        if (_inventory == null) return;

        ItemOptionSO chosen = _pool[UnityEngine.Random.Range(0, _pool.Count)];
        if (chosen != null && chosen.item != null)
            _inventory.Add(chosen.item, chosen.quantity);
    }

    private void CheckAutoFinish()
    {
        if (_hero == null)
            return;

        bool anySmallChestsLeft = _smallRemaining > 0;
        bool anyLargeChestsLeft = _largeRemaining > 0;

        if (!anySmallChestsLeft && !anyLargeChestsLeft)
        {
            Finish();
            return;
        }

        bool canOpenSmall = anySmallChestsLeft && _hero.SmallKeys > 0;
        bool canOpenLarge = anyLargeChestsLeft && _hero.LargeKeys > 0;

        if (!canOpenSmall && !canOpenLarge)
            Finish();
    }

    private void Finish()
    {
        _onDone?.Invoke();
    }

    private void DisableButton(Button btn)
    {
        if (btn == null) return;
        btn.interactable = false;

        var cg = btn.GetComponent<CanvasGroup>();
        if (cg != null)
            cg.alpha = 0.35f;
    }

    private void RefreshInfo(string extra = null)
    {
        if (infoText == null) return;

        int sk = _hero != null ? _hero.SmallKeys : 0;
        int lk = _hero != null ? _hero.LargeKeys : 0;
        long g = _hero != null ? _hero.Gold : 0;

        string baseLine = $"Gold: {g}    Small Keys: {sk}    Large Keys: {lk}";
        if (!string.IsNullOrEmpty(extra))
            baseLine += "\n" + extra;

        infoText.text = baseLine;
    }

    private void ClearSpawned()
    {
        for (int i = 0; i < _spawned.Count; i++)
        {
            if (_spawned[i] != null)
                Destroy(_spawned[i].gameObject);
        }
        _spawned.Clear();
    }
}
