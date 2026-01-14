using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Wires PlayerInventory (pool) + party HeroStats (equipment slots) to the Inventory UI.
/// Handles drag/drop swapping across any two slots (pool <-> pool, equip <-> equip, pool <-> equip).
///
/// Assumptions based on your screenshot:
/// - Pool grid: 3 rows x 5 columns = 15 slots
/// - Equipment grid: 3 heroes x 3 slots = 9 slots
/// </summary>
public class InventoryPanelUI : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private PlayerInventory inventory;

    [Tooltip("Heroes in party order (top-to-bottom equipment rows). If empty, we will FindObjectsOfType and you should order them manually in the inspector.")]
    [SerializeField] private List<HeroStats> partyHeroes = new List<HeroStats>();

    [Header("UI Slots")]
    [Tooltip("All pool slot UI components (15). If empty, we auto-collect from children.")]
    [SerializeField] private List<InventorySlotUI> poolSlots = new List<InventorySlotUI>();

    [Tooltip("All equipment slot UI components (9). If empty, we auto-collect from children.")]
    [SerializeField] private List<InventorySlotUI> equipSlots = new List<InventorySlotUI>();

    [Header("Drag Overlay")]
    [SerializeField] private InventoryDragController dragController;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private InventorySlotUI _dragSourceSlot;
    private ItemSO _dragItem;

    private void Awake()
    {
        if (inventory == null)
            inventory = FindFirstObjectByType<PlayerInventory>();

        if (dragController == null)
            dragController = GetComponentInChildren<InventoryDragController>(includeInactive: true);

        AutoCollectSlotsIfNeeded();
        ResolvePartyHeroesIfNeeded();
    }

    private void OnEnable()
    {
        if (inventory != null)
            inventory.OnInventoryChanged += RefreshAll;

        HookHeroEvents(true);

        RefreshAll();
    }

    private void OnDisable()
    {
        if (inventory != null)
            inventory.OnInventoryChanged -= RefreshAll;

        HookHeroEvents(false);

        EndDrag();
    }

    private void HookHeroEvents(bool hook)
    {
        ResolvePartyHeroesIfNeeded();

        for (int i = 0; i < partyHeroes.Count; i++)
        {
            var h = partyHeroes[i];
            if (h == null) continue;

            if (hook) h.OnChanged += RefreshAll;
            else h.OnChanged -= RefreshAll;
        }
    }

    private void AutoCollectSlotsIfNeeded()
    {
        if ((poolSlots == null || poolSlots.Count == 0) || (equipSlots == null || equipSlots.Count == 0))
        {
            var all = GetComponentsInChildren<InventorySlotUI>(includeInactive: true);
            poolSlots = new List<InventorySlotUI>();
            equipSlots = new List<InventorySlotUI>();

            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].slotKind == InventorySlotUI.SlotKind.Pool) poolSlots.Add(all[i]);
                else equipSlots.Add(all[i]);
            }
        }
    }

    private void ResolvePartyHeroesIfNeeded()
    {
        // If partyHeroes already populated (non-null), keep it.
        bool hasAny = false;
        for (int i = 0; i < partyHeroes.Count; i++)
        {
            if (partyHeroes[i] != null) { hasAny = true; break; }
        }
        if (hasAny) return;

        // Fallback: find all heroes in scene (order may be arbitrary).
#if UNITY_6000_0_OR_NEWER
        partyHeroes = new List<HeroStats>(FindObjectsByType<HeroStats>(FindObjectsInactive.Include, FindObjectsSortMode.None));
#else
        partyHeroes = new List<HeroStats>(FindObjectsOfType<HeroStats>(true));
#endif
    }

    public void RefreshAll()
    {
        if (inventory == null) return;

        for (int i = 0; i < poolSlots.Count; i++)
        {
            if (poolSlots[i] == null) continue;
            poolSlots[i].RefreshFromModel();
        }

        for (int i = 0; i < equipSlots.Count; i++)
        {
            if (equipSlots[i] == null) continue;
            equipSlots[i].RefreshFromModel();
        }
    }

    // ---------------- Drag API called by InventorySlotUI ----------------

    public void BeginDrag(InventorySlotUI sourceSlot, ItemSO item)
    {
        if (sourceSlot == null || item == null) return;

        _dragSourceSlot = sourceSlot;
        _dragItem = item;

        // Hide the icon in the source slot during drag (visual only).
        _dragSourceSlot.SetItem(item);
        _dragSourceSlot.SetIconVisible(false);

        if (dragController == null)
            dragController = InventoryDragController.Instance;

        if (dragController != null)
            dragController.Show(item.icon, Input.mousePosition, sourceSlot.GetIconSizeForDrag());

        if (debugLogs)
            Debug.Log($"[InventoryPanelUI] BeginDrag from {sourceSlot.name} item={item.itemName}", this);
    }

    public void Drag(Vector2 screenPos)
    {
        if (_dragItem == null) return;
        if (dragController == null) dragController = InventoryDragController.Instance;
        if (dragController != null) dragController.SetPosition(screenPos);
    }

    public void EndDrag()
    {
        if (_dragSourceSlot != null)
            _dragSourceSlot.RefreshFromModel();

        _dragSourceSlot = null;
        _dragItem = null;

        if (dragController == null) dragController = InventoryDragController.Instance;
        if (dragController != null) dragController.Hide();
    }

    public void DropOn(InventorySlotUI targetSlot)
    {
        if (_dragSourceSlot == null || _dragItem == null)
            return;

        if (targetSlot == null)
        {
            EndDrag();
            return;
        }

        if (targetSlot == _dragSourceSlot)
        {
            EndDrag();
            return;
        }

        RefreshAll();
        EndDrag();
    }
}
