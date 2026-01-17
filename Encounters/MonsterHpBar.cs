using UnityEngine;
using UnityEngine.UI;

public class MonsterHpBar : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Monster monster;
    [SerializeField] private Image fillImage;

    [Header("Damage Preview (Yellow Segment)")]
    [Tooltip("Optional. If not assigned, this is created at runtime.")]
    [SerializeField] private RectTransform hpBarFullRect;
    [SerializeField] private RectTransform hpDamagePreviewRect;
    [SerializeField] private Image hpDamagePreviewImage;

    [Range(0f, 1f)]
    [SerializeField] private float previewAlpha = 0.85f;

    [Tooltip("Color used for the damage preview segment.")]
    [SerializeField] private Color previewColor = new Color(1f, 0.92f, 0.1f, 1f);

    private int _lastHp = -1;
    private int _lastMaxHp = -1;

    // Some prefabs use a Filled Image (fillAmount), others use a Simple/Sliced image with a mask.
    // Support both by falling back to resizing/scaling when fillAmount has no effect.
    private bool _useFillAmount = true;
    private bool _useScaleFallback = false;
    private float _fullWidth = 0f;
    private Vector3 _baseScale = Vector3.one;

    private void Reset()
    {
        monster = GetComponentInParent<Monster>();
        AutoFindFillImage();
    }

    private void Awake()
    {
        if (monster == null) monster = GetComponentInParent<Monster>();
        AutoFindFillImage();

        CacheFillMode();

        DisableLegacyGhostFill();
        EnsurePreviewObjects();

        ClearPreview();

        if (monster != null)
            HandleHpChanged(monster.CurrentHp, monster.MaxHp);
    }

    private void OnEnable()
    {
        if (monster != null)
            monster.OnHpChanged += HandleHpChanged;

        if (monster != null)
            HandleHpChanged(monster.CurrentHp, monster.MaxHp);

        ClearPreview();
    }

    private void OnDisable()
    {
        if (monster != null)
            monster.OnHpChanged -= HandleHpChanged;
    }

    private void AutoFindFillImage()
    {
        if (fillImage != null) return;

        var images = GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            var img = images[i];
            if (img == null) continue;
            if (img.name.Equals("Fill", System.StringComparison.OrdinalIgnoreCase))
            {
                fillImage = img;
                return;
            }
        }

        // fallback: first Filled Image
        for (int i = 0; i < images.Length; i++)
        {
            var img = images[i];
            if (img == null) continue;
            if (img.type == Image.Type.Filled)
            {
                fillImage = img;
                return;
            }
        }
    }

    private void DisableLegacyGhostFill()
    {
        var imgs = GetComponentsInChildren<Image>(true);
        for (int i = 0; i < imgs.Length; i++)
        {
            if (imgs[i] != null && imgs[i].name == "GhostFill")
                imgs[i].enabled = false;
        }
    }

    private void EnsurePreviewObjects()
    {
        if (fillImage == null) return;

        if (hpBarFullRect == null)
            hpBarFullRect = fillImage.rectTransform;

        if (hpDamagePreviewRect != null && hpDamagePreviewImage != null)
            return;

        // Create a preview segment as a sibling on top of the fill.
        GameObject segGO = new GameObject("HPDamagePreview", typeof(RectTransform), typeof(Image));
        segGO.transform.SetParent(fillImage.transform.parent, false);
        segGO.transform.SetSiblingIndex(fillImage.transform.GetSiblingIndex() + 1);

        hpDamagePreviewRect = segGO.GetComponent<RectTransform>();
        hpDamagePreviewImage = segGO.GetComponent<Image>();

        // Copy sprite/material so it matches your bar shape (but tinted).
        hpDamagePreviewImage.sprite = fillImage.sprite;
        hpDamagePreviewImage.type = Image.Type.Sliced; // segment is sized by width, not filled amount
        hpDamagePreviewImage.material = fillImage.material;
        hpDamagePreviewImage.raycastTarget = false;

        Color c = previewColor;
        c.a = previewAlpha;
        hpDamagePreviewImage.color = c;

        // Match vertical sizing/anchors to the fill rect
        RectTransform src = fillImage.rectTransform;
        hpDamagePreviewRect.anchorMin = new Vector2(0f, src.anchorMin.y);
        hpDamagePreviewRect.anchorMax = new Vector2(0f, src.anchorMax.y);
        hpDamagePreviewRect.pivot = new Vector2(0f, src.pivot.y);
        hpDamagePreviewRect.anchoredPosition = new Vector2(0f, src.anchoredPosition.y);
        hpDamagePreviewRect.sizeDelta = new Vector2(0f, src.sizeDelta.y);
        hpDamagePreviewRect.localRotation = src.localRotation;
        hpDamagePreviewRect.localScale = src.localScale;
    }

    private void CacheFillMode()
    {
        if (fillImage == null) return;

        // If it's a Filled image, fillAmount works.
        _useFillAmount = (fillImage.type == Image.Type.Filled);

        RectTransform rt = fillImage.rectTransform;
        if (rt == null)
            return;

        _baseScale = rt.localScale;

        if (_useFillAmount)
        {
            _useScaleFallback = false;
            _fullWidth = 0f;
            return;
        }

        // Non-filled images: either resize (if fixed anchors) or scale (if stretch anchors).
        _useScaleFallback = !Mathf.Approximately(rt.anchorMin.x, rt.anchorMax.x);

        // Try to capture the full width at "100% HP". If current HP isn't full, we'll still use the current rect width
        // and scale from there – better than doing nothing.
        _fullWidth = rt.rect.width;
        if (_fullWidth <= 0.01f)
            _fullWidth = rt.sizeDelta.x;
    }

    private void SetFill01(float value01)
    {
        if (fillImage == null) return;

        value01 = Mathf.Clamp01(value01);

        if (_useFillAmount && fillImage.type == Image.Type.Filled)
        {
            fillImage.fillAmount = value01;
            return;
        }

        RectTransform rt = fillImage.rectTransform;
        if (rt == null) return;

        // If the prefab uses stretch anchors, changing sizeDelta won't do what we want.
        if (_useScaleFallback)
        {
            Vector3 s = _baseScale;
            s.x = _baseScale.x * value01;
            rt.localScale = s;
        }
        else
        {
            float w = _fullWidth;
            if (w <= 0.01f)
                w = rt.rect.width;
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w * value01);
        }
    }

    private void HandleHpChanged(int current, int max)
    {
        _lastHp = current;
        _lastMaxHp = max;

        if (fillImage == null) return;

        int safeMax = Mathf.Max(1, max);
        float current01 = Mathf.Clamp01((float)current / safeMax);
        SetFill01(current01);
    }

    /// <summary>
    /// Show a yellow segment representing HP that will be removed.
    /// IMPORTANT: This function expects the predicted HP AFTER damage (not "damage amount").
    /// </summary>
    public void SetDamagePreview(int predictedHpAfterDamage)
    {
        if (monster == null) return;
        if (fillImage == null) AutoFindFillImage();
        if (fillImage == null) return;

        EnsurePreviewObjects();
        if (hpBarFullRect == null || hpDamagePreviewRect == null || hpDamagePreviewImage == null) return;

        int currentHP = monster.CurrentHp;
        int maxHP = Mathf.Max(1, monster.MaxHp);

        // Clamp predicted HP into a valid range
        int predictedHP = Mathf.Clamp(predictedHpAfterDamage, 0, currentHP);

        // Derive damage from predicted HP
        int dmg = Mathf.Max(0, currentHP - predictedHP);

        float current01 = Mathf.Clamp01((float)currentHP / maxHP);
        float predicted01 = Mathf.Clamp01((float)predictedHP / maxHP);

        // Shrink main fill to predicted
        SetFill01(predicted01);

        // Create a segment from predicted -> current
        float barWidth = hpBarFullRect.rect.width;
        float leftX = predicted01 * barWidth;
        float rightX = current01 * barWidth;
        float width = Mathf.Max(0f, rightX - leftX);

        // Position segment
        hpDamagePreviewRect.anchoredPosition = new Vector2(leftX, hpDamagePreviewRect.anchoredPosition.y);
        hpDamagePreviewRect.sizeDelta = new Vector2(width, hpDamagePreviewRect.sizeDelta.y);

        bool visible = dmg > 0 && width > 0.5f; // avoid tiny slivers
        hpDamagePreviewImage.enabled = visible;
    }

    public void ClearPreview()
    {
        if (hpDamagePreviewImage != null)
            hpDamagePreviewImage.enabled = false;

        // Restore fill to current HP
        if (fillImage != null && monster != null)
        {
            int maxHP = Mathf.Max(1, monster.MaxHp);
            float current01 = Mathf.Clamp01((float)monster.CurrentHp / maxHP);
            SetFill01(current01);
        }
    }
}

