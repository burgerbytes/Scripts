using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Player item ownership and the 15-slot "inventory pool" grid shown in the top rows of the Inventory panel.
///
/// Requirements implemented:
/// - Items are UNIQUE: only one of each item can exist across the entire player state (pool + equipped).
/// - Rewards that grant items automatically place them into the pool (first empty slot).
/// - Pool supports swapping/reordering via UI (see InventoryPanelUI).
///
/// Notes:
/// - We keep the legacy Add(item, quantity) signature so existing reward code compiles.
/// - Quantity is intentionally ignored for now (items are unique, 0/1 only).
/// </summary>
public class PlayerInventory : MonoBehaviour
{
    [Header("Pool Grid")]
    [Tooltip("How many slots exist in the inventory pool (top grid). Your UI is currently 3 rows x 5 columns = 15.")]
    [SerializeField] private int poolSize = 15;

    [Tooltip("The pool slot contents (index 0..poolSize-1).")]
    [SerializeField] private List<ItemSO> poolSlots = new List<ItemSO>();

    [Header("Optional: Equipment Uniqueness Check")]
    [Tooltip("If assigned, we will check equipment items on these heroes when enforcing uniqueness. If empty, we will FindObjectsOfType<HeroStats>().")]
    [SerializeField] private List<HeroStats> partyHeroes = new List<HeroStats>();

    public event Action OnInventoryChanged;

    /// <summary>Read-only view of pool slots (0..poolSize-1).</summary>
    public IReadOnlyList<ItemSO> PoolSlots => poolSlots;

    public int PoolSize => poolSize;

    private void Awake()
    {
        EnsurePoolSized();
    }

    private void OnValidate()
    {
        poolSize = Mathf.Max(1, poolSize);
        EnsurePoolSized();
    }

    private void EnsurePoolSized()
    {
        if (poolSlots == null) poolSlots = new List<ItemSO>();

        // Resize list to poolSize
        while (poolSlots.Count < poolSize) poolSlots.Add(null);
        while (poolSlots.Count > poolSize) poolSlots.RemoveAt(poolSlots.Count - 1);
    }

    /// <summary>
    /// Legacy entry point used by reward screens. Quantity is ignored (unique items only).
    /// </summary>
    public void Add(ItemSO item, int quantity = 1)
    {
        TryAddUniqueToPool(item);
    }

    /// <summary>
    /// True if the player already owns this item anywhere (pool OR equipped on any hero).
    /// </summary>
    public bool ContainsAnywhere(ItemSO item)
    {
        if (item == null) return false;

        // Pool
        for (int i = 0; i < poolSlots.Count; i++)
        {
            if (poolSlots[i] == item) return true;
        }

        // Equipment
        var heroes = GetHeroesForUniquenessCheck();
        for (int h = 0; h < heroes.Count; h++)
        {
            var hs = heroes[h];
            if (hs == null) continue;
            if (hs.HasEquipped(item)) return true;
        }

        return false;
    }

    /// <summary>
    /// Try to add the item to the first empty pool slot.
    /// Returns false if (a) item is null, (b) already owned, or (c) no empty slot exists.
    /// </summary>
    public bool TryAddUniqueToPool(ItemSO item)
    {
        Debug.Log("[PlayerInventory] TryAddUniqueToPool");
        if (item == null) return false;

        EnsurePoolSized();

        if (ContainsAnywhere(item))
        {
            Debug.Log($"[PlayerInventory] Skipping add. Item already owned: {item.itemName}", this);
            return false;
        }

        int empty = FindFirstEmptyPoolSlot();
        if (empty < 0)
        {
            Debug.LogWarning($"[PlayerInventory] Pool is full. Could not add item: {item.itemName}", this);
            return false;
        }

        poolSlots[empty] = item;
        NotifyChanged();
        return true;
    }

    public int FindFirstEmptyPoolSlot()
    {
        Debug.Log("[PlayerInventory] FindFirstEmptyPoolSlot");
        EnsurePoolSized();

        for (int i = 0; i < poolSlots.Count; i++)
        {
            if (poolSlots[i] == null) return i;
        }
        return -1;
    }

    public ItemSO GetPoolItem(int index)
    {
        Debug.Log("[PlayerInventory] GetPoolItem");
        EnsurePoolSized();
        if (index < 0 || index >= poolSlots.Count) return null;
        return poolSlots[index];
    }

    /// <summary>
    /// Set a pool slot. Caller is responsible for uniqueness (panel enforces by moving/swapping).
    /// </summary>
    public void SetPoolItem(int index, ItemSO item, bool notify = true)
    {
        Debug.Log("[PlayerInventory] SetPoolItem");
        EnsurePoolSized();
        if (index < 0 || index >= poolSlots.Count) return;

        poolSlots[index] = item;

        if (notify) NotifyChanged();
    }

    /// <summary>Swap two pool indices.</summary>
    public void SwapPoolSlots(int a, int b)
    {
        EnsurePoolSized();
        if (a < 0 || a >= poolSlots.Count) return;
        if (b < 0 || b >= poolSlots.Count) return;
        if (a == b) return;

        ItemSO tmp = poolSlots[a];
        poolSlots[a] = poolSlots[b];
        poolSlots[b] = tmp;

        NotifyChanged();
    }

    public void NotifyChanged() => OnInventoryChanged?.Invoke();

    private List<HeroStats> GetHeroesForUniquenessCheck()
    {
        // Prefer explicit list if populated.
        bool hasAny = false;
        for (int i = 0; i < partyHeroes.Count; i++)
        {
            if (partyHeroes[i] != null) { hasAny = true; break; }
        }

        if (hasAny)
            return partyHeroes;

        // Fallback: scene-wide scan (includes inactive in Unity 6).
#if UNITY_6000_0_OR_NEWER
        return new List<HeroStats>(FindObjectsByType<HeroStats>(FindObjectsInactive.Include, FindObjectsSortMode.None));
#else
        return new List<HeroStats>(FindObjectsOfType<HeroStats>(true));
#endif
    }
}
