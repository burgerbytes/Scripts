using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Popup used to show resource gain (e.g., +1) near a resource counter.
/// Timing is split into: pop duration, hold duration, and fade duration.
/// </summary>
public class ResourceGainPopup : MonoBehaviour
{
    [Header("Wiring (optional)")]
    [Tooltip("If left null, we'll try GetComponentInChildren<TextMeshProUGUI>().")]
    [SerializeField] private TextMeshProUGUI text;

    [Tooltip("Optional icon image shown beside the +amount.")]
    [SerializeField] private Image iconImage;

    [Header("Text Style")]
    [SerializeField] private bool forceBold = true;
    [SerializeField] private float fontSize = 50f;

    [Header("Motion")]
    [SerializeField] private float floatDistance = 30f;

    [Header("Timing")]
    [Tooltip("How long the scale 'pop' animation takes (up + down).")]
    [SerializeField] private float popDuration = 0.20f;

    [Tooltip("How long the popup stays fully visible after the pop finishes.")]
    [SerializeField] private float holdDuration = 0.60f;

    [Tooltip("How long it takes to fade out after the hold.")]
    [SerializeField] private float fadeDuration = 0.25f;

    [Header("Pop")]
    [SerializeField] private bool popOnSpawn = true;
    [SerializeField] private float popScale = 1.15f;

    private Vector3 _startPos;
    private Vector3 _baseScale;

    private float _timer;
    private CanvasGroup _cg;

    /// <summary>
    /// Backwards-compatible init used by ResourceBarPopupSpawner.
    /// </summary>
    public void Initialize(long amount, Color color, Sprite icon = null)
    {
        EnsureRefs();

        _startPos = transform.localPosition;
        _baseScale = transform.localScale;
        _timer = 0f;

        if (text != null)
        {
            text.text = "+" + amount.ToString();
            text.color = color;
            text.fontSize = fontSize;

            if (forceBold)
                text.fontStyle |= FontStyles.Bold;
        }

        if (iconImage != null)
        {
            if (icon != null)
            {
                iconImage.enabled = true;
                iconImage.sprite = icon;
                // keep icon neutral unless you want tinting
                // iconImage.color = color;
            }
            else
            {
                iconImage.enabled = false;
            }
        }

        if (popOnSpawn)
            transform.localScale = Vector3.zero;

        if (_cg != null) _cg.alpha = 1f;
    }

    private void Awake()
    {
        EnsureRefs();
        _startPos = transform.localPosition;
        _baseScale = transform.localScale;

        if (_cg != null) _cg.alpha = 1f;
    }

    private void EnsureRefs()
    {
        if (text == null) text = GetComponentInChildren<TextMeshProUGUI>(true);
        if (iconImage == null) iconImage = GetComponentInChildren<Image>(true);

        // Use a CanvasGroup so we can fade both text + icon consistently.
        _cg = GetComponent<CanvasGroup>();
        if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();
    }

    private void Update()
    {
        _timer += Time.deltaTime;

        float popDur = Mathf.Max(0.01f, popDuration);
        float holdDur = Mathf.Max(0f, holdDuration);
        float fadeDur = Mathf.Max(0.01f, fadeDuration);

        // 1) POP PHASE (scale from 0 to baseScale, with a little overshoot)
        if (popOnSpawn && _timer < popDur)
        {
            float t = Mathf.Clamp01(_timer / popDur);
            float eased = EaseOutBack(t);

            // overshoot by popScale (e.g., 1.15) during the easing
            float s = Mathf.LerpUnclamped(1f, Mathf.Max(1f, popScale), eased);
            transform.localScale = _baseScale * s;

            // Float upward during pop phase too (feels nicer)
            transform.localPosition = _startPos + Vector3.up * floatDistance * (t * 0.33f);
            return;
        }

        // Lock final scale after pop
        transform.localScale = _baseScale;

        float afterPop = popOnSpawn ? Mathf.Max(0f, _timer - popDur) : _timer;

        // 2) HOLD PHASE (visible, floats upward gently)
        if (afterPop < holdDur)
        {
            float t = (holdDur <= 0.0001f) ? 1f : Mathf.Clamp01(afterPop / holdDur);
            transform.localPosition = _startPos + Vector3.up * floatDistance * (0.33f + 0.47f * t);
            if (_cg != null) _cg.alpha = 1f;
            return;
        }

        // 3) FADE PHASE (fade out while finishing the float)
        float fadeT = Mathf.Clamp01((afterPop - holdDur) / fadeDur);
        if (_cg != null) _cg.alpha = Mathf.Lerp(1f, 0f, fadeT);

        transform.localPosition = _startPos + Vector3.up * floatDistance * Mathf.Lerp(0.80f, 1.00f, fadeT);

        if (fadeT >= 1f)
            Destroy(gameObject);
    }

    // Snappy pop easing.
    private float EaseOutBack(float t)
    {
        float c1 = 1.70158f;
        float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }
}
