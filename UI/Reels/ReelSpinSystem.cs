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
    }

    [Serializable]
    public class SymbolResourceMapEntry
    {
        public ReelSymbolSO symbol;
        public ResourceType resourceType;

        [Tooltip("How much of the resource this symbol grants (e.g. DEF2 = 2).")]
        public int amount = 1;
    }

    public enum ResourceType { Attack, Defend, Magic, Wild }

    [Header("Reels")]
    [SerializeField] private List<ReelEntry> reels = new List<ReelEntry>();

    [Header("Spin Control")]
    [SerializeField] private int spinsPerTurn = 3;
    [SerializeField] private Button stopSpinningButton;

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

    [Header("3-in-a-Row SFX")]
    [Tooltip("Play a special sound when the 3 landed midrow symbols match.")]
    [SerializeField] private bool playThreeMatchSfx = true;

    [Tooltip("AudioSource for the 3-in-a-row sound. If null, uses spinSfxSource.")]
    [SerializeField] private AudioSource threeMatchSfxSource;

    [Tooltip("Clip to play when 3-in-a-row happens.")]
    [SerializeField] private AudioClip threeMatchSfxClip;

    [Header("Reward Reel Mode (Post-Battle)")]
    [Tooltip("Optional default reward config. BattleManager can override by calling EnterRewardMode(...)")]
    [SerializeField] private RewardReelConfigSO defaultRewardConfig;

    [Header("Symbol -> Resource Mapping")]
    [SerializeField] private List<SymbolResourceMapEntry> symbolToResourceMap = new List<SymbolResourceMapEntry>();

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
    public bool HasCurrentLandedSymbols => _currentLandedSymbols != null && _currentLandedSymbols.Count >= 3;

    /// <summary>Read-only view of the most recent landed symbols for this reel phase.</summary>
    public IReadOnlyList<ReelSymbolSO> CurrentLandedSymbols => _currentLandedSymbols;

    // State
    private bool spinning;
    private int spinsRemaining;

    // Pending payout (deferred until stop/collect or out of spins)
    private int pendingA;
    private int pendingD;
    private int pendingM;
    private int pendingW;

    // Reelcraft integration: keep track of the last landed symbols so we can nudge/transform without re-spinning.
    private List<ReelSymbolSO> _currentLandedSymbols;

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

    private void Awake()
    {
        spinsRemaining = spinsPerTurn;
        OnSpinsRemainingChanged?.Invoke(spinsRemaining);

        BuildSymbolMapCache();

        if (stopSpinningButton != null)
            stopSpinningButton.onClick.AddListener(StopSpinningAndCollect);

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
    }

    private void OnDestroy()
    {
        if (stopSpinningButton != null)
            stopSpinningButton.onClick.RemoveListener(StopSpinningAndCollect);
    }

    public void BeginTurn()
    {
        spinsRemaining = spinsPerTurn;
        OnSpinsRemainingChanged?.Invoke(spinsRemaining);

        // New reel phase -> clear any previous landed state.
        _currentLandedSymbols = null;
        OnCurrentLandedChanged?.Invoke(default);

        SetReelPhase(true);

        // New turn = open shutters (reveal reels) and re-enable 3D reels.
        Set3DReelsActive(true);
        if (shutterController != null)
            shutterController.OpenShutters();
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
            amount = Mathf.Max(1, v.amount);
            return true;
        }
        return false;
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

    private void SetPendingFromSymbols(List<ReelSymbolSO> syms)
    {
        pendingA = pendingD = pendingM = pendingW = 0;
        if (syms == null) return;

        // Track mapped symbols for 3-of-a-kind style bonuses.
        // IMPORTANT: bonus logic must be based on the actual landed symbols, not on summed totals,
        // otherwise combos like NULL + WLD + WLD2 can incorrectly look like "3 wild".
        var mappedTypes = new List<ResourceType>(syms.Count);
        var mappedAmounts = new List<int>(syms.Count);

        for (int i = 0; i < syms.Count; i++)
        {
            var s = syms[i];
            if (s != null && TryMapSymbol(s, out ResourceType rt, out int amt))
            {
                mappedTypes.Add(rt);
                mappedAmounts.Add(Mathf.Max(0, amt));

                switch (rt)
                {
                    case ResourceType.Attack: pendingA += amt; break;
                    case ResourceType.Defend: pendingD += amt; break;
                    case ResourceType.Magic: pendingM += amt; break;
                    case ResourceType.Wild: pendingW += amt; break;
                }
            }
        }

        // --- Bonus rules ---
        // 1) Only consider a bonus if ALL 3 reels landed on mapped (non-null) symbols.
        // 2) If all three are the same type -> +bonusAmount of that type.
        // 3) If exactly 2 are the same non-wild type and the third is Wild -> +bonusAmount of the matching type.
        //    Example: ATK, ATK, WLD => +2 ATK +1 WLD, plus bonus +1 ATK => +3 ATK +1 WLD
        //    (Wild is still paid out as Wild; it just enables the match bonus.)
        if (mappedTypes.Count == 3)
        {
            int wildCount = 0;
            int atkCount = 0;
            int defCount = 0;
            int magCount = 0;

            int maxAtkAmt = 0, maxDefAmt = 0, maxMagAmt = 0, maxWildAmt = 0;
            for (int i = 0; i < 3; i++)
            {
                switch (mappedTypes[i])
                {
                    case ResourceType.Attack: atkCount++; maxAtkAmt = Mathf.Max(maxAtkAmt, mappedAmounts[i]); break;
                    case ResourceType.Defend: defCount++; maxDefAmt = Mathf.Max(maxDefAmt, mappedAmounts[i]); break;
                    case ResourceType.Magic: magCount++; maxMagAmt = Mathf.Max(maxMagAmt, mappedAmounts[i]); break;
                    case ResourceType.Wild: wildCount++; maxWildAmt = Mathf.Max(maxWildAmt, mappedAmounts[i]); break;
                }
            }

            // All three same type
            if (atkCount == 3) pendingA += Mathf.Max(1, maxAtkAmt);
            else if (defCount == 3) pendingD += Mathf.Max(1, maxDefAmt);
            else if (magCount == 3) pendingM += Mathf.Max(1, maxMagAmt);
            else if (wildCount == 3) pendingW += Mathf.Max(1, maxWildAmt);
            else if (wildCount == 1)
            {
                // Two-of-a-kind + Wild
                if (atkCount == 2) pendingA += Mathf.Max(1, maxAtkAmt);
                else if (defCount == 2) pendingD += Mathf.Max(1, maxDefAmt);
                else if (magCount == 2) pendingM += Mathf.Max(1, maxMagAmt);
            }
        }

        OnPendingPayoutChanged?.Invoke(pendingA, pendingD, pendingM, pendingW);
    }

    /// <summary>
    /// Attempts to nudge a specific reel up/down one step while stopped.
    /// Updates the current landed symbols and recalculates the pending payout.
    /// </summary>
    public bool TryNudgeReel(int reelIndex, int deltaSteps)
    {
        if (!InReelPhase) return false;
        if (spinning) return false;
        if (!HasCurrentLandedSymbols) return false;

        if (reels == null || reelIndex < 0 || reelIndex >= reels.Count)
            return false;

        var entry = reels[reelIndex];
        if (entry == null || entry.reel3d == null)
            return false;

        if (!entry.reel3d.TryNudgeSteps(deltaSteps))
            return false;

        // Re-read the symbol at midrow for that reel.
        int qi;
        ReelSymbolSO sym = entry.reel3d.GetMidrowSymbolByIntersection(midrowPlane, out qi);
        if (sym == null)
            return false;

        // Ensure list size >= 3
        while (_currentLandedSymbols.Count < 3)
            _currentLandedSymbols.Add(null);

        _currentLandedSymbols[reelIndex] = sym;
        SetPendingFromSymbols(_currentLandedSymbols);

        SpinLandedInfo info = BuildSpinLandedInfo(_currentLandedSymbols);
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

        src.PlayOneShot(threeMatchSfxClip);
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

        // ✅ Play spin SFX exactly when we instruct reels to spin
        PlaySpinSfx();

        foreach (var e in three)
            e.reel3d.SpinRandom(rng, minFullRotations3D);

        while (!All3DReelsFinished(three))
            yield return null;

        var landed = new List<ReelSymbolSO>(3);
        var parts = new List<string>(3);

        for (int i = 0; i < three.Count; i++)
        {
            var entry = three[i];
            int qi;
            ReelSymbolSO sym = entry.reel3d.GetMidrowSymbolByIntersection(midrowPlane, out qi);
            landed.Add(sym);

            string id = !string.IsNullOrEmpty(entry.reelId) ? entry.reelId : $"slot{i}";
            string name = sym != null ? sym.name : "<null>";
            parts.Add($"{id}={name}(quad {qi})");
        }

        if (log3DMidRowSymbolsEachSpin)
            Debug.Log($"[ReelSpinSystem] 3D MidRow (post-select): {string.Join(" | ", parts)}");

        // ✅ Play special SFX if 3-in-a-row
        if (IsThreeOfAKind(landed))
            PlayThreeMatchSfx();

        if (_rewardModeActive)
        {
            EvaluateRewardPayout(landed);
        }
        else
        {
            // Build landed info and notify listeners (item synergies, UI, etc.)
            SpinLandedInfo info = BuildSpinLandedInfo(landed);
            OnSpinLanded?.Invoke(info);

            // Cache current landed symbols so Reelcraft can operate during this reel phase.
            _currentLandedSymbols = new List<ReelSymbolSO>(landed);
            OnCurrentLandedChanged?.Invoke(info);

            SetPendingFromSymbols(landed);

            spinsRemaining = Mathf.Max(0, spinsRemaining - 1);
            OnSpinsRemainingChanged?.Invoke(spinsRemaining);

            if (spinsRemaining <= 0)
                CollectPendingPayout();
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

    public void StopSpinningAndCollect()
    {
        // In reward mode, payouts happen immediately on stop; nothing to collect.
        if (_rewardModeActive) return;

        SetReelPhase(false);

        CollectPendingPayout();

        // After cashout, close shutters and disable reels/buttons so the "post-spin" space can be used.
        Set3DReelsActive(false);
        if (stopSpinningButton != null)
            stopSpinningButton.interactable = false;

        if (shutterController != null)
            shutterController.CloseShutters();
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

    private void CollectPendingPayout()
    {
        Debug.Log($"[ReelSpinSystem] CollectPendingPayout CALLED. pendingA={pendingA}, pendingD={pendingD}, pendingM={pendingM}, pendingW={pendingW}, spinsRemaining={spinsRemaining}");
        if (pendingA == 0 && pendingD == 0 && pendingM == 0 && pendingW == 0)
            return;

        if (resourcePool != null)
            resourcePool.Add(pendingA, pendingD, pendingM, pendingW);

        pendingA = pendingD = pendingM = pendingW = 0;

        // After payout, clear current landed symbols so Reelcraft can't modify a settled spin.
        _currentLandedSymbols = null;
        OnPendingPayoutChanged?.Invoke(pendingA, pendingD, pendingM, pendingW);
    }
}
////////////////////////////////////////////////////////////

////////////////////////////////////////////////////////////


