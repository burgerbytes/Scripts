// GUID: 1b9947b6d65d049459a446a098bd7cb3
////////////////////////////////////////////////////////////
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
    }

    public enum ResourceType { Attack, Defend, Magic, Wild }

    [Header("Reels")]
    [SerializeField] private List<ReelEntry> reels = new List<ReelEntry>();

    [Header("Spin Control")]
    [SerializeField] private int spinsPerTurn = 3;
    [SerializeField] private Button stopSpinningButton;

    [Header("Spin SFX")]
    [Tooltip("AudioSource used to play the reel spin sound. If left null, we'll try GetComponent<AudioSource>().")]
    [SerializeField] private AudioSource spinSfxSource;

    [Tooltip("Optional clip override. If null, uses spinSfxSource.clip.")]
    [SerializeField] private AudioClip spinSfxClip;

    [Tooltip("Play the spin sound when a spin begins.")]
    [SerializeField] private bool playSpinSfx = true;

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
    /// Fired when a non-reward-mode spin lands. Provides the landed symbols and a computed summary.
    /// </summary>
    public event Action<SpinLandedInfo> OnSpinLanded;

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

    // State
    private bool spinning;
    private int spinsRemaining;

    // Pending payout (deferred until stop/collect or out of spins)
    private int pendingA;
    private int pendingD;
    private int pendingM;
    private int pendingW;

    // Map cache
    private Dictionary<ReelSymbolSO, ResourceType> _symbolMap;

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

        if (resourcePool == null)
            resourcePool = ResourcePool.Instance;

        // If you put the AudioSource on the same GameObject as this script, this will auto-find it.
        if (spinSfxSource == null)
            spinSfxSource = GetComponent<AudioSource>();
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
        _symbolMap = new Dictionary<ReelSymbolSO, ResourceType>();
        foreach (var e in symbolToResourceMap)
        {
            if (e == null || e.symbol == null) continue;
            _symbolMap[e.symbol] = e.resourceType;
        }
    }

    private bool TryMapSymbol(ReelSymbolSO sym, out ResourceType rt)
    {
        rt = ResourceType.Attack;
        if (sym == null) return false;
        if (_symbolMap == null) BuildSymbolMapCache();
        return _symbolMap.TryGetValue(sym, out rt);
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

        foreach (var s in syms)
        {
            if (s != null && TryMapSymbol(s, out ResourceType rt))
            {
                switch (rt)
                {
                    case ResourceType.Attack: pendingA++; break;
                    case ResourceType.Defend: pendingD++; break;
                    case ResourceType.Magic: pendingM++; break;
                    case ResourceType.Wild: pendingW++; break;
                }
            }
        }
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

        // If an override clip is set, use it. Otherwise use the AudioSource's clip.
        AudioClip clipToPlay = (spinSfxClip != null) ? spinSfxClip : spinSfxSource.clip;

        if (clipToPlay == null)
        {
            Debug.LogWarning("[ReelSpinSystem] Spin SFX requested but no clip is assigned (spinSfxClip and spinSfxSource.clip are null).", this);
            return;
        }

        // PlayOneShot is safer for SFX (doesn't care if source is already playing).
        spinSfxSource.PlayOneShot(clipToPlay);
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

        // ✅ Play SFX at the exact moment we're about to start spinning the reels
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

        if (_rewardModeActive)
        {
            EvaluateRewardPayout(landed);
        }
        else
        {
            // Build landed info and notify listeners (item synergies, UI, etc.)
            SpinLandedInfo info = BuildSpinLandedInfo(landed);
            OnSpinLanded?.Invoke(info);

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

        CollectPendingPayout();
    }

    private void CollectPendingPayout()
    {
        Debug.Log($"[ReelSpinSystem] CollectPendingPayout CALLED. pendingA={pendingA}, pendingD={pendingD}, pendingM={pendingM}, pendingW={pendingW}, spinsRemaining={spinsRemaining}");
        if (pendingA == 0 && pendingD == 0 && pendingM == 0 && pendingW == 0)
            return;

        if (resourcePool != null)
            resourcePool.Add(pendingA, pendingD, pendingM, pendingW);

        pendingA = pendingD = pendingM = pendingW = 0;
    }
}
////////////////////////////////////////////////////////////
