using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Simple UI panel shown after a battle victory (before reward reels/chests)
/// that displays gold gained and XP gained (including bonus XP sources).
///
/// Wiring:
/// - Attach to the root GameObject of your Results Panel.
/// - Assign goldText, resultsText, and continueButton.
/// - Disable the panel by default.
/// </summary>
public class PostBattleResultsPanel : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text goldText;
    [SerializeField] private TMP_Text resultsText;
    [SerializeField] private Button continueButton;

    [Header("XP Bars")]
    [Tooltip("Parent object containing one child per party member (ex: XpBarsGroup/Ally, Ally (1), Ally (2)).")]
    [SerializeField] private Transform xpBarsGroup;
    [Tooltip("If true, bars animate one-by-one. If false, they animate in parallel.")]
    [SerializeField] private bool animateSequentially = true;
    [SerializeField] private float minSegmentSeconds = 0.15f;
    [SerializeField] private float maxSegmentSeconds = 0.55f;
    [SerializeField] private float levelUpFlashSeconds = 0.12f;
    [SerializeField] private float betweenHeroesDelaySeconds = 0.08f;

    [Header("XP Bars - Level UI")]
    [Tooltip("Format used for the per-hero level label. Example: 'Lv {0}'.")]
    [SerializeField] private string levelFormat = "Lv {0}";
    [Tooltip("Text shown when a hero levels up during the XP animation.")]
    [SerializeField] private string levelUpPopupText = "LEVEL UP!";
    [Tooltip("How long the LEVEL UP! popup lasts (rise + fade).")]
    [SerializeField] private float levelUpPopupSeconds = 0.75f;
    [Tooltip("How far the LEVEL UP! popup rises (UI units).")]
    [SerializeField] private float levelUpPopupRise = 40f;
    [Tooltip("Offset from the portrait for where the LEVEL UP! popup spawns.")]
    [SerializeField] private Vector2 levelUpPopupOffset = new Vector2(0f, 30f);
    [Tooltip("Pulse duration for the level label when leveling up.")]
    [SerializeField] private float levelLabelPulseSeconds = 0.18f;
    [Tooltip("Temporary font-size multiplier for the pulse.")]
    [SerializeField] private float levelLabelPulseScale = 1.35f;

    [Serializable]
    private class XpRow
    {
        public Transform root;
        public Image portrait;
        public Slider slider;
        public TMP_Text levelText;
    }

    private readonly List<XpRow> _rows = new();
    private Coroutine _xpRoutine;

    private Action _onContinue;

    private void Awake()
    {
        if (continueButton != null)
        {
            continueButton.onClick.RemoveListener(HandleContinueClicked);
            continueButton.onClick.AddListener(HandleContinueClicked);
        }

        CacheXpRows();

        // By default, this panel is hidden.
        gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        if (_xpRoutine != null)
        {
            StopCoroutine(_xpRoutine);
            _xpRoutine = null;
        }
    }

    private void CacheXpRows()
    {
        _rows.Clear();

        if (xpBarsGroup == null)
        {
            var t = transform.Find("XpBarsGroup");
            if (t != null) xpBarsGroup = t;
        }

        if (xpBarsGroup == null) return;

        for (int i = 0; i < xpBarsGroup.childCount; i++)
        {
            var allyRoot = xpBarsGroup.GetChild(i);
            if (allyRoot == null) continue;

            var portraitT = allyRoot.Find("Portrait");
            var sliderT = allyRoot.Find("XpBar/Slider");
            var levelT = allyRoot.Find("LevelText");

            var portraitImg = portraitT != null ? portraitT.GetComponent<Image>() : allyRoot.GetComponentInChildren<Image>(true);
            var slider = sliderT != null ? sliderT.GetComponent<Slider>() : allyRoot.GetComponentInChildren<Slider>(true);
            var levelText = levelT != null ? levelT.GetComponent<TMP_Text>() : null;

            if (portraitImg == null || slider == null) continue;

            slider.minValue = 0f;
            slider.maxValue = 1f;

            _rows.Add(new XpRow
            {
                root = allyRoot,
                portrait = portraitImg,
                slider = slider,
                levelText = levelText
            });
        }
    }

    public void Show(long goldGained, List<BattlePerformanceTracker.HeroSummary> heroSummaries, Action onContinue)
    {
        _onContinue = onContinue;

        if (continueButton != null)
            continueButton.interactable = false;

        if (goldText != null)
            goldText.text = $"Gold gained: +{goldGained}";

        if (resultsText != null)
        {
            var sb = new StringBuilder(256);

            if (heroSummaries == null || heroSummaries.Count == 0)
            {
                sb.AppendLine("XP gained: (no party data)");
            }
            else
            {
                sb.AppendLine("XP gained:");
                for (int i = 0; i < heroSummaries.Count; i++)
                {
                    var s = heroSummaries[i];
                    if (s == null) continue;

                    sb.Append("• ");
                    sb.Append(s.heroName);
                    sb.Append(": +");
                    sb.Append(s.totalXp);

                    if (s.bonusXp > 0)
                    {
                        sb.Append(" (");
                        sb.Append(s.baseXp);
                        sb.Append(" base + ");
                        sb.Append(s.bonusXp);
                        sb.Append(" bonus)");
                    }

                    if (s.bonusReasons != null && s.bonusReasons.Count > 0)
                    {
                        sb.Append("  [");
                        for (int r = 0; r < s.bonusReasons.Count; r++)
                        {
                            if (r > 0) sb.Append(", ");
                            sb.Append(s.bonusReasons[r]);
                        }
                        sb.Append("]");
                    }

                    sb.AppendLine();
                }
            }

            resultsText.text = sb.ToString();
        }

        gameObject.SetActive(true);

        // Ensure we have cached rows (in case the UI was constructed after Awake).
        if (_rows.Count == 0) CacheXpRows();
        StartXpAnimation(heroSummaries);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
        _onContinue = null;
    }

    private void StartXpAnimation(List<BattlePerformanceTracker.HeroSummary> heroSummaries)
    {
        if (_xpRoutine != null)
        {
            StopCoroutine(_xpRoutine);
            _xpRoutine = null;
        }

        if (_rows.Count == 0 || heroSummaries == null || heroSummaries.Count == 0)
        {
            if (continueButton != null) continueButton.interactable = true;
            return;
        }

        // Bind initial state
        int count = Mathf.Min(_rows.Count, heroSummaries.Count);
        for (int i = 0; i < count; i++)
        {
            var row = _rows[i];
            var s = heroSummaries[i];
            if (row == null || s == null) continue;

            if (row.root != null) row.root.gameObject.SetActive(true);
            if (row.portrait != null) row.portrait.sprite = s.portrait;

            float startFrac = SafeFrac(s.startXp, s.startXpToNextLevel);
            if (row.slider != null) row.slider.value = startFrac;

            if (row.levelText != null)
                row.levelText.text = string.Format(levelFormat, Mathf.Max(1, s.startLevel));
        }

        // Hide unused rows
        for (int i = count; i < _rows.Count; i++)
        {
            if (_rows[i]?.root != null)
                _rows[i].root.gameObject.SetActive(false);
        }

        _xpRoutine = StartCoroutine(animateSequentially
            ? AnimateXpSequential(heroSummaries, count)
            : AnimateXpParallel(heroSummaries, count));
    }

    private static float SafeFrac(int num, int den)
    {
        if (den <= 0) return 0f;
        return Mathf.Clamp01(num / (float)den);
    }

    private IEnumerator AnimateXpSequential(List<BattlePerformanceTracker.HeroSummary> summaries, int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return AnimateSingleRow(_rows[i], summaries[i]);
            if (betweenHeroesDelaySeconds > 0f)
                yield return new WaitForSeconds(betweenHeroesDelaySeconds);
        }

        if (continueButton != null) continueButton.interactable = true;
        _xpRoutine = null;
    }

    private IEnumerator AnimateXpParallel(List<BattlePerformanceTracker.HeroSummary> summaries, int count)
    {
        int finished = 0;
        for (int i = 0; i < count; i++)
            StartCoroutine(AnimateSingleRowWrapper(_rows[i], summaries[i], () => finished++));

        while (finished < count)
            yield return null;

        if (continueButton != null) continueButton.interactable = true;
        _xpRoutine = null;
    }

    private IEnumerator AnimateSingleRowWrapper(XpRow row, BattlePerformanceTracker.HeroSummary summary, Action onDone)
    {
        yield return AnimateSingleRow(row, summary);
        onDone?.Invoke();
    }

    private IEnumerator AnimateSingleRow(XpRow row, BattlePerformanceTracker.HeroSummary summary)
    {
        if (row == null || summary == null || row.slider == null)
            yield break;

        int cap = Mathf.Max(1, summary.startXpToNextLevel);
        int xp = Mathf.Clamp(summary.startXp, 0, cap);
        int remaining = Mathf.Max(0, summary.totalXp);
        int level = Mathf.Max(1, summary.startLevel);

        // Start state already set in StartXpAnimation, but ensure correctness.
        row.slider.value = SafeFrac(xp, cap);

        while (remaining > 0)
        {
            int space = cap - xp;
            int add = Mathf.Min(space, remaining);

            float from = SafeFrac(xp, cap);
            float to = SafeFrac(xp + add, cap);

            float segT = Mathf.Lerp(minSegmentSeconds, maxSegmentSeconds, cap > 0 ? (add / (float)cap) : 0f);
            yield return TweenSlider(row.slider, from, to, segT);

            xp += add;
            remaining -= add;

            // Level-up reached
            if (xp >= cap && remaining >= 0)
            {
                level += 1;
                TriggerLevelUpVisuals(row, level);
                yield return FlashRow(row);
                xp = 0;
                cap = NextXpToNext(cap);
                row.slider.value = 0f;
            }
        }
    }

    private static int NextXpToNext(int current)
    {
        // Mirrors HeroStats.LevelUp(): xpToNextLevel = Round(xpToNextLevel * 1.25) + 5
        return Mathf.RoundToInt(current * 1.25f) + 5;
    }

    private static IEnumerator TweenSlider(Slider slider, float from, float to, float seconds)
    {
        if (slider == null) yield break;
        if (seconds <= 0f)
        {
            slider.value = to;
            yield break;
        }

        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(t / seconds);
            slider.value = Mathf.Lerp(from, to, a);
            yield return null;
        }
        slider.value = to;
    }

    private IEnumerator FlashRow(XpRow row)
    {
        if (row == null || row.root == null) yield break;

        // Simple pulse flash: scale up then back down.
        var tr = row.root;
        Vector3 baseScale = tr.localScale;
        Vector3 up = baseScale * 1.06f;

        float half = Mathf.Max(0.01f, levelUpFlashSeconds);
        yield return TweenScale(tr, baseScale, up, half);
        yield return TweenScale(tr, up, baseScale, half);
    }

    private static IEnumerator TweenScale(Transform tr, Vector3 from, Vector3 to, float seconds)
    {
        if (tr == null) yield break;
        if (seconds <= 0f)
        {
            tr.localScale = to;
            yield break;
        }

        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(t / seconds);
            tr.localScale = Vector3.Lerp(from, to, a);
            yield return null;
        }
        tr.localScale = to;
    }

    private void TriggerLevelUpVisuals(XpRow row, int newLevel)
    {
        if (row == null) return;

        // Update/pulse the level label.
        if (row.levelText != null)
        {
            // Kill any existing pulse running on this label by resetting font size on completion.
            StartCoroutine(PulseLevelLabel(row.levelText, newLevel));
        }

        // Spawn a floating "LEVEL UP!" popup.
        SpawnLevelUpPopup(row);
    }

    private IEnumerator PulseLevelLabel(TMP_Text label, int newLevel)
    {
        if (label == null) yield break;

        string newText = string.Format(levelFormat, Mathf.Max(1, newLevel));

        float baseSize = label.fontSize;
        float bigSize = baseSize * Mathf.Max(1.0f, levelLabelPulseScale);
        Color baseColor = label.color;

        // Update text right at threshold.
        label.text = newText;

        float upT = Mathf.Max(0.01f, levelLabelPulseSeconds);
        float downT = Mathf.Max(0.01f, levelLabelPulseSeconds);

        // Up
        float t = 0f;
        while (t < upT)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(t / upT);
            label.fontSize = Mathf.Lerp(baseSize, bigSize, a);
            // Quick flash towards white
            label.color = Color.Lerp(baseColor, Color.white, a);
            yield return null;
        }

        // Down
        t = 0f;
        while (t < downT)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(t / downT);
            label.fontSize = Mathf.Lerp(bigSize, baseSize, a);
            label.color = Color.Lerp(Color.white, baseColor, a);
            yield return null;
        }

        label.fontSize = baseSize;
        label.color = baseColor;
    }

    private void SpawnLevelUpPopup(XpRow row)
    {
        if (row == null || row.root == null) return;

        // Pick a TMP template so we inherit the project's font/material.
        TMP_Text template = null;
        if (row.levelText != null) template = row.levelText;
        else if (resultsText != null) template = resultsText;
        else if (goldText != null) template = goldText;
        if (template == null) return;

        var popup = Instantiate(template, row.root);
        popup.name = "LevelUpPopup";
        popup.gameObject.SetActive(true);
        popup.text = levelUpPopupText;
        popup.enableWordWrapping = false;
        popup.alignment = TextAlignmentOptions.Center;
        popup.raycastTarget = false;

        // Position near the portrait if available.
        var popupRt = popup.GetComponent<RectTransform>();
        if (popupRt != null)
        {
            popupRt.localScale = Vector3.one;
            popupRt.SetAsLastSibling();

            Vector2 basePos = Vector2.zero;
            if (row.portrait != null)
            {
                var pr = row.portrait.rectTransform;
                if (pr != null)
                    basePos = pr.anchoredPosition;
            }

            popupRt.anchoredPosition = basePos + levelUpPopupOffset;
        }

        StartCoroutine(AnimateLevelUpPopup(popup));
    }

    private IEnumerator AnimateLevelUpPopup(TMP_Text popup)
    {
        if (popup == null) yield break;

        var rt = popup.GetComponent<RectTransform>();
        Vector2 start = rt != null ? rt.anchoredPosition : Vector2.zero;
        Vector2 end = start + new Vector2(0f, levelUpPopupRise);

        Color c0 = popup.color;
        c0.a = Mathf.Clamp01(c0.a);
        popup.color = c0;

        float dur = Mathf.Max(0.05f, levelUpPopupSeconds);
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(t / dur);

            if (rt != null)
                rt.anchoredPosition = Vector2.Lerp(start, end, a);

            var col = popup.color;
            col.a = Mathf.Lerp(c0.a, 0f, a);
            popup.color = col;
            yield return null;
        }

        if (popup != null)
            Destroy(popup.gameObject);
    }

    private void HandleContinueClicked()
    {
        // Allow player to skip animations.
        if (_xpRoutine != null)
        {
            StopCoroutine(_xpRoutine);
            _xpRoutine = null;
        }

        var cb = _onContinue;
        _onContinue = null;
        cb?.Invoke();
    }
}
