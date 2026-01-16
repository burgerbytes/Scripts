using UnityEngine;
using TMPro;

/// <summary>
/// World-space status icon controller for monsters.
///
/// - Currently supports Bleeding stacks.
/// - Attach (or auto-add) to a child transform named "_StatusIcon".
/// - Creates a SpriteRenderer for the icon and a TextMeshPro label for stack count.
///
/// This avoids Unity UI (Canvas/Image) to keep prefab wiring simple and robust.
/// </summary>
public class MonsterStatusEffectIconController : MonoBehaviour
{
    [Header("Layout")]
    [Tooltip("Local offset from this transform.")]
    [SerializeField] private Vector3 localOffset = Vector3.zero;

    [Tooltip("Local scale applied to the icon + text.")]
    [SerializeField] private Vector3 localScale = Vector3.one;

    [Tooltip("Sorting order for the icon SpriteRenderer.")]
    [SerializeField] private int sortingOrder = 200;

    [Header("Text")]
    [SerializeField] private float stackTextSize = 2.5f;
    [SerializeField] private Vector3 stackTextLocalOffset = new Vector3(0.18f, -0.18f, 0f);

    private Sprite _bleedSprite;

    private Transform _iconRoot;
    private SpriteRenderer _iconRenderer;
    private TextMeshPro _stackText;

    private void Awake()
    {
        EnsureBuilt();
        ApplyLayout();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            EnsureBuilt();
            ApplyLayout();
        }
    }

    private void EnsureBuilt()
    {
        if (_iconRoot == null)
        {
            // Child object so you can tweak this component's transform separately if desired.
            var existing = transform.Find("Icon");
            if (existing != null) _iconRoot = existing;
            else
            {
                var go = new GameObject("Icon");
                go.transform.SetParent(transform, false);
                _iconRoot = go.transform;
            }
        }

        if (_iconRenderer == null)
        {
            _iconRenderer = _iconRoot.GetComponent<SpriteRenderer>();
            if (_iconRenderer == null)
                _iconRenderer = _iconRoot.gameObject.AddComponent<SpriteRenderer>();

            _iconRenderer.sortingOrder = sortingOrder;
        }

        if (_stackText == null)
        {
            var t = transform.Find("Stacks");
            if (t != null) _stackText = t.GetComponent<TextMeshPro>();

            if (_stackText == null)
            {
                var go = new GameObject("Stacks");
                go.transform.SetParent(transform, false);
                _stackText = go.AddComponent<TextMeshPro>();
            }

            // Reasonable defaults for a small overlay count.
            _stackText.alignment = TextAlignmentOptions.Center;
            _stackText.fontSize = stackTextSize;
            _stackText.enableAutoSizing = false;
            _stackText.text = "";

            // Make sure it renders above most sprite layers.
            var mr = _stackText.GetComponent<MeshRenderer>();
            if (mr != null) mr.sortingOrder = sortingOrder + 1;
        }
    }

    private void ApplyLayout()
    {
        if (_iconRoot != null)
        {
            _iconRoot.localPosition = localOffset;
            _iconRoot.localScale = localScale;
        }

        if (_stackText != null)
        {
            _stackText.transform.localPosition = localOffset + stackTextLocalOffset;
            _stackText.transform.localScale = localScale;
            _stackText.fontSize = stackTextSize;
        }

        if (_iconRenderer != null)
            _iconRenderer.sortingOrder = sortingOrder;

        if (_stackText != null)
        {
            var mr = _stackText.GetComponent<MeshRenderer>();
            if (mr != null) mr.sortingOrder = sortingOrder + 1;
        }
    }

    /// <summary>
    /// Provide the sprite used for Bleeding.
    /// </summary>
    public void Configure(Sprite bleedSprite)
    {
        _bleedSprite = bleedSprite;

        EnsureBuilt();
        ApplyLayout();

        if (_iconRenderer != null)
            _iconRenderer.sprite = _bleedSprite;
    }

    /// <summary>
    /// Set current bleed stack count. 0 hides the icon.
    /// </summary>
    public void SetBleedStacks(int stacks)
    {
        EnsureBuilt();
        ApplyLayout();

        bool active = stacks > 0 && _bleedSprite != null;

        if (_iconRenderer != null)
        {
            _iconRenderer.sprite = _bleedSprite;
            _iconRenderer.enabled = active;
        }

        if (_stackText != null)
        {
            _stackText.text = active ? stacks.ToString() : "";
            _stackText.enabled = active;
        }
    }
}
