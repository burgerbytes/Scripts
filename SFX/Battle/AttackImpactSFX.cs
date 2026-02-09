using UnityEngine;

public class AttackImpactSFX : MonoBehaviour
{
    [Header("Impact Sounds")]
    [SerializeField] private AudioClip impactClip;
    [SerializeField] private Vector2 pitchRange = new Vector2(0.95f, 1.05f);

    private AudioSource _audioSource;

    private void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
        {
            Debug.LogError($"[AttackImpactSFX] Missing AudioSource on {name}");
        }
    }

    // CALLED BY ANIMATION EVENT
    public void PlayAttackImpact()
    {
        if (_audioSource == null || impactClip == null)
            return;

        _audioSource.pitch = Random.Range(pitchRange.x, pitchRange.y);
        _audioSource.PlayOneShot(impactClip);
    }
}
