using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small helper that renders the dragged item icon on top of the UI while dragging.
/// InventorySlotUI drives it.
/// </summary>
public class InventoryDragController : MonoBehaviour
{
    public static InventoryDragController Instance { get; private set; }

    [Header("UI")]
    [Tooltip("A RectTransform under the same Canvas that should contain the drag icon (usually a top-most overlay layer). If null, we use our own RectTransform.")]
    [SerializeField] private RectTransform dragLayer;

    [Tooltip("Scale multiplier applied to the dragged icon.")]
    [SerializeField] private float dragIconScale = 1.0f;

    private Canvas _canvas;
    private RectTransform _rt;
    private Image _dragImage;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _canvas = GetComponentInParent<Canvas>();
        _rt = GetComponent<RectTransform>();

        if (dragLayer == null)
            dragLayer = _rt;

        EnsureDragImage();
        Hide();
    }

    private void EnsureDragImage()
    {
        if (_dragImage != null) return;

        GameObject go = new GameObject("DragIcon", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        go.transform.SetParent(dragLayer, worldPositionStays: false);

        _dragImage = go.GetComponent<Image>();
        _dragImage.raycastTarget = false;

        var cg = go.GetComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        cg.interactable = false;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(64, 64);
        rt.localScale = Vector3.one * Mathf.Max(0.1f, dragIconScale);
    }

    public void Show(Sprite sprite, Vector2 screenPos, Vector2 size)
    {
        EnsureDragImage();

        _dragImage.sprite = sprite;
        _dragImage.enabled = (sprite != null);

        RectTransform rt = _dragImage.rectTransform;
        rt.sizeDelta = size;
        rt.localScale = Vector3.one * Mathf.Max(0.1f, dragIconScale);

        SetPosition(screenPos);

        _dragImage.gameObject.SetActive(true);
    }

    public void SetPosition(Vector2 screenPos)
    {
        if (_dragImage == null) return;

        // Works for Screen Space - Overlay or Camera (as long as the canvas has the correct event camera).
        RectTransform canvasRt = _canvas != null ? _canvas.transform as RectTransform : null;
        Camera cam = null;

        if (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = _canvas.worldCamera;

        if (canvasRt != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, screenPos, cam, out Vector2 local))
            _dragImage.rectTransform.anchoredPosition = local;
    }

    public void Hide()
    {
        if (_dragImage != null)
            _dragImage.gameObject.SetActive(false);
    }
}
