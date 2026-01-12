using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Configuration for the post-battle "reward reels".
/// - Player pays gold per spin.
/// - Only pays out on 3-in-a-row of a non-null symbol.
/// - Payouts are data-driven so you can add more symbols later.
/// </summary>
[CreateAssetMenu(menuName = "Idle Wasteland/Rewards/Reward Reel Config", fileName = "RewardReelConfig_")]
public class RewardReelConfigSO : ScriptableObject
{
    public enum PayoutType
    {
        SmallKey,
        LargeKey
    }

    [Serializable]
    public struct PayoutEntry
    {
        public ReelSymbolSO symbol;
        public PayoutType payoutType;
        public int amount;
    }

    [Header("Spin Cost")]
    [Tooltip("Gold required to spin the reward reels once.")]
    public int goldCostPerSpin = 1;

    [Header("Reel Strip")]
    [Tooltip("Strip used for ALL reward reels while in reward mode.")]
    public ReelStripSO rewardStrip;

    [Header("Null / No-Payout Symbol")]
    [Tooltip("If set, 3-in-a-row of this symbol will NOT pay out.")]
    public ReelSymbolSO nullSymbol;

    [Header("Payout Mapping")]
    [Tooltip("Map symbols to a key payout.")]
    public List<PayoutEntry> payouts = new List<PayoutEntry>();

    public bool TryGetPayout(ReelSymbolSO symbol, out PayoutType type, out int amount)
    {
        type = PayoutType.SmallKey;
        amount = 0;

        if (symbol == null || payouts == null) return false;

        for (int i = 0; i < payouts.Count; i++)
        {
            if (payouts[i].symbol == symbol)
            {
                type = payouts[i].payoutType;
                amount = payouts[i].amount;
                return true;
            }
        }

        return false;
    }
}
