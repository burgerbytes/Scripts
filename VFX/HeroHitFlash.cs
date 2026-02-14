using System.Collections;
using UnityEngine;

namespace SlotsAndSorcery.VFX
{
    /// <summary>
    /// Simple white-flash hit effect for pixel-art heroes.
    /// Attach this to the hero avatar prefab (root or child).
    /// Assign a Flash Material that uses a "white silhouette" sprite shader (outputs white with sprite alpha).
    /// </summary>
    public class HeroHitFlash : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private SpriteRenderer targetRenderer;

        [Header("Flash Look")]
        [Tooltip("Material used during the flash. Use a shader that renders WHITE using the sprite's alpha.")]
        [SerializeField] private Material flashMaterial;

        [Tooltip("How long the flash lasts in seconds.")]
        [SerializeField] private float flashDuration = 0.06f;

        [Tooltip("If true, logs when flash is triggered.")]
        [SerializeField] private bool logFlash = false;

        private Material _originalSharedMaterial;
        private Coroutine _flashRoutine;

        private void Awake()
        {
            if (targetRenderer == null)
                targetRenderer = GetComponentInChildren<SpriteRenderer>(true);

            if (targetRenderer != null)
                _originalSharedMaterial = targetRenderer.sharedMaterial;
        }

        public void Flash()
        {
            if (!isActiveAndEnabled) return;
            if (targetRenderer == null) return;
            if (flashMaterial == null) return;

            if (_flashRoutine != null)
                StopCoroutine(_flashRoutine);

            _flashRoutine = StartCoroutine(FlashRoutine());
        }

        private IEnumerator FlashRoutine()
        {
            // Cache original (in case something else changed it at runtime)
            var orig = _originalSharedMaterial != null ? _originalSharedMaterial : targetRenderer.sharedMaterial;

            if (logFlash)
                Debug.Log($"[HeroHitFlash] Flash start on {gameObject.name}", this);

            targetRenderer.sharedMaterial = flashMaterial;
            yield return new WaitForSeconds(flashDuration);

            if (targetRenderer != null)
                targetRenderer.sharedMaterial = orig;

            if (logFlash)
                Debug.Log($"[HeroHitFlash] Flash end on {gameObject.name}", this);

            _flashRoutine = null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (flashDuration < 0f) flashDuration = 0f;
        }
#endif
    }
}
