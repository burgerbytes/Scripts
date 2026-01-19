using System;
using UnityEngine;

/// <summary>
/// Manages per-hero "Reelcraft" (once-per-battle) abilities and applies their effects to ReelSpinSystem.
///
/// Current starter Reelcrafts:
/// - Fighter: Steel Nudge (nudge their own reel up/down 1 step)
/// - Mage: Arcane Transmutation (convert one pending resource type into Magic for this reel phase)
/// - Ninja: Twofold Shadow (double contribution of their own reel's landed symbol for this reel phase)
/// </summary>
public class ReelcraftController : MonoBehaviour
{
    public enum ReelcraftArchetype { None, Fighter, Mage, Ninja }

    [Header("Refs")]
    [SerializeField] private BattleManager battleManager;
    [SerializeField] private ReelSpinSystem reelSpinSystem;

    [Header("Debug")]
    [SerializeField] private bool logFlow = true;

    private bool[] _usedThisBattle;
    private int _cachedPartyCount;

    private void Awake()
    {
        if (battleManager == null)
            battleManager = FindFirstObjectByType<BattleManager>();

        if (reelSpinSystem == null)
            reelSpinSystem = FindFirstObjectByType<ReelSpinSystem>();
    }

    private void OnEnable()
    {
        if (battleManager != null)
            battleManager.OnBattleStateChanged += HandleBattleStateChanged;
    }

    private void OnDisable()
    {
        if (battleManager != null)
            battleManager.OnBattleStateChanged -= HandleBattleStateChanged;
    }

    private void HandleBattleStateChanged(BattleManager.BattleState state)
    {
        // Reset once-per-battle uses whenever a new battle starts.
        if (state == BattleManager.BattleState.BattleStart)
            ResetForBattle();
    }

    public void ResetForBattle()
    {
        int count = 0;
        if (battleManager != null)
            count = Mathf.Max(0, battleManager.PartyCount);

        if (count <= 0)
            count = Mathf.Max(0, _cachedPartyCount);

        _cachedPartyCount = count;
        _usedThisBattle = (count > 0) ? new bool[count] : Array.Empty<bool>();

        if (logFlow)
            Debug.Log($"[Reelcraft] ResetForBattle. partyCount={count}", this);
    }

    public bool HasUsed(int partyIndex)
    {
        if (_usedThisBattle == null) return false;
        if (partyIndex < 0 || partyIndex >= _usedThisBattle.Length) return false;
        return _usedThisBattle[partyIndex];
    }

    public bool CanUse(int partyIndex)
    {
        if (reelSpinSystem == null) return false;
        if (!reelSpinSystem.InReelPhase) return false;
        if (!reelSpinSystem.HasCurrentLandedSymbols) return false;
        return !HasUsed(partyIndex);
    }

    public ReelcraftArchetype GetArchetype(HeroStats hero)
    {
        if (hero == null) return ReelcraftArchetype.None;

        ClassDefinitionSO classDef = hero.AdvancedClassDef != null ? hero.AdvancedClassDef : hero.BaseClassDef;
        string name = classDef != null ? (classDef.className ?? string.Empty) : string.Empty;
        name = name.ToLowerInvariant();

        // Simple heuristic mapping. We can switch to an explicit enum on ClassDefinitionSO later.
        if (name.Contains("fighter") || name.Contains("berserker") || name.Contains("templar"))
            return ReelcraftArchetype.Fighter;
        if (name.Contains("mage") || name.Contains("wizard") || name.Contains("sorcer"))
            return ReelcraftArchetype.Mage;
        if (name.Contains("ninja") || name.Contains("assassin") || name.Contains("rogue"))
            return ReelcraftArchetype.Ninja;

        return ReelcraftArchetype.None;
    }

    private void MarkUsed(int partyIndex)
    {
        if (_usedThisBattle == null) return;
        if (partyIndex < 0 || partyIndex >= _usedThisBattle.Length) return;
        _usedThisBattle[partyIndex] = true;
    }

    // ---------------- Reelcrafts ----------------

    public bool TrySteelNudge(int partyIndex, int deltaSteps)
    {
        if (!CanUse(partyIndex)) return false;
        if (reelSpinSystem == null) return false;

        bool ok = reelSpinSystem.TryNudgeReel(partyIndex, deltaSteps);
        if (!ok) return false;

        MarkUsed(partyIndex);
        if (logFlow)
            Debug.Log($"[Reelcraft] Steel Nudge used. partyIndex={partyIndex} deltaSteps={deltaSteps}", this);
        return true;
    }

    public bool TryArcaneTransmutation(int partyIndex, ReelSpinSystem.ResourceType fromType)
    {
        if (!CanUse(partyIndex)) return false;
        if (reelSpinSystem == null) return false;

        bool ok = reelSpinSystem.TryConvertPending(fromType, ReelSpinSystem.ResourceType.Magic);
        if (!ok) return false;

        MarkUsed(partyIndex);
        if (logFlow)
            Debug.Log($"[Reelcraft] Arcane Transmutation used. partyIndex={partyIndex} from={fromType} -> Magic", this);
        return true;
    }

    public bool TryTwofoldShadow(int partyIndex)
    {
        if (!CanUse(partyIndex)) return false;
        if (reelSpinSystem == null) return false;

        bool ok = reelSpinSystem.TryMultiplyReelContribution(partyIndex, multiplier: 2);
        if (!ok) return false;

        MarkUsed(partyIndex);
        if (logFlow)
            Debug.Log($"[Reelcraft] Twofold Shadow used. partyIndex={partyIndex}", this);
        return true;
    }
}
