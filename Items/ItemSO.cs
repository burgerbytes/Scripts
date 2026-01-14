// GUID: 122e1e8db5e7eaf479c00489c2840073
////////////////////////////////////////////////////////////
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

[CreateAssetMenu(menuName = "Scriptable object/Item", fileName = "NewItem")]
public class ItemSO : ScriptableObject
{
    [Header("UI")]
    public string itemName;
    [TextArea(2, 6)] public string description;
    public Sprite icon;

    [Header("Effects")]
    public List<ItemEffect> effects = new List<ItemEffect>();
}

public enum ItemEffect
{
    ThreeOfAKind, // Ally holding this item will get +3 attack when the reels payout is 3 of a kind
    AllIn,       // You can only attack once per turn with the character using this item
    FirstAid,    // Recover 1 HP after taking damage
    TripleBlade  // When reels land on three Attack symbols, double Attack for the turn but limit to one attack
}

////////////////////////////////////////////////////////////
