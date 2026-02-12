using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Listens for resource gains and spawns a short-lived popup near the resource bar.
/// This version spawns popups under a dedicated overlay root so they always render in front
/// of the resource slot UI (and won't be clipped by masks/layout).
/// </summary>
public class ResourceBarPopupSpawner : MonoBehaviour
{
    [Header("Prefab")]
    [SerializeField] private ResourceGainPopup popupPrefab;

    [Header("Popup Overlay Root (recommended)")]
    [Tooltip("Popups will be spawned under this RectTransform (typically on an overlay canvas above the resource bar). " +
             "If left null, this component will auto-create one under the nearest parent Canvas and force it to render on top.")]
    [SerializeField] private RectTransform popupRoot;

    [Tooltip("If true, a nested Canvas will be created/used on the popupRoot with Override Sorting enabled.")]
    [SerializeField] private bool ensurePopupRootCanvas = true;

    [Tooltip("Sorting order used for the popupRoot Canvas when Override Sorting is enabled.")]
    [SerializeField] private int popupSortingOrder = 500;

    [Tooltip("If true, popups are spawned using screen-space conversion so their position matches the resource slot even if popupRoot is elsewhere in the hierarchy.")]
    [SerializeField] private bool positionByScreenSpace = true;

    [Header("Anchors (Resource Slots)")]
    [Tooltip("RectTransform of the Attack resource slot (or an empty child anchor under it).")]
    [SerializeField] private RectTransform attackAnchor;
    [Tooltip("RectTransform of the Defense resource slot (or an empty child anchor under it).")]
    [SerializeField] private RectTransform defenseAnchor;
    [Tooltip("RectTransform of the Magic resource slot (or an empty child anchor under it).")]
    [SerializeField] private RectTransform magicAnchor;
    [Tooltip("RectTransform of the Wild resource slot (or an empty child anchor under it).")]
    [SerializeField] private RectTransform wildAnchor;

    [Header("Per-Resource Offsets (local to popupRoot)")]
    [SerializeField] private Vector2 attackOffset = new Vector2(0f, 20f);
    [SerializeField] private Vector2 defenseOffset = new Vector2(0f, 20f);
    [SerializeField] private Vector2 magicOffset = new Vector2(0f, 20f);
    [SerializeField] private Vector2 wildOffset = new Vector2(0f, 20f);

    [Header("Popup Colors")]
    [SerializeField] private Color attackColor = new Color(1f, 0.35f, 0.35f, 1f);
    [SerializeField] private Color defenseColor = new Color(0.35f, 0.75f, 1f, 1f);
    [SerializeField] private Color magicColor = new Color(0.75f, 0.45f, 1f, 1f);
    [SerializeField] private Color wildColor = new Color(1f, 0.9f, 0.35f, 1f);

    [Header("Resource Icons (optional)")]
    [Tooltip("Sprite shown beside +amount for Attack.")]
    [SerializeField] private Sprite attackIcon;
    [Tooltip("Sprite shown beside +amount for Defense.")]
    [SerializeField] private Sprite defenseIcon;
    [Tooltip("Sprite shown beside +amount for Magic.")]
    [SerializeField] private Sprite magicIcon;
    [Tooltip("Sprite shown beside +amount for Wild.")]
    [SerializeField] private Sprite wildIcon;

    [Header("Popup Layout / Size")]
    [Tooltip("Uniform scale applied to the spawned popup root transform.")]
    [SerializeField] private float popupScale = 1f;

    [Tooltip("Font size applied to the popup amount text (TMP).")]
    [SerializeField] private float fontSize = 200f;

    [Tooltip("If true, popup text is forced bold (by OR-ing bold onto the TMP fontStyle).")]
    [SerializeField] private bool forceBold = true;

    [Tooltip("Optional icon size (pixels). If <= 0, leave as prefab/default.")]
    [SerializeField] private float iconSize = 0f;

    [Header("Popup Text Outline (TMP Material)")]
    [Tooltip("If enabled, override the popup text outline on the spawned instance only (won't affect other UI text that shares the font).")]
    [SerializeField] private bool overrideOutline = true;

    [Tooltip("Outline width (TMP material). Higher values = chunkier outline.")]
    [Range(0f, 1f)]
    [SerializeField] private float outlineWidth = 0.01f;

    [Tooltip("Outline color (TMP material).")]
    [SerializeField] private Color outlineColor = Color.black;

    [Header("Popup Motion / Timing")]
    [SerializeField] private float floatDistance = 30f;

    [Tooltip("How long the scale 'pop' animation takes (up + down).")]
    [SerializeField] private float popDuration = 0.20f;

    [Tooltip("How long the popup stays fully visible after the pop finishes.")]
    [SerializeField] private float holdDuration = 0.60f;

    [Tooltip("How long it takes to fade out after the hold.")]
    [SerializeField] private float fadeDuration = 0.25f;

    [Header("Popup Pop")]
    [SerializeField] private bool popOnSpawn = true;
    [SerializeField] private float popScale = 1.5f;

    private Canvas _rootCanvas;

    private void Awake()
    {
        EnsurePopupRoot();
    }

    private void OnEnable()
    {
        ResourcePool.OnResourceAdded += HandleResourceAdded;
    }

    private void OnDisable()
    {
        ResourcePool.OnResourceAdded -= HandleResourceAdded;
    }

    private void EnsurePopupRoot()
    {
        // Prefer an explicitly assigned root.
        if (popupRoot == null)
        {
            _rootCanvas = GetComponentInParent<Canvas>();
            if (_rootCanvas == null)
            {
                Debug.LogWarning("[ResourceBarPopupSpawner] No parent Canvas found. Popups may not render correctly.");
                return;
            }

            var go = new GameObject("ResourcePopups", typeof(RectTransform));
            popupRoot = go.GetComponent<RectTransform>();
            popupRoot.SetParent(_rootCanvas.transform, false);
            popupRoot.anchorMin = Vector2.zero;
            popupRoot.anchorMax = Vector2.one;
            popupRoot.offsetMin = Vector2.zero;
            popupRoot.offsetMax = Vector2.zero;
            popupRoot.pivot = new Vector2(0.5f, 0.5f);

            // Ensure it renders on top within the canvas hierarchy.
            popupRoot.SetAsLastSibling();
        }
        else
        {
            _rootCanvas = popupRoot.GetComponentInParent<Canvas>();
            if (popupRoot.parent != null)
                popupRoot.SetAsLastSibling();
        }

        if (popupRoot == null) return;

        if (ensurePopupRootCanvas)
        {
            var c = popupRoot.GetComponent<Canvas>();
            if (c == null) c = popupRoot.gameObject.AddComponent<Canvas>();
            c.overrideSorting = true;
            c.sortingOrder = popupSortingOrder;

            // Popups should not block clicks.
            var gr = popupRoot.GetComponent<GraphicRaycaster>();
            if (gr != null) gr.enabled = false;
        }
    }

    private void HandleResourceAdded(ResourceType type, long amount)
    {
        if (amount <= 0) return;
        if (popupPrefab == null) return;

        EnsurePopupRoot();
        if (popupRoot == null) return;

        RectTransform anchor = GetAnchor(type);
        if (anchor == null) return;

        var popup = Instantiate(popupPrefab, popupRoot);
        if (popupScale > 0f) popup.transform.localScale = Vector3.one * popupScale;

        // Positioning: convert anchor world -> screen -> popupRoot local
        Vector2 anchoredPos = Vector2.zero;
        if (positionByScreenSpace)
        {
            var canvas = _rootCanvas != null ? _rootCanvas : popupRoot.GetComponentInParent<Canvas>();
            Camera cam = null;

            // If in Screen Space - Camera / World Space, use canvas.worldCamera.
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                cam = canvas.worldCamera;

            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, anchor.position);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(popupRoot, screen, cam, out anchoredPos);
        }
        else
        {
            // Fallback: same-parent local space (only correct if popupRoot is in the same layout space as the anchors).
            anchoredPos = (Vector2)popupRoot.InverseTransformPoint(anchor.position);
        }

        anchoredPos += GetOffset(type);
        popup.SetAnchoredPosition(anchoredPos);

        // Apply styling + drive animation parameters from one place (this component).
        var style = new ResourceGainPopup.Style
        {
            fontSize = fontSize,
            forceBold = forceBold,
            iconSize = iconSize,
            overrideOutline = overrideOutline,
            outlineWidth = outlineWidth,
            outlineColor = outlineColor,

            floatDistance = floatDistance,
            popDuration = popDuration,
            holdDuration = holdDuration,
            fadeDuration = fadeDuration,
            popOnSpawn = popOnSpawn,
            popScale = popScale
        };

        popup.Initialize(amount, GetColor(type), GetIcon(type), style);
    }

    private RectTransform GetAnchor(ResourceType type)
    {
        return type switch
        {
            ResourceType.Attack => attackAnchor,
            ResourceType.Defense => defenseAnchor,
            ResourceType.Magic => magicAnchor,
            ResourceType.Wild => wildAnchor,
            _ => null
        };
    }

    private Vector2 GetOffset(ResourceType type)
    {
        return type switch
        {
            ResourceType.Attack => attackOffset,
            ResourceType.Defense => defenseOffset,
            ResourceType.Magic => magicOffset,
            ResourceType.Wild => wildOffset,
            _ => Vector2.zero
        };
    }

    private Color GetColor(ResourceType type)
    {
        return type switch
        {
            ResourceType.Attack => attackColor,
            ResourceType.Defense => defenseColor,
            ResourceType.Magic => magicColor,
            ResourceType.Wild => wildColor,
            _ => Color.white
        };
    }

    private Sprite GetIcon(ResourceType type)
    {
        return type switch
        {
            ResourceType.Attack => attackIcon,
            ResourceType.Defense => defenseIcon,
            ResourceType.Magic => magicIcon,
            ResourceType.Wild => wildIcon,
            _ => null
        };
    }
}
