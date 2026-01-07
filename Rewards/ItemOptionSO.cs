// PATH: Assets/Scripts/Rewards/ItemOptionSO.cs
using UnityEngine;

/// <summary>
/// Post-battle reward option that grants an ItemSO.
/// Uses the same "optionName/description/pros/cons/icon" fields as CampfireOptionSO,
/// so it can reuse RewardOptionCard.
/// </summary>
[CreateAssetMenu(menuName = "Idle Wasteland/Rewards/Item Option", fileName = "NewItemOption")]
public class ItemOptionSO : ScriptableObject
{
    [Header("UI")]
    public string optionName;
    [TextArea(2, 6)] public string description;
    public Sprite icon;

    [Header("Pros / Cons")]
    [TextArea(1, 3)] public string[] pros;
    [TextArea(1, 3)] public string[] cons;

    [Header("Reward")]
    public ItemSO item;
    [Min(1)] public int quantity = 1;

    public void PrintItemDetails()
    {
        Debug.Log("Item name: " + optionName + ", Item description: " + description);
    }
}
