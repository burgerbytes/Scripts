// PATH: Assets/Scripts/UI/Reels/ReelSpinSystem.cs
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
    [SerializeField] private int seed = 12345;

    [Header("Payout Targets (optional)")]
    [Tooltip("If set, payouts add here. If null, we try to FindFirstObjectByType<TopStatusBar>().")]
    [SerializeField] private TopStatusBar topStatusBar;

    [Header("Symbol → Resource Mapping (REQUIRED)")]
    [Tooltip("Define the resource type for each ReelSymbolSO explicitly. This prevents incorrect payouts due to name-based guessing.")]
    [SerializeField] private List<SymbolResourceMapEntry> symbolResourceMap = new List<SymbolResourceMapEntry>();

    [Tooltip("If true, logs warnings when a symbol has no mapping entry.")]
    [SerializeField] private bool warnOnMissingMapping = true;

    public event Action OnSpinStarted;
    public event Action<Dictionary<string, ReelSymbolSO>> OnSpinFinished;

    private System.Random rng;
    private bool spinning;

    // Runtime lookup
    private Dictionary<ReelSymbolSO, ResourceType> mapLookup;

    private void Awake()
    {
        rng = new System.Random(seed);

        if (topStatusBar == null)
            topStatusBar = FindFirstObjectByType<TopStatusBar>();

        BuildMappingLookup();
    }

    private void OnValidate()
    {
        // Keep lookup sane in editor changes (play mode rebuild in Awake).
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

        // Ensure mapping is ready (in case assets changed after Awake)
        if (mapLookup == null || mapLookup.Count == 0)
            BuildMappingLookup();

        List<Coroutine> running = new List<Coroutine>();

        foreach (var r in reels)
        {
            if (r == null || r.ui == null) continue;
            running.Add(StartCoroutine(r.ui.SpinToRandom(rng)));
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

        // Base payout + counts
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

        // Bonus logic: 3-of-a-kind, and wild substitution only if wilds exist.
        if (midRow.Count >= 3)
        {
            // Pure matches
            if (cntA >= 3) addA += 1;
            else if (cntD >= 3) addD += 1;
            else if (cntM >= 3) addM += 1;
            else if (cntW >= 3) addW += 1;
            else if (cntW > 0)
            {
                // Wild substitution (only if at least one wild is present)
                if (cntA + cntW >= 3 && cntA > 0) addA += 1;
                else if (cntD + cntW >= 3 && cntD > 0) addD += 1;
                else if (cntM + cntW >= 3 && cntM > 0) addM += 1;
            }
        }

        if (topStatusBar != null)
            topStatusBar.AddResources(addA, addD, addM, addW);
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

            // Last one wins if duplicates exist.
            mapLookup[e.symbol] = e.resourceType;
        }
    }

    private bool TryMapSymbol(ReelSymbolSO sym, out ResourceType t)
    {
        t = default;

        if (sym == null)
            return false;

        if (mapLookup != null && mapLookup.TryGetValue(sym, out t))
            return true;

        if (warnOnMissingMapping)
            Debug.LogWarning($"[ReelSpinSystem] No symbol→resource mapping for '{sym.name}'. It will award NOTHING until mapped.", sym);

        return false;
    }
}
