using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// A single square in the Inventory UI (either a pool slot or an equipment slot).
/// Supports click+drag and drop swapping.
/// </summary>
[DisallowMultipleComponent]
public class InventorySlotUI : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    public enum SlotKind { Pool, Equipment }

    [Header("Binding")]
    public SlotKind slotKind = SlotKind.Pool;

    [Tooltip("Pool index (0..PoolSize-1). Used when SlotKind = Pool.")]
    public int poolIndex = 0;

    [Tooltip("Hero index (0..partyCount-1). Used when SlotKind = Equipment.")]
    public int heroIndex = 0;

    [Tooltip("Equipment index (0..2). Used when SlotKind = Equipment.")]
    public int equipmentIndex = 0;

    [Header("UI")]
    [SerializeField] private Image iconImage;

    private InventoryPanelUI _panel;
    private ItemSO _cachedItem;

    private void Awake()
    {
        _panel = GetComponentInParent<InventoryPanelUI>();

        // Ensure our slot itself can receive raycasts.
        var bg = GetComponent<Image>();
        if (bg != null)
            bg.raycastTarget = true;

        EnsureIconImage();
        SetItem(null);
    }

    private void EnsureIconImage()
    {
        if (iconImage != null) return;

        // Look for an existing child Image.
        iconImage = GetComponentInChildren<Image>(includeInactive: true);
        if (iconImage != null && iconImage.gameObject != gameObject)
        {
            // This might be your border image; in that case create a separate icon child.
            // Heuristic: if the found image is on our root, it's the border; else it's OK.
            if (iconImage.gameObject == gameObject)
                iconImage = null;
        }

        if (iconImage == null)
        {
            GameObject go = new GameObject("Icon", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            go.transform.SetParent(transform, worldPositionStays: false);

            iconImage = go.GetComponent<Image>();
            iconImage.raycastTarget = false;

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.1f, 0.1f);
            rt.anchorMax = new Vector2(0.9f, 0.9f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }

    public void SetItem(ItemSO item)
    {
        _cachedItem = item;

        if (iconImage == null) EnsureIconImage();

        if (iconImage != null)
        {
            if (item != null && item.icon != null)
            {
                iconImage.sprite = item.icon;
                iconImage.enabled = true;
                iconImage.color = Color.white;
            }
            else
            {
                iconImage.sprite = null;
                iconImage.enabled = false;
            }
        }
    }

    public ItemSO GetCachedItem() => _cachedItem;

    public void RefreshFromModel()
    {
        if (_panel == null) _panel = GetComponentInParent<InventoryPanelUI>();
        if (_panel == null) return;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (_panel == null) _panel = GetComponentInParent<InventoryPanelUI>();
        if (_panel == null) return;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_panel == null) return;
        _panel.Drag(eventData.position);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (_panel == null) return;
        _panel.EndDrag();
    }

    public void OnDrop(PointerEventData eventData)
    {
        if (_panel == null) _panel = GetComponentInParent<InventoryPanelUI>();
        if (_panel == null) return;

        _panel.DropOn(this);
    }

    public Vector2 GetIconSizeForDrag()
    {
        if (iconImage == null) EnsureIconImage();
        if (iconImage == null) return new Vector2(64, 64);

        return iconImage.rectTransform.rect.size;
    }

    public void SetIconVisible(bool visible)
    {
        if (iconImage == null) EnsureIconImage();
        if (iconImage == null) return;
        iconImage.enabled = visible && _cachedItem != null && _cachedItem.icon != null;
    }
}
