// GUID: 8119e84ce7006d14d8b69d2fbcb3a6b1
////////////////////////////////////////////////////////////
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reel-driven party selection flow (CLEAN version):
/// - Midrow symbol updates ONLY Ally1 preview UI (StartupClassSelectionPanel.PreviewAlly1BySymbolId)
/// - Clicking NextHero locks in the current midrow hero for the current party slot, then transitions to the next reel.
/// 
/// Notes:
/// - This script uses reflection to:
///   (1) obtain the currently previewed prefab from StartupClassSelectionPanel (works across panel variants)
///   (2) store chosen prefabs into StartupPartySelectionData (works across different API naming)
/// This avoids compile errors when your helper APIs differ.
/// </summary>
public class StartupReelPartySelectionController : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private CharacterSelectReelScroller reelScroller;
    [SerializeField] private StartupClassSelectionPanel selectionPanel;

    [Header("Buttons")]
    [SerializeField] private Button nextHeroButton;
    [SerializeField] private Button previousReelButton;

    [Header("Reels (in pick order)")]
    [SerializeField] private Reel3DColumn[] reels = new Reel3DColumn[3];
    [SerializeField] private Transform[] reelRoots = new Transform[3];
    [SerializeField] private CanvasGroup[] reelCanvasGroups = new CanvasGroup[3];

    [Header("Transition Animation")]
    [SerializeField] private bool animateBetweenReels = true;
    [SerializeField] private float transitionDuration = 0.35f;
    [SerializeField] private float slideRightDistance = 700f;
    [SerializeField] private float incomingStartLeftDistance = 700f;
    [SerializeField] private AnimationCurve transitionEase = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Selection")]
    [SerializeField] private int partySize = 3;

    [Header("Debug")]
    [SerializeField] private bool logFlow = true;

    private int _activeSlot = 0;
    private bool _busy;
    private Vector3[] _reelHomeLocalPos = new Vector3[3];

    private void Awake()
    {
        if (nextHeroButton != null)
        {
            nextHeroButton.onClick.RemoveAllListeners();
            nextHeroButton.onClick.AddListener(OnNextHero);
        }

        if (previousReelButton != null)
        {
            previousReelButton.onClick.RemoveAllListeners();
            previousReelButton.onClick.AddListener(OnPreviousReel);
        }

        CacheHomePositions();
        ActivateSlot(0, instant: true);
        UpdateNavButtons();
    }

    private void OnEnable()
    {
        if (reelScroller != null)
        {
            reelScroller.AddMidrowSymbolIdChangedListener(OnMidrowSymbolIdChanged);
            reelScroller.ForceInvokeMidrowChanged();
        }

        RefreshAlly1PreviewFromCurrentMidrow();
    
        UpdateNavButtons();
    }


    private void OnDisable()
    {
        if (reelScroller != null)
            reelScroller.RemoveMidrowSymbolIdChangedListener(OnMidrowSymbolIdChanged);
    }

    private void CacheHomePositions()
    {
        for (int i = 0; i < _reelHomeLocalPos.Length; i++)
        {
            if (reelRoots != null && i < reelRoots.Length && reelRoots[i] != null)
                _reelHomeLocalPos[i] = reelRoots[i].localPosition;
            else
                _reelHomeLocalPos[i] = Vector3.zero;
        }
    }

    private void OnMidrowSymbolIdChanged(string symbolId)
    {
        if (_busy) return;
        if (selectionPanel == null) return;

        // CLEAN: always drive Ally1 preview only
        selectionPanel.PreviewAlly1BySymbolId(symbolId);
    }

    private void RefreshAlly1PreviewFromCurrentMidrow()
    {
        if (_busy) return;
        if (reelScroller == null || selectionPanel == null) return;

        if (reelScroller.TryGetCurrentMidrowSymbolId(out string symbolId))
            selectionPanel.PreviewAlly1BySymbolId(symbolId);
    }

    private void OnNextHero()
    {
        if (_busy) return;

        if (reelScroller == null || selectionPanel == null)
        {
            Debug.LogWarning("[StartupReelPartySelectionController] Missing reelScroller or selectionPanel reference.", this);
            return;
        }

        if (!reelScroller.TryGetCurrentMidrowSymbolId(out string symbolId))
        {
            Debug.LogWarning("[StartupReelPartySelectionController] Could not read current midrow symbol id.", this);
            return;
        }

        // Ensure Ally1 preview matches current midrow
        selectionPanel.PreviewAlly1BySymbolId(symbolId);

        // Get the prefab that Ally1 is currently previewing (works across panel variants)
        GameObject chosenPrefab = TryGetAlly1PreviewPrefab(selectionPanel);
        if (chosenPrefab == null)
        {
            Debug.LogWarning("[StartupReelPartySelectionController] Could not resolve chosen prefab from panel. (ReelSymbols updating suggests it should exist.)", this);
            return;
        }

        // Store into StartupPartySelectionData using reflection so we don't depend on a specific API name
        bool stored = TryStoreChosenPrefabIntoStartupPartySelectionData(_activeSlot, chosenPrefab);
        // Also store via the concrete API (keeps things explicit and future-proof).
        StartupPartySelectionData.SetPartyMemberPrefab(_activeSlot, chosenPrefab);

        // Update selected party portraits UI.
        Sprite portrait = null;
        var hs = chosenPrefab.GetComponent<HeroStats>();
        if (hs != null) portrait = hs.Portrait;
        selectionPanel.SetSelectedPartySlotPortrait(_activeSlot, portrait);


        if (logFlow)
        {
            Debug.Log($"[StartupReelPartySelectionController] Locked party slot {_activeSlot} to prefab='{chosenPrefab.name}'. stored={stored}", this);
        }

        int fromSlot = _activeSlot;
        _activeSlot++;

        // Finished picking?
        if (_activeSlot >= Mathf.Clamp(partySize, 1, 3))
        {
            if (nextHeroButton != null)
                nextHeroButton.interactable = false;

            UpdateNavButtons();
            if (logFlow) Debug.Log("[StartupReelPartySelectionController] All slots chosen.", this);
            return;
        }

        UpdateNavButtons();

        StartCoroutine(AdvanceToNextReelRoutine(fromSlot, _activeSlot));
    }

    
    private void UpdateNavButtons()
    {
        // Previous is only available once at least one hero has been locked in.
        if (previousReelButton != null)
            previousReelButton.interactable = !_busy && _activeSlot > 0;

        // Next is available while we still have slots to pick.
        if (nextHeroButton != null)
        {
            int partySize = Mathf.Clamp(this.partySize, 1, 3);
            nextHeroButton.interactable = !_busy && _activeSlot < partySize;
        }
    }

    private void OnPreviousReel()
    {
        if (_busy) return;

        if (reelScroller == null || selectionPanel == null)
        {
            Debug.LogWarning("[StartupReelPartySelectionController] Missing reelScroller or selectionPanel reference.", this);
            return;
        }

        // Nothing to undo yet
        if (_activeSlot <= 0)
            return;

        int fromSlot = _activeSlot;          // current reel index (the one we are about to pick for)
        int slotToClear = _activeSlot - 1;   // last chosen party slot
        int toSlot = slotToClear;            // previous reel to return to

        // Clear selection data + portrait for the slot we're undoing
        StartupPartySelectionData.SetPartyMemberPrefab(slotToClear, null);
        selectionPanel.ClearSelectedPartySlotPortrait(slotToClear);

        // Move back one slot
        _activeSlot = slotToClear;

        UpdateNavButtons();

        StartCoroutine(BackToPreviousReelRoutine(fromSlot, toSlot));
    }

    private IEnumerator BackToPreviousReelRoutine(int fromSlot, int toSlot)
    {
        _busy = true;

        // Temporarily disable nudge by clearing active reel.
        if (reelScroller != null)
            reelScroller.SetActiveReel(null);

        if (animateBetweenReels && transitionDuration > 0f)
            yield return SlideFadeTransitionBack(fromSlot, toSlot);

        ActivateSlot(toSlot, instant: true);

        _busy = false;

        UpdateNavButtons();
        RefreshAlly1PreviewFromCurrentMidrow();
    }

    private IEnumerator SlideFadeTransitionBack(int fromSlot, int toSlot)
    {
        if (reelRoots == null || fromSlot >= reelRoots.Length || toSlot >= reelRoots.Length)
            yield break;

        Transform fromRoot = reelRoots[fromSlot];
        Transform toRoot = reelRoots[toSlot];
        if (fromRoot == null || toRoot == null)
            yield break;

        // Reverse direction: current slides LEFT out, previous comes from RIGHT.
        Vector3 fromStart = fromRoot.localPosition;
        Vector3 fromEnd = fromStart - new Vector3(slideRightDistance, 0f, 0f);

        Vector3 toHome = _reelHomeLocalPos[toSlot];
        Vector3 toStart = toHome + new Vector3(incomingStartLeftDistance, 0f, 0f);

        toRoot.gameObject.SetActive(true);
        toRoot.localPosition = toStart;

        CanvasGroup fromCg = (reelCanvasGroups != null && fromSlot < reelCanvasGroups.Length) ? reelCanvasGroups[fromSlot] : null;
        CanvasGroup toCg = (reelCanvasGroups != null && toSlot < reelCanvasGroups.Length) ? reelCanvasGroups[toSlot] : null;

        if (toCg != null) toCg.alpha = 1f;
        if (fromCg != null) fromCg.alpha = 1f;

        float t = 0f;
        while (t < transitionDuration)
        {
            float u = Mathf.Clamp01(t / transitionDuration);
            float e = (transitionEase != null) ? transitionEase.Evaluate(u) : u;

            fromRoot.localPosition = Vector3.Lerp(fromStart, fromEnd, e);
            toRoot.localPosition = Vector3.Lerp(toStart, toHome, e);

            if (fromCg != null)
                fromCg.alpha = Mathf.Lerp(1f, 0f, e);

            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (fromCg != null) fromCg.alpha = 0f;
        fromRoot.gameObject.SetActive(false);

        toRoot.localPosition = toHome;
        if (toCg != null) toCg.alpha = 1f;
    }

    /// <summary>
    /// Called by the Start button flow to hard-disable the class selection reels/buttons so they can't be interacted with.
    /// </summary>
    public void DisableSelectionReels()
    {
        // Called after pressing Start.
        // We want the current reel to slide out and disappear (like normal Next transitions),
        // then fully disable all class-selection reel visuals and interaction.
        if (_busy) return;

        StartCoroutine(DisableSelectionReelsRoutine());
    }

    private IEnumerator DisableSelectionReelsRoutine()
    {
        _busy = true;

        // Disable navigation immediately.
        if (nextHeroButton != null) nextHeroButton.interactable = false;
        if (previousReelButton != null) previousReelButton.interactable = false;

        // Stop reel input/nudging.
        if (reelScroller != null)
            reelScroller.SetActiveReel(null);

        // Slide the currently-visible reel out (no incoming reel).
        if (animateBetweenReels && transitionDuration > 0f)
        {
            int slot = Mathf.Clamp(_activeSlot, 0, (reelRoots != null ? reelRoots.Length - 1 : 0));
            yield return SlideFadeOutOnly(slot);
        }

        // Hard-disable all reel visuals/interaction.
        if (reelRoots != null)
        {
            for (int i = 0; i < reelRoots.Length; i++)
            {
                if (reelRoots[i] != null)
                    reelRoots[i].gameObject.SetActive(false);
            }
        }
    }

    private IEnumerator SlideFadeOutOnly(int slot)
    {
        if (reelRoots == null || slot < 0 || slot >= reelRoots.Length)
            yield break;

        Transform root = reelRoots[slot];
        if (root == null)
            yield break;

        Vector3 fromStart = root.localPosition;
        Vector3 fromEnd = fromStart + new Vector3(slideRightDistance, 0f, 0f);

        CanvasGroup cg = (reelCanvasGroups != null && slot < reelCanvasGroups.Length) ? reelCanvasGroups[slot] : null;
        if (cg != null) cg.alpha = 1f;

        float t = 0f;
        while (t < transitionDuration)
        {
            float u = Mathf.Clamp01(t / transitionDuration);
            float e = (transitionEase != null) ? transitionEase.Evaluate(u) : u;

            root.localPosition = Vector3.Lerp(fromStart, fromEnd, e);
            if (cg != null)
                cg.alpha = Mathf.Lerp(1f, 0f, e);

            t += Time.unscaledDeltaTime;
            yield return null;
        }

        root.localPosition = fromEnd;
        if (cg != null) cg.alpha = 0f;

        root.gameObject.SetActive(false);
    }



private IEnumerator AdvanceToNextReelRoutine(int fromSlot, int toSlot)
    {
        _busy = true;

        // Temporarily disable nudge by clearing active reel.
        if (reelScroller != null)
            reelScroller.SetActiveReel(null);

        if (animateBetweenReels && transitionDuration > 0f)
            yield return SlideFadeTransition(fromSlot, toSlot);

        ActivateSlot(toSlot, instant: true);

        if (reelScroller != null)
            reelScroller.ForceInvokeMidrowChanged();

        RefreshAlly1PreviewFromCurrentMidrow();

        _busy = false;
        UpdateNavButtons();
    }

    private void ActivateSlot(int slot, bool instant)
    {
        if (slot < 0 || slot >= reels.Length) return;

        if (reelRoots != null && slot < reelRoots.Length && reelRoots[slot] != null)
            reelRoots[slot].gameObject.SetActive(true);

        if (reelCanvasGroups != null && slot < reelCanvasGroups.Length && reelCanvasGroups[slot] != null)
            reelCanvasGroups[slot].alpha = 1f;

        if (reelRoots != null && slot < reelRoots.Length && reelRoots[slot] != null)
            reelRoots[slot].localPosition = _reelHomeLocalPos[slot];

        if (reelScroller != null)
            reelScroller.SetActiveReel(reels[slot]);

        RefreshAlly1PreviewFromCurrentMidrow();
    }

    private IEnumerator SlideFadeTransition(int fromSlot, int toSlot)
    {
        if (reelRoots == null || fromSlot >= reelRoots.Length || toSlot >= reelRoots.Length)
            yield break;

        Transform fromRoot = reelRoots[fromSlot];
        Transform toRoot = reelRoots[toSlot];
        if (fromRoot == null || toRoot == null)
            yield break;

        Vector3 fromStart = fromRoot.localPosition;
        Vector3 fromEnd = fromStart + new Vector3(slideRightDistance, 0f, 0f);

        Vector3 toHome = _reelHomeLocalPos[toSlot];
        Vector3 toStart = toHome - new Vector3(incomingStartLeftDistance, 0f, 0f);

        toRoot.gameObject.SetActive(true);
        toRoot.localPosition = toStart;

        CanvasGroup fromCg = (reelCanvasGroups != null && fromSlot < reelCanvasGroups.Length) ? reelCanvasGroups[fromSlot] : null;
        CanvasGroup toCg = (reelCanvasGroups != null && toSlot < reelCanvasGroups.Length) ? reelCanvasGroups[toSlot] : null;

        if (toCg != null) toCg.alpha = 1f;
        if (fromCg != null) fromCg.alpha = 1f;

        float t = 0f;
        while (t < transitionDuration)
        {
            float u = Mathf.Clamp01(t / transitionDuration);
            float e = (transitionEase != null) ? transitionEase.Evaluate(u) : u;

            fromRoot.localPosition = Vector3.Lerp(fromStart, fromEnd, e);
            toRoot.localPosition = Vector3.Lerp(toStart, toHome, e);

            if (fromCg != null)
                fromCg.alpha = Mathf.Lerp(1f, 0f, e);

            t += Time.deltaTime;
            yield return null;
        }

        fromRoot.localPosition = fromEnd;
        toRoot.localPosition = toHome;

        if (fromCg != null) fromCg.alpha = 0f;
        if (toCg != null) toCg.alpha = 1f;

        fromRoot.gameObject.SetActive(false);
    }

    // ============================================================
    // Reflection helpers
    // ============================================================

    private static GameObject TryGetAlly1PreviewPrefab(StartupClassSelectionPanel panel)
    {
        if (panel == null) return null;

        Type t = panel.GetType();

        // Prefer explicit helper if present
        string[] methodNames =
        {
            "GetAlly1PreviewPrefab",
            "GetSelectedAlly1Prefab",
            "GetSelectedPrefabFromAlly1",
            "GetSelectedPrefabForSlot"
        };

        foreach (var mName in methodNames)
        {
            MethodInfo mi = null;

            // GetSelectedPrefabForSlot(int slot)
            if (mName == "GetSelectedPrefabForSlot")
            {
                mi = t.GetMethod(mName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null);
                if (mi != null)
                {
                    try
                    {
                        object o = mi.Invoke(panel, new object[] { 0 });
                        if (o is GameObject go && go != null) return go;
                    }
                    catch { }
                }
                continue;
            }

            mi = t.GetMethod(mName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (mi != null)
            {
                try
                {
                    object o = mi.Invoke(panel, null);
                    if (o is GameObject go && go != null) return go;
                }
                catch { }
            }
        }

        return null;
    }

    private bool TryStoreChosenPrefabIntoStartupPartySelectionData(int slot, GameObject prefab)
    {
        if (prefab == null) return false;

        Type t = typeof(StartupPartySelectionData);

        // Try common static method names first
        string[] methodNames =
        {
            "SetChosenPrefab",
            "SetChosenHeroPrefab",
            "SetHeroPrefab",
            "SetSelectedPrefab",
            "SetPartyMemberPrefab",
            "SetSlotPrefab",
            "SetHeroAt"
        };

        foreach (string name in methodNames)
        {
            MethodInfo mi = t.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(GameObject) }, null);
            if (mi != null)
            {
                try
                {
                    mi.Invoke(null, new object[] { slot, prefab });
                    return true;
                }
                catch { }
            }
        }

        // Try common static array/list fields/properties
        string[] containerNames =
        {
            "SelectedPrefabs",
            "selectedPrefabs",
            "PartyPrefabs",
            "partyPrefabs",
            "ChosenPrefabs",
            "chosenPrefabs"
        };

        foreach (string name in containerNames)
        {
            // Property
            var pi = t.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (pi != null)
            {
                try
                {
                    object container = pi.GetValue(null);
                    if (TryAssignIntoContainer(container, slot, prefab)) return true;
                }
                catch { }
            }

            // Field
            var fi = t.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (fi != null)
            {
                try
                {
                    object container = fi.GetValue(null);
                    if (TryAssignIntoContainer(container, slot, prefab)) return true;
                }
                catch { }
            }
        }

        if (logFlow)
            Debug.LogWarning("[StartupReelPartySelectionController] Could not find a compatible API/field on StartupPartySelectionData to store chosen prefab. (This might still be fine if another system reads selection differently.)", this);

        return false;
    }

    private static bool TryAssignIntoContainer(object container, int slot, GameObject prefab)
    {
        if (container == null) return false;

        // GameObject[]
        if (container is GameObject[] arr)
        {
            if (slot >= 0 && slot < arr.Length)
            {
                arr[slot] = prefab;
                return true;
            }
            return false;
        }

        // IList<GameObject> or non-generic IList
        if (container is System.Collections.IList list)
        {
            if (slot < 0) return false;

            // Ensure capacity
            while (list.Count <= slot)
                list.Add(null);

            list[slot] = prefab;
            return true;
        }

        return false;
    }
}


////////////////////////////////////////////////////////////
