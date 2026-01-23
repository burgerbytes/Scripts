using UnityEngine;

namespace SlotsAndSorcery.VFX
{
    public class HealVFXSpawner : MonoBehaviour
    {
        [Header("Heal VFX Prefab")]
        [Tooltip("A prefab that contains a ParticleSystem (or multiple) configured for the heal swirl effect.")]
        [SerializeField] private GameObject healVfxPrefab;

        [Header("Battle Rhythm VFX Prefab")]
        [SerializeField] private GameObject brVfxPrefab;

        [Header("Placement")]
        [Tooltip("If set, VFX will spawn at this transform. If null, spawns at targetRoot.")]
        [SerializeField] private Transform defaultAnchor;

        [Tooltip("Local offset applied after choosing the spawn transform.")]
        [SerializeField] private Vector3 localOffset = Vector3.zero;

        [Header("Lifetime")]
        [Tooltip("If true, will destroy the spawned VFX after its particle systems finish.")]
        [SerializeField] private bool autoDestroyWhenFinished = true;

        [Tooltip("Fallback destroy time if we can't find particle durations.")]
        [SerializeField] private float fallbackDestroySeconds = 2.0f;

        public void PlayHealVfx(Transform targetRoot, Transform optionalAnchorOverride = null)
        {
            if (healVfxPrefab == null || targetRoot == null) return;

            Transform anchor = optionalAnchorOverride != null ? optionalAnchorOverride :
                               (defaultAnchor != null ? defaultAnchor : targetRoot);

            var go = Instantiate(healVfxPrefab, anchor);
            go.transform.localPosition += localOffset;
            go.transform.localRotation = Quaternion.identity;

            if (!autoDestroyWhenFinished)
                return;

            float life = ComputeLifetimeSeconds(go);
            Destroy(go, life);
        }
        public void PlayBRVfx(Transform targetRoot, Transform optionalAnchorOverride = null)
        {
            if (brVfxPrefab == null || targetRoot == null) return;

            Transform anchor = optionalAnchorOverride != null ? optionalAnchorOverride :
                               (defaultAnchor != null ? defaultAnchor : targetRoot);

            var go = Instantiate(brVfxPrefab, anchor);
            go.transform.localPosition += localOffset;
            go.transform.localRotation = Quaternion.identity;

            if (!autoDestroyWhenFinished)
                return;

            float life = ComputeLifetimeSeconds(go);
            Destroy(go, life);
        }

        private float ComputeLifetimeSeconds(GameObject vfxInstance)
        {
            // Try to compute "how long until everything is done"
            var systems = vfxInstance.GetComponentsInChildren<ParticleSystem>(true);
            if (systems == null || systems.Length == 0)
                return fallbackDestroySeconds;

            float maxEnd = 0f;

            foreach (var ps in systems)
            {
                var main = ps.main;

                float duration = main.duration;
                float startDelay = 0f;

                // startDelay is a MinMaxCurve
                var delay = main.startDelay;
                if (delay.mode == ParticleSystemCurveMode.Constant)
                    startDelay = delay.constant;
                else if (delay.mode == ParticleSystemCurveMode.TwoConstants)
                    startDelay = delay.constantMax;

                float lifetime = 0f;
                var lt = main.startLifetime;
                if (lt.mode == ParticleSystemCurveMode.Constant)
                    lifetime = lt.constant;
                else if (lt.mode == ParticleSystemCurveMode.TwoConstants)
                    lifetime = lt.constantMax;

                float end = startDelay + duration + lifetime;
                if (end > maxEnd) maxEnd = end;
            }

            // small padding to ensure fade completes
            return Mathf.Max(fallbackDestroySeconds, maxEnd + 0.15f);
        }
    }
}
