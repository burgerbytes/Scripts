using UnityEngine;
using TMPro;

/// <summary>
/// World-space status icon controller for heroes.
///
/// Shows ONE icon at a time (priority-based):
///   Stunned > Hidden > TripleBladeEmpowered > Bleeding
///
/// Bleeding supports a small stack count overlay using TextMeshPro (created automatically).
///
/// Backward compatible:
///   - ConfigureSprites(hidden, stunned, triple)
///   - SetStates(hidden, stunned, triple)
/// </summary>
public class StatusEffectIconController : MonoBehaviour
{
    [Header("Layout")]
    [SerializeField] private Vector3 localOffset = new Vector3(0f, 1.25f, 0f);
    [SerializeField] private Vector3 localScale = new Vector3(0.35f, 0.35f, 0.35f);
    [SerializeField] private int sortingOrder = 50;

    [Header("Billboard")]
    [SerializeField] private bool billboardToMainCamera = true;

    [Header("Sprites")]
    [SerializeField] private Sprite stunnedSprite;
    [SerializeField] private Sprite hiddenSprite;
    [SerializeField] private Sprite tripleBladeEmpoweredSprite;
    [SerializeField] private Sprite bleedingSprite;

    [Header("Bleed Stack Text")]
    [Tooltip("If enabled, a TextMeshPro child is auto-created to show bleed stacks when Bleeding is the active icon.")]
    [SerializeField] private bool showBleedStackText = true;
    [SerializeField] private float bleedStackFontSize = 1.0f;
    [SerializeField]
    [Tooltip("Scale multiplier applied only to the bleed stack text (relative to the icon scale). 0.2 = ~5x smaller.")]
    private float bleedStackScaleMultiplier = 0.2f;
    [SerializeField] private Vector3 bleedStackLocalOffset = new Vector3(0.18f, -0.18f, 0f);

    private SpriteRenderer _sr;
    private TextMeshPro _bleedText;

    // We only want to apply the serialized default font size ONCE (when the text object is created).
    // After that, designers can tweak the TMP component in the prefab/scene and it will not be
    // overwritten every refresh.
    private bool _bleedTextInitialized = false;

    private bool _hidden;
    private bool _stunned;
    private bool _tripleBladeEmpowered;
    private bool _bleeding;
    private int _bleedStacks;

    private void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        if (_sr == null) _sr = gameObject.AddComponent<SpriteRenderer>();
        _sr.sortingOrder = sortingOrder;

        EnsureBleedText();
        ApplyTransformDefaults();
        ApplyVisual();
    }

    private void LateUpdate()
    {
        if (!billboardToMainCamera) return;
        var cam = Camera.main;
        if (cam == null) return;
        transform.rotation = cam.transform.rotation;
    }

    // -----------------------------
    // Backward-compatible API
    // -----------------------------

    public void ConfigureSprites(Sprite hidden, Sprite stunned, Sprite tripleBladeEmpowered)
    {
        ConfigureSprites(hidden, stunned, tripleBladeEmpowered, null);
    }

    public void SetStates(bool hidden, bool stunned, bool tripleBladeEmpowered)
    {
        SetStates(hidden, stunned, tripleBladeEmpowered, false);
    }

    // -----------------------------
    // New API (Bleeding support)
    // -----------------------------

    public void ConfigureSprites(Sprite hidden, Sprite stunned, Sprite tripleBladeEmpowered, Sprite bleeding)
    {
        // Prefer inspector assignments; only fill if missing.
        if (hiddenSprite == null) hiddenSprite = hidden;
        if (stunnedSprite == null) stunnedSprite = stunned;
        if (tripleBladeEmpoweredSprite == null) tripleBladeEmpoweredSprite = tripleBladeEmpowered;
        if (bleedingSprite == null) bleedingSprite = bleeding;

        ApplyVisual();
    }

    public void SetStates(bool hidden, bool stunned, bool tripleBladeEmpowered, bool bleeding)
    {
        if (_hidden == hidden && _stunned == stunned && _tripleBladeEmpowered == tripleBladeEmpowered && _bleeding == bleeding)
            return;

        _hidden = hidden;
        _stunned = stunned;
        _tripleBladeEmpowered = tripleBladeEmpowered;
        _bleeding = bleeding;

        ApplyVisual();
    }

    /// <summary>
    /// Set current bleed stack count for the overlay. Safe to call even if not bleeding.
    /// </summary>
    public void SetBleedStacks(int stacks)
    {
        _bleedStacks = Mathf.Max(0, stacks);
        ApplyVisual();
    }

    private void EnsureBleedText()
    {
        if (!showBleedStackText) return;

        if (_bleedText == null)
        {
            bool created = false;

            var t = transform.Find("Stacks");
            if (t != null) _bleedText = t.GetComponent<TextMeshPro>();

            if (_bleedText == null)
            {
                var go = new GameObject("Stacks");
                go.transform.SetParent(transform, false);
                _bleedText = go.AddComponent<TextMeshPro>();
                created = true;
            }

            _bleedText.alignment = TextAlignmentOptions.Center;
            _bleedText.enableAutoSizing = false;
            // Only set the default font size when we CREATED the TMP object.
            // If the "Stacks" text already exists in the prefab/scene, do not override designer values.
            if (created && !_bleedTextInitialized)
            {
                _bleedText.fontSize = bleedStackFontSize;
                _bleedTextInitialized = true;
            }
            _bleedText.text = "";

            var mr = _bleedText.GetComponent<MeshRenderer>();
            if (mr != null) mr.sortingOrder = sortingOrder + 1;
        }

        // Keep transform in sync.
        if (_bleedText != null)
        {
            _bleedText.transform.localPosition = localOffset + bleedStackLocalOffset;
            _bleedText.transform.localScale = localScale * bleedStackScaleMultiplier;

            var mr = _bleedText.GetComponent<MeshRenderer>();
            if (mr != null) mr.sortingOrder = sortingOrder + 1;
        }
    }

    private void ApplyTransformDefaults()
    {
        transform.localPosition = localOffset;
        transform.localScale = localScale;

        if (_sr != null)
            _sr.sortingOrder = sortingOrder;

        EnsureBleedText();
    }

    private void ApplyVisual()
    {
        if (_sr == null) _sr = GetComponent<SpriteRenderer>();
        if (_sr == null) return;

        Sprite spriteToUse = null;

        // Priority: Stunned > Hidden > TripleBladeEmpowered > Bleeding
        if (_stunned) spriteToUse = stunnedSprite;
        else if (_hidden) spriteToUse = hiddenSprite;
        else if (_tripleBladeEmpowered) spriteToUse = tripleBladeEmpoweredSprite;
        else if (_bleeding) spriteToUse = bleedingSprite;

        _sr.sprite = spriteToUse;
        _sr.enabled = (spriteToUse != null);

        // Bleed overlay
        EnsureBleedText();
        if (_bleedText != null)
        {
            bool show = showBleedStackText && _bleeding && _bleedStacks > 0 && spriteToUse == bleedingSprite && bleedingSprite != null;
            _bleedText.text = show ? _bleedStacks.ToString() : "";
            _bleedText.enabled = show;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        _sr = GetComponent<SpriteRenderer>();
        if (_sr != null)
            _sr.sortingOrder = sortingOrder;

        // Keep looking correct in editor.
        transform.localPosition = localOffset;
        transform.localScale = localScale;

        EnsureBleedText();
        ApplyVisual();
    }
#endif
}
