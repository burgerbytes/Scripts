using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
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
    [SerializeField]
    private List<ReelSymbolSO> noPayoutSymbols = new List<ReelSymbolSO>();
    public event Action OnSpinStarted;
    public event Action<Dictionary<string, ReelSymbolSO>> OnSpinFinished;

    private System.Random rng;
    private bool spinning;

    // Runtime lookup
    private Dictionary<ReelSymbolSO, ResourceType> mapLookup;

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
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
            BuildMappingLookup();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.T))
            TrySpin();
    }

    // ✅ Compatibility for TurnSimulator (and any older callers)
    public void SpinAll()
    {
        TrySpin();
    }

    public void TrySpin()
    {
        if (spinning) return;
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

        ApplyMiddleRowPayout(midRowTypes);

        OnSpinFinished?.Invoke(resolved);
        spinning = false;
    }

    private void ApplyMiddleRowPayout(List<ResourceType> midRow)
    {
        if (midRow == null || midRow.Count == 0)
            return;

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

        // ✅ AUTHORITATIVE ADD: gameplay resource pool
        if (resourcePool != null)
        {
            resourcePool.Add(addA, addD, addM, addW);
        }
        else
        {
            // Fallback: UI-only add (won't enable gameplay spending, but avoids silent behavior)
            if (topStatusBar != null)
                topStatusBar.AddResources(addA, addD, addM, addW);

            Debug.LogWarning("[ReelSpinSystem] No ResourcePool found. Resources were applied to UI only. Add a ResourcePool to the scene.");
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
        // These should not warn or map to a resource.
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

}