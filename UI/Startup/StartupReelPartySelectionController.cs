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

        CacheHomePositions();
        ActivateSlot(0, instant: true);
    }

    private void OnEnable()
    {
        if (reelScroller != null)
        {
            reelScroller.AddMidrowSymbolIdChangedListener(OnMidrowSymbolIdChanged);
            reelScroller.ForceInvokeMidrowChanged();
        }

        RefreshAlly1PreviewFromCurrentMidrow();
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

            if (logFlow) Debug.Log("[StartupReelPartySelectionController] All slots chosen.", this);
            return;
        }

        StartCoroutine(AdvanceToNextReelRoutine(fromSlot, _activeSlot));
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
