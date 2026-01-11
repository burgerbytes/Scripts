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
        public Reel3DColumn reel3d;
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

    // Public API expected by other scripts
    public event Action<int> OnSpinsRemainingChanged;
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

    private void Awake()
    {
        spinsRemaining = spinsPerTurn;
        OnSpinsRemainingChanged?.Invoke(spinsRemaining);

        BuildSymbolMapCache();

        if (stopSpinningButton != null)
            stopSpinningButton.onClick.AddListener(StopSpinningAndCollect);

        if (resourcePool == null)
            resourcePool = ResourcePool.Instance;
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

    private IEnumerator Spin3DPostSelectRoutine(System.Random rng)
    {
        var three = GetFirstThree3DReels();
        if (three.Count == 0)
        {
            spinning = false;
            _threeDSpinRoutine = null;
            yield break;
        }

        // Spin all reels. We do not pre-pick a landing symbol; SpinRandom() chooses random steps >= 1 full rev.
        foreach (var e in three)
            e.reel3d.SpinRandom(rng, minFullRotations3D);

        while (!All3DReelsFinished(three))
            yield return null;

        // After stopping, determine which quad intersects MidrowPlane and read its fixed symbol
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

        SetPendingFromSymbols(landed);

        spinsRemaining = Mathf.Max(0, spinsRemaining - 1);
        OnSpinsRemainingChanged?.Invoke(spinsRemaining);

        spinning = false;

        if (spinsRemaining <= 0)
            CollectPendingPayout();

        _threeDSpinRoutine = null;
    }

    public void TrySpin()
    {
        if (spinning) return;
        if (spinsRemaining <= 0) return;

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

        spinning = false;
    }

    public void StopSpinningAndCollect()
    {
        // Post-select mode: if a spin is in flight, let it finish for visuals.
        // We only collect whatever pending is currently computed.
        CollectPendingPayout();
    }

    void CollectPendingPayout()
    {
        Debug.Log($"[ReelSpinSystem] CollectPendingPayout CALLED. pendingA={pendingA}, pendingD={pendingD}, pendingM={pendingM}, pendingW={pendingW}, spinsRemaining={spinsRemaining}");
        if (pendingA == 0 && pendingD == 0 && pendingM == 0 && pendingW == 0)
            return;

        if (resourcePool != null)
            resourcePool.Add(pendingA, pendingD, pendingM, pendingW);

        pendingA = pendingD = pendingM = pendingW = 0;
    }
}
