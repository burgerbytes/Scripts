using UnityEngine;
using UnityEngine.EventSystems;

public class EquipmentSlotDropLogger : MonoBehaviour, IDropHandler
{
    public void OnDrop(PointerEventData eventData)
    {
        if (eventData == null || eventData.pointerDrag == null) return;

        var invItem = eventData.pointerDrag.GetComponent<InventoryItem>();
        if (invItem == null) return;

        // IMPORTANT: tell the dragged item to snap into THIS slot
        invItem.parentAfterDrag = transform;

        // Log item name
        string itemName = (invItem.item != null) ? invItem.item.itemName : invItem.gameObject.name;
        Debug.Log($"[EquipGrid] Item dropped into equipment slot: {itemName}");
    }
}
