using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small UI panel that displays the currently selected hero's key stats.
/// Intended to live in the "post-spin" space under the reels (behind shutters), but can be used anywhere.
///
/// Wire this in the inspector:
/// - nameText, levelText, hpText, staminaText, attackText, defenseText, shieldText
/// - (optional) portraitImage
///
/// Then PartyHUD will call SetHero(...) when the player clicks a party slot.
/// </summary>
public class HeroStatsPanelUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private TMP_Text hpText;
    [SerializeField] private TMP_Text staminaText;
    [SerializeField] private TMP_Text attackText;
    [SerializeField] private TMP_Text defenseText;
    [SerializeField] private TMP_Text shieldText;
    [SerializeField] private TMP_Text goldText;

    [Header("Optional")]
    [SerializeField] private Image portraitImage;

    [Header("Live Refresh")]
    [Tooltip("If true, the panel refreshes every frame so HP/shield/etc stay current.")]
    [SerializeField] private bool liveRefresh = true;

    [Header("Visibility")]
    [Tooltip("If true, calling SetHero(non-null) will automatically enable this GameObject.\nIf false, you must call Show() explicitly (recommended: only show after the player clicks a PickAlly slot).")]
    [SerializeField] private bool autoShowOnSetHero = false;

    [Tooltip("If true, calling SetHero(null) will hide this panel.")]
    [SerializeField] private bool hideWhenCleared = true;

    [Header("Reel Phase")]
    [SerializeField] private ReelSpinSystem reelSpinSystem;

    private HeroStats _hero;

    private void Update()
    {
        if (liveRefresh && _hero != null)
            Refresh();
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
        // Status/stats panel should be hidden during reel phase.
        if (inReelPhase)
            Hide();
    }

    public void SetHero(HeroStats hero)
    {
        _hero = hero;
        if (_hero != null && autoShowOnSetHero)
            Show();
        else if (_hero == null && hideWhenCleared)
            Hide();
        Refresh();
    }

    /// <summary>
    /// Convenience for "player selected a hero". Sets the hero and ensures the panel is visible.
    /// </summary>
    public void ShowForHero(HeroStats hero)
    {
        _hero = hero;
        Show();
        Refresh();
    }

    public void Show()
    {
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }

    private void Refresh()
    {
        if (_hero == null)
        {
            if (nameText != null) nameText.text = "";
            if (levelText != null) levelText.text = "";
            if (hpText != null) hpText.text = "";
            if (staminaText != null) staminaText.text = "";
            if (attackText != null) attackText.text = "";
            if (defenseText != null) defenseText.text = "";
            if (shieldText != null) shieldText.text = "";
            if (goldText != null) goldText.text = "";
            if (portraitImage != null) portraitImage.enabled = false;
            return;
        }

        if (nameText != null) nameText.text = _hero.name;
        if (levelText != null) levelText.text = $"Lv {_hero.Level}";

        if (hpText != null)
            hpText.text = $"HP {_hero.CurrentHp}/{_hero.MaxHp}";

        if (staminaText != null)
            staminaText.text = $"STA {_hero.CurrentStamina:0}/{_hero.MaxStamina}";

        if (attackText != null)
            attackText.text = $"ATK {_hero.Attack}";

        if (defenseText != null)
            defenseText.text = $"DEF {_hero.Defense}";

        if (shieldText != null)
            shieldText.text = $"SHD {_hero.Shield}";

        if (goldText != null)
            goldText.text = $"G {_hero.Gold}";

        if (portraitImage != null)
        {
            portraitImage.sprite = _hero.Portrait;
            portraitImage.enabled = (_hero.Portrait != null);
            portraitImage.preserveAspect = true;
        }
    }
}


////////////////////////////////////////////////////////////
