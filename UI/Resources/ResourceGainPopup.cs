using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Popup used to show resource gain (e.g., +1) near a resource counter.
/// All styling/timing should be driven by the spawner (ResourceBarPopupSpawner) so tuning is centralized.
/// </summary>
public class ResourceGainPopup : MonoBehaviour
{
    [System.Serializable]
    public struct Style
    {
        // Text
        public float fontSize;
        public bool forceBold;

        // Icon
        public float iconSize; // <= 0 => keep prefab size

        // Outline
        public bool overrideOutline;
        public float outlineWidth;
        public Color outlineColor;

        // Motion / timing
        public float floatDistance;
        public float popDuration;
        public float holdDuration;
        public float fadeDuration;

        // Pop
        public bool popOnSpawn;
        public float popScale;
    }

    [Header("Wiring (optional)")]
    [Tooltip("If left null, we'll try GetComponentInChildren<TextMeshProUGUI>().")]
    [SerializeField] private TextMeshProUGUI text;

    [Tooltip("Optional icon image shown beside the +amount.")]
    [SerializeField] private Image iconImage;

    private Vector2 _startAnchoredPos;
    private Vector3 _baseScale;
    private float _timer;
    private CanvasGroup _cg;

    private RectTransform _rt;
    private Style _style;

    /// <summary>
    /// Initialize and start the popup animation.
    /// </summary>
    public void Initialize(long amount, Color color, Sprite icon, Style style)
    {
        EnsureRefs();

        _style = style;

        _startAnchoredPos = _rt.anchoredPosition;
        _baseScale = transform.localScale;
        _timer = 0f;

        if (text != null)
        {
            text.text = "+" + amount.ToString();
            text.color = color;

            if (_style.fontSize > 0f)
                text.fontSize = _style.fontSize;

            if (_style.forceBold)
                text.fontStyle |= FontStyles.Bold;

            if (_style.overrideOutline)
                ApplyOutline(text, _style.outlineWidth, _style.outlineColor);
        }

        if (iconImage != null)
        {
            if (icon != null)
            {
                iconImage.enabled = true;
                iconImage.sprite = icon;

                if (_style.iconSize > 0f)
                {
                    var irt = iconImage.rectTransform;
                    irt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, _style.iconSize);
                    irt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, _style.iconSize);
                }
            }
            else
            {
                iconImage.enabled = false;
            }
        }

        if (_style.popOnSpawn)
            transform.localScale = Vector3.zero;

        if (_cg != null) _cg.alpha = 1f;
    }

    /// <summary>
    /// Called by ResourceBarPopupSpawner immediately after instantiation.
    /// </summary>
    public void SetAnchoredPosition(Vector2 anchoredPos)
    {
        EnsureRefs();
        _rt.anchoredPosition = anchoredPos;
        _startAnchoredPos = anchoredPos;
    }

    private void Awake()
    {
        EnsureRefs();
        _startAnchoredPos = _rt != null ? _rt.anchoredPosition : Vector2.zero;
        _baseScale = transform.localScale;
        if (_cg != null) _cg.alpha = 1f;
    }

    private void EnsureRefs()
    {
        if (_rt == null) _rt = GetComponent<RectTransform>();

        if (text == null) text = GetComponentInChildren<TextMeshProUGUI>(true);

        // If you have multiple Images, prefer assigning iconImage in the prefab.
        if (iconImage == null) iconImage = GetComponentInChildren<Image>(true);

        _cg = GetComponent<CanvasGroup>();
        if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();
    }

    private void Update()
    {
        // If Initialize wasn't called yet, do nothing.
        if (_rt == null) return;

        _timer += Time.deltaTime;

        float popDur = Mathf.Max(0.01f, _style.popDuration <= 0f ? 0.20f : _style.popDuration);
        float holdDur = Mathf.Max(0f, _style.holdDuration);
        float fadeDur = Mathf.Max(0.01f, _style.fadeDuration <= 0f ? 0.25f : _style.fadeDuration);
        float dist = _style.floatDistance;

        // 1) POP PHASE
        if (_style.popOnSpawn && _timer < popDur)
        {
            float t = Mathf.Clamp01(_timer / popDur);
            float eased = EaseOutBack(t);

            float targetScale = Mathf.Max(1f, _style.popScale <= 0f ? 1.5f : _style.popScale);
            float s = Mathf.LerpUnclamped(1f, targetScale, eased);
            transform.localScale = _baseScale * s;

            _rt.anchoredPosition = _startAnchoredPos + Vector2.up * dist * (t * 0.33f);
            return;
        }

        // Lock final scale after pop
        transform.localScale = _baseScale;

        float afterPop = _style.popOnSpawn ? Mathf.Max(0f, _timer - popDur) : _timer;

        // 2) HOLD PHASE
        if (afterPop < holdDur)
        {
            float t = (holdDur <= 0.0001f) ? 1f : Mathf.Clamp01(afterPop / holdDur);
            _rt.anchoredPosition = _startAnchoredPos + Vector2.up * dist * (0.33f + 0.47f * t);
            if (_cg != null) _cg.alpha = 1f;
            return;
        }

        // 3) FADE PHASE
        float fadeT = Mathf.Clamp01((afterPop - holdDur) / fadeDur);
        if (_cg != null) _cg.alpha = Mathf.Lerp(1f, 0f, fadeT);

        _rt.anchoredPosition = _startAnchoredPos + Vector2.up * dist * Mathf.Lerp(0.80f, 1.00f, fadeT);

        if (fadeT >= 1f)
            Destroy(gameObject);
    }

    private void ApplyOutline(TextMeshProUGUI tmp, float width, Color color)
    {
        if (tmp == null) return;

        // Clone the material so we don't modify shared font material.
        tmp.fontMaterial = new Material(tmp.fontMaterial);
        tmp.fontMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, width);
        tmp.fontMaterial.SetColor(ShaderUtilities.ID_OutlineColor, color);
    }

    // Snappy pop easing.
    private float EaseOutBack(float t)
    {
        float c1 = 1.70158f;
        float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }
}
