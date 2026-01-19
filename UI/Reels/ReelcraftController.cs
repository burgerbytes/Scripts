using System;
using UnityEngine;

/// <summary>
/// Manages per-hero "Reelcraft" (once-per-battle) abilities and applies their effects to ReelSpinSystem.
///
/// Current starter Reelcrafts:
/// - Fighter: MeasuredBash (nudge their own reel up/down 1 step)
/// - Mage: Arcane Transmutation (pick a symbol icon on any reel to permanently transmute to Magic for this battle)
/// - Ninja: Twofold Shadow (pick the ninja's currently landed icon and make it count as 2 for this battle)
/// </summary>
public class ReelcraftController : MonoBehaviour
{
    public enum ReelcraftArchetype { None, Fighter, Mage, Ninja }

    [Header("Refs")]
    [SerializeField] private BattleManager battleManager;
    [SerializeField] private ReelSpinSystem reelSpinSystem;
    [SerializeField] private AudioSource reelcraftAudioSource;
    [SerializeField] private AudioClip reelcraftActivateClip;


    [Header("Debug")]
    [SerializeField] private bool logFlow = true;

    private bool[] _usedThisBattle;
    private int _cachedPartyCount;

    /// <summary>
    /// Fired when a hero successfully consumes their once-per-battle Reelcraft.
    /// For Measured Bash / Twofold Shadow this fires immediately when pressed.
    /// For Arcane Transmutation this fires after the icon click applies.
    /// </summary>
    public event Action<int> OnReelcraftUsed;

    // Mage transmute selection state
    private bool _transmuteSelecting;
    private int _transmutePartyIndex = -1;
    private ReelSymbolSO _magicSymbol;

    [Header("Input")]
    [Tooltip("Optional: camera used for reelcraft click selection. If null, uses Camera.main, then any camera found in scene.")]
    [SerializeField] private Camera selectionCamera;

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
        {
            ResetForBattle();
            ClearAllReelcraftBattleOverrides();
        }

        if (state == BattleManager.BattleState.BattleEnd)
        {
            // Revert all temporary reel edits (transmutes + doubles) at battle end.
            ClearAllReelcraftBattleOverrides();
        }
    }

    private void Update()
    {
        if (!_transmuteSelecting) return;
        if (reelSpinSystem == null) return;
        if (!reelSpinSystem.InReelPhase) { CancelTransmuteSelection(); return; }

        // Mouse click selection. IMPORTANT:
        // We *do not* early-return when over UI, because many UI setups have large RaycastTargets
        // that would otherwise block clicks on the 3D reels behind them.
        // Instead: try a physics raycast first; only if we hit nothing do we honor "pointer over UI".
        if (!Input.GetMouseButtonDown(0)) return;

        Camera cam = selectionCamera;
        if (cam == null) cam = Camera.main;
        if (cam == null) cam = FindFirstObjectByType<Camera>();
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        bool hitSomething = Physics.Raycast(ray, out RaycastHit hit, 200f);
        if (!hitSomething)
        {
            if (UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                return;
            return;
        }

        var target = hit.collider != null ? hit.collider.GetComponentInParent<Reel3DQuadClickTarget>() : null;
        if (target == null || target.Column == null) return;

        var column = target.Column;
        int qi = target.QuadIndex;

        // Only allow clicking the currently-landed (midrow) symbol on each reel.
        if (reelSpinSystem != null && reelSpinSystem.MidrowPlane != null)
        {
            int midQi;
            column.GetMidrowSymbolByIntersection(reelSpinSystem.MidrowPlane, out midQi);
            if (midQi >= 0 && qi != midQi)
                return;
        }

        ReelSymbolSO currentSym = column.GetSymbolOnQuad(qi);

        if (!CanTransmuteSymbol(currentSym))
            return;

        // Apply: shake the reel, then swap that quad to magic.
        StartCoroutine(ApplyTransmuteRoutine(column, qi));
    }

    private bool CanTransmuteSymbol(ReelSymbolSO sym)
    {
        // Allow NULL -> MAGIC
        if (sym == null) return true;
        if (reelSpinSystem == null) return false;

        if (reelSpinSystem.TryMapSymbolPublic(sym, out ReelSpinSystem.ResourceType rt, out _))
        {
            // Allow ATK/DEF/WILD/NULL, but not MAGIC -> MAGIC
            return rt != ReelSpinSystem.ResourceType.Magic;
        }

        // Unmapped symbols are treated like NULL (also transmutable)
        return true;
    }

    private System.Collections.IEnumerator ApplyTransmuteRoutine(Reel3DColumn column, int quadIndex)
    {
        // Prevent double-click spam
        _transmuteSelecting = false;
        ShowTransmuteGlow(false);

        if (column != null)
            yield return column.ShakeRoutine(0.12f, 6f);

        if (_magicSymbol == null && reelSpinSystem != null)
            _magicSymbol = reelSpinSystem.GetDefaultMagicSymbol();

        if (_magicSymbol == null || column == null)
        {
            if (logFlow) Debug.LogWarning("[Reelcraft] Transmute failed: missing magic symbol or column.", this);
            yield break;
        }

        bool ok = column.SetQuadTemporarilyTransmutedTo(_magicSymbol, quadIndex);
        if (!ok)
        {
            if (logFlow) Debug.LogWarning($"[Reelcraft] Transmute failed. quadIndex={quadIndex}", this);
            yield break;
        }

        // Recompute pending based on current landed symbols/multipliers.
        // (deltaSteps=0 -> no movement, just re-read + recalc)
        int idx = reelSpinSystem.FindReelIndexForColumn(column);
        if (idx >= 0) reelSpinSystem.TryNudgeReel(idx, 0);
        else
        {
            // Fallback: refresh first three
            reelSpinSystem.TryNudgeReel(0, 0);
            reelSpinSystem.TryNudgeReel(1, 0);
            reelSpinSystem.TryNudgeReel(2, 0);
        }

        MarkUsed(_transmutePartyIndex);

        if (logFlow)
            Debug.Log($"[Reelcraft] Arcane Transmutation used. partyIndex={_transmutePartyIndex} quadIndex={quadIndex}", this);

        _transmutePartyIndex = -1;
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
        if (_usedThisBattle[partyIndex])
            return;

        _usedThisBattle[partyIndex] = true;
        try
        {
            OnReelcraftUsed?.Invoke(partyIndex);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex, this);
        }
    }

    // ---------------- Reelcrafts ----------------

    public bool TryMeasuredBash(int partyIndex, int deltaSteps)
    {
        if (!CanUse(partyIndex)) return false;
        if (reelSpinSystem == null) return false;

        // Mark as used immediately so UI disables right away.
        // If the routine fails for any reason, we don't refund the use.
        MarkUsed(partyIndex);

        StartCoroutine(MeasuredBashRoutine(partyIndex, deltaSteps));
        return true;
    }

    private System.Collections.IEnumerator MeasuredBashRoutine(int partyIndex, int deltaSteps)
    {
        var entry = (reelSpinSystem != null) ? reelSpinSystem.GetReelEntryAt(partyIndex) : null;
        if (entry == null || entry.reel3d == null)
            yield break;

        yield return entry.reel3d.ShakeRoutine(0.12f, 6f);

        bool ok = reelSpinSystem.TryNudgeReel(partyIndex, deltaSteps);
        if (!ok) yield break;
        if (logFlow)
            Debug.Log($"[Reelcraft] Measured Bash used. partyIndex={partyIndex} deltaSteps={deltaSteps}", this);
    }

    /// <summary>
    /// Mage: start transmute selection (one click on a glowing icon to transmute it).
    /// </summary>
    public bool BeginArcaneTransmutationSelect(int partyIndex)
    {
        if (!CanUse(partyIndex)) return false;
        if (reelSpinSystem == null) return false;

        _magicSymbol = reelSpinSystem.GetDefaultMagicSymbol();
        if (_magicSymbol == null)
        {
            if (logFlow) Debug.LogWarning("[Reelcraft] No MAGIC symbol found in symbolToResourceMap.", this);
            return false;
        }

        _transmuteSelecting = true;
        _transmutePartyIndex = partyIndex;
        ShowTransmuteGlow(true);

        if (logFlow)
            Debug.Log($"[Reelcraft] Arcane Transmutation selecting... partyIndex={partyIndex}", this);
        return true;
    }

    public bool TryTwofoldShadow(int partyIndex)
    {
        if (!CanUse(partyIndex)) return false;
        if (reelSpinSystem == null) return false;

        var entry = reelSpinSystem.GetReelEntryAt(partyIndex);
        if (entry == null || entry.reel3d == null)
            return false;

        int qi;
        ReelSymbolSO sym = entry.reel3d.GetMidrowSymbolByIntersection(reelSpinSystem.MidrowPlane, out qi);
        if (qi < 0)
            return false;

        // Mark used immediately so UI disables right away.
        MarkUsed(partyIndex);

        StartCoroutine(TwofoldShadowRoutine(partyIndex, entry.reel3d, qi, sym));
        return true;
    }

    private System.Collections.IEnumerator TwofoldShadowRoutine(int partyIndex, Reel3DColumn column, int quadIndex, ReelSymbolSO sym)
    {
        if (column == null) yield break;

        // Shake the selected icon (matches the "upgrade" feedback feel, but scoped to the icon)
        yield return column.ShakeIconRoutine(quadIndex);

        // Poof of dense smoke to obscure the change
        column.SpawnTwofoldShadowSmoke(quadIndex);
        column.SetFrontQuadVisible(quadIndex, false);
        yield return new WaitForSeconds(0.08f);
        column.SetFrontQuadVisible(quadIndex, true);

        bool ok = column.MarkQuadDoubled(quadIndex, enableShadowVisual: true);
        if (!ok) yield break;

        // Force refresh / recalculation
        if (reelSpinSystem != null)
            reelSpinSystem.TryNudgeReel(partyIndex, 0);

        if (reelcraftAudioSource != null && reelcraftActivateClip != null)
        {
            reelcraftAudioSource.PlayOneShot(reelcraftActivateClip);
        }
        if (logFlow)
            Debug.Log($"[Reelcraft] Twofold Shadow used. partyIndex={partyIndex} quadIndex={quadIndex} sym={(sym != null ? sym.name : "<null>")}", this);
    }

    private void CancelTransmuteSelection()
    {
        if (!_transmuteSelecting) return;
        _transmuteSelecting = false;
        _transmutePartyIndex = -1;
        ShowTransmuteGlow(false);
    }

    private void ShowTransmuteGlow(bool on)
    {
        if (reelSpinSystem == null) return;
        var three = reelSpinSystem.GetFirstThree3DReelsPublic();
        foreach (var e in three)
        {
            if (e?.reel3d == null) continue;
            if (on) e.reel3d.SetGlowForTransmutableMidrow(reelSpinSystem.MidrowPlane, CanTransmuteSymbol);
            else e.reel3d.ClearAllGlow();
        }
    }

    private void ClearAllReelcraftBattleOverrides()
    {
        CancelTransmuteSelection();

        if (reelSpinSystem == null) return;
        var three = reelSpinSystem.GetFirstThree3DReelsPublic();
        foreach (var e in three)
        {
            if (e?.reel3d == null) continue;
            e.reel3d.RestoreAllTemporaryTransmutes();
            e.reel3d.ClearAllDoubles();
        }
    }
}
