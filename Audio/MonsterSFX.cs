using UnityEngine;

public class MonsterSFX : MonoBehaviour
{
    [Header("Death SFX")]
    [SerializeField] private AudioClip[] deathClips;
    [SerializeField, Range(0f, 1f)] private float deathVolume = 1f;

    [Header("Audio Settings")]
    [Tooltip("0 = 2D (recommended for UI-style combat), 1 = full 3D")]
    [SerializeField, Range(0f, 1f)] private float spatialBlend = 0f;

    /// <summary>
    /// Plays a monster death sound that survives the monster being deactivated.
    /// Safe to call right before RemoveMonster().
    /// </summary>
    public void PlayDeathSFX()
    {
        if (deathClips == null || deathClips.Length == 0)
            return;

        var clip = deathClips[Random.Range(0, deathClips.Length)];
        if (clip == null)
            return;

        // Create a temporary GameObject that is NOT tied to the monster lifetime
        GameObject oneShot = new GameObject($"MonsterDeathSFX_{clip.name}");
        oneShot.transform.position = transform.position;

        AudioSource source = oneShot.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = deathVolume;
        source.spatialBlend = spatialBlend;
        source.playOnAwake = false;

        source.Play();

        // Clean up after the clip finishes
        Destroy(oneShot, clip.length + 0.1f);
    }
}
