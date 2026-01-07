// PATH: Assets/Scripts/Items/ItemSO.cs
using UnityEngine;

[CreateAssetMenu(menuName = "Idle Wasteland/Items/Item", fileName = "NewItem")]
public class ItemSO : ScriptableObject
{
    [Header("UI")]
    public string itemName;
    [TextArea(2, 6)] public string description;
    public Sprite icon;
}
