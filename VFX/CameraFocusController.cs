using System.Collections;
using UnityEngine;

/// <summary>
/// Orthographic camera "punch-in" focus controller (no Cinemachine).
///
/// Features:
/// - Zooms in (reduces orthographicSize) then restores.
/// - Optionally pans camera to keep a target centered during the focus.
/// - Safe to call repeatedly (cancels previous focus and restores baseline first).
///
/// Typical use:
///   FocusZoomTo(heroTransform);
///   FocusZoomTo(heroTransform, multiplier:0.85f, inDur:0.10f, holdDur:0.80f, outDur:0.10f);
/// </summary>
public class CameraFocusController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("If null, uses Camera.main at runtime.")]
    [SerializeField] private Camera cam;

    [Header("Defaults")]
    [Tooltip("Multiplier applied to the current orthographicSize during focus. < 1 zooms in.")]
    [Range(0.5f, 1.0f)]
    [SerializeField] private float defaultZoomInMultiplier = 0.85f;

    [Tooltip("Seconds to zoom in.")]
    [SerializeField] private float defaultZoomInDuration = 0.10f;

    [Tooltip("Seconds to hold zoom.")]
    [SerializeField] private float defaultHoldDuration = 0.20f;

    [Tooltip("Seconds to zoom out.")]
    [SerializeField] private float defaultZoomOutDuration = 0.20f;

    [Header("Focus (optional pan)")]
    [Tooltip("If enabled, camera will pan to keep the target centered during focus.")]
    [SerializeField] private bool panToTarget = true;

    [Tooltip("Offset from the target position while focusing (world units).")]
    [SerializeField] private Vector3 focusOffset = new Vector3(0f, 0.75f, 0f);

    [Tooltip("How strongly to smooth the pan. Higher = snappier.")]
    [Range(1f, 40f)]
    [SerializeField] private float panSmoothing = 16f;

    [Header("Debug")]
    [SerializeField] private bool logDebug = false;

    private Coroutine _routine;

    private float _baseOrthoSize;
    private Vector3 _baseCamPos;

    private void Awake()
    {
        if (cam == null) cam = Camera.main;
        CacheBase();
    }

    private void CacheBase()
    {
        if (cam == null) return;
        _baseOrthoSize = cam.orthographicSize;
        _baseCamPos = cam.transform.position;
    }

    public void FocusZoomTo(Transform target)
    {
        FocusZoomTo(target, defaultZoomInMultiplier, defaultZoomInDuration, defaultHoldDuration, defaultZoomOutDuration);
    }

    public void FocusZoomTo(Transform target, float multiplier, float inDur, float holdDur, float outDur)
    {
        if (cam == null || target == null) return;

        // Cancel any in-progress focus and restore first.
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
            RestoreImmediate();
        }

        // Re-cache base in case another system changed the camera.
        CacheBase();

        _routine = StartCoroutine(FocusRoutine(target, multiplier, inDur, holdDur, outDur));
    }

    public void CancelAndRestore()
    {
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }

        RestoreImmediate();
    }

    private void RestoreImmediate()
    {
        if (cam == null) return;
        cam.orthographicSize = _baseOrthoSize;
        cam.transform.position = _baseCamPos;
    }

    private IEnumerator FocusRoutine(Transform target, float multiplier, float inDur, float holdDur, float outDur)
    {
        if (cam == null || target == null) yield break;
        if (!cam.orthographic)
        {
            Debug.LogWarning("[CameraFocusController] Camera is not orthographic; this controller is intended for orthographic cameras.", this);
        }

        if (logDebug) Debug.Log($"[CameraFocus] Start target='{target.name}'", this);

        float fromSize = _baseOrthoSize;
        float toSize = _baseOrthoSize * Mathf.Clamp(multiplier, 0.1f, 2f);

        Vector3 fromPos = _baseCamPos;
        Vector3 toPos = fromPos;
        if (panToTarget)
        {
            Vector3 desired = target.position + focusOffset;
            desired.z = fromPos.z;
            toPos = desired;
        }

        // Zoom/Pan in
        yield return Tween(fromSize, toSize, fromPos, toPos, Mathf.Max(0.0001f, inDur), followDuringTween: target);

        // Hold (keep following target if it moves)
        float t = 0f;
        while (t < Mathf.Max(0f, holdDur))
        {
            t += Time.deltaTime;

            if (panToTarget && target != null)
            {
                Vector3 desired = target.position + focusOffset;
                desired.z = cam.transform.position.z;
                cam.transform.position = Vector3.Lerp(
                    cam.transform.position,
                    desired,
                    1f - Mathf.Exp(-panSmoothing * Time.deltaTime)
                );
            }

            yield return null;
        }

        // Zoom/Pan out back to baseline
        yield return Tween(toSize, fromSize, cam.transform.position, _baseCamPos, Mathf.Max(0.0001f, outDur), followDuringTween: null);

        if (logDebug) Debug.Log($"[CameraFocus] End target='{target.name}'", this);
        _routine = null;
    }

    private IEnumerator Tween(float fromSize, float toSize, Vector3 fromPos, Vector3 toPos, float duration, Transform followDuringTween)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float u = Mathf.Clamp01(elapsed / duration);
            float s = u * u * (3f - 2f * u); // SmoothStep

            cam.orthographicSize = Mathf.Lerp(fromSize, toSize, s);

            if (panToTarget)
            {
                Vector3 desired = toPos;
                if (followDuringTween != null)
                {
                    desired = followDuringTween.position + focusOffset;
                    desired.z = fromPos.z;
                }
                cam.transform.position = Vector3.Lerp(fromPos, desired, s);
            }

            yield return null;
        }

        cam.orthographicSize = toSize;
        if (panToTarget) cam.transform.position = toPos;
    }
}
