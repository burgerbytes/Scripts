using TMPro;
using UnityEngine;

public class StatsPanelUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private HeroStats heroStats;

    [Header("Text Fields")]
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private TMP_Text xpText;
    [SerializeField] private TMP_Text hpText;
    [SerializeField] private TMP_Text attackText;
    [SerializeField] private TMP_Text defenseText;
    [SerializeField] private TMP_Text speedText;

    private void Awake()
    {
        // Default to hidden at start (you can change this)
        gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (heroStats != null)
            heroStats.OnChanged += Refresh;

        Refresh();
    }

    private void OnDisable()
    {
        if (heroStats != null)
            heroStats.OnChanged -= Refresh;
    }

    public void SetHero(HeroStats stats)
    {
        if (heroStats != null)
            heroStats.OnChanged -= Refresh;

        heroStats = stats;

        if (heroStats != null)
            heroStats.OnChanged += Refresh;

        Refresh();
    }

    public void Show() => gameObject.SetActive(true);
    public void Hide() => gameObject.SetActive(false);

    public void Toggle()
    {
        gameObject.SetActive(!gameObject.activeSelf);
    }

    public void Refresh()
    {
        if (heroStats == null) return;

        if (levelText != null) levelText.text = $"Level: {heroStats.Level}";
        if (xpText != null) xpText.text = $"XP: {heroStats.XP} / {heroStats.XPToNextLevel}";
        if (hpText != null) hpText.text = $"HP: {heroStats.CurrentHp} / {heroStats.MaxHp}";
        if (attackText != null) attackText.text = $"Attack: {heroStats.Attack}";
        if (defenseText != null) defenseText.text = $"Defense: {heroStats.Defense}";
        if (speedText != null) speedText.text = $"Speed: {heroStats.Speed}";
    }
}
