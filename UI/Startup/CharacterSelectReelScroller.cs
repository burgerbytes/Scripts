using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class CharacterSelectReelScroller : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button upButton;
    [SerializeField] private Button downButton;

    [Header("Active Reel (runtime)")]
    [SerializeField] private Reel3DColumn activeReel;

    [Header("Midrow Query (optional)")]
    [SerializeField] private GameObject midrowPlane;

    [Header("Nudge Animation")]
    [SerializeField] private float nudgeDurationSeconds = 0.14f;
    [SerializeField] private AnimationCurve nudgeEase;

    [Header("Events")]
    [SerializeField] private UnityEvent<string> onMidrowSymbolIdChanged;

    [Header("Debug")]
    [SerializeField] private bool logMidrow = false;

    private Coroutine _postNudgeRoutine;

    private void Awake()
    {
        if (upButton != null)
            upButton.onClick.AddListener(OnUp);

        if (downButton != null)
            downButton.onClick.AddListener(OnDown);

        RefreshButtons();
    }

    public void SetActiveReel(Reel3DColumn reel)
    {
        activeReel = reel;
        RefreshButtons();
        ForceInvokeMidrowChanged();
    }

    public void SetMidrowPlane(GameObject plane)
    {
        midrowPlane = plane;
        ForceInvokeMidrowChanged();
    }

    public void AddMidrowSymbolIdChangedListener(UnityAction<string> listener)
    {
        if (listener == null) return;
        onMidrowSymbolIdChanged?.AddListener(listener);
    }

    public void RemoveMidrowSymbolIdChangedListener(UnityAction<string> listener)
    {
        if (listener == null) return;
        onMidrowSymbolIdChanged?.RemoveListener(listener);
    }

    public void ForceInvokeMidrowChanged()
    {
        LogMidrow(forceEvenIfNotLogging: true);
    }

    public bool TryGetCurrentMidrowSymbolId(out string symbolId)
    {
        symbolId = null;

        int qi, mult;
        var sym = GetMidrowSymbol(out qi, out mult);
        if (sym == null) return false;

        symbolId = sym.id;
        return !string.IsNullOrEmpty(symbolId);
    }

    private void OnUp()
    {
        if (activeReel == null) return;

        if (activeReel.TryNudgeStepsAnimated(+1, nudgeDurationSeconds, nudgeEase))
            BeginPostNudgeUpdate();
    }

    private void OnDown()
    {
        if (activeReel == null) return;

        if (activeReel.TryNudgeStepsAnimated(-1, nudgeDurationSeconds, nudgeEase))
            BeginPostNudgeUpdate();
    }

    private void BeginPostNudgeUpdate()
    {
        RefreshButtons();

        if (_postNudgeRoutine != null)
            StopCoroutine(_postNudgeRoutine);

        _postNudgeRoutine = StartCoroutine(PostNudgeUpdateRoutine());
    }

    private IEnumerator PostNudgeUpdateRoutine()
    {
        // Wait until the reel finishes animating
        while (activeReel != null && activeReel.IsNudging)
            yield return null;

        LogMidrow();
        RefreshButtons();
        _postNudgeRoutine = null;
    }

    private void RefreshButtons()
    {
        bool canInteract = activeReel != null && !activeReel.IsNudging;

        if (upButton != null)
            upButton.interactable = canInteract;

        if (downButton != null)
            downButton.interactable = canInteract;
    }

    private ReelSymbolSO GetMidrowSymbol(out int qi, out int mult)
    {
        qi = 0;
        mult = 0;

        if (activeReel == null || midrowPlane == null)
            return null;

        return activeReel.GetMidrowSymbolAndMultiplier(midrowPlane, out qi, out mult);
    }

    private void LogMidrow(bool forceEvenIfNotLogging = false)
    {
        if ((!logMidrow && !forceEvenIfNotLogging) || activeReel == null || midrowPlane == null)
            return;

        int qi, mult;
        var sym = GetMidrowSymbol(out qi, out mult);

        if (sym != null)
        {
            if (logMidrow) Debug.Log($"[CharSelect] Invoke onMidrowSymbolIdChanged id={sym.id}", this);
            onMidrowSymbolIdChanged?.Invoke(sym.id);
        }

        if (logMidrow)
            Debug.Log($"[CharSelect] ActiveReel midrow={(sym != null ? sym.id : "NULL")} qi={qi} mult={mult}", this);
    }
}
