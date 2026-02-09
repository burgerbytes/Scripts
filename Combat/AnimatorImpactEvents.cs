using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Receives animation events (AttackImpact / AttackFinished) and fans them out to BattleManager + impact SFX.
/// ZERO per-attack wiring: on AttackImpact it asks BattleManager whether the current impact should be treated as magic.
/// 
/// NOTE: This file includes FormerlySerializedAs attributes so your previously-assigned Inspector fields
/// (from the old single-clip version) don't get wiped when upgrading.
/// </summary>
public class AnimatorImpactEvents : MonoBehaviour
{
    [Header("SFX")]
    // Back-compat: this used to be named "attackImpactSfx" (single-clip version)
    [FormerlySerializedAs("attackImpactSfx")]
    [SerializeField] private AudioClip meleeImpactSfx;

    [SerializeField] private AudioClip magicImpactSfx;

    // Back-compat: this used to be named "attackImpactVolume"
    [FormerlySerializedAs("attackImpactVolume")]
    [SerializeField, Range(0f, 1f)] private float impactVolume = 1f;

    [SerializeField] private bool randomizePitch = true;
    [SerializeField] private Vector2 pitchRange = new Vector2(0.95f, 1.05f);

    [Header("Fallback")]
    [Tooltip("If BattleManager cannot be found, which clip should we use?")]
    [SerializeField] private bool fallbackToMagic = false;

    [Header("Debug")]
    [SerializeField] private bool logMissingClips = false;

    private BattleManager _bm;
    private AudioSource _audioSource;

    private void Awake()
    {
        // Cache BattleManager once (instead of searching every animation event call)
        _bm = FindObjectOfType<BattleManager>();

        // Prefer an AudioSource on this same object (or parent). If none exists, we’ll fall back gracefully.
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
            _audioSource = GetComponentInParent<AudioSource>();
    }

    // Animation Event hook (impact frame)
    public void AttackImpact()
    {
        // Existing behavior
        if (_bm != null) _bm.NotifyAttackImpact();

        // Impact SFX (auto-select melee vs magic)
        bool isMagic = (_bm != null) ? _bm.IsCurrentImpactMagic() : fallbackToMagic;
        PlayImpactSfx(isMagic);
    }

    // Optional Animation Event hook (end of animation)
    public void AttackFinished()
    {
        if (_bm != null) _bm.NotifyAttackFinished();
    }

    private void PlayImpactSfx(bool magic)
    {
        AudioClip clip = magic ? magicImpactSfx : meleeImpactSfx;

        if (clip == null)
        {
            if (logMissingClips)
                Debug.LogWarning($"[AnimatorImpactEvents] Missing {(magic ? "magic" : "melee")}ImpactSfx on {name}.", this);
            return;
        }

        // Best path: use a cached AudioSource (no temp objects, no searches)
        if (_audioSource != null)
        {
            float originalPitch = _audioSource.pitch;

            if (randomizePitch)
                _audioSource.pitch = Random.Range(pitchRange.x, pitchRange.y);

            _audioSource.PlayOneShot(clip, impactVolume);

            // Restore pitch so other sounds (or subsequent clips) aren’t affected
            if (randomizePitch)
                _audioSource.pitch = originalPitch;

            return;
        }

        // Fallback: play at camera position if no AudioSource exists on the attacker
        if (Camera.main != null)
            AudioSource.PlayClipAtPoint(clip, Camera.main.transform.position, impactVolume);
        else if (logMissingClips)
            Debug.LogWarning($"[AnimatorImpactEvents] No AudioSource and no Camera.main to play clip at point on {name}.", this);
    }
}
