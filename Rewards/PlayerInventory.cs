// PATH: Assets/Scripts/Items/PlayerInventory.cs
using System;
using System.Collections.Generic;
using UnityEngine;

public class PlayerInventory : MonoBehaviour
{
    [Serializable]
    public class ItemStack
    {
        public ItemSO item;
        public int quantity;
    }

    [SerializeField] private List<ItemStack> items = new List<ItemStack>();

    public event Action OnInventoryChanged;

    public IReadOnlyList<ItemStack> Items => items;

    public void Add(ItemSO item, int quantity = 1)
    {
        if (item == null) return;
        quantity = Mathf.Max(1, quantity);

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] != null && items[i].item == item)
            {
                items[i].quantity = Mathf.Max(0, items[i].quantity + quantity);
                OnInventoryChanged?.Invoke();
                return;
            }
        }

        items.Add(new ItemStack { item = item, quantity = quantity });
        OnInventoryChanged?.Invoke();
    }
}
