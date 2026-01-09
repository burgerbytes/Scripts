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
        public ReelStripSO strip;
        public ReelColumnUI ui;
    }

    [Serializable]
    public class SymbolResourceMapEntry
    {
        [Tooltip("The exact ReelSymbolSO asset.")]
        public ReelSymbolSO symbol;

        [Tooltip("What resource this symbol should award.")]
        public ResourceType resourceType;
    }

    [Header("Reels")]
    [SerializeField] private List<ReelEntry> reels = new List<ReelEntry>();

    [Header("Turn Spin Limit")]
    [Tooltip("How many spins the player is allowed to perform per turn.")]
    [SerializeField] private int spinsPerTurn = 3;

    [Tooltip("If true, locked reels are automatically cleared when a new turn begins.")]
    [SerializeField] private bool clearReelLocksOnNewTurn = true;

    [Header("Optional UI Controls")]
    [Tooltip("Optional: wire this to the new 'Stop spinning' button.")]
    [SerializeField] private Button stopSpinningButton;

    [Header("RNG")]
    [Tooltip("If enabled, the reel results will be deterministic across play sessions (useful for debugging).")]
    [SerializeField] private bool useFixedSeed = false;

    [Tooltip("Only used when Use Fixed Seed is enabled.")]
    [SerializeField] private int fixedSeed = 12345;

    [Header("Spin Timing")]
    [Tooltip("Time between successive reel stops. Reels start together but stop one-by-one in a random order.")]
    [SerializeField] private float stopStaggerSeconds = 0.25f;

    [Header("Payout Targets")]
    [Tooltip("Authoritative resource store. If null, we use ResourcePool.Instance or find one in scene.")]
    [SerializeField] private ResourcePool resourcePool;

    [Tooltip("Optional UI-only target. Not used as authority anymore.")]
    [SerializeField] private TopStatusBar topStatusBar;

    [Header("Symbol → Resource Mapping (REQUIRED)")]
    [Tooltip("Define the resource type for each ReelSymbolSO explicitly. This prevents incorrect payouts due to name-based guessing.")]
    [SerializeField] private List<SymbolResourceMapEntry> symbolResourceMap = new List<SymbolResourceMapEntry>();

    [Tooltip("If true, logs warnings when a symbol has no mapping entry.")]
    [SerializeField] private bool warnOnMissingMapping = true;

    [Header("Symbol Behavior")]
    [SerializeField] private List<ReelSymbolSO> noPayoutSymbols = new List<ReelSymbolSO>();

    public event Action OnSpinStarted;
    public event Action<Dictionary<string, ReelSymbolSO>> OnSpinFinished;

    public event Action<int> OnSpinsRemainingChanged;

    private System.Random rng;
    private bool spinning;

    // Runtime lookup
    private Dictionary<ReelSymbolSO, ResourceType> mapLookup;

    // Turn state
    private int spinsRemaining;
    private long pendingA, pendingD, pendingM, pendingW;
    private bool _collectAfterCurrentSpin;
    private bool _forceStopAfterCurrentSpin;

    public int SpinsRemaining => spinsRemaining;

    private void Awake()
    {
        // If you always seed with the same number, you'll always get the same spin sequence
        // every time you press Play (first spin identical, second spin identical, etc.).
        // Default behavior: randomize seed each run.
        int runtimeSeed = useFixedSeed
            ? fixedSeed
            : unchecked(Environment.TickCount * 31 + GetInstanceID());

        rng = new System.Random(runtimeSeed);

        if (resourcePool == null)
            resourcePool = ResourcePool.Instance != null ? ResourcePool.Instance : FindFirstObjectByType<ResourcePool>();

        if (topStatusBar == null)
            topStatusBar = FindFirstObjectByType<TopStatusBar>();

        BuildMappingLookup();

        BeginTurn(); // default start state
    }

    private void OnEnable()
    {
        if (stopSpinningButton != null)
            stopSpinningButton.onClick.AddListener(StopSpinningAndCollect);
    }

    private void OnDisable()
    {
        if (stopSpinningButton != null)
            stopSpinningButton.onClick.RemoveListener(StopSpinningAndCollect);
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
            BuildMappingLookup();

        spinsPerTurn = Mathf.Max(0, spinsPerTurn);
    }

    private void Update()
    {
        // Debug input: press T to spin (now limited by spinsRemaining)
        if (Input.GetKeyDown(KeyCode.T))
            TrySpin();
    }

    /// <summary>
    /// Call this at the start of the PLAYER turn.
    /// Resets spin count, clears pending payouts, and (optionally) clears reel locks.
    /// </summary>
    public void BeginTurn()
    {
        spinsRemaining = spinsPerTurn;
        pendingA = pendingD = pendingM = pendingW = 0;
        _collectAfterCurrentSpin = false;
        _forceStopAfterCurrentSpin = false;

        if (clearReelLocksOnNewTurn)
            ClearAllReelLocks();

        OnSpinsRemainingChanged?.Invoke(spinsRemaining);
    }

    /// <summary>
    /// Optional: wire to the new "Stop spinning" button.
    /// Immediately collects pending resources if not currently spinning.
    /// If currently spinning, collection happens right after the current spin resolves.
    /// </summary>
    public void StopSpinningAndCollect()
    {
        // If we're mid-spin, defer the collection until after results resolve.
        if (spinning)
        {
            _collectAfterCurrentSpin = true;
            _forceStopAfterCurrentSpin = true;
            return;
        }

        spinsRemaining = 0;
        OnSpinsRemainingChanged?.Invoke(spinsRemaining);

        CollectPendingPayout();
    }

    // ✅ Compatibility for older callers
    public void SpinAll()
    {
        TrySpin();
    }

    public void TrySpin()
    {
        if (spinning)
            return;

        if (spinsRemaining <= 0)
        {
            // No spins left this turn. Do NOT award/collect anything automatically.
            return;
        }

        StartCoroutine(SpinRoutine());
    }

    private IEnumerator SpinRoutine()
    {
        spinning = true;
        OnSpinStarted?.Invoke();

        if (mapLookup == null || mapLookup.Count == 0)
            BuildMappingLookup();

        // Start all reels together, but make them STOP one-by-one in a random order
        // by overriding each reel's duration (longer duration => later stop).
        List<ReelEntry> active = new List<ReelEntry>();
        for (int i = 0; i < reels.Count; i++)
        {
            var r = reels[i];
            if (r == null || r.ui == null) continue;
            active.Add(r);
        }

        // Build a shuffled stop order.
        List<int> order = new List<int>();
        for (int i = 0; i < active.Count; i++) order.Add(i);
        Shuffle(order, rng);

        List<Coroutine> running = new List<Coroutine>();
        for (int stopRank = 0; stopRank < order.Count; stopRank++)
        {
            ReelEntry r = active[order[stopRank]];
            float baseDuration = r.ui.ConfiguredSpinDuration;
            float durationOverride = baseDuration + (stopRank * Mathf.Max(0f, stopStaggerSeconds));

            // ReelColumnUI now handles "locked" reels by immediately yielding.
            running.Add(StartCoroutine(r.ui.SpinToRandom(rng, durationOverride)));
        }

        foreach (var c in running)
            yield return c;

        Dictionary<string, ReelSymbolSO> resolved = new Dictionary<string, ReelSymbolSO>();
        List<ResourceType> midRowTypes = new List<ResourceType>();

        foreach (var r in reels)
        {
            if (r == null || r.ui == null) continue;

            ReelSymbolSO sym = r.ui.GetMiddleRowSymbol();
            resolved[r.reelId] = sym;

            if (sym != null && TryMapSymbol(sym, out ResourceType t))
                midRowTypes.Add(t);
        }

        // Defer payout (pending) instead of awarding immediately.
        SetPendingFromMiddleRowPayout(midRowTypes);
        // after evaluating middle-row symbols and adding to pending*
        DebugLogSpinResult("AfterSpin_PendingSet");

        // Consume 1 spin now that results are resolved.
        spinsRemaining = Mathf.Max(0, spinsRemaining - 1);
        OnSpinsRemainingChanged?.Invoke(spinsRemaining);

        OnSpinFinished?.Invoke(resolved);

        spinning = false;

        // If the player pressed Stop Spinning during the spin, force collection now.
        if (_collectAfterCurrentSpin)
        {
            _collectAfterCurrentSpin = false;
            spinsRemaining = 0;
            OnSpinsRemainingChanged?.Invoke(spinsRemaining);
            CollectPendingPayout();
            yield break;
        }

        // Auto-collect when we run out of spins.
        if (spinsRemaining <= 0)
            CollectPendingPayout();
    }

    private void SetPendingFromMiddleRowPayout(List<ResourceType> midRow)
    {
        // We only award resources based on the FINAL reel state of the turn (after last spin / Stop Spinning).
        // So each spin REPLACES the pending payout instead of accumulating across spins.
        pendingA = pendingD = pendingM = pendingW = 0;
        if (midRow == null || midRow.Count == 0)
        {
            // No payout symbols: clear pending since only the latest spin matters.
            pendingA = pendingD = pendingM = pendingW = 0;
            return;
        }

        long addA = 0, addD = 0, addM = 0, addW = 0;

        int cntA = 0, cntD = 0, cntM = 0, cntW = 0;

        foreach (var t in midRow)
        {
            switch (t)
            {
                case ResourceType.Attack: cntA++; addA += 1; break;
                case ResourceType.Defense: cntD++; addD += 1; break;
                case ResourceType.Magic: cntM++; addM += 1; break;
                case ResourceType.Wild: cntW++; addW += 1; break;
            }
        }

        if (midRow.Count >= 3)
        {
            if (cntA >= 3) addA += 1;
            else if (cntD >= 3) addD += 1;
            else if (cntM >= 3) addM += 1;
            else if (cntW >= 3) addW += 1;
            else if (cntW > 0)
            {
                if (cntA + cntW >= 3 && cntA > 0) addA += 1;
                else if (cntD + cntW >= 3 && cntD > 0) addD += 1;
                else if (cntM + cntW >= 3 && cntM > 0) addM += 1;
            }
        }

        // IMPORTANT: We do NOT want payouts to accumulate across spins within a turn.
        // The player is effectively "rerolling" until they stop or run out of spins, so only the
        // most recent (current) mid-row result should be pending for collection.
        pendingA = addA;
        pendingD = addD;
        pendingM = addM;
        pendingW = addW;
    }

    private void CollectPendingPayout()
    {
        Debug.Log($"[ReelSpinSystem] CollectPendingPayout CALLED. pendingA={pendingA}, pendingD={pendingD}, pendingM={pendingM}, pendingW={pendingW}, spinsRemaining={spinsRemaining}");
        if (pendingA == 0 && pendingD == 0 && pendingM == 0 && pendingW == 0)
            return;

        // ✅ AUTHORITATIVE ADD: gameplay resource pool
        if (resourcePool != null)
        {
            resourcePool.Add(pendingA, pendingD, pendingM, pendingW);
        }
        else
        {
            // Fallback: UI-only add (won't enable gameplay spending, but avoids silent behavior)
            if (topStatusBar != null)
                topStatusBar.AddResources(pendingA, pendingD, pendingM, pendingW);

            Debug.LogWarning("[ReelSpinSystem] No ResourcePool found. Resources were applied to UI only. Add a ResourcePool to the scene.");
        }

        pendingA = pendingD = pendingM = pendingW = 0;
    }

    private void ClearAllReelLocks()
    {
        foreach (var r in reels)
        {
            if (r == null || r.ui == null) continue;
            r.ui.SetLocked(false);
        }
    }

    private void BuildMappingLookup()
    {
        if (mapLookup == null)
            mapLookup = new Dictionary<ReelSymbolSO, ResourceType>();
        else
            mapLookup.Clear();

        if (symbolResourceMap == null)
            return;

        for (int i = 0; i < symbolResourceMap.Count; i++)
        {
            var e = symbolResourceMap[i];
            if (e == null || e.symbol == null) continue;
            mapLookup[e.symbol] = e.resourceType;
        }
    }

    private bool TryMapSymbol(ReelSymbolSO sym, out ResourceType t)
    {
        t = default;

        if (sym == null)
            return false;

        // Explicit "null/blank" (or other) symbols that intentionally award nothing.
        if (noPayoutSymbols != null && noPayoutSymbols.Contains(sym))
            return false;

        if (mapLookup != null && mapLookup.TryGetValue(sym, out t))
            return true;

        if (warnOnMissingMapping)
            Debug.LogWarning($"[ReelSpinSystem] No symbol→resource mapping for '{sym.name}'. It will award NOTHING until mapped.", sym);

        return false;
    }

    private static void Shuffle<T>(IList<T> list, System.Random rng)
    {
        // Fisher–Yates shuffle
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private void DebugLogSpinResult(string reason)
    {
        Debug.Log(
            $"[SPIN RESULT] reason={reason} | " +
            $"pending: A={pendingA}, D={pendingD}, M={pendingM}, W={pendingW} | " +
            $"spinsRemaining={spinsRemaining}"
        );
    }
}
