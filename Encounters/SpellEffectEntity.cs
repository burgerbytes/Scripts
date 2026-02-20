using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Visual-only spell effect entity.
/// Intended to be instantiated as a separate prefab on a target (e.g., Consume).
/// Plays the Spell animation, then auto-destroys itself.
/// </summary>
public class SpellEffectEntity : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("Optional MonsterAnimationDriver on this prefab (preferred).")]
    [SerializeField] private MonsterAnimationDriver animationDriver;

    [Tooltip("Optional Animator if no MonsterAnimationDriver is used.")]
    [SerializeField] private Animator animator;

    [Header("Animator Parameters")]
    [Tooltip("Trigger parameter used to start the spell animation if using a raw Animator.")]
    [SerializeField] private string spellTrigger = "Spell";

    [Header("Failsafes")]
    [Tooltip("Maximum time to wait before auto-finishing, even if animation state can't be detected.")]
    [Min(0.1f)]
    [SerializeField] private float maxLifetimeSeconds = 5.0f;

    private Action _onFinished;
    private bool _finished;

    private void Awake()
    {
        if (animationDriver == null)
            animationDriver = GetComponentInChildren<MonsterAnimationDriver>(true);

        if (animator == null)
        {
            if (animationDriver != null && animationDriver.animator != null)
                animator = animationDriver.animator;
            else
                animator = GetComponentInChildren<Animator>(true);
        }
    }

    /// <summary>
    /// Begin playing the Spell animation. When it finishes, this object destroys itself.
    /// </summary>
    public void Play(Action onFinished = null)
    {
        _onFinished = onFinished;
        _finished = false;

        // Fire Spell trigger.
        if (animationDriver != null)
        {
            animationDriver.PlaySpell();
        }
        else if (animator != null && !string.IsNullOrWhiteSpace(spellTrigger))
        {
            animator.ResetTrigger(spellTrigger);
            animator.SetTrigger(spellTrigger);
        }

        StopAllCoroutines();
        StartCoroutine(WaitForSpellToFinishRoutine());
    }

    // Animation Event hook (optional). Place this at the end of the spell clip.
    public void AnimationEvent_SpellFinished()
    {
        Finish();
    }

    private IEnumerator WaitForSpellToFinishRoutine()
    {
        float elapsed = 0f;

        // Give Animator a frame to enter the new state.
        yield return null;

        // Best-effort: compute remaining time from current clip length.
        // If we can't, fall back to maxLifetimeSeconds.
        float waitSeconds = 0f;

        if (animator != null)
        {
            try
            {
                AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(0);
                if (st.length > 0f)
                {
                    // Remaining time in seconds (normalizedTime can exceed 1 for looping states).
                    float norm = st.normalizedTime;
                    float frac = norm - Mathf.Floor(norm);
                    waitSeconds = Mathf.Clamp(st.length * (1f - frac), 0f, maxLifetimeSeconds);
                }
            }
            catch { /* ignore */ }
        }

        // If we couldn't determine a duration, just wait up to the max lifetime while
        // allowing AnimationEvent_SpellFinished to finish early.
        if (waitSeconds <= 0.01f)
            waitSeconds = maxLifetimeSeconds;

        while (!_finished && elapsed < waitSeconds)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        Finish();
    }

    private void Finish()
    {
        if (_finished) return;
        _finished = true;

        try { _onFinished?.Invoke(); }
        catch { /* swallow */ }

        Destroy(gameObject);
    }
}
