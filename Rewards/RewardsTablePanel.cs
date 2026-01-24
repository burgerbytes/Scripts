using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Post-battle panel that lets the player choose ONE reward type:
/// - Reelforging (go to PostBattleReelUpgradeMinigamePanel) - now requires a hero selection + confirm.
/// - Treasure Reels (go to PostBattleChestPanel / reward reels)
/// - Skip
///
/// Wiring:
/// - Attach this script to the root GameObject of the RewardsTablePanel.
/// - Assign the buttons (Reelforging, Treasure Reels, Skip, Confirm) and the hero dropdown.
/// - The dropdown + confirm are disabled by default and are enabled after pressing Reelforging.
/// </summary>
public class RewardsTablePanel : MonoBehaviour
{
    public enum RewardsTableChoice
    {
        Skip = 0,
        Reelforging = 1,
        TreasureReels = 2,
    }

    [Header("UI")]
    [SerializeField] private Button upgradeReelButton;
    [SerializeField] private Button chestMinigameButton;
    [SerializeField] private Button skipButton;

    [Header("Reelforging Select + Confirm")]
    [SerializeField] private TMP_Dropdown heroDropdown;
    [SerializeField] private Button confirmButton;

    [SerializeField] private TMP_Text headerText;

    private Action<RewardsTableChoice, int> _onChosen;
    private readonly List<HeroStats> _partyHeroes = new List<HeroStats>();
    private readonly List<int> _partyHeroIndices = new List<int>();
    private bool _awaitingReelforgeConfirm = false;

    private void Awake()
    {
        // Avoid duplicate listeners if panel is reused / disabled-enabled.
        if (upgradeReelButton != null) upgradeReelButton.onClick.RemoveAllListeners();
        if (chestMinigameButton != null) chestMinigameButton.onClick.RemoveAllListeners();
        if (skipButton != null) skipButton.onClick.RemoveAllListeners();
        if (confirmButton != null) confirmButton.onClick.RemoveAllListeners();

        if (upgradeReelButton != null) upgradeReelButton.onClick.AddListener(OnPressedReelforging);
        if (chestMinigameButton != null) chestMinigameButton.onClick.AddListener(() => ChooseImmediate(RewardsTableChoice.TreasureReels));
        if (skipButton != null) skipButton.onClick.AddListener(() => ChooseImmediate(RewardsTableChoice.Skip));
        if (confirmButton != null) confirmButton.onClick.AddListener(OnPressedConfirm);

        SetReelforgeControlsEnabled(false);
    }

    /// <summary>
    /// Show the table and populate the hero dropdown with the current party.
    /// </summary>
    public void Show(HeroStats[] party, Action<RewardsTableChoice, int> onChosen, string titleOverride = null)
    {
        _onChosen = onChosen;

        if (headerText != null && !string.IsNullOrEmpty(titleOverride))
            headerText.text = titleOverride;

        PopulatePartyDropdown(party);
        _awaitingReelforgeConfirm = false;
        SetReelforgeControlsEnabled(false);

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);
    }

    public void Hide()
    {
        _onChosen = null;
        _partyHeroes.Clear();
        _partyHeroIndices.Clear();
        _awaitingReelforgeConfirm = false;

        if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }

    private void PopulatePartyDropdown(HeroStats[] party)
    {
        _partyHeroes.Clear();
        _partyHeroIndices.Clear();

        if (heroDropdown == null)
            return;

        heroDropdown.ClearOptions();

        var options = new List<TMP_Dropdown.OptionData>();

        if (party != null)
        {
            for (int i = 0; i < party.Length; i++)
            {
                var h = party[i];
                if (h == null) continue;

                _partyHeroes.Add(h);
                _partyHeroIndices.Add(i);
                options.Add(new TMP_Dropdown.OptionData(GetPrettyHeroName(h)));
            }
        }

        // If no valid heroes, still add a placeholder so TMP_Dropdown doesn't throw / render weird.
        if (options.Count == 0)
        {
            options.Add(new TMP_Dropdown.OptionData("No heroes"));
            _partyHeroIndices.Add(-1);
        }

        heroDropdown.AddOptions(options);
        heroDropdown.value = 0;
        heroDropdown.RefreshShownValue();
    }

    private void OnPressedReelforging()
    {
        // Don't instantly choose anymore. Enable dropdown + confirm.
        _awaitingReelforgeConfirm = true;
        SetReelforgeControlsEnabled(true);
    }

    private void OnPressedConfirm()
    {
        if (!_awaitingReelforgeConfirm)
            return;

        int heroIndex = GetSelectedHeroIndexSafe();
        if (heroIndex < 0)
        {
            // No valid hero selected; treat as skip rather than soft-lock.
            ChooseImmediate(RewardsTableChoice.Skip);
            return;
        }

        Choose(RewardsTableChoice.Reelforging, heroIndex);
    }

    private int GetSelectedHeroIndexSafe()
    {
        if (heroDropdown == null) return -1;
        if (_partyHeroIndices.Count == 0) return -1;

        int idx = heroDropdown.value;
        if (idx < 0 || idx >= _partyHeroIndices.Count) idx = 0;

        return _partyHeroIndices[idx];
    }

    private void SetReelforgeControlsEnabled(bool enabled)
    {
        if (heroDropdown != null) heroDropdown.interactable = enabled;
        if (confirmButton != null) confirmButton.interactable = enabled;

        // Optional: visually gray out by disabling the components.
        if (enabled)
        {
            if (heroDropdown != null) heroDropdown.gameObject.SetActive(true);
            if (confirmButton != null) confirmButton.gameObject.SetActive(true);
        }
        else
        {
            // Keep them visible but disabled by default. If you want them hidden instead, flip these to SetActive(false).
            if (heroDropdown != null) heroDropdown.gameObject.SetActive(true);
            if (confirmButton != null) confirmButton.gameObject.SetActive(true);
        }
    }

    private void ChooseImmediate(RewardsTableChoice choice)
    {
        Choose(choice, -1);
    }

    private void Choose(RewardsTableChoice choice, int selectedHeroIndex)
    {
        // Prevent double-click issues.
        var cb = _onChosen;
        _onChosen = null;

        cb?.Invoke(choice, selectedHeroIndex);
    }

    private static string GetPrettyHeroName(HeroStats hero)
    {
        if (hero == null) return "Hero";

        // Prefer GameObject name (common in your project: "Fighter(Clone)").
        string n = hero.gameObject != null ? hero.gameObject.name : "Hero";
        n = n.Replace("(Clone)", "").Trim();
        if (string.IsNullOrEmpty(n)) n = "Hero";
        return n;
    }
}
