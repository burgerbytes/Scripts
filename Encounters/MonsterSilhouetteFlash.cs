using System.Collections;
using UnityEngine;

public class MonsterSilhouetteFlash : MonoBehaviour
{
    [Header("Material")]
    [SerializeField] private Material silhouetteMaterial;

    [Header("Defaults")]
    [SerializeField] private float defaultFlashDuration = 0.08f;
    [SerializeField] private int defaultPulses = 1;
    [SerializeField] private float defaultIntensity = 1.5f;

    [Header("Sorting")]
    [SerializeField] private int sortingOrderOffset = 50;

    private SpriteRenderer _baseSr;
    private SpriteRenderer _flashSr;
    private Coroutine _flashRoutine;

    private MaterialPropertyBlock _mpb;
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    private void Awake()
    {
        _baseSr = GetComponent<SpriteRenderer>();
        if (_baseSr == null)
            _baseSr = GetComponentInChildren<SpriteRenderer>(true);

        if (_baseSr == null)
        {
            Debug.LogWarning($"[{nameof(MonsterSilhouetteFlash)}] No SpriteRenderer found on {name} or children.");
            enabled = false;
            return;
        }

        // Create overlay renderer as child of the base renderer so it matches transforms/anim swaps.
        var go = new GameObject("SilhouetteFlashOverlay");
        go.transform.SetParent(_baseSr.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        _flashSr = go.AddComponent<SpriteRenderer>();
        _flashSr.enabled = false;

        _mpb = new MaterialPropertyBlock();

        CopyFromBase();
        ApplyIntensity(defaultIntensity);
    }

    private void LateUpdate()
    {
        if (_flashSr != null && _baseSr != null)
            CopyFromBase();
    }

    private void CopyFromBase()
    {
        _flashSr.sprite = _baseSr.sprite;
        _flashSr.flipX = _baseSr.flipX;
        _flashSr.flipY = _baseSr.flipY;
        _flashSr.sortingLayerID = _baseSr.sortingLayerID;
        _flashSr.sortingOrder = _baseSr.sortingOrder + sortingOrderOffset;

        if (silhouetteMaterial != null)
            _flashSr.sharedMaterial = silhouetteMaterial;
    }

    private void ApplyIntensity(float intensity)
    {
        if (_flashSr == null) return;

        _flashSr.GetPropertyBlock(_mpb);
        _mpb.SetFloat(IntensityId, intensity);
        _flashSr.SetPropertyBlock(_mpb);
    }

    /// <summary>
    /// Triggers a flash with optional overrides. Returns the total duration used.
    /// </summary>
    public float TriggerFlash(float? duration = null, int? pulses = null, float? intensity = null)
    {
        if (_flashSr == null || silhouetteMaterial == null) return 0f;

        float d = Mathf.Max(0.01f, duration ?? defaultFlashDuration);
        int p = Mathf.Max(1, pulses ?? defaultPulses);
        float i = Mathf.Max(0f, intensity ?? defaultIntensity);

        if (_flashRoutine != null)
            StopCoroutine(_flashRoutine);

        ApplyIntensity(i);
        _flashRoutine = StartCoroutine(FlashRoutine(d, p));

        return d;
    }

    private IEnumerator FlashRoutine(float duration, int pulses)
    {
        float pulseLen = duration / pulses;

        for (int i = 0; i < pulses; i++)
        {
            _flashSr.enabled = true;
            yield return new WaitForSeconds(pulseLen * 0.5f);

            _flashSr.enabled = false;
            yield return new WaitForSeconds(pulseLen * 0.5f);
        }

        _flashSr.enabled = false;
        _flashRoutine = null;
    }
}
