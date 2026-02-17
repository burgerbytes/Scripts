using UnityEngine;

[CreateAssetMenu(menuName = "Slots & Sorcery/Monsters/Monster Reel Definition", fileName = "MonsterReelDefinition")]
public class MonsterReelDefinitionSO : ScriptableObject
{
    [Tooltip("Authored reel strip asset used to display the monster's 6-slot kit. Use the same ReelSymbolSO pipeline as your working reels.")]
    [SerializeField] private ReelStripSO strip;

    [Tooltip("Maps each strip slot (0..5) to an index in Monster.Attacks. Use -1 for NULL/empty slots.")]
    [SerializeField] private int[] slotToAttackIndex = new int[6] { -1, -1, -1, -1, -1, -1 };

    public ReelStripSO Strip => strip;
    public int[] SlotToAttackIndex => slotToAttackIndex;
}
