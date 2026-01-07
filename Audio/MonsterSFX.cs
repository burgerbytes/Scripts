// PATH: Assets/Scripts/Audio/MonsterSFX.cs
using UnityEngine;
using System.Collections;

public class MonsterSFX : MonoBehaviour
{
    [Header("Death SFX")]
    [SerializeField] private AudioClip[] deathClips;

    [Tooltip("Optional volume multiplier for death sounds.")]
    [Range(0f, 1f)]
    [SerializeField] private float deathVolume = 1f;

    private AudioSource _audioSource;

    private void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
            _audioSource = gameObject.AddComponent<AudioSource>();

        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 0f; // 2D by default
    }

    /// <summary>
    /// Plays a death clip (random if multiple). Returns clip length in seconds (0 if none).
    /// </summary>
    public float PlayDeathSFX(float trimSeconds = 2.2f)
    {
        if (deathClips == null || deathClips.Length == 0 || _audioSource == null)
            return 0f;

        AudioClip clip = deathClips[Random.Range(0, deathClips.Length)];
        if (clip == null)
            return 0f;

        _audioSource.clip = clip;
        _audioSource.volume = deathVolume;
        _audioSource.Play();

        float playTime = Mathf.Max(0f, clip.length - trimSeconds);
        StartCoroutine(FadeOutAfter(playTime, 0.08f));

        return playTime;
    }

    private IEnumerator FadeOutAfter(float delay, float fadeDuration)
    {
        yield return new WaitForSeconds(delay);

        float startVol = _audioSource.volume;
        float t = 0f;

        while (t < fadeDuration)
        {
            _audioSource.volume = Mathf.Lerp(startVol, 0f, t / fadeDuration);
            t += Time.deltaTime;
            yield return null;
        }

        _audioSource.Stop();
        _audioSource.volume = startVol;
    }

}