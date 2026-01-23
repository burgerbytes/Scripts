using System;
using UnityEngine;

/// <summary>
/// Per-hero event hub used by passive abilities.
///
/// Keep this small and stable: add new events here as the game grows.
/// Passives should subscribe/unsubscribe in their Register/Unregister methods.
/// </summary>
public sealed class HeroCombatEvents
{
    private readonly HeroStats _hero;

    public HeroCombatEvents(HeroStats hero)
    {
        _hero = hero;
    }

    /// <summary>
    /// Fired when this hero's reel lands on a symbol (including Reelcraft updates).
    /// resourceType/amount come from ReelSpinSystem's symbol->resource mapping.
    /// multiplier is the current multiplier applied to that reel's payout (if any).
    /// </summary>
    public event Action<ReelSymbolSO, ReelSpinSystem.ResourceType, int, int> OnReelSymbolLanded;

    internal void RaiseReelSymbolLanded(ReelSymbolSO symbol, ReelSpinSystem.ResourceType resourceType, int amount, int multiplier)
    {
        try
        {
            OnReelSymbolLanded?.Invoke(symbol, resourceType, amount, multiplier);
        }
        catch (Exception e)
        {
            Debug.LogError($"[HeroCombatEvents] Exception in OnReelSymbolLanded listeners for hero='{(_hero != null ? _hero.name : "<null>")}'. {e}");
        }
    }
}
