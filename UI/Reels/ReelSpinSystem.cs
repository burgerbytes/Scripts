using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UI.Reels;

public class ReelSpinSystem : MonoBehaviour
{
    [Serializable]
    public class ReelEntry
    {
        public string reelId;

        [Tooltip("Optional fallback strip if party assignment fails.")]
        public ReelStripSO strip;

        public ReelColumnUI ui;
        public Reel3DColumn reel3d;

        [Header("UI")]
        [Tooltip("Image on the pick-ally button that shows which hero this reel belongs to.")]
        public Image pickAllyPortraitImage;

        [Header("Spin Tuning (Per Reel)")]
        [Tooltip("If enabled, overrides this reel's 3D spin speed (degrees/second).")]
        public bool overrideSpinSpeed = false;

        [Tooltip("Spin speed in degrees/second when overrideSpinSpeed is enabled.")]
        public float spinDegreesPerSecond = 720f;

        [Tooltip("If enabled, overrides this reel's minimum spin duration (seconds). This effectively controls how long the reel spins for.")]
        public bool overrideMinSpinDuration = false;

        [Tooltip("Minimum spin duration (seconds) when overrideMinSpinDuration is enabled.")]
        public float minSpinDurationSeconds = 1.5f;

        [Header("Spin SFX (Per Reel)")]
        [Tooltip("AudioSource used for THIS reel's spin loop SFX. If null, ReelSpinSystem will try to create one on the reel's 3D GameObject.")]
        public AudioSource spinSfxSource;

        [Tooltip("Optional clip override for THIS reel. If null, ReelSpinSystem will fall back to the global spinSfxClip / source clip.")]
        public AudioClip spinSfxClip;

        [Range(0f, 1f)]
        [Tooltip("Volume for THIS reel's spin SFX.")]
        public float spinSfxVolume = 0.7f;


        [Range(0f, 1f)]
        [Tooltip("Master volume multiplier for ALL SFX on THIS reel (applied on top of per-SFX volume).")]
        public float reelSfxVolume = 1f;

        [Tooltip("If true, the spin SFX will loop and be stopped automatically when the reel stops.")]
        public bool loopSpinSfx = true;

        [Tooltip("If true, randomizes pitch each spin for a little variety.")]
        public bool randomizeSpinPitch = false;

        [Tooltip("Pitch range used when randomizeSpinPitch is enabled.")]
        public Vector2 spinPitchRange = new Vector2(0.95f, 1.05f);

        [Header("Spin SFX Pitch Scaling")]
        [Tooltip("If true, the spin SFX pitch will scale with reel speed multipliers (e.g., combo/momentum spins).")]
        public bool scaleSpinPitchWithSpeed = true;

        [Tooltip("Base pitch used before applying speed scaling. If randomizeSpinPitch is enabled, this is applied after randomization.")]
        public float baseSpinPitch = 1f;

        [Tooltip("Maximum pitch allowed after scaling (prevents extreme chipmunking on long combos).")]
        public float maxScaledSpinPitch = 2.5f;
    }

    [Serializable]
    public class SymbolResourceMapEntry
    {
        public ReelSymbolSO symbol;
        public ResourceType resourceType;

        [Tooltip("How much of the resource this symbol grants (e.g. DEF2 = 2).")]
        public int amount = 1;
    }

    public enum ResourceType { Attack, Defend, Magic, Wild, Null }

    [Header("Reels")]
    [SerializeField] private List<ReelEntry> reels = new List<ReelEntry>();
    [Serializable]
    public struct InstantSpinResult
    {
        public bool valid;
        public int reelIndex;
        public ReelSymbolSO symbol;
        public ResourceType resourceType;
        public int amount;      // base amount from symbol mapping
        public int multiplier;  // reel multiplier (e.g., Twofold Shadow)
        public int total;       // amount * multiplier
    }

    /// <summary>
    /// Last result produced by MomentumSpinAndInstantCollect (and other instant-spin helpers).
    /// This is primarily used by combo-style abilities that need to know what was rolled.
    /// </summary>
    public InstantSpinResult LastInstantSpinResult { get; private set; }



    [Header("Spin Control")]
    [SerializeField] private int spinsPerTurn = 3;

    [Tooltip("Main Spin button (calls TrySpin). Optional: if left null, ReelSpinSystem will try to auto-find a Button named 'SpinButton'.")]
    [SerializeField] private Button spinButton;

    [Tooltip("Cashout/Stop button (same as your Cashout button).")]
    [SerializeField] private Button stopSpinningButton;

    public enum PayoutMode { CashoutOnly, AutoPayoutOnSpin }

    [Header("Payout Mode")]
    [Tooltip("CashoutOnly: resources are only granted when the player presses Cashout. AutoPayoutOnSpin: each time a spin (or reelcraft edit) updates the landed symbols, the delta is immediately applied to the ResourcePool.")]
    [SerializeField] private PayoutMode payoutMode = PayoutMode.AutoPayoutOnSpin;

    [Tooltip("If true, cashing out will NOT disable/hide the 3D reel objects. This keeps the reels available for future mid-battle spins.")]
    [SerializeField] private bool keepReelsEnabledAfterCashout = true;

    [Tooltip("If true, cashing out will close the ReelShutterController (if assigned). Leave false if you want reels visible after cashout.")]
    [SerializeField] private bool closeShuttersOnCashout = false;

    [Header("Shutters / Post-Spin Space")]
    [Tooltip("Optional. If set, pressing Cashout/Stop will close shutters and disable spin/cashout + 3D reels.\n" +
             "This creates a temporary space below the reels for ability UI + stats panels.")]
    [SerializeField] private ReelShutterController shutterController;

    [Header("Spin Timing (3D)")]
    [Tooltip("If enabled, forces all 3D reels to spin for at least this many seconds.")]
    [SerializeField] private bool overrideMinSpinDuration3D = true;

    [Tooltip("Minimum time (seconds) the 3D reels must spin before they can stop.")]
    [SerializeField] private float minSpinDurationOverride3D = 1.5f;

    [Header("Spin SFX")]
    [Tooltip("AudioSource used to play the reel spin sound. If left null, we'll try GetComponent<AudioSource>().")]
    [SerializeField] private AudioSource spinSfxSource;

    [Tooltip("Optional clip override. If null, uses spinSfxSource.clip.")]
    [SerializeField] private AudioClip spinSfxClip;

    [Tooltip("Play the spin sound when a spin begins.")]
    [SerializeField] private bool playSpinSfx = true;


    [Header("Reel SFX Volume")]
    [Range(0f, 1f)]
    [Tooltip("Master volume multiplier applied to all reel-related SFX (spin loops, match stingers, etc.).")]
    [SerializeField] private float reelSfxMasterVolume = 1f;

    [Tooltip("Additional global multiplier applied ONLY to looping reel spin SFX volumes. Useful for balancing reels vs chimes.")]
    [SerializeField] private float spinLoopGlobalVolumeMultiplier = 0.35f;
    [Header("3-in-a-Row SFX")]
    [Tooltip("Play a special sound when the 3 landed midrow symbols match.")]
    [SerializeField] private bool playThreeMatchSfx = true;

    [Tooltip("AudioSource for the 3-in-a-row sound. If null, uses spinSfxSource.")]
    [SerializeField] private AudioSource threeMatchSfxSource;

    [Tooltip("Clip to play when 3-in-a-row happens.")]
    [SerializeField] private AudioClip threeMatchSfxClip;

    
    [Header("Midrow Stop Chime")]
    [Tooltip("If true, plays a chime when each reel stops and its midrow icon pops.")]
    [SerializeField] private bool playMidrowStopChime = true;

    [Tooltip("AudioSource used for the midrow stop chime. If null, ReelSpinSystem will create one on this GameObject.")]
    [SerializeField] private AudioSource midrowStopChimeSource;

    [Tooltip("Chime clip to play when a reel stops and the midrow token pops.")]
    [SerializeField] private AudioClip midrowStopChimeClip;

    [Range(0f, 1f)]
    [Tooltip("Volume for the midrow stop chime (multiplied by Reel SFX Master Volume).")]
    [SerializeField] private float midrowStopChimeVolume = 0.8f;

[Tooltip("Extra gain multiplier for the chime. Can exceed 1 to cut through other SFX without an AudioMixer.")]
    [SerializeField] private float midrowStopChimeGain = 2.0f;

    [Tooltip("If true, chime volume is also multiplied by Reel SFX Master Volume. If false, chime is independent of the reel master.")]
    [SerializeField] private bool chimeUsesReelSfxMasterVolume = false;

    [Tooltip("Starting pitch for the first reel's chime each spin.")]
    [SerializeField] private float midrowStopChimePitchStart = 1f;

    [Tooltip("Pitch increase per reel stopped within a single spin.")]
    [SerializeField] private float midrowStopChimePitchStep = 0.08f;

    [Tooltip("Maximum pitch cap for the chime.")]
    [SerializeField] private float midrowStopChimePitchMax = 1.4f;

    [Header("Midrow Stop Chime - Null Pitch")]
    [Tooltip("When a reel lands on a NULL symbol, multiply the chime pitch by this value (lower = deeper).")]
    [SerializeField] private float midrowStopChimeNullPitchMultiplier = 0.82f;


[Header("Spin Feedback FX")]
    [Tooltip("If true, when the reels stop the midrow icon on each reel will 'pop' (scale punch).")]
    [SerializeField] private bool popMidrowOnStop = true;

    [Tooltip("Scale multiplier for the midrow icon pop.")]
    [SerializeField] private float midrowPopScale = 1.18f;

    [Tooltip("Duration (seconds) of the midrow icon pop.")]
    [SerializeField] private float midrowPopDuration = 0.12f;



    [Tooltip("If true, resource gains will be applied (and popups will spawn) asynchronously as each reel stops, rather than waiting for all reels to finish.")]
    [SerializeField] private bool asyncResourcePopupsPerReelStop = true;

    [Tooltip("If true, 3-of-a-kind will also pop the whole reels for a celebratory effect.")]
    [SerializeField] private bool popReelsOnThreeOfAKind = true;

    [Tooltip("Scale multiplier for the whole-reel pop on 3-of-a-kind.")]
    [SerializeField] private float threeOfAKindReelPopScale = 1.06f;

    [Tooltip("Duration (seconds) for the whole-reel pop on 3-of-a-kind.")]
    [SerializeField] private float threeOfAKindReelPopDuration = 0.16f;

    [Tooltip("Optional extra shake (local position jitter) on 3-of-a-kind.")]
    [SerializeField] private bool shakeReelsOnThreeOfAKind = true;

    [SerializeField] private float threeOfAKindShakeDuration = 0.14f;
    [SerializeField] private float threeOfAKindShakeMagnitude = 10f;


    [Header("Reward Reel Mode (Post-Battle)")]
    [Tooltip("Optional default reward config. BattleManager can override by calling EnterRewardMode(...)")]
    [SerializeField] private RewardReelConfigSO defaultRewardConfig;

    [Header("Symbol -> Resource Mapping")]
    [SerializeField] private List<SymbolResourceMapEntry> symbolToResourceMap = new List<SymbolResourceMapEntry>();

    [Header("Heal VFX Spawner")]
    [SerializeField] private SlotsAndSorcery.VFX.HealVFXSpawner healVfxSpawner;

    [Header("Debug / Randomness")]
    [SerializeField] private bool useFixedSeed = false;
    [SerializeField] private int fixedSeed = 12345;

    [Header("3D Mode")]
    [SerializeField] private bool use3DPostSelectMode = true;

    [Tooltip("3D reels will spin at least this many full rotations before landing.")]
    [SerializeField] private int minFullRotations3D = 1;

    [Tooltip("Reference object that passes through the midrow (thin collider recommended).")]
    [SerializeField] private GameObject midrowPlane;

    [Tooltip("Log midrow symbols for 3D reels each time we spin.")]
    [SerializeField] private bool log3DMidRowSymbolsEachSpin = true;

    [Tooltip("Log passive bridge events (symbol landed notifications).")]
    [SerializeField] private bool logPassiveBridge = true;

    [Tooltip("Debug: when a 3D spin lands, log the symbols directly above and below the landed (midrow) symbol for each reel.")]
    [SerializeField] private bool log3DAdjacentSymbolsEachSpin = true;

    public event Action<int> OnSpinsRemainingChanged;

    /// <summary>
    /// True while the player is in the "reel phase" of their turn (spinning / choosing when to cash out).
    /// Used by UI systems to hide ability/status panels during reel interaction.
    /// </summary>
    public bool InReelPhase { get; private set; }

    public event Action<bool> OnReelPhaseChanged;

    /// <summary>
    /// Fired when a non-reward-mode spin lands. Provides the landed symbols and a computed summary.
    /// </summary>
    public event Action<SpinLandedInfo> OnSpinLanded;

    /// <summary>
    /// Fired when the current landed symbols for the ongoing reel phase are updated
    /// (initial spin land, or Reelcraft modifications like nudges/transmutations).
    /// </summary>
    public event Action<SpinLandedInfo> OnCurrentLandedChanged;

    /// <summary>
    /// Fired whenever the pending payout totals change.
    /// Useful for UI that previews what will be collected on cashout.
    /// </summary>
    public event Action<int, int, int, int> OnPendingPayoutChanged;

    /// <summary>
    /// Fired whenever corrosion count changes on a reel.
    /// Args: (reelIndex, corrodedTokenCount).
    /// BattleManager can listen to refresh status icons above heroes.
    /// </summary>
    public event Action<int, int> OnCorrosionChanged;

    // --- Cashout hooks (BattleManager can inject passive logic without modifying core spin flow elsewhere) ---
    [Tooltip("If true, StopSpinningAndCollect will attempt to apply the 'Substitution' (NULL -> WILD) mutation before collecting payout.")]
    [SerializeField] private bool enableSubstitutionOnCashout = true;

    /// <summary>
    /// Optional gate per reel index (0..2) to decide whether Substitution may apply.
    /// BattleManager should set this each encounter/turn based on hero unlocks.
    /// If null, Substitution applies to any NULL midrow.
    /// </summary>
    public Func<int, bool> CanApplySubstitutionForReelIndex;

    // Battle-only: Substitution should only run on the first cashout of a battle.
    private bool _substitutionAttemptedThisBattle = false;

    
    // Cashout-press gate: Substitution should only be available BEFORE the first cashout press of the battle.
    private bool _cashoutPressedThisBattle = false;
    private int _cashoutPressCountThisBattle = 0;
    private int _substitutionTriggerCountThisBattle = 0;
    // ---------------- Corrosion (battle-only) ----------------
    // Tracks which quad indices on each reel are corroded (per battle).
    private readonly Dictionary<int, HashSet<int>> _corrodedQuadsByReelIndex = new Dictionary<int, HashSet<int>>();

    [Header("Debug")]
    [SerializeField] private bool logCorrosion = true;
    [SerializeField] private bool logCorrosionConversionProbe = true;


    [SerializeField] private bool logFlow = false;

/// <summary>Called by BattleManager at the start of each battle.</summary>
    public void ResetBattleSubstitutionState()
    {
        _substitutionAttemptedThisBattle = false;
        _cashoutPressedThisBattle = false;
        _cashoutPressCountThisBattle = 0;
        _substitutionTriggerCountThisBattle = 0;
        Debug.Log($"[ReelSpinSystem][SubstitutionDebug] ResetBattleSubstitutionState -> cashoutPressCount=0 substitutionTriggerCount=0", this);
    }

    /// <summary>Called by BattleManager at the start of each battle.</summary>
    public void ResetBattleCorrosionState()
    {
        _corrodedQuadsByReelIndex.Clear();

        // Clear visual tint on 3D reels (if present).
        if (reels != null)
        {
            for (int i = 0; i < reels.Count; i++)
            {
                var e = reels[i];
                if (e == null || e.reel3d == null) continue;
                e.reel3d.SetCorrodedQuadIndices(null);
            }
        }

        if (logCorrosion)
            Debug.Log("[ReelSpinSystem][Corrosion] ResetBattleCorrosionState -> cleared all corroded indices.", this);
    }

    /// <summary>
    /// Called by BattleManager when an encounter ends (victory/defeat) to undo any per-battle
    /// symbol swaps (like corrosion converting a landed token to NULL). This ensures the reel
    /// returns to its original strip for post-battle panels and the next encounter.
    /// </summary>
    public void RestoreReelsAfterBattle()
    {
        // Undo any per-battle symbol replacements by rebuilding from the current strip.
        if (reels != null)
        {
            for (int i = 0; i < reels.Count; i++)
            {
                var e = reels[i];
                if (e == null) continue;

                // Restore 3D reel symbols back to strip defaults (clears ReplaceSymbolAtQuadIndex state).
                if (e.reel3d != null && e.strip != null)
                    e.reel3d.SetStrip(e.strip, rebuildNow: true);

                // Clear corrosion tint, regardless.
                if (e.reel3d != null)
                    e.reel3d.SetCorrodedQuadIndices(null);

                // Optional: refresh 2D UI strip view too (safe even if already correct).
                if (e.ui != null && e.strip != null)
                    e.ui.SetStrip(e.strip, startIndex: 0, refreshNow: true);
            }
        }

        // Clear corrosion bookkeeping.
        _corrodedQuadsByReelIndex.Clear();

        if (logCorrosion)
            Debug.Log("[ReelSpinSystem][Corrosion] RestoreReelsAfterBattle -> rebuilt reels from strip + cleared corrosion state.", this);
    }

    public int GetCorrosionCountForReel(int reelIndex)
    {
        if (_corrodedQuadsByReelIndex.TryGetValue(reelIndex, out var set) && set != null)
            return set.Count;
        return 0;
    }

    private bool IsReelQuadCorroded(int reelIndex, int quadIndex)
{
    // We store corroded STRIP token indices per reel.
    // A physical quad maps to a strip token via (quadIndex % stripCount).
    if (reels == null || reelIndex < 0 || reelIndex >= reels.Count)
        return false;

    var entry = reels[reelIndex];
    if (entry == null || entry.strip == null || entry.strip.symbols == null || entry.strip.symbols.Count == 0)
        return false;

    int stripCount = entry.strip.symbols.Count;
    int tokenIndex = Mod(quadIndex, stripCount);

    if (_corrodedQuadsByReelIndex.TryGetValue(reelIndex, out var set) && set != null)
        return set.Contains(tokenIndex);

    return false;
}

    private bool SymbolGrantsResources(ReelSymbolSO sym)
    {
        if (sym == null) return false;
        if (TryMapSymbol(sym, out ResourceType rt, out int amt))
            return rt != ResourceType.Null && amt > 0;
        return false;
    }

    private ReelSymbolSO ApplyCorrosionIfNeeded(int reelIndex, int quadIndex, ReelSymbolSO sym)
{
    // UPDATED CORROSION BEHAVIOR:
    // - Corrosion does NOT prevent payout on the landing spin.
    // - If a corroded token lands on midrow, it pays out normally, THEN becomes NULL for future spins.
    // So we do NOT change the symbol here.
    return sym;
}

    /// <summary>
    /// Applies corrosion to a random quad index on the given reel index (per battle). Returns true if a NEW index was corroded.
    /// </summary>
    public bool ApplyCorrosionToReel(int reelIndex)
    {
        return ApplyCorrosionToReel(reelIndex, 1);
    }

    /// <summary>
    /// Apply corrosion to multiple unique icon indices on this reel.
    /// Returns true if at least one new index was corroded.
    /// </summary>
    public bool ApplyCorrosionToReel(int reelIndex, int iconCount)
{
    if (reels == null || reelIndex < 0 || reelIndex >= reels.Count)
        return false;

    var entry = reels[reelIndex];
    if (entry == null || entry.reel3d == null)
        return false;

    ReelStripSO strip = entry.strip;
    if (strip == null || strip.symbols == null || strip.symbols.Count == 0)
        return false;

    int stripCount = strip.symbols.Count;
    int quadCount = Mathf.Max(1, entry.reel3d.QuadCount);

    if (!_corrodedQuadsByReelIndex.TryGetValue(reelIndex, out var tokenSet) || tokenSet == null)
    {
        tokenSet = new HashSet<int>();
        _corrodedQuadsByReelIndex[reelIndex] = tokenSet;
    }

    iconCount = Mathf.Max(1, iconCount);

    // Build eligible token indices: not already corroded AND not currently NULL.
    List<int> eligible = new List<int>(stripCount);
    for (int ti = 0; ti < stripCount; ti++)
    {
        if (tokenSet.Contains(ti))
            continue;

        // Representative quad for this token
        int repQuad = Mod(ti, quadCount);
        ReelSymbolSO sym = entry.reel3d.GetSymbolOnQuad(repQuad);

        // Skip NULL tokens (either originally NULL or previously converted)
        if (sym != null && TryMapSymbol(sym, out ResourceType rt, out int amt) && rt == ResourceType.Null)
            continue;

        if (_rewardConfig != null && _rewardConfig.nullSymbol != null && sym == _rewardConfig.nullSymbol)
            continue;

        eligible.Add(ti);
    }

    if (eligible.Count == 0)
        return false;

    bool anyAdded = false;
    int toApply = Mathf.Min(iconCount, eligible.Count);

    for (int k = 0; k < toApply; k++)
    {
        int pick = UnityEngine.Random.Range(0, eligible.Count);
        int tokenIndex = eligible[pick];
        eligible.RemoveAt(pick);

        if (tokenSet.Add(tokenIndex))
        {
            anyAdded = true;
            if (logCorrosion)
            {
                string id = !string.IsNullOrEmpty(entry.reelId) ? entry.reelId : $"slot{reelIndex}";
                Debug.Log($"[ReelSpinSystem][Corrosion] ApplyCorrosionToReel reel={id} tokenIndex={tokenIndex} totalTokensCorroded={tokenSet.Count}", this);
            }
        }
    }

    UpdateCorrosionVisualsForReel(reelIndex);
    return anyAdded;
}

    /// <summary>
    /// Rebuilds and pushes corroded PHYSICAL quad indices to the 3D reel based on stored STRIP token indices.
    /// (Example: 6 tokens, 12 quads => each token tints 2 quads.)
    /// Also fires OnCorrosionChanged(reelIndex, tokenCount).
    /// </summary>
    private void UpdateCorrosionVisualsForReel(int reelIndex)
    {
        if (reels == null || reelIndex < 0 || reelIndex >= reels.Count)
            return;

        var entry = reels[reelIndex];
        if (entry == null || entry.reel3d == null || entry.strip == null || entry.strip.symbols == null || entry.strip.symbols.Count == 0)
            return;

        int stripCount = entry.strip.symbols.Count;
        int quadCount = Mathf.Max(1, entry.reel3d.QuadCount);

        if (!_corrodedQuadsByReelIndex.TryGetValue(reelIndex, out var tokenSet) || tokenSet == null || tokenSet.Count == 0)
        {
            entry.reel3d.SetCorrodedQuadIndices(null);
            OnCorrosionChanged?.Invoke(reelIndex, 0);
            return;
        }

        HashSet<int> quadSet = new HashSet<int>();
        for (int qi = 0; qi < quadCount; qi++)
        {
            int ti = Mod(qi, stripCount);
            if (tokenSet.Contains(ti))
                quadSet.Add(qi);
        }

        entry.reel3d.SetCorrodedQuadIndices(quadSet);
        OnCorrosionChanged?.Invoke(reelIndex, tokenSet.Count);
    }


/// <summary>
/// Resolves the project's NULL symbol. In combat mode, _rewardConfig may be null,
/// so we fall back to defaultRewardConfig.
/// </summary>
private ReelSymbolSO ResolveNullSymbol()
{
    // Active reward config (e.g., reward reel mode)
    if (_rewardConfig != null && _rewardConfig.nullSymbol != null)
        return _rewardConfig.nullSymbol;

    // Default config used in battle/combat mode
    if (defaultRewardConfig != null && defaultRewardConfig.nullSymbol != null)
        return defaultRewardConfig.nullSymbol;

    return null;
}

    /// <summary>
    /// After payout is computed for a spin, if the landed quad maps to a corroded token,
    /// convert that token into the NULL symbol for future spins.
    /// (Token still paid out for THIS spin.)
    /// </summary>
    
    /// <summary>
    /// After payout is computed for a spin, if the landed quad maps to a corroded token,
    /// convert that token into the NULL symbol for future spins.
    /// (Token still paid out for THIS spin.)
    /// </summary>
    private void ConvertCorrodedLandedTokenToNull(int reelIndex, int landedQuadIndex)
    {
        // Probe line: shows this method is actually being hit every spin.
        if (logCorrosionConversionProbe)
            Debug.Log($"[ReelSpinSystem][Corrosion][Probe] ConvertCheck reelIndex={reelIndex} landedQuadIndex={landedQuadIndex}", this);

        if (reels == null || reelIndex < 0 || reelIndex >= reels.Count)
        {
            if (logCorrosionConversionProbe)
                Debug.Log($"[ReelSpinSystem][Corrosion] Convert skipped: reels null or reelIndex out of range (reelIndex={reelIndex})", this);
            return;
        }

        var entry = reels[reelIndex];
        if (entry == null || entry.reel3d == null || entry.strip == null || entry.strip.symbols == null || entry.strip.symbols.Count == 0)
        {
            if (logCorrosionConversionProbe)
                Debug.Log($"[ReelSpinSystem][Corrosion] Convert skipped: missing entry/reel3d/strip for reelIndex={reelIndex}", this);
            return;
        }
int stripCount = entry.strip.symbols.Count;
        int quadCount = Mathf.Max(1, entry.reel3d.QuadCount);

        int tokenIndex = Mod(landedQuadIndex, stripCount);

        if (!_corrodedQuadsByReelIndex.TryGetValue(reelIndex, out var tokenSet) || tokenSet == null || tokenSet.Count == 0)
        {
            if (logCorrosionConversionProbe)
                Debug.Log($"[ReelSpinSystem][Corrosion] Convert skipped: no corroded tokens on reelIndex={reelIndex}", this);
            return;
        }

        if (logCorrosionConversionProbe)
            Debug.Log($"[ReelSpinSystem][Corrosion][Probe] reelIndex={reelIndex} tokenIndex={tokenIndex} corrodedCount={tokenSet.Count} isCorroded={tokenSet.Contains(tokenIndex)}", this);

        if (!tokenSet.Contains(tokenIndex))
        {
            // No conversion this spin (landed token wasn't one of the corroded tokens).
            return;
        }
// Landed token IS corroded -> convert this token index into NULL for future spins/moves.
ReelSymbolSO nullSym = ResolveNullSymbol();
if (nullSym == null)
{
    Debug.LogWarning("[ReelSpinSystem][Corrosion] Convert skipped: NULL symbol could not be resolved. Assign nullSymbol on defaultRewardConfig (and/or active reward config).", this);
    return;
}

ReelSymbolSO landedSym = entry.reel3d.GetSymbolOnQuad(landedQuadIndex);

if (logCorrosionConversionProbe)
    Debug.Log($"[ReelSpinSystem][Corrosion] CONVERT tokenIndex={tokenIndex} landedQuad={landedQuadIndex} reel={reelIndex} sym='{(landedSym != null ? landedSym.name : "<null>")}' -> NULL '{nullSym.name}'", this);

// Replace all quads that correspond to this token index.
for (int qi = 0; qi < quadCount; qi++)
{
    if (Mod(qi, stripCount) == tokenIndex)
    {
        entry.reel3d.ReplaceSymbolAtQuadIndex(qi, nullSym);
        if (logCorrosion)
            Debug.Log($"[ReelSpinSystem][Corrosion]   -> Replaced quad {qi} with NULL (tokenIndex={tokenIndex})", this);
    }
}



        // Remove corrosion marker (it is now permanently NULL).
        tokenSet.Remove(tokenIndex);

        UpdateCorrosionVisualsForReel(reelIndex);
    }






    [Serializable]
    public struct SpinLandedInfo
    {
        public List<ReelSymbolSO> symbols;
        public int attackCount;
        public int defendCount;
        public int magicCount;
        public int wildCount;

        /// <summary>True when all landed symbols map to Attack (e.g., 3 reels -> 3 Attacks).</summary>
        public bool IsTripleAttack => symbols != null && symbols.Count > 0 && attackCount == symbols.Count;
    }

    public int SpinsRemaining => spinsRemaining;

    /// <summary>
    /// True if we have a full 3-symbol landed set that Reelcraft can modify.
    /// (This is set after a spin lands and cleared on cashout / begin turn.)
    /// </summary>
    public bool HasCurrentLandedSymbols => _currentLandedSymbols != null && _currentLandedSymbols.Count >= 3
                                          && (_currentLandedMultipliers == null || _currentLandedMultipliers.Count >= 3);

    /// <summary>Read-only view of the most recent landed symbols for this reel phase.</summary>
    public IReadOnlyList<ReelSymbolSO> CurrentLandedSymbols => _currentLandedSymbols;

    /// <summary>Read-only view of the most recent landed multipliers for this reel phase (parallel to CurrentLandedSymbols).</summary>
    public IReadOnlyList<int> CurrentLandedMultipliers => _currentLandedMultipliers;

    /// <summary>
    /// Recomputes pending payout based on the currently-landed symbols/multipliers.
    /// Used when an external effect (e.g., Corrosion) changes the effective meaning of a landed symbol
    /// without requiring a new spin.
    /// </summary>
    private void RecalculatePendingFromCurrentLanded()
    {
        if (_currentLandedSymbols == null || _currentLandedSymbols.Count < 3)
            return;

        // Ensure multipliers list exists and is sized.
        if (_currentLandedMultipliers == null)
            _currentLandedMultipliers = new List<int> { 1, 1, 1 };

        while (_currentLandedMultipliers.Count < 3)
            _currentLandedMultipliers.Add(1);

        SetPendingFromSymbols(_currentLandedSymbols, _currentLandedMultipliers);
        // Notify any listeners that depend on the effective landed state.
        SpinLandedInfo info = BuildSpinLandedInfo(_currentLandedSymbols);
        if (logPassiveBridge)
            Debug.Log($"[ReelSpinSystem][PassiveBridge] RecalculatePendingFromCurrentLanded: symbols={(info.symbols != null ? info.symbols.Count : 0)} A={info.attackCount} D={info.defendCount} M={info.magicCount} W={info.wildCount}", this);

        OnCurrentLandedChanged?.Invoke(info);
    }

    // (duplicate event declaration removed)

    // State
    private bool spinning;
    private int spinsRemaining;

    // Per-spin stop order counter for midrow stop chime pitch stepping.
    private int _midrowStopChimeIndex;


    // Pending payout (current computed totals for the currently-landed symbols)
    // In CashoutOnly mode, these are collected on Cashout.
    // In AutoPayoutOnSpin mode, we apply the delta to ResourcePool immediately whenever these totals change.
    private int pendingA;
    private int pendingD;
    private int pendingM;
    private int pendingW;

    // Auto-payout tracking (combat only)
    private bool _autoPayoutAppliedForCurrentLanded = false;
    private int _autoPaidA;
    private int _autoPaidD;
    private int _autoPaidM;
    private int _autoPaidW;

    // Reelcraft integration: keep track of the last landed symbols so we can nudge/transform without re-spinning.
    private List<ReelSymbolSO> _currentLandedSymbols;
    private List<int> _currentLandedMultipliers;
    // Parallel to _currentLandedSymbols (stores the midrow quad index for each reel on the last resolve).
    private List<int> _currentLandedQuadIndices;

    public void GetPendingPayout(out int a, out int d, out int m, out int w)
    {
        a = pendingA;
        d = pendingD;
        m = pendingM;
        w = pendingW;
    }

    // Map cache (type + amount)
    private struct SymbolMapValue
    {
        public ResourceType type;
        public int amount;
    }

    private Dictionary<ReelSymbolSO, SymbolMapValue> _symbolMap;

    // Resource pool integration
    [SerializeField] private ResourcePool resourcePool;

    private Coroutine _threeDSpinRoutine;

    public bool IsIdle => !spinning;

    // Reward mode state
    private bool _rewardModeActive;
    private RewardReelConfigSO _rewardConfig;
    private HeroStats _rewardHero;
    private readonly List<ReelStripSO> _savedStrips = new List<ReelStripSO>();

    public bool IsRewardMode => _rewardModeActive;
    public bool IsSpinning => spinning;


    private void Awake()
    {
        spinsRemaining = spinsPerTurn;
        OnSpinsRemainingChanged?.Invoke(spinsRemaining);

        BuildSymbolMapCache();

        // Cashout mechanic removed: payouts happen on every spin and abilities are usable at all times.
        // If the Cashout button still exists in a prefab/scene, disable it to avoid accidental gating.
        if (stopSpinningButton != null)
            stopSpinningButton.gameObject.SetActive(false);

        // Optional: wire Spin button automatically.
        if (spinButton == null)
        {
            var allButtons = Resources.FindObjectsOfTypeAll<Button>();
            for (int i = 0; i < allButtons.Length; i++)
            {
                var b = allButtons[i];
                if (b == null) continue;
                if (b.gameObject == null) continue;
                if (!b.gameObject.scene.IsValid()) continue;
                if (b.gameObject.name == "SpinButton")
                {
                    spinButton = b;
                    break;
                }
            }
        }

        if (spinButton != null)
        {
            spinButton.onClick.RemoveListener(TrySpin);
            spinButton.onClick.AddListener(TrySpin);
        }

        if (shutterController == null)
            shutterController = FindFirstObjectByType<ReelShutterController>();

        if (resourcePool == null)
            resourcePool = ResourcePool.Instance;

        // Auto-find spin audio source if on same GO.
        if (spinSfxSource == null)
            spinSfxSource = GetComponent<AudioSource>();

        // Default 3-match source to spin source if not provided.
        if (threeMatchSfxSource == null)
            threeMatchSfxSource = spinSfxSource;
        // Ensure midrow stop chime source exists if chime is enabled.
        if (playMidrowStopChime && midrowStopChimeClip != null)
        {
            if (midrowStopChimeSource == null)
                midrowStopChimeSource = gameObject.AddComponent<AudioSource>();

            midrowStopChimeSource.playOnAwake = false;
            midrowStopChimeSource.spatialBlend = 0f; // 2D UI sound
            midrowStopChimeSource.loop = false;
        }
    }

    private void OnDestroy()
    {
        // Cashout mechanic removed (button disabled in Awake).

        if (spinButton != null)
            spinButton.onClick.RemoveListener(TrySpin);
    }

    public void BeginTurn()
    {
        // Spins are per-battle now. BattleManager calls BeginBattle() once at encounter start.
        // Do NOT reset spinsRemaining here.
        // Cashout mechanic removed.
        if (stopSpinningButton != null)
            stopSpinningButton.interactable = false;

        // Spin is enabled at the start of each turn if spins remain.
        if (spinButton != null)
            spinButton.interactable = (spinsRemaining > 0);

        // New reel phase -> clear any previous landed state.
        _currentLandedSymbols = null;
        _currentLandedMultipliers = null;
        _currentLandedQuadIndices = null;
        pendingA = pendingD = pendingM = pendingW = 0;
        ResetAutoPayoutTracking();
        OnCurrentLandedChanged?.Invoke(default);
        OnPendingPayoutChanged?.Invoke(pendingA, pendingD, pendingM, pendingW);
        SetReelPhase(true);

        // New turn = open shutters (reveal reels) and re-enable 3D reels.
        Set3DReelsActive(true);
        if (shutterController != null)
            shutterController.OpenShutters();
    }


    /// <summary>
    /// Called by BattleManager at the start of each battle/encounter.
    /// Resets spinsRemaining to the inspector value (spinsPerTurn). During the battle,
    /// mechanics can modify spinsRemaining via ModifySpinsRemaining().
    /// </summary>
    public void BeginBattle()
    {
        spinsRemaining = Mathf.Max(0, spinsPerTurn);
        OnSpinsRemainingChanged?.Invoke(spinsRemaining);

        // Clear any previous landed state / pending payout.
        _currentLandedSymbols = null;
        _currentLandedMultipliers = null;
        _currentLandedQuadIndices = null;
        pendingA = pendingD = pendingM = pendingW = 0;
        ResetAutoPayoutTracking();
        OnCurrentLandedChanged?.Invoke(default);
        OnPendingPayoutChanged?.Invoke(pendingA, pendingD, pendingM, pendingW);

        // Battle start = reel phase begins.
        SetReelPhase(true);

        // Reveal reels.
        Set3DReelsActive(true);
        if (shutterController != null)
            shutterController.OpenShutters();
    }

    /// <summary>
    /// Modify remaining spins mid-battle (bonuses, penalties, items, etc.). Pass negative to remove spins.
    /// </summary>
    public void ModifySpinsRemaining(int delta)
    {
        // Reward mode uses gold-limited spins; don't interfere.
        if (_rewardModeActive) return;

        spinsRemaining = Mathf.Max(0, spinsRemaining + delta);
        OnSpinsRemainingChanged?.Invoke(spinsRemaining);

        if (spinButton != null)
            spinButton.interactable = (spinsRemaining > 0);
    }

    private void SetReelPhase(bool value)
    {
        if (InReelPhase == value) return;
        InReelPhase = value;
        OnReelPhaseChanged?.Invoke(InReelPhase);
    }

    /// <summary>
    /// Called by BattleManager after it instantiates the ally party.
    /// Assigns each reel's strip + pick-ally portrait from the corresponding hero prefab instance.
    /// Mapping is index-based: party[0] -> reels[0], party[1] -> reels[1], etc.
    /// </summary>
    public void ConfigureFromParty(IReadOnlyList<HeroStats> party)
    {
        if (party == null) return;
        if (reels == null || reels.Count == 0) return;

        int count = Mathf.Min(reels.Count, party.Count);

        for (int i = 0; i < count; i++)
        {
            var entry = reels[i];
            var hero = party[i];
            if (entry == null || hero == null) continue;

            // Strip from hero prefab instance
            ReelStripSO heroStrip = hero.ReelStrip;
            if (heroStrip != null)
            {
                entry.strip = heroStrip;

                if (entry.ui != null)
                    entry.ui.SetStrip(heroStrip, startIndex: 0, refreshNow: true);

                if (entry.reel3d != null)
                    entry.reel3d.SetStrip(heroStrip, rebuildNow: true);
            }

            // Portrait from hero prefab instance
            if (entry.pickAllyPortraitImage != null)
            {
                entry.pickAllyPortraitImage.sprite = hero.Portrait;
                entry.pickAllyPortraitImage.enabled = (hero.Portrait != null);
                entry.pickAllyPortraitImage.preserveAspect = true;
            }
        }
    }

    // ---------------- Reward Reel Mode ----------------

    /// <summary>
    /// Temporarily swaps the reels to a reward-strip and changes payout logic:
    /// - Each spin costs gold (config.goldCostPerSpin) from the provided hero.
    /// - Only pays out when all 3 midrow symbols match AND map to a reward payout.
    /// </summary>
    public void EnterRewardMode(RewardReelConfigSO config, HeroStats goldSource)
    {
        if (config == null) config = defaultRewardConfig;
        if (config == null)
        {
            Debug.LogWarning("[ReelSpinSystem] EnterRewardMode called but no RewardReelConfigSO provided.", this);
            return;
        }

        _rewardModeActive = true;
        _rewardConfig = config;
        _rewardHero = goldSource;

        // Save current strips so we can restore later.
        _savedStrips.Clear();
        for (int i = 0; i < reels.Count; i++)
            _savedStrips.Add(reels[i] != null ? reels[i].strip : null);

        // Apply reward strip to all reels that exist.
        for (int i = 0; i < reels.Count; i++)
        {
            var entry = reels[i];
            if (entry == null) continue;

            entry.strip = config.rewardStrip;

            if (entry.ui != null && config.rewardStrip != null)
                entry.ui.SetStrip(config.rewardStrip, startIndex: 0, refreshNow: true);

            if (entry.reel3d != null && config.rewardStrip != null)
                entry.reel3d.SetStrip(config.rewardStrip, rebuildNow: true);
        }

        // In reward mode, spins are limited by gold, not turn count.
        spinsRemaining = int.MaxValue;
        OnSpinsRemainingChanged?.Invoke(spinsRemaining);
    }

    /// <summary>
    /// Restores the previous reel strips (or re-configures from party if provided).
    /// </summary>
    public void ExitRewardMode(IReadOnlyList<HeroStats> partyToRestore = null)
    {
        _rewardModeActive = false;
        _rewardConfig = null;
        _rewardHero = null;

        // Restore from party if provided (preferred; also restores portraits)
        if (partyToRestore != null)
        {
            ConfigureFromParty(partyToRestore);
        }
        else
        {
            // Restore saved strips
            for (int i = 0; i < reels.Count && i < _savedStrips.Count; i++)
            {
                var entry = reels[i];
                if (entry == null) continue;

                entry.strip = _savedStrips[i];

                if (entry.ui != null && entry.strip != null)
                    entry.ui.SetStrip(entry.strip, startIndex: 0, refreshNow: true);

                if (entry.reel3d != null && entry.strip != null)
                    entry.reel3d.SetStrip(entry.strip, rebuildNow: true);
            }
        }

        _savedStrips.Clear();
    }

    // Compatibility
    public void SpinOnce() => SpinAll();
    public void SpinAll() => TrySpin();

    private void BuildSymbolMapCache()
    {
        _symbolMap = new Dictionary<ReelSymbolSO, SymbolMapValue>();

        if (symbolToResourceMap != null)
        {
            foreach (var e in symbolToResourceMap)
            {
                if (e == null || e.symbol == null) continue;

                int amt = Mathf.Max(1, e.amount);
                _symbolMap[e.symbol] = new SymbolMapValue
                {
                    type = e.resourceType,
                    amount = amt
                };
            }
        }

        // Ensure Null symbol is mapped (used by corrosion and other systems).
        if (_rewardConfig != null && _rewardConfig.nullSymbol != null)
        {
            _symbolMap[_rewardConfig.nullSymbol] = new SymbolMapValue
            {
                type = ResourceType.Null,
                amount = 0
            };
        }
    }

    // Backward-compatible: old callers that only care about type
    private bool TryMapSymbol(ReelSymbolSO sym, out ResourceType rt)
    {
        rt = ResourceType.Attack;
        if (sym == null) return false;
        if (_symbolMap == null) BuildSymbolMapCache();

        if (_symbolMap.TryGetValue(sym, out var v))
        {
            rt = v.type;
            return true;
        }
        return false;
    }

    // New: callers that need amount too
    private bool TryMapSymbol(ReelSymbolSO sym, out ResourceType rt, out int amount)
    {
        rt = ResourceType.Attack;
        amount = 1;
        if (sym == null) return false;
        if (_symbolMap == null) BuildSymbolMapCache();

        if (_symbolMap.TryGetValue(sym, out var v))
        {
            rt = v.type;
            amount = (v.type == ResourceType.Null) ? 0 : Mathf.Max(1, v.amount);
            return true;
        }
        return false;
    }

    // --- Public helpers for ReelcraftController ---
    public bool TryMapSymbolPublic(ReelSymbolSO sym, out ResourceType rt, out int amount)
    {
        return TryMapSymbol(sym, out rt, out amount);
    }

    public ReelSymbolSO GetDefaultMagicSymbol()
    {
        if (symbolToResourceMap == null) return null;
        foreach (var e in symbolToResourceMap)
        {
            if (e != null && e.symbol != null && e.resourceType == ResourceType.Magic)
                return e.symbol;
        }
        return null;
    }

    public ReelSymbolSO GetDefaultWildSymbol()
    {
        if (symbolToResourceMap == null) return null;
        foreach (var e in symbolToResourceMap)
        {
            if (e != null && e.symbol != null && e.resourceType == ResourceType.Wild)
                return e.symbol;
        }
        return null;
    }

    private List<ReelEntry> GetFirstThree3DReels()
    {
        var list = new List<ReelEntry>(3);
        foreach (var r in reels)
        {
            if (list.Count >= 3) break;
            if (r == null || r.reel3d == null) continue;
            if (r.strip == null || r.strip.symbols == null || r.strip.symbols.Count == 0) continue;
            list.Add(r);
        }
        return list;
    }

    // --- Public accessors for ReelcraftController ---
    public GameObject MidrowPlane => midrowPlane;

    public ReelEntry GetReelEntryAt(int index)
    {
        if (reels == null) return null;
        if (index < 0 || index >= reels.Count) return null;
        return reels[index];
    }

    public List<ReelEntry> GetFirstThree3DReelsPublic()
    {
        return GetFirstThree3DReels();
    }

    public int FindReelIndexForColumn(Reel3DColumn column)
    {
        if (column == null || reels == null) return -1;
        for (int i = 0; i < reels.Count; i++)
        {
            if (reels[i]?.reel3d == column)
                return i;
        }
        return -1;
    }

    private bool All3DReelsFinished(List<ReelEntry> three)
    {
        foreach (var e in three)
        {
            if (e?.reel3d == null) continue;
            if (e.reel3d.IsSpinning) return false;
        }
        return true;
    }

    private SpinLandedInfo BuildSpinLandedInfo(List<ReelSymbolSO> landed)
    {
        SpinLandedInfo info = new SpinLandedInfo
        {
            symbols = landed != null ? new List<ReelSymbolSO>(landed) : new List<ReelSymbolSO>(),
            attackCount = 0,
            defendCount = 0,
            magicCount = 0,
            wildCount = 0
        };

        if (landed == null)
            return info;

        // NOTE: Keep these counts as "number of symbols" (not amount),
        // so triple-attack/item checks that expect 3 are not broken.
        foreach (var sym in landed)
        {
            if (sym == null)
                continue;

            if (TryMapSymbol(sym, out ResourceType rt))
            {
                switch (rt)
                {
                    case ResourceType.Attack: info.attackCount++; break;
                    case ResourceType.Defend: info.defendCount++; break;
                    case ResourceType.Magic: info.magicCount++; break;
                    case ResourceType.Wild: info.wildCount++; break;
                }
            }
        }

        return info;
    }

    private void SetPendingFromSymbols(List<ReelSymbolSO> syms, List<int> multipliers = null)
    {
        pendingA = pendingD = pendingM = pendingW = 0;
        if (syms == null) return;

        // Track mapped symbol contributions for match bonuses.
        // IMPORTANT: bonus logic must be based on *symbol contributions*, not on summed totals.
        var contribTypes = new List<ResourceType>(syms.Count);
        var contribAmounts = new List<int>(syms.Count);

        for (int i = 0; i < syms.Count; i++)
        {
            var s = syms[i];
            int mult = (multipliers != null && i < multipliers.Count) ? Mathf.Max(1, multipliers[i]) : 1;

            if (s != null && TryMapSymbol(s, out ResourceType rt, out int amt))
            {
                int single = Mathf.Max(0, amt);

                // Base payout: amount * multiplier.
                int totalAmt = single * mult;
                switch (rt)
                {
                    case ResourceType.Attack: pendingA += totalAmt; break;
                    case ResourceType.Defend: pendingD += totalAmt; break;
                    case ResourceType.Magic: pendingM += totalAmt; break;
                    case ResourceType.Wild: pendingW += totalAmt; break;
                }

                // Contribution list: one entry per "count" (so a doubled quad counts as 2).
                for (int k = 0; k < mult; k++)
                {
                    contribTypes.Add(rt);
                    contribAmounts.Add(single);
                }
            }
        }

        // --- Bonus rules (generalized) ---
        // We look for any type that reaches 3+ contributions.
        // Wild can act as a joker to complete a 3-of-a-kind with a non-wild type.
        int wildCount = 0, atkCount = 0, defCount = 0, magCount = 0;
        int maxAtkAmt = 0, maxDefAmt = 0, maxMagAmt = 0, maxWildAmt = 0;

        for (int i = 0; i < contribTypes.Count; i++)
        {
            switch (contribTypes[i])
            {
                case ResourceType.Attack: atkCount++; maxAtkAmt = Mathf.Max(maxAtkAmt, contribAmounts[i]); break;
                case ResourceType.Defend: defCount++; maxDefAmt = Mathf.Max(maxDefAmt, contribAmounts[i]); break;
                case ResourceType.Magic: magCount++; maxMagAmt = Mathf.Max(maxMagAmt, contribAmounts[i]); break;
                case ResourceType.Wild: wildCount++; maxWildAmt = Mathf.Max(maxWildAmt, contribAmounts[i]); break;
            }
        }

        // Pure 3+ of a kind
        if (atkCount >= 3) pendingA += Mathf.Max(1, maxAtkAmt);
        else if (defCount >= 3) pendingD += Mathf.Max(1, maxDefAmt);
        else if (magCount >= 3) pendingM += Mathf.Max(1, maxMagAmt);
        else if (wildCount >= 3) pendingW += Mathf.Max(1, maxWildAmt);
        else if (wildCount > 0)
        {
            // Joker completion: (two or more of a kind) + wild => 3 total
            if (atkCount + wildCount >= 3 && atkCount >= 2) pendingA += Mathf.Max(1, maxAtkAmt);
            else if (defCount + wildCount >= 3 && defCount >= 2) pendingD += Mathf.Max(1, maxDefAmt);
            else if (magCount + wildCount >= 3 && magCount >= 2) pendingM += Mathf.Max(1, maxMagAmt);
        }

        OnPendingPayoutChanged?.Invoke(pendingA, pendingD, pendingM, pendingW);
        ApplyAutoPayoutDeltaIfEnabled();
    }

    /// <summary>
    /// Attempts to nudge a specific reel up/down one step while stopped.
    /// Updates the current landed symbols and recalculates the pending payout.
    /// </summary>
    public bool TryNudgeReel(int reelIndex, int deltaSteps)
    {
        // Reelcraft can nudge reels outside reel phase (e.g., after cashout) as long as reels are not spinning.
        if (spinning) return false;
        if (IsRewardMode) return false;

        bool isReelPhaseEdit = InReelPhase && HasCurrentLandedSymbols;

        if (reels == null || reelIndex < 0 || reelIndex >= reels.Count)
            return false;

        var entry = reels[reelIndex];
        if (entry == null || entry.reel3d == null)
            return false;

        // deltaSteps==0 is used as a 'refresh' in some flows (e.g., after transmutation).
        // Outside reel phase, we allow this as a no-op success.
        if (deltaSteps != 0)
        {
            if (!entry.reel3d.TryNudgeSteps(deltaSteps))
                return false;
        }
        else
        {
            entry.reel3d.TryNudgeSteps(0);
        }

        if (!isReelPhaseEdit)
            return true;

        // Re-read the symbol at midrow for that reel.
        int qi;
        int mult;
        ReelSymbolSO sym = entry.reel3d.GetMidrowSymbolAndMultiplier(midrowPlane, out qi, out mult);
        if (sym == null)
            return false;

        // Corrosion: a reel move can also be a payout-affecting event. Apply conversion now so payout treats midrow as NULL.
        ConvertCorrodedLandedTokenToNull(reelIndex, qi);

        // Refresh symbol after possible conversion.
        sym = entry.reel3d.GetMidrowSymbolAndMultiplier(midrowPlane, out qi, out mult);
        if (sym == null)
            return false;

        ReelSymbolSO effectiveSym = sym;

        // Ensure list size >= 3
        while (_currentLandedSymbols.Count < 3)
            _currentLandedSymbols.Add(null);

        _currentLandedSymbols[reelIndex] = effectiveSym;
        if (_currentLandedMultipliers == null) _currentLandedMultipliers = new List<int> { 1, 1, 1 };
        while (_currentLandedMultipliers.Count < 3) _currentLandedMultipliers.Add(1);
        _currentLandedMultipliers[reelIndex] = Mathf.Max(1, mult);

        

// Track midrow quad index for this reel and allow Substitution to trigger on the first NULL resolve.
if (_currentLandedQuadIndices == null) _currentLandedQuadIndices = new List<int> { -1, -1, -1 };
while (_currentLandedQuadIndices.Count < 3) _currentLandedQuadIndices.Add(-1);
_currentLandedQuadIndices[reelIndex] = qi;

// Substitution: if this nudge/refresh causes the first NULL this battle, convert it immediately.
MaybeApplySubstitution_FirstNullOnResolve(_currentLandedSymbols, _currentLandedQuadIndices);
SetPendingFromSymbols(_currentLandedSymbols, _currentLandedMultipliers);

        SpinLandedInfo info = BuildSpinLandedInfo(_currentLandedSymbols);
                    if (logPassiveBridge)
                Debug.Log($"[ReelSpinSystem][PassiveBridge] OnCurrentLandedChanged invoke: symbols={(info.symbols != null ? info.symbols.Count : 0)} A={info.attackCount} D={info.defendCount} M={info.magicCount} W={info.wildCount}", this);
            OnCurrentLandedChanged?.Invoke(info);
        return true;
    }

    /// <summary>
    /// Converts ALL pending payout of one resource type into another.
    /// Intended for Arcane Transmutation.
    /// </summary>
    public bool TryConvertPending(ResourceType from, ResourceType to)
    {
        if (!InReelPhase) return false;
        if (spinning) return false;

        if (from == to) return false;

        int amount = 0;
        switch (from)
        {
            case ResourceType.Attack: amount = pendingA; pendingA = 0; break;
            case ResourceType.Defend: amount = pendingD; pendingD = 0; break;
            case ResourceType.Magic: amount = pendingM; pendingM = 0; break;
            case ResourceType.Wild: amount = pendingW; pendingW = 0; break;
        }

        if (amount <= 0) return false;

        switch (to)
        {
            case ResourceType.Attack: pendingA += amount; break;
            case ResourceType.Defend: pendingD += amount; break;
            case ResourceType.Magic: pendingM += amount; break;
            case ResourceType.Wild: pendingW += amount; break;
        }

        OnPendingPayoutChanged?.Invoke(pendingA, pendingD, pendingM, pendingW);
        ApplyAutoPayoutDeltaIfEnabled();
        return true;
    }

    /// <summary>
    /// Doubles (or generally multiplies) the contribution of a specific reel's currently landed symbol.
    /// Intended for Twofold Shadow.
    /// </summary>
    public bool TryMultiplyReelContribution(int reelIndex, int multiplier)
    {
        if (!InReelPhase) return false;
        if (spinning) return false;
        if (!HasCurrentLandedSymbols) return false;
        if (multiplier <= 1) return false;
        if (reelIndex < 0 || reelIndex >= _currentLandedSymbols.Count) return false;

        var sym = _currentLandedSymbols[reelIndex];
        if (sym == null) return false;

        if (!TryMapSymbol(sym, out ResourceType rt, out int amt))
            return false;

        int extra = amt * (multiplier - 1);
        switch (rt)
        {
            case ResourceType.Attack: pendingA += extra; break;
            case ResourceType.Defend: pendingD += extra; break;
            case ResourceType.Magic: pendingM += extra; break;
            case ResourceType.Wild: pendingW += extra; break;
        }

        OnPendingPayoutChanged?.Invoke(pendingA, pendingD, pendingM, pendingW);
        ApplyAutoPayoutDeltaIfEnabled();
        return true;
    }

    private void EvaluateRewardPayout(List<ReelSymbolSO> landed)
    {
        if (_rewardConfig == null) _rewardConfig = defaultRewardConfig;
        if (_rewardConfig == null) return;
        if (_rewardHero == null) return;
        if (landed == null || landed.Count < 3) return;

        ReelSymbolSO a = landed[0];
        ReelSymbolSO b = landed[1];
        ReelSymbolSO c = landed[2];

        // Must be 3-of-a-kind and not the configured null symbol.
        if (a == null || b == null || c == null) return;
        if (a != b || a != c) return;

        if (_rewardConfig.nullSymbol != null && a == _rewardConfig.nullSymbol) return;

        if (!_rewardConfig.TryGetPayout(a, out var payoutType, out var amount))
            return;

        amount = Mathf.Max(0, amount);
        if (amount <= 0) return;

        switch (payoutType)
        {
            case RewardReelConfigSO.PayoutType.SmallKey:
                _rewardHero.AddSmallKeys(amount);
                break;
            case RewardReelConfigSO.PayoutType.LargeKey:
                _rewardHero.AddLargeKeys(amount);
                break;
        }

        Debug.Log($"[ReelSpinSystem] Reward payout: {payoutType} x{amount} (symbol={a.name})");
    }

    private void PlaySpinSfx()
    {
        if (!playSpinSfx) return;

        if (spinSfxSource == null)
        {
            Debug.LogWarning("[ReelSpinSystem] Spin SFX requested but spinSfxSource is null.", this);
            return;
        }

        AudioClip clipToPlay = (spinSfxClip != null) ? spinSfxClip : spinSfxSource.clip;
        if (clipToPlay == null)
        {
            Debug.LogWarning("[ReelSpinSystem] Spin SFX requested but no clip is assigned (spinSfxClip and spinSfxSource.clip are null).", this);
            return;
        }

        spinSfxSource.PlayOneShot(clipToPlay);
    }

// ---------------- Per-Reel Spin Tuning + SFX ----------------

private void ApplyPerReelSpinTuning(ReelEntry entry)
{
    if (entry == null || entry.reel3d == null) return;

    if (entry.overrideSpinSpeed)
        entry.reel3d.SpinDegreesPerSecond = Mathf.Max(1f, entry.spinDegreesPerSecond);

    if (entry.overrideMinSpinDuration)
        entry.reel3d.MinSpinDurationSeconds = Mathf.Max(0f, entry.minSpinDurationSeconds);
}

private AudioSource EnsurePerReelSpinSfxSource(ReelEntry entry)
{
    if (entry == null) return null;

    if (entry.spinSfxSource != null)
        return entry.spinSfxSource;

    // Prefer attaching the audio source to the 3D reel object (world-space reel).
    if (entry.reel3d != null)
    {
        var src = entry.reel3d.GetComponent<AudioSource>();
        if (src == null) src = entry.reel3d.gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        entry.spinSfxSource = src;
        return src;
    }

    // Fallback: attach to the 2D UI object if present.
    if (entry.ui != null)
    {
        var src = entry.ui.GetComponent<AudioSource>();
        if (src == null) src = entry.ui.gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        entry.spinSfxSource = src;
        return src;
    }

    return null;
}

private void StartPerReelSpinSfx(ReelEntry entry, float speedMultiplier = 1f)
{
    if (!playSpinSfx) return;
    if (entry == null) return;

    var src = EnsurePerReelSpinSfxSource(entry);
    if (src == null) return;

    // Prefer per-reel clip, then global override, then whatever is on the source.
    AudioClip clipToPlay = entry.spinSfxClip != null
        ? entry.spinSfxClip
        : (spinSfxClip != null ? spinSfxClip : src.clip);

    // As a last resort, fall back to the global source's clip (if any).
    if (clipToPlay == null && spinSfxSource != null)
        clipToPlay = spinSfxSource.clip;

    if (clipToPlay == null)
    {
        // Don't spam warnings every frame; only warn if we're about to spin this reel.
        if (logFlow)
            Debug.LogWarning($"[ReelSpinSystem] Per-reel spin SFX requested but no clip is assigned for reelId='{entry.reelId}'.", this);
        return;
    }

    src.clip = clipToPlay;
    src.loop = entry.loopSpinSfx;

    src.volume = Mathf.Clamp01(entry.spinSfxVolume) * Mathf.Clamp01(entry.reelSfxVolume) * Mathf.Clamp01(reelSfxMasterVolume) * Mathf.Clamp01(spinLoopGlobalVolumeMultiplier);
    float pitch = 1f;
    if (entry.randomizeSpinPitch)
    {
        float lo = Mathf.Min(entry.spinPitchRange.x, entry.spinPitchRange.y);
        float hi = Mathf.Max(entry.spinPitchRange.x, entry.spinPitchRange.y);
        pitch = UnityEngine.Random.Range(lo, hi);
    }

    // Apply base pitch then optionally scale with speed.
    pitch *= (Mathf.Approximately(entry.baseSpinPitch, 0f) ? 1f : entry.baseSpinPitch);
    if (entry.scaleSpinPitchWithSpeed)
        pitch *= Mathf.Max(0.05f, speedMultiplier);

    src.pitch = Mathf.Clamp(pitch, 0.1f, Mathf.Max(0.1f, entry.maxScaledSpinPitch));

    // Restart cleanly.
    if (src.isPlaying) src.Stop();
    src.Play();
}

private void StopPerReelSpinSfx(ReelEntry entry)
{
    if (entry == null) return;
    var src = entry.spinSfxSource;
    if (src == null) return;

    // Only stop if we're the ones looping (avoid interrupting other UI sounds).
    if (src.isPlaying)
        src.Stop();
}


    private void PlayThreeMatchSfx()
    {
        if (!playThreeMatchSfx) return;

        if (threeMatchSfxClip == null)
        {
            // If you haven't assigned it yet, silently do nothing.
            return;
        }

        AudioSource src = (threeMatchSfxSource != null) ? threeMatchSfxSource : spinSfxSource;
        if (src == null)
        {
            Debug.LogWarning("[ReelSpinSystem] 3-match SFX requested but no AudioSource is available.", this);
            return;
        }

        src.PlayOneShot(threeMatchSfxClip, Mathf.Clamp01(reelSfxMasterVolume));
}

    private void EnsureMidrowStopChimeSource()
    {
        if (midrowStopChimeSource == null)
            midrowStopChimeSource = gameObject.AddComponent<AudioSource>();

        midrowStopChimeSource.playOnAwake = false;
        midrowStopChimeSource.spatialBlend = 0f; // 2D UI sound
        midrowStopChimeSource.loop = false;
        // Keep volume at 1; we use PlayOneShot(volumeScale) per-chime.
        midrowStopChimeSource.volume = 1f;
    }

    private bool IsNullLandedSymbol(ReelSymbolSO sym)
    {
        if (sym == null) return false;

        // Prefer the explicit mapping when available.
        if (TryMapSymbol(sym, out ResourceType rt, out int amt))
            return rt == ResourceType.Null;

        // Fallback by name (covers cases where the symbol isn’t in the map).
        string n = sym.name ?? string.Empty;
        return n.IndexOf("null", StringComparison.OrdinalIgnoreCase) >= 0 ||
               n.IndexOf("nul", StringComparison.OrdinalIgnoreCase) >= 0;
    }


/// <summary>
/// Substitution passive: when the reels resolve and ANY reel lands on NULL for the first time this battle,
/// immediately convert that first NULL into WILD (once per battle).
/// This is intentionally NOT tied to Cashout / Stop.
/// </summary>
private bool MaybeApplySubstitution_FirstNullOnResolve(List<ReelSymbolSO> landed, List<int> landedQuadIndices)
{
    if (!enableSubstitutionOnCashout) // legacy inspector toggle; now governs Substitution passive in general
        return false;

    if (_rewardModeActive)
        return false;

    if (_substitutionAttemptedThisBattle)
        return false;

    if (landed == null || landed.Count == 0)
        return false;

    ReelSymbolSO wild = GetDefaultWildSymbol();
    if (wild == null)
        return false;

    int count = Mathf.Min(3, landed.Count);

    for (int i = 0; i < count; i++)
    {
        ReelSymbolSO sym = landed[i];
        if (!IsNullLandedSymbol(sym))
            continue;

        bool allowed = (CanApplySubstitutionForReelIndex == null) || CanApplySubstitutionForReelIndex(i);
        if (!allowed)
            continue;

        // Apply the mutation.
        landed[i] = wild;

        // Keep our cached landed symbols consistent when the caller passed a reference to it.
        if (_currentLandedSymbols != null && _currentLandedSymbols.Count > i)
            _currentLandedSymbols[i] = wild;

        _substitutionAttemptedThisBattle = true;
        _substitutionTriggerCountThisBattle++;

        Debug.Log($"[ReelSpinSystem][SubstitutionDebug] APPLY on resolve -> reelIndex={i} quadIndex={(landedQuadIndices != null && landedQuadIndices.Count > i ? landedQuadIndices[i] : -1)} triggerCount={_substitutionTriggerCountThisBattle}", this);

        // Update 3D visuals without permanently mutating the strip (temporary transmute).
        if (reels != null && i >= 0 && i < reels.Count)
        {
            var e = reels[i];
            if (e != null && e.reel3d != null && landedQuadIndices != null && landedQuadIndices.Count > i)
            {
                int qi = landedQuadIndices[i];
                if (qi >= 0)
                    e.reel3d.SetQuadTemporarilyTransmutedTo(wild, qi);
            }
        }

        return true; // only convert the first NULL this battle
    }

    return false;
}

    private void PlayMidrowStopChime(bool landedNull)
    {
        if (!playMidrowStopChime) return;
        if (midrowStopChimeClip == null) return;

        EnsureMidrowStopChimeSource();
        AudioSource src = midrowStopChimeSource;
        if (src == null)
        {
            Debug.LogWarning("[ReelSpinSystem] Midrow stop chime requested but no AudioSource is available.", this);
            return;
        }

        // Pitch steps up for each reel that stops within this spin.
        float pitch = midrowStopChimePitchStart + (midrowStopChimePitchStep * Mathf.Max(0, _midrowStopChimeIndex));
        pitch = Mathf.Min(pitch, midrowStopChimePitchMax);

        // If the reel landed on NULL, deepen the chime.
        if (landedNull)
            pitch *= Mathf.Max(0.01f, midrowStopChimeNullPitchMultiplier);

        src.pitch = pitch;

        float vol = Mathf.Max(0f, midrowStopChimeVolume) * Mathf.Max(0f, midrowStopChimeGain);
        if (chimeUsesReelSfxMasterVolume)
            vol *= Mathf.Clamp01(reelSfxMasterVolume);

        // Use PlayOneShot(volumeScale) so we don’t permanently alter AudioSource.volume.
        src.PlayOneShot(midrowStopChimeClip, vol);

        _midrowStopChimeIndex++;
    }

    private void TriggerThreeOfAKindFX(List<ReelEntry> three)
    {
        if (three == null) return;

        if (popReelsOnThreeOfAKind)
        {
            for (int i = 0; i < three.Count; i++)
            {
                var e = three[i];
                if (e != null && e.reel3d != null)
                    e.reel3d.PopReel(threeOfAKindReelPopScale, threeOfAKindReelPopDuration);
            }
        }

        if (shakeReelsOnThreeOfAKind)
        {
            for (int i = 0; i < three.Count; i++)
            {
                var e = three[i];
                if (e != null && e.reel3d != null)
                    StartCoroutine(e.reel3d.ShakeRoutine(threeOfAKindShakeDuration, threeOfAKindShakeMagnitude));
            }
        }
    }

    private static bool IsThreeOfAKind(List<ReelSymbolSO> landed)
    {
        if (landed == null || landed.Count < 3) return false;
        ReelSymbolSO a = landed[0];
        ReelSymbolSO b = landed[1];
        ReelSymbolSO c = landed[2];
        if (a == null || b == null || c == null) return false;
        return (a == b && a == c);
    }

    private IEnumerator Spin3DPostSelectRoutine(System.Random rng)
    {
        var three = GetFirstThree3DReels();
        if (three.Count == 0)
        {
            spinning = false;
            _threeDSpinRoutine = null;
            yield break;
        }

                // Reset per-spin chime pitch stepping.
        _midrowStopChimeIndex = 0;

// Apply global min duration override (optional)
        if (overrideMinSpinDuration3D)
        {
            float dur = Mathf.Max(0f, minSpinDurationOverride3D);
            for (int i = 0; i < three.Count; i++)
            {
                if (three[i]?.reel3d != null)
                    three[i].reel3d.MinSpinDurationSeconds = dur;
            }
        }

        // Per-reel tuning + per-reel looping SFX
// (Audio loops are stopped automatically as each reel finishes.)
bool[] sfxStopped = new bool[three.Count];

// Async stop processing (resource popups can fire as each reel stops)
bool[] reelProcessed = new bool[three.Count];
ReelSymbolSO[] stoppedSymbols = new ReelSymbolSO[three.Count];
int[] stoppedQuadIndices = new int[three.Count];
int[] stoppedMultipliers = new int[three.Count];

// Track what we've already paid this spin (so auto-payout doesn't double-add later)
int asyncPaidA = 0, asyncPaidD = 0, asyncPaidM = 0, asyncPaidW = 0;

bool asyncWillPayResources = asyncResourcePopupsPerReelStop && payoutMode == PayoutMode.AutoPayoutOnSpin && !_rewardModeActive && resourcePool != null;

for (int i = 0; i < three.Count; i++)
{
    var entry = three[i];
    if (entry == null || entry.reel3d == null) continue;

    ApplyPerReelSpinTuning(entry);
    StartPerReelSpinSfx(entry);
    entry.reel3d.SpinRandom(rng, minFullRotations3D);
}

while (!All3DReelsFinished(three))
{
    // Stop each reel's spin SFX (and optionally fire resource gain) as soon as that reel completes.
    for (int i = 0; i < three.Count; i++)
    {
        if (sfxStopped[i] && (!asyncResourcePopupsPerReelStop || reelProcessed[i]))
            continue;

        var entry = three[i];
        if (entry == null || entry.reel3d == null)
        {
            sfxStopped[i] = true;
            reelProcessed[i] = true;
            continue;
        }

        if (!entry.reel3d.IsSpinning)
        {
            if (!sfxStopped[i])
            {
                StopPerReelSpinSfx(entry);
                sfxStopped[i] = true;
            }

            if (asyncResourcePopupsPerReelStop && !reelProcessed[i])
            {
                int qi;
                int mult;
                ReelSymbolSO sym = entry.reel3d.GetMidrowSymbolAndMultiplier(midrowPlane, out qi, out mult);

                stoppedSymbols[i] = sym;
                stoppedQuadIndices[i] = qi;
                stoppedMultipliers[i] = Mathf.Max(1, mult);

                // Midrow emphasis pop immediately when this reel lands.
                if (popMidrowOnStop)
                {
                    entry.reel3d.PopIcon(qi, midrowPopScale, midrowPopDuration);
                    PlayMidrowStopChime(IsNullLandedSymbol(sym));
                }

                // Apply resources immediately so popups spawn while other reels are still spinning.
                if (asyncWillPayResources)
                {
                    if (TryMapSymbol(sym, out ResourceType rt, out int amt) && rt != ResourceType.Null && amt > 0)
                    {
                        int total = amt * stoppedMultipliers[i];

                        switch (rt)
                        {
                            case ResourceType.Attack: asyncPaidA += total; resourcePool.Add(total, 0, 0, 0); break;
                            case ResourceType.Defend: asyncPaidD += total; resourcePool.Add(0, total, 0, 0); break;
                            case ResourceType.Magic:  asyncPaidM += total; resourcePool.Add(0, 0, total, 0); break;
                            case ResourceType.Wild:   asyncPaidW += total; resourcePool.Add(0, 0, 0, total); break;
                        }
                    }
                }

                // Corrosion rule: pay out normally THIS spin, then convert to NULL for future spins.
                if (IsReelQuadCorroded(i, qi) && SymbolGrantsResources(sym))
                    ConvertCorrodedLandedTokenToNull(i, qi);

                reelProcessed[i] = true;
            }
        }
    }

    yield return null;
}

// If the final reel stops between frames, the while-condition can exit before we process it.
// Process any remaining reels now (only those that are actually stopped).
if (asyncResourcePopupsPerReelStop)
{
    for (int i = 0; i < three.Count; i++)
    {
        if (reelProcessed[i]) continue;

        var entry = three[i];
        if (entry == null || entry.reel3d == null)
        {
            sfxStopped[i] = true;
            reelProcessed[i] = true;
            continue;
        }

        // Safety: don't process while it's still spinning (shouldn't happen because the while-loop ended,
        // but timing can be tricky if IsSpinning flips late).
        if (entry.reel3d.IsSpinning)
            continue;

        // Stop per-reel SFX if still playing
        if (!sfxStopped[i])
        {
            StopPerReelSpinSfx(entry);
            sfxStopped[i] = true;
        }

        int qi;
        int mult;
        ReelSymbolSO sym = entry.reel3d.GetMidrowSymbolAndMultiplier(midrowPlane, out qi, out mult);

        stoppedSymbols[i] = sym;
        stoppedQuadIndices[i] = qi;
        stoppedMultipliers[i] = Mathf.Max(1, mult);

        // Midrow emphasis pop immediately when this reel lands.
        if (popMidrowOnStop)
        {
            entry.reel3d.PopIcon(qi, midrowPopScale, midrowPopDuration);
            PlayMidrowStopChime(IsNullLandedSymbol(sym));
        }

        // Apply resources immediately so popups spawn while other reels are still spinning.
        if (asyncWillPayResources)
        {
            if (TryMapSymbol(sym, out ResourceType rt, out int amt) && rt != ResourceType.Null && amt > 0)
            {
                int total = amt * stoppedMultipliers[i];

                switch (rt)
                {
                    case ResourceType.Attack: asyncPaidA += total; resourcePool.Add(total, 0, 0, 0); break;
                    case ResourceType.Defend: asyncPaidD += total; resourcePool.Add(0, total, 0, 0); break;
                    case ResourceType.Magic:  asyncPaidM += total; resourcePool.Add(0, 0, total, 0); break;
                    case ResourceType.Wild:   asyncPaidW += total; resourcePool.Add(0, 0, 0, total); break;
                }
            }
        }

        // Corrosion rule: pay out normally THIS spin, then convert to NULL for future spins.
        if (IsReelQuadCorroded(i, qi) && SymbolGrantsResources(sym))
            ConvertCorrodedLandedTokenToNull(i, qi);

        reelProcessed[i] = true;
    }
}

var landed = new List<ReelSymbolSO>(3);
        var multipliers = new List<int>(3);
        var landedQuadIndices = new List<int>(3);
        var parts = new List<string>(3);

        for (int i = 0; i < three.Count; i++)
        {
            var entry = three[i];
            int qi;
            int mult;
            ReelSymbolSO sym = entry.reel3d.GetMidrowSymbolAndMultiplier(midrowPlane, out qi, out mult);
            landed.Add(sym);
            landedQuadIndices.Add(qi);
            multipliers.Add(Mathf.Max(1, mult));

            if (log3DAdjacentSymbolsEachSpin && entry != null && entry.reel3d != null)
            {
                int qc = Mathf.Max(1, entry.reel3d.QuadCount);

                // IMPORTANT: In our 3D reel implementation, quad indices increase "up" the column
                // (visually). That means the symbol directly *above* the midrow is qi+1, and the
                // symbol directly *below* is qi-1.
                int aboveQi = Mod(qi + 1, qc);
                int belowQi = Mod(qi - 1, qc);
                ReelSymbolSO above = entry.reel3d.GetSymbolOnQuad(aboveQi);
                ReelSymbolSO below = entry.reel3d.GetSymbolOnQuad(belowQi);

                string id2 = !string.IsNullOrEmpty(entry.reelId) ? entry.reelId : $"slot{i}";
                string midName = sym != null ? sym.name : "<null>";
                string aboveName = above != null ? above.name : "<null>";
                string belowName = below != null ? below.name : "<null>";
                Debug.Log($"[ReelSpinSystem] 3D Adjacent (post-select): {id2} mid={midName}(quad {qi}) above={aboveName}(quad {aboveQi}) below={belowName}(quad {belowQi})");
            }

            string id = !string.IsNullOrEmpty(entry.reelId) ? entry.reelId : $"slot{i}";
            string name = sym != null ? sym.name : "<null>";
            if (IsReelQuadCorroded(i, qi) && SymbolGrantsResources(sym))
                name += " (CORRODED)";
            parts.Add($"{id}={name}(quad {qi})");
        }

        if (log3DMidRowSymbolsEachSpin)
            Debug.Log($"[ReelSpinSystem] 3D MidRow (post-select): {string.Join(" | ", parts)}");

        // Midrow emphasis pop (each reel)
        if (popMidrowOnStop && !asyncResourcePopupsPerReelStop)
        {
            for (int i = 0; i < three.Count && i < landedQuadIndices.Count && i < landed.Count; i++)
            {
                var e = three[i];
                if (e != null && e.reel3d != null)
                {
                    e.reel3d.PopIcon(landedQuadIndices[i], midrowPopScale, midrowPopDuration);
                    PlayMidrowStopChime(IsNullLandedSymbol(landed[i]));
                }
            }
        }
// ✅ 3-in-a-row feedback
        bool isThreeOfAKind = IsThreeOfAKind(landed);
        if (isThreeOfAKind)
        {
            PlayThreeMatchSfx();
            TriggerThreeOfAKindFX(three);
        }

        if (_rewardModeActive)
        {
            EvaluateRewardPayout(landed);
        }
        else
        {
            if (!asyncResourcePopupsPerReelStop)
            {


            // Corrosion: if a corroded token is in the midrow during payout, convert it into NULL NOW
            // and refresh the landed symbols so this payout treats it as NULL.
            for (int ci = 0; ci < three.Count; ci++)
            {
                int cqi; int cm;
                ReelSymbolSO before = three[ci].reel3d.GetMidrowSymbolAndMultiplier(midrowPlane, out cqi, out cm);

                ConvertCorrodedLandedTokenToNull(ci, cqi);

                // Refresh after possible conversion (midrow may now be NULL).
                int rq; int rm;
                ReelSymbolSO refreshed = three[ci].reel3d.GetMidrowSymbolAndMultiplier(midrowPlane, out rq, out rm);
                if (refreshed != null)
                {
                    landed[ci] = refreshed;
                    multipliers[ci] = Mathf.Max(1, rm);
                }

                if (logCorrosionConversionProbe && before != refreshed)
                    Debug.Log($"[ReelSpinSystem][Corrosion] Midrow payout refresh reel={ci}: '{(before != null ? before.name : "<null>")}' -> '{(refreshed != null ? refreshed.name : "<null>")}'", this);
            }

            }




// Track midrow quad indices for this resolve (used for temporary symbol swaps like Substitution).
_currentLandedQuadIndices = new List<int>(landedQuadIndices);

// Substitution: convert the first NULL this battle into WILD immediately on resolve (not on Cashout).
MaybeApplySubstitution_FirstNullOnResolve(landed, landedQuadIndices);
            // Build landed info and notify listeners (item synergies, UI, etc.)
            SpinLandedInfo info = BuildSpinLandedInfo(landed);
            OnSpinLanded?.Invoke(info);

            // Cache current landed symbols so Reelcraft can operate during this reel phase.
            _currentLandedSymbols = new List<ReelSymbolSO>(landed);
            _currentLandedMultipliers = new List<int>(multipliers);
            if (_currentLandedQuadIndices == null)
                _currentLandedQuadIndices = new List<int>(landedQuadIndices);
            else
            {
                _currentLandedQuadIndices.Clear();
                _currentLandedQuadIndices.AddRange(landedQuadIndices);
            }
                        if (logPassiveBridge)
                Debug.Log($"[ReelSpinSystem][PassiveBridge] OnCurrentLandedChanged invoke: symbols={(info.symbols != null ? info.symbols.Count : 0)} A={info.attackCount} D={info.defendCount} M={info.magicCount} W={info.wildCount}", this);
            OnCurrentLandedChanged?.Invoke(info);

            if (asyncWillPayResources)
            {
                // We've already applied resources as each reel stopped; prevent auto-payout from double-adding.
                _autoPayoutAppliedForCurrentLanded = true;
                _autoPaidA = asyncPaidA;
                _autoPaidD = asyncPaidD;
                _autoPaidM = asyncPaidM;
                _autoPaidW = asyncPaidW;
            }
            else
            {
                ResetAutoPayoutTracking();
            }
            SetPendingFromSymbols(landed, multipliers);
        }

        spinning = false;
        _threeDSpinRoutine = null;
    }

    /// <summary>
    /// Main entry point used by existing code (TurnSimulator, UI buttons, BattleManager).
    /// In reward-mode, each spin costs gold. In combat-mode, spins are limited per turn.
    /// </summary>
    public void TrySpin()
    {
        if (spinning) return;

        // Spinning should always reveal the reels.
        Set3DReelsActive(true);
        if (shutterController != null)
            shutterController.OpenShutters();

        if (_rewardModeActive)
        {
            if (_rewardConfig == null) _rewardConfig = defaultRewardConfig;
            if (_rewardConfig == null) return;
            if (_rewardHero == null) return;

            int cost = Mathf.Max(0, _rewardConfig.goldCostPerSpin);
            if (cost > 0)
            {
                // Requires HeroStats.TrySpendGold(int)
                if (!_rewardHero.TrySpendGold(cost))
                {
                    Debug.Log("[ReelSpinSystem] Not enough gold to spin reward reels.");
                    return;
                }
            }
        }
        else
        {
            if (spinsRemaining <= 0) return;

            // ✅ Consume a spin immediately when the spin begins.
            spinsRemaining = Mathf.Max(0, spinsRemaining - 1);
            OnSpinsRemainingChanged?.Invoke(spinsRemaining);

            if (spinButton != null)
                spinButton.interactable = (spinsRemaining > 0);
        }

        spinning = true;

        int seed = useFixedSeed
            ? fixedSeed
            : unchecked(Environment.TickCount * 31 + (int)(Time.realtimeSinceStartup * 1000f));
        System.Random rng = new System.Random(seed);

        if (use3DPostSelectMode)
        {
            if (_threeDSpinRoutine != null)
                StopCoroutine(_threeDSpinRoutine);

            _threeDSpinRoutine = StartCoroutine(Spin3DPostSelectRoutine(rng));
            return;
        }

        // If you ever disable 3D mode, you'd implement 2D spin here.
        spinning = false;
    }

    
    private IEnumerator DoTwofoldShadowTransmuteRoutine(ReelEntry entry, GameObject midrowPlane, ReelSymbolSO wild)
    {
        if (entry != null && entry.reel3d != null && midrowPlane != null)
        {
            int qi;
            int mult;
            ReelSymbolSO currentMid = entry.reel3d.GetMidrowSymbolAndMultiplier(midrowPlane, out qi, out mult);

            int idx = FindReelIndexForColumn(entry.reel3d);
            // Corrosion can nullify resource-granting tokens; treat corroded resources as NUL for logic.
            ReelSymbolSO effectiveMid = (idx >= 0) ? ApplyCorrosionIfNeeded(idx, qi, currentMid) : currentMid;

            entry.reel3d.SetQuadTemporarilyTransmutedTo(wild, qi);

            
            if (idx >= 0) TryNudgeReel(idx, 0);
            else
            {
                TryNudgeReel(0, 0);
                TryNudgeReel(1, 0);
                TryNudgeReel(2, 0);
            }
            entry.reel3d.ShakeIconRoutine(qi);
            entry.reel3d.SpawnTwofoldShadowSmoke(qi);

            // THIS actually delays continuation
            yield return new WaitForSeconds(1f);
        }
    }

    private IEnumerator ApplySubstitutionBeforeCashoutRoutine()
    {
        // Substitution is a battle passive. Never apply it in reward mode.
        if (_rewardModeActive) yield break;

        if (!InReelPhase) yield break;
        if (spinning) yield break;
        if (!HasCurrentLandedSymbols) yield break;

        // Only attempt once per battle (BattleManager calls ResetBattleSubstitutionState at battle start).
        if (_substitutionAttemptedThisBattle)
        {
            Debug.Log($"[ReelSpinSystem][SubstitutionDebug] ApplySubstitutionBeforeCashoutRoutine SKIP: already attempted this battle. cashoutPressCount={_cashoutPressCountThisBattle} triggerCount={_substitutionTriggerCountThisBattle}", this);
            yield break;
        }
        _substitutionAttemptedThisBattle = true;
        _substitutionTriggerCountThisBattle++;
        Debug.Log($"[ReelSpinSystem][SubstitutionDebug] APPLY attempt #{_substitutionTriggerCountThisBattle} (cashoutPressCount={_cashoutPressCountThisBattle}).", this);
ReelSymbolSO wild = GetDefaultWildSymbol();
        if (wild == null) yield break;

        int count = Mathf.Min(3, _currentLandedSymbols.Count);
        bool anyChanged = false;

        int running = 0;

        for (int i = 0; i < count; i++)
        {
            ReelSymbolSO sym = _currentLandedSymbols[i];
            if (sym == null) continue;

            // If the reel already landed on a WLD token, do not overwrite it.
            if (sym == wild) continue;

            if (CanApplySubstitutionForReelIndex != null && !CanApplySubstitutionForReelIndex(i))
                continue;

            var entry = GetReelEntryAt(i);

            // Start the VFX coroutine, but track completion
            running++;
            StartCoroutine(DoTwofoldShadowTransmuteRoutine_WithDone(entry, midrowPlane, wild, () => running--));

            // Update data immediately so UI/payout preview reflects substitution
            _currentLandedSymbols[i] = wild;
            anyChanged = true;
        }

        if (!anyChanged)
        {
            Debug.Log($"[ReelSpinSystem][SubstitutionDebug] APPLY resulted in NO changes (all were WILD or disallowed).", this);
            yield break;
        }

        // Recompute pending + notify listeners BEFORE payout is collected.
        SetPendingFromSymbols(_currentLandedSymbols, _currentLandedMultipliers);

        SpinLandedInfo info = BuildSpinLandedInfo(_currentLandedSymbols);
        OnCurrentLandedChanged?.Invoke(info);

        // Wait for all VFX routines to report done
        while (running > 0)
            yield return null;
    }

    private IEnumerator DoTwofoldShadowTransmuteRoutine_WithDone(
        ReelEntry entry, GameObject midrowPlane, ReelSymbolSO wild, System.Action onDone)
    {
        yield return StartCoroutine(DoTwofoldShadowTransmuteRoutine(entry, midrowPlane, wild));
        onDone?.Invoke();
    }

    /// <summary>
    /// Momentum bonus: spins ONLY the specified reel once and immediately grants resources from that reel's midrow symbol.
    /// This does NOT change spinsRemaining and does NOT modify the normal pending payout state.
    /// Intended to be called mid-battle when an ability kill triggers a bonus spin.
    /// </summary>
    public IEnumerator MomentumSpinAndInstantCollect(int reelIndex, float speedMultiplier = 1f)
    {
        // Reset last instant-spin result (combo systems may read this after the coroutine completes).
        LastInstantSpinResult = new InstantSpinResult { valid = false, reelIndex = reelIndex };

        if (reels == null) yield break;
        if (reelIndex < 0 || reelIndex >= reels.Count) yield break;

        var entry = reels[reelIndex];
        if (entry == null || entry.reel3d == null)
            yield break;

        // Apply inspector per-reel tuning (so momentum/combo spins behave like normal spins).
        ApplyPerReelSpinTuning(entry);

        // Optional: temporarily accelerate this particular spin.
        // We scale spin speed up, and scale min spin duration down, then restore after the spin.
        speedMultiplier = Mathf.Max(0.05f, speedMultiplier);
        float prevSpeed = entry.reel3d.SpinDegreesPerSecond;
        float prevMinDur = entry.reel3d.MinSpinDurationSeconds;
        entry.reel3d.SpinDegreesPerSecond = prevSpeed * speedMultiplier;
        entry.reel3d.MinSpinDurationSeconds = prevMinDur / speedMultiplier;

        // Prevent overlapping spins with the normal spin flow.
        while (spinning)
            yield return null;

        spinning = true;

        // Make sure the reel is visible/open.
        Set3DReelsActive(true);
        if (shutterController != null)
            shutterController.OpenShutters();
        // Spin SFX: for momentum/combo spins, use the per-reel loop so pitch can scale with speed.
        StartPerReelSpinSfx(entry, speedMultiplier);

        System.Random rng = new System.Random();
        entry.reel3d.SpinRandom(rng, minFullRotations3D);

        while (entry.reel3d != null && entry.reel3d.IsSpinning)
            yield return null;

        // Stop per-reel spin loop SFX now that this reel has stopped.
        StopPerReelSpinSfx(entry);

        // Restore tuning even if something went odd.
        if (entry.reel3d != null)
        {
            entry.reel3d.SpinDegreesPerSecond = prevSpeed;
            entry.reel3d.MinSpinDurationSeconds = prevMinDur;
        }

        spinning = false;

        if (entry.reel3d == null)
            yield break;

        int qi;
        int mult;
        ReelSymbolSO sym = entry.reel3d.GetMidrowSymbolAndMultiplier(midrowPlane, out qi, out mult);
        ReelSymbolSO effectiveSym = sym;
        if (effectiveSym == null)
            yield break;

        if (!TryMapSymbol(effectiveSym, out ResourceType rt, out int amt))
            yield break;

        int total = Mathf.Max(0, amt) * Mathf.Max(1, mult);
        if (total <= 0)
            yield break;

        LastInstantSpinResult = new InstantSpinResult
        {
            valid = true,
            reelIndex = reelIndex,
            symbol = effectiveSym,
            resourceType = rt,
            amount = Mathf.Max(0, amt),
            multiplier = Mathf.Max(1, mult),
            total = total
        };

        // Update the current landed symbol/mult for this reel so battle passives that listen to
        // OnCurrentLandedChanged (e.g., Battle Rhythm bridge) can react to momentum spins.
        // IMPORTANT: We intentionally do NOT call SetPendingFromSymbols() here, so normal pending payout state is unchanged.
        if (_currentLandedSymbols == null)
            _currentLandedSymbols = new List<ReelSymbolSO>();

        while (_currentLandedSymbols.Count < 3)
            _currentLandedSymbols.Add(null);

        _currentLandedSymbols[reelIndex] = effectiveSym;

        if (_currentLandedMultipliers == null)
            _currentLandedMultipliers = new List<int> { 1, 1, 1 };

        while (_currentLandedMultipliers.Count < 3)
            _currentLandedMultipliers.Add(1);

        _currentLandedMultipliers[reelIndex] = Mathf.Max(1, mult);

        // Notify listeners that the midrow changed due to a momentum spin.
        SpinLandedInfo momentumInfo = BuildSpinLandedInfo(_currentLandedSymbols);
        OnCurrentLandedChanged?.Invoke(momentumInfo);

        if (resourcePool != null)
        {
            switch (rt)
            {
                case ResourceType.Attack: resourcePool.Add(total, 0, 0, 0); break;
                case ResourceType.Defend: resourcePool.Add(0, total, 0, 0); break;
                case ResourceType.Magic:  resourcePool.Add(0, 0, total, 0); break;
                case ResourceType.Wild:   resourcePool.Add(0, 0, 0, total); break;
            }
        }

        if (log3DMidRowSymbolsEachSpin)
        {
            string id = !string.IsNullOrEmpty(entry.reelId) ? entry.reelId : $"slot{reelIndex}";
            Debug.Log($"[ReelSpinSystem][Momentum] Instant cashout reel={id} symbol={(sym != null ? sym.name : "<null>")} x{Mathf.Max(1, mult)} => {rt}+{total}", this);
        }
    }


    public void StopSpinningAndCollect()
    {
        // Cashout mechanic removed.
        Debug.LogWarning("[ReelSpinSystem] StopSpinningAndCollect called, but Cashout has been removed from gameplay. Ignoring.", this);
    }

    private IEnumerator StopSpinningAndCollectRoutine(bool firstCashoutThisBattle)
    {
        bool canApplySubstitution = firstCashoutThisBattle && CanApplySubstitutionNow();

        Debug.Log($"[ReelSpinSystem][SubstitutionDebug] StopSpinningAndCollectRoutine: firstCashout={firstCashoutThisBattle} CanApplySubstitutionNow={CanApplySubstitutionNow()} => willApply={canApplySubstitution} (triggerCount={_substitutionTriggerCountThisBattle})", this);

        if (canApplySubstitution)
            yield return StartCoroutine(ApplySubstitutionBeforeCashoutRoutine());
// End reel-phase, but keep the reels available/visible if desired.
        if (!keepReelsEnabledAfterCashout)
            Set3DReelsActive(false);

        SetReelPhase(false);
        CollectPendingPayout();

        if (closeShuttersOnCashout && shutterController != null)
            shutterController.CloseShutters();
    }

    private IEnumerator WaitForParticleSystem(ParticleSystem ps)
    {
        // If it was destroyed or missing, just continue
        if (ps == null) yield break;

        // Wait until it stops emitting AND all particles are gone
        while (ps != null && ps.IsAlive(true))
            yield return null;
    }
    private bool CanApplySubstitutionNow()
    {
        if (_currentLandedSymbols == null) return false;

        int count = Mathf.Min(3, _currentLandedSymbols.Count);
        for (int i = 0; i < count; i++)
        {
            bool allowed = (CanApplySubstitutionForReelIndex == null) || CanApplySubstitutionForReelIndex(i);
            if (allowed) return true;
        }
        return false;
    }
    private void Set3DReelsActive(bool active)
    {
        if (reels == null) return;
        for (int i = 0; i < reels.Count; i++)
        {
            var entry = reels[i];
            if (entry == null) continue;
            if (entry.reel3d != null)
                entry.reel3d.gameObject.SetActive(active);
        }
    }


    private void ResetAutoPayoutTracking()
    {
        _autoPayoutAppliedForCurrentLanded = false;
        _autoPaidA = _autoPaidD = _autoPaidM = _autoPaidW = 0;
    }

    private void ApplyAutoPayoutDeltaIfEnabled()
    {
        if (payoutMode != PayoutMode.AutoPayoutOnSpin) return;
        if (_rewardModeActive) return;
        if (resourcePool == null) return;

        if (!_autoPayoutAppliedForCurrentLanded)
        {
            if (pendingA != 0 || pendingD != 0 || pendingM != 0 || pendingW != 0)
                resourcePool.Add(pendingA, pendingD, pendingM, pendingW);

            _autoPaidA = pendingA;
            _autoPaidD = pendingD;
            _autoPaidM = pendingM;
            _autoPaidW = pendingW;
            _autoPayoutAppliedForCurrentLanded = true;
            return;
        }

        int da = pendingA - _autoPaidA;
        int dd = pendingD - _autoPaidD;
        int dm = pendingM - _autoPaidM;
        int dw = pendingW - _autoPaidW;

        if (da != 0 || dd != 0 || dm != 0 || dw != 0)
            resourcePool.Add(da, dd, dm, dw);

        _autoPaidA = pendingA;
        _autoPaidD = pendingD;
        _autoPaidM = pendingM;
        _autoPaidW = pendingW;
    }

    private void CollectPendingPayout()
    {
        if (payoutMode == PayoutMode.AutoPayoutOnSpin)
        {
            // In auto mode, resources are already applied as the pending totals change.
            pendingA = pendingD = pendingM = pendingW = 0;
            _currentLandedSymbols = null;
            _currentLandedMultipliers = null;
            ResetAutoPayoutTracking();
            OnPendingPayoutChanged?.Invoke(pendingA, pendingD, pendingM, pendingW);
            return;
        }

        Debug.Log($"[ReelSpinSystem] CollectPendingPayout CALLED. pendingA={pendingA}, pendingD={pendingD}, pendingM={pendingM}, pendingW={pendingW}, spinsRemaining={spinsRemaining}");
        if (pendingA == 0 && pendingD == 0 && pendingM == 0 && pendingW == 0)
            return;

        if (resourcePool != null)
            resourcePool.Add(pendingA, pendingD, pendingM, pendingW);

        pendingA = pendingD = pendingM = pendingW = 0;

        // After payout, clear current landed symbols so Reelcraft can't modify a settled spin.
        _currentLandedSymbols = null;
        _currentLandedMultipliers = null;
        _currentLandedQuadIndices = null;
        OnPendingPayoutChanged?.Invoke(pendingA, pendingD, pendingM, pendingW);
        ApplyAutoPayoutDeltaIfEnabled();
    }

    public void ClearAllTemporaryDoubles()
    {
        if (reels == null) return;
        for (int i = 0; i < reels.Count; i++)
        {
            var e = reels[i];
            if (e == null || e.reel3d == null) continue;
            e.reel3d.ClearAllDoubles();
        }
    }

    private static int Mod(int x, int m)
    {
        if (m <= 0) return 0;
        int r = x % m;
        return r < 0 ? r + m : r;
    }


    private string DebugSymbols(List<ReelSymbolSO> syms)
    {
        if (syms == null) return "null";
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < syms.Count; i++)
        {
            var s = syms[i];
            sb.Append(i).Append(":").Append(s != null ? s.name : "NULL");
            if (i < syms.Count - 1) sb.Append(" | ");
        }
        return sb.ToString();
    }
    private Coroutine _sleepRoutine;

    private void SleepTemporarily(float duration)
    {
        if (_sleepRoutine != null)
            StopCoroutine(_sleepRoutine);

        _sleepRoutine = StartCoroutine(SleepRoutine(duration));
    }

    private IEnumerator SleepRoutine(float duration)
    {

        yield return new WaitForSeconds(duration);

        _sleepRoutine = null;
    }
}

////////////////////////////////////////////////////////////

////////////////////////////////////////////////////////////


