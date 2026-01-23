using System.Collections;
using UnityEngine;

/// <summary>
/// Scene-level presentation helper for passive procs.
///
/// Keeps visuals (screen dim, camera punch-in, VFX prefabs) out of the passive logic.
/// Passives can request a presentation via PassiveAbilitySO.PlayProcPresentation.
///
/// Setup:
/// - Add ONE instance of this to your combat scene (e.g., under GameRoot).
/// - Assign ScreenDimmer and CameraFocusController (or leave null to auto-find).
///
/// Behavior:
/// - When PlayProcPresentation is called, it dims, zooms, and spawns VFX immediately.
/// - After durationSeconds, it restores dim + camera back to normal.
/// </summary>
public class PassivePresentationDirector : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private ScreenDimmer screenDimmer;
    [SerializeField] private CameraFocusController cameraFocus;

    [Header("VFX Spawn")]
    [Tooltip("World-space offset added to the hero position when spawning VFX.")]
    [SerializeField] private Vector3 vfxSpawnOffset = new Vector3(0f, 0.9f, 0f);

    [Tooltip("If true, parents the spawned VFX under the hero transform.")]
    [SerializeField] private bool parentVfxToHero = false;

    [Header("Debug")]
    [SerializeField] private bool logDebug = false;

    private Coroutine _activeRoutine;

    private void Awake()
    {
        // Optional auto-wire to reduce scene setup friction.
        if (screenDimmer == null)
        {
#if UNITY_2023_1_OR_NEWER
            screenDimmer = FindFirstObjectByType<ScreenDimmer>();
#else
            screenDimmer = FindObjectOfType<ScreenDimmer>();
#endif
        }

        if (cameraFocus == null)
        {
#if UNITY_2023_1_OR_NEWER
            cameraFocus = FindFirstObjectByType<CameraFocusController>();
#else
            cameraFocus = FindObjectOfType<CameraFocusController>();
#endif
        }
    }

    public void PlayProcPresentation(HeroStats hero, float dimAlpha, float zoomMultiplier, float durationSeconds, GameObject vfxPrefab)
    {
        if (logDebug)
        {
            string heroName = hero != null ? hero.name : "<null>";
            string vfxName = vfxPrefab != null ? vfxPrefab.name : "<null>";
            Debug.Log($"[PassivePresentation] START hero='{heroName}' dimAlpha={dimAlpha} zoomMult={zoomMultiplier} duration={durationSeconds} vfx='{vfxName}'");
        }

        if (hero == null) return;

        // Cancel any previous proc presentation so procs don't stack weirdly.
        if (_activeRoutine != null)
        {
            StopCoroutine(_activeRoutine);
            _activeRoutine = null;
        }

        _activeRoutine = StartCoroutine(ProcRoutine(hero, dimAlpha, zoomMultiplier, durationSeconds, vfxPrefab));
    }

    private IEnumerator ProcRoutine(HeroStats hero, float dimAlpha, float zoomMultiplier, float durationSeconds, GameObject vfxPrefab)
    {
        Transform t = hero.transform;

        if (logDebug) Debug.Log($"[PassivePresentation] Start hero='{hero.name}' dim={dimAlpha} zoomMult={zoomMultiplier} dur={durationSeconds}", this);

        // 1) Dim screen
        if (screenDimmer != null)
        {
            screenDimmer.DimScreenTo(Mathf.Clamp01(dimAlpha));
        }

        // 2) Camera focus/zoom
        if (cameraFocus != null)
        {
            cameraFocus.FocusZoomTo(t, zoomMultiplier, 0.10f, Mathf.Max(0f, durationSeconds - 0.20f), 0.10f);
            // The durations above are a "default feel"; cameraFocus still exposes defaults in inspector.
            // We shorten in/out so most of the time is spent holding the zoom during the VFX.
        }

        // 3) Spawn VFX
        GameObject spawned = null;
        if (vfxPrefab != null)
        {
            Vector3 pos = t.position + vfxSpawnOffset;
            spawned = Instantiate(vfxPrefab, pos, Quaternion.identity);

            if (parentVfxToHero)
                spawned.transform.SetParent(t, worldPositionStays: true);
        }

        // Hold until effect is done
        if (durationSeconds > 0f)
            yield return new WaitForSeconds(durationSeconds);

        // Clean up spawned VFX
        if (spawned != null)
            Destroy(spawned);

        // Restore presentation
        if (cameraFocus != null)
            if (logDebug) Debug.Log("[PassivePresentation]   Camera restore");
            cameraFocus.CancelAndRestore();

        if (screenDimmer != null)
            if (logDebug) Debug.Log("[PassivePresentation]   DimScreenTo(0) restore");
            screenDimmer.DimScreenTo(0f);

        if (logDebug) Debug.Log($"[PassivePresentation] End hero='{hero.name}'", this);

        _activeRoutine = null;
    }
}
