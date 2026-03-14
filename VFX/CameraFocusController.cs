using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Orthographic camera "punch-in" focus controller (no Cinemachine).
///
/// Features:
/// - Zooms in (reduces orthographicSize) then restores.
/// - Optionally pans camera to keep a target centered during the focus.
/// - Can optionally pause a specific Animator once max zoom is reached.
/// - Safe to call repeatedly (cancels previous focus and restores baseline first).
/// </summary>
public class CameraFocusController : MonoBehaviour
{
    [Header("Master Enable")]
    [Tooltip("Master switch to enable/disable all camera focus behavior.")]
    [SerializeField] private bool enableCameraFocus = true;

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

    [Header("Optional Max-Zoom Animation Pause")]
    [Tooltip("Animator speed to use while paused at max zoom. Normally 0.")]
    [Range(0f, 1f)]
    [SerializeField] private float defaultPausedAnimatorSpeed = 0f;

    [Tooltip("Seconds to pause the animator once max zoom is reached.")]
    [Min(0f)]
    [SerializeField] private float defaultAnimatorPauseDuration = 1f;

    [Header("Combatant Isolation During Focus")]
    [Tooltip("If true, non-participating combatant sprites/models are hidden during the focus shot.")]
    [SerializeField] private bool isolateNonParticipants = true;

    [Tooltip("If true, disable SpriteRenderer, MeshRenderer, and SkinnedMeshRenderer on non-participants during focus.")]
    [SerializeField] private bool isolateRenderersOnly = true;

    [Header("Debug")]
    [SerializeField] private bool logDebug = false;

    public bool EnableCameraFocus
    {
        get => enableCameraFocus;
        set
        {
            enableCameraFocus = value;
            if (!enableCameraFocus)
                CancelAndRestore();
        }
    }

    private Coroutine _routine;

    private float _baseOrthoSize;
    private Vector3 _baseCamPos;

    private Animator _activeSlowedAnimator;
    private float _activeSlowedAnimatorOriginalSpeed = 1f;

    private readonly List<RendererState> _isolatedRenderers = new List<RendererState>(32);

    private struct RendererState
    {
        public Renderer renderer;
        public bool wasEnabled;
    }

    private void Awake()
    {
        if (cam == null)
            cam = GetComponentInChildren<Camera>(true);
        if (cam == null)
            cam = Camera.main;

        CacheBase();
    }

    private void OnDisable()
    {
        CancelAndRestore();
    }

    private void CacheBase()
    {
        if (cam == null) return;
        _baseOrthoSize = cam.orthographicSize;
        _baseCamPos = cam.transform.position;
    }

    public void FocusZoomTo(Transform target)
    {
        if (!enableCameraFocus)
            return;

        FocusZoomTo(
            target,
            defaultZoomInMultiplier,
            defaultZoomInDuration,
            defaultHoldDuration,
            defaultZoomOutDuration,
            null,
            defaultPausedAnimatorSpeed,
            defaultAnimatorPauseDuration,
            null);
    }

    public void FocusZoomTo(Transform target, float multiplier, float inDur, float holdDur, float outDur)
    {
        if (!enableCameraFocus)
            return;

        FocusZoomTo(
            target,
            multiplier,
            inDur,
            holdDur,
            outDur,
            null,
            defaultPausedAnimatorSpeed,
            0f,
            null);
    }

    public void FocusZoomTo(
        Transform target,
        float multiplier,
        float inDur,
        float holdDur,
        float outDur,
        Animator animatorToPause,
        float pausedAnimatorSpeed,
        float pauseDurationSeconds)
    {
        if (!enableCameraFocus)
            return;

        FocusZoomTo(
            target,
            multiplier,
            inDur,
            holdDur,
            outDur,
            animatorToPause,
            pausedAnimatorSpeed,
            pauseDurationSeconds,
            null);
    }

    public void FocusZoomTo(
        Transform target,
        float multiplier,
        float inDur,
        float holdDur,
        float outDur,
        Animator animatorToPause,
        float pausedAnimatorSpeed,
        float pauseDurationSeconds,
        IList<Transform> keepVisibleRoots)
    {
        if (!enableCameraFocus)
            return;

        if (cam == null || target == null)
            return;

        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }

        RestoreAnimatorSpeedImmediate();
        RestoreIsolationImmediate();
        RestoreImmediate();

        CacheBase();

        _routine = StartCoroutine(
            FocusRoutine(
                target,
                multiplier,
                inDur,
                holdDur,
                outDur,
                animatorToPause,
                pausedAnimatorSpeed,
                pauseDurationSeconds,
                keepVisibleRoots));
    }

    public void CancelAndRestore()
    {
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }

        RestoreAnimatorSpeedImmediate();
        RestoreIsolationImmediate();
        RestoreImmediate();
    }

    private void RestoreImmediate()
    {
        if (cam == null) return;

        cam.orthographicSize = _baseOrthoSize;
        cam.transform.position = _baseCamPos;
    }

    private void RestoreAnimatorSpeedImmediate()
    {
        if (_activeSlowedAnimator != null)
        {
            _activeSlowedAnimator.speed = _activeSlowedAnimatorOriginalSpeed;
            _activeSlowedAnimator = null;
            _activeSlowedAnimatorOriginalSpeed = 1f;
        }
    }

    private IEnumerator FocusRoutine(
        Transform target,
        float multiplier,
        float inDur,
        float holdDur,
        float outDur,
        Animator animatorToPause,
        float pausedAnimatorSpeed,
        float pauseDurationSeconds,
        IList<Transform> keepVisibleRoots)
    {
        if (cam == null || target == null)
            yield break;

        if (!cam.orthographic)
            Debug.LogWarning("[CameraFocusController] Camera is not orthographic; this controller is intended for orthographic cameras.", this);

        if (logDebug)
            Debug.Log($"[CameraFocus] Start target='{target.name}'", this);

        ApplyIsolation(keepVisibleRoots);

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

        yield return Tween(fromSize, toSize, fromPos, toPos, Mathf.Max(0.0001f, inDur), target);

        if (animatorToPause != null && pauseDurationSeconds > 0f)
        {
            _activeSlowedAnimator = animatorToPause;
            _activeSlowedAnimatorOriginalSpeed = animatorToPause.speed;
            animatorToPause.speed = Mathf.Clamp(pausedAnimatorSpeed, 0f, 100f);

            if (logDebug)
            {
                Debug.Log(
                    $"[CameraFocus] Pausing animator='{animatorToPause.name}' speed={animatorToPause.speed} pauseSeconds={pauseDurationSeconds}",
                    this);
            }

            float pauseElapsed = 0f;
            while (pauseElapsed < pauseDurationSeconds)
            {
                pauseElapsed += Time.unscaledDeltaTime;

                if (panToTarget && target != null)
                {
                    Vector3 desired = target.position + focusOffset;
                    desired.z = cam.transform.position.z;
                    cam.transform.position = Vector3.Lerp(
                        cam.transform.position,
                        desired,
                        1f - Mathf.Exp(-panSmoothing * Time.unscaledDeltaTime));
                }

                yield return null;
            }

            RestoreAnimatorSpeedImmediate();
        }

        float t = 0f;
        while (t < Mathf.Max(0f, holdDur))
        {
            t += Time.unscaledDeltaTime;

            if (panToTarget && target != null)
            {
                Vector3 desired = target.position + focusOffset;
                desired.z = cam.transform.position.z;
                cam.transform.position = Vector3.Lerp(
                    cam.transform.position,
                    desired,
                    1f - Mathf.Exp(-panSmoothing * Time.unscaledDeltaTime));
            }

            yield return null;
        }

        yield return Tween(toSize, fromSize, cam.transform.position, _baseCamPos, Mathf.Max(0.0001f, outDur), null);

        RestoreIsolationImmediate();

        if (logDebug)
            Debug.Log($"[CameraFocus] End target='{target.name}'", this);

        _routine = null;
    }

    private void ApplyIsolation(IList<Transform> keepVisibleRoots)
    {
        RestoreIsolationImmediate();

        if (!enableCameraFocus)
            return;

        if (!isolateNonParticipants || keepVisibleRoots == null || keepVisibleRoots.Count == 0)
            return;

        BattleManager bm = BattleManager.Instance != null ? BattleManager.Instance : FindObjectOfType<BattleManager>();
        if (bm == null)
            return;

        IReadOnlyList<Transform> candidates = bm.GetCameraIsolationCandidateRoots();
        if (candidates == null || candidates.Count == 0)
            return;

        HashSet<Transform> keepSet = new HashSet<Transform>();
        for (int i = 0; i < keepVisibleRoots.Count; i++)
        {
            Transform root = keepVisibleRoots[i];
            if (root != null)
                keepSet.Add(root);
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            Transform candidate = candidates[i];
            if (candidate == null || keepSet.Contains(candidate))
                continue;

            if (isolateRenderersOnly)
                DisableRenderersUnder(candidate);
            else
                candidate.gameObject.SetActive(false);
        }
    }

    private void DisableRenderersUnder(Transform root)
    {
        if (root == null)
            return;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null)
                continue;

            _isolatedRenderers.Add(new RendererState
            {
                renderer = r,
                wasEnabled = r.enabled
            });

            r.enabled = false;
        }
    }

    private void RestoreIsolationImmediate()
    {
        for (int i = _isolatedRenderers.Count - 1; i >= 0; i--)
        {
            RendererState state = _isolatedRenderers[i];
            if (state.renderer != null)
                state.renderer.enabled = state.wasEnabled;
        }

        _isolatedRenderers.Clear();
    }

    private IEnumerator Tween(
        float fromSize,
        float toSize,
        Vector3 fromPos,
        Vector3 toPos,
        float duration,
        Transform followDuringTween)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(elapsed / duration);
            float s = u * u * (3f - 2f * u);

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

        if (panToTarget)
            cam.transform.position = toPos;
    }
}