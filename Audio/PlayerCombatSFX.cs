// PATH: Assets/Scripts/Audio/PlayerCombatSFX.cs
using UnityEngine;

public class PlayerCombatSFX : MonoBehaviour
{
    [Header("Attack SFX")]
    [SerializeField] private AudioClip[] attackClips;

    [Header("Block SFX")]
    [SerializeField] private AudioClip[] blockClips;

    private AudioSource _audioSource;

    private void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
            _audioSource = gameObject.AddComponent<AudioSource>();

        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 0f; // 2D sound
    }

    // Animation Event
    public void PlayAttackSFX()
    {
        PlayRandomOneShot(attackClips);
    }

    // Animation Event
    public void PlayBlockSFX()
    {
        PlayRandomOneShot(blockClips);
    }

    private void PlayRandomOneShot(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0 || _audioSource == null)
            return;

        var clip = clips[Random.Range(0, clips.Length)];
        if (clip != null)
            _audioSource.PlayOneShot(clip);
    }
}
