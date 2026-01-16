// PATH: Assets/Scripts/Encounters/EnemyPartyCompositionSO.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Defines a fixed enemy lineup ("enemy party") and an optional loot table override.
///
/// Authoring notes:
/// - Enemies are spawned in list order into BattleManager's Monster Spawn Points.
/// - If the party has more enemies than spawn points, extra enemies are ignored (BattleManager logs a warning).
/// - If lootTable is null or empty, BattleManager falls back to the existing global reward pool.
/// </summary>
[CreateAssetMenu(menuName = "Idle Wasteland/Encounters/Enemy Party Composition", fileName = "NewEnemyParty")]
public class EnemyPartyCompositionSO : ScriptableObject
{
    [Header("Enemies")]
    [Tooltip("Enemy prefabs to spawn (in order). Duplicates are allowed.")]
    public List<GameObject> enemies = new List<GameObject>();

    [Header("Battle Rewards")]
    [Tooltip("Gold awarded immediately on victory. Added to the party's shared gold (HeroStats).")]
    public long goldReward = 0;

    [Tooltip("How many Small Chests are offered after this battle.")]
    public int smallChestCount = 2;

    [Tooltip("How many Large Chests are offered after this battle.")]
    public int largeChestCount = 1;

    [Tooltip("Reward reel configuration used after this battle (keys / special symbols). If null, the normal combat reels remain.")]
    public RewardReelConfigSO rewardReelConfig;

    [Header("Progression Gating")]
    [Tooltip("First fight index this party can appear (1-based).")]
    [Min(1)] public int minFightIndex = 1;

    [Tooltip("Last fight index this party can appear (inclusive).")]
    [Min(1)] public int maxFightIndex = 999;

    [Tooltip("Relative chance when multiple parties are eligible for the same fight.")]
    [Min(0f)] public float selectionWeight = 1f;

    public bool IsEligibleForFight(int fightIndex)
    {
        return fightIndex >= minFightIndex && fightIndex <= maxFightIndex;
    }

    [Header("Loot (Optional Override)")]
    [Tooltip("If set, post-battle rewards will be rolled ONLY from this list for this encounter.\nIf null/empty, BattleManager uses the global reward pool (existing behavior).")]
    public List<ItemOptionSO> lootTable;
}