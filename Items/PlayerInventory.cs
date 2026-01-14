using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerInventory : MonoBehaviour
{
    [Header("Inventory Grid")]
    [SerializeField] private int inventorySize = 21;

    public event Action OnInventoryChanged;

    public InventorySlot[] inventorySlots;
    public GameObject InventoryItemPrefab;

    private void Awake()
    {

    }

    public void Add(ItemSO item, int quantity = 1)
    {
        for (int i = 0; i< inventorySlots.Length; i++)
        {
            InventorySlot slot = inventorySlots[i];
            InventoryItem itemInSlot = slot.GetComponentInChildren<InventoryItem>();
            if (itemInSlot == null)
            {
                SpawnNewItem(item, slot);
                return;
            }        
        }
    }

    void SpawnNewItem(ItemSO item, InventorySlot slot)
    {
        if (item != null)
        {
            GameObject newItemGo = Instantiate(InventoryItemPrefab, slot.transform);
            InventoryItem inventoryItem = newItemGo.GetComponentInChildren<InventoryItem>();
            inventoryItem.InitializeItem(item);
        }
    }
}
