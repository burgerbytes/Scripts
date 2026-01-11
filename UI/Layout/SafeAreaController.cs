using UnityEngine;

/// <summary>
/// Attach to a UI GameObject with a RectTransform (usually named "SafeArea").
/// Place all visible UI as children of this object.
/// Automatically resizes/positions to Unity's Screen.safeArea (notches, bars, gesture areas).
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class SafeAreaController : MonoBehaviour
{
    [Tooltip("If true, prints debug logs when safe area changes.")]
    [SerializeField] private bool debugLogs = false;

    private RectTransform _rt;
    private Rect _lastSafeArea;
    private ScreenOrientation _lastOrientation;
    private Vector2Int _lastResolution;

    private void Awake()
    {
        _rt = GetComponent<RectTransform>();
        ApplySafeAreaIfChanged(force: true);
    }

    private void OnEnable()
    {
        _rt = GetComponent<RectTransform>();
        ApplySafeAreaIfChanged(force: true);
    }

    private void Update()
    {
        ApplySafeAreaIfChanged(force: false);
    }

    private void ApplySafeAreaIfChanged(bool force)
    {
        if (_rt == null) _rt = GetComponent<RectTransform>();

        // In edit mode / prefab mode, Screen dimensions can be invalid (0),
        // which would cause NaNs when normalizing anchors.
        if (Screen.width <= 0 || Screen.height <= 0)
        {
            if (debugLogs)
                Debug.LogWarning("[SafeAreaController] Screen width/height invalid; skipping safe area apply.", this);
            return;
        }

        Rect safe = Screen.safeArea;

        // Some editor contexts can report a zero safe area. Don't apply it.
        if (safe.width <= 0 || safe.height <= 0)
        {
            if (debugLogs)
                Debug.LogWarning($"[SafeAreaController] Safe area invalid ({safe}); skipping.", this);
            return;
        }

        bool changed =
            force ||
            safe != _lastSafeArea ||
            Screen.orientation != _lastOrientation ||
            Screen.width != _lastResolution.x ||
            Screen.height != _lastResolution.y;

        if (!changed) return;

        _lastSafeArea = safe;
        _lastOrientation = Screen.orientation;
        _lastResolution = new Vector2Int(Screen.width, Screen.height);

        // Convert safe area rectangle from pixel coords into normalized anchor coords.
        Vector2 anchorMin = safe.position;
        Vector2 anchorMax = safe.position + safe.size;

        anchorMin.x /= Screen.width;
        anchorMin.y /= Screen.height;
        anchorMax.x /= Screen.width;
        anchorMax.y /= Screen.height;

        // Clamp in case platform returns slightly-out-of-range values.
        anchorMin = new Vector2(Mathf.Clamp01(anchorMin.x), Mathf.Clamp01(anchorMin.y));
        anchorMax = new Vector2(Mathf.Clamp01(anchorMax.x), Mathf.Clamp01(anchorMax.y));

        // Guard against NaN just in case.
        if (float.IsNaN(anchorMin.x) || float.IsNaN(anchorMin.y) ||
            float.IsNaN(anchorMax.x) || float.IsNaN(anchorMax.y))
        {
            if (debugLogs)
                Debug.LogError("[SafeAreaController] Computed NaN anchors; skipping apply.", this);
            return;
        }

        _rt.anchorMin = anchorMin;
        _rt.anchorMax = anchorMax;
        _rt.offsetMin = Vector2.zero;
        _rt.offsetMax = Vector2.zero;
    }
}
