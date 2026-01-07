using System;
using System.Collections;
using UnityEngine;

public class ScrollingBackground : MonoBehaviour
{
    [SerializeField] private Transform bgA;
    [SerializeField] private Transform bgB;
    [SerializeField] private float scrollSpeed = 1.5f;

    [Header("Runtime Control")]
    [SerializeField] private bool paused = false;

    private float spriteWidth;

    private bool _segmentActive;
    private float _segmentRemainingDistance;
    private float _segmentVelocity; // units/sec (signed)
    private bool _segmentRestorePaused;
    private bool _pausedBeforeSegment;
    private Action _onSegmentComplete;

    public float ScrollSpeed => scrollSpeed;

    void Awake()
    {
        if (bgA == null || bgB == null)
        {
            Debug.LogError("[ScrollingBackground] bgA/bgB not assigned.", this);
            enabled = false;
            return;
        }

        SpriteRenderer sr = bgA.GetComponent<SpriteRenderer>();
        if (sr == null || sr.sprite == null)
        {
            Debug.LogError("[ScrollingBackground] BG_A missing SpriteRenderer or Sprite.", this);
            enabled = false;
            return;
        }

        spriteWidth = sr.bounds.size.x;
    }

    void Update()
    {
        if (_segmentActive)
        {
            StepSegment(Time.deltaTime);
            return;
        }

        if (paused) return;

        float delta = scrollSpeed * Time.deltaTime;
        MoveLeft(delta);
    }

    private void StepSegment(float dt)
    {
        if (dt <= 0f) return;

        float move = _segmentVelocity * dt; // signed distance this frame
        float remaining = _segmentRemainingDistance;

        // We track remaining as signed. Stop when we would overshoot past 0.
        bool willOvershoot = (remaining > 0f && remaining - move <= 0f) ||
                             (remaining < 0f && remaining - move >= 0f);

        if (willOvershoot)
        {
            // Apply final correction to hit exactly zero remaining.
            float finalMove = remaining;
            ApplySignedMove(finalMove);

            _segmentRemainingDistance = 0f;
            EndSegment();
            return;
        }

        ApplySignedMove(move);
        _segmentRemainingDistance -= move;
    }

    private void ApplySignedMove(float signedDistance)
    {
        // Positive distance means "scroll left" (same as normal).
        // Negative distance means "scroll right".
        if (signedDistance > 0f)
            MoveLeft(signedDistance);
        else if (signedDistance < 0f)
            MoveRight(-signedDistance);
    }

    private void MoveLeft(float delta)
    {
        bgA.position += Vector3.left * delta;
        bgB.position += Vector3.left * delta;

        if (bgA.position.x <= -spriteWidth)
            Reposition(bgA, bgB);

        if (bgB.position.x <= -spriteWidth)
            Reposition(bgB, bgA);
    }

    private void MoveRight(float delta)
    {
        bgA.position += Vector3.right * delta;
        bgB.position += Vector3.right * delta;

        if (bgA.position.x >= spriteWidth)
            RepositionRight(bgA, bgB);

        if (bgB.position.x >= spriteWidth)
            RepositionRight(bgB, bgA);
    }

    private void Reposition(Transform toMove, Transform reference)
    {
        toMove.position = new Vector3(
            reference.position.x + spriteWidth,
            toMove.position.y,
            toMove.position.z
        );
    }

    private void RepositionRight(Transform toMove, Transform reference)
    {
        toMove.position = new Vector3(
            reference.position.x - spriteWidth,
            toMove.position.y,
            toMove.position.z
        );
    }

    public void SetPaused(bool isPaused) => paused = isPaused;
    public bool IsPaused() => paused;

    /// <summary>
    /// Plays a controlled scroll segment by distance over duration.
    /// Positive distance moves left (normal). Negative moves right.
    /// This does not permanently change ScrollSpeed.
    /// </summary>
    public void PlayScrollSegment(float distance, float durationSeconds, Action onComplete = null, bool restorePausedState = true)
    {
        if (bgA == null || bgB == null)
        {
            onComplete?.Invoke();
            return;
        }

        if (Mathf.Approximately(distance, 0f) || durationSeconds <= 0f)
        {
            onComplete?.Invoke();
            return;
        }

        // If a segment is already running, finish it immediately (deterministic).
        if (_segmentActive)
            EndSegment(invokeCallback: false);

        _segmentActive = true;
        _segmentRemainingDistance = distance;
        _segmentVelocity = distance / durationSeconds; // signed
        _onSegmentComplete = onComplete;

        _segmentRestorePaused = restorePausedState;
        _pausedBeforeSegment = paused;

        // Ensure Update steps the segment.
        paused = false;
    }

    private void EndSegment(bool invokeCallback = true)
    {
        _segmentActive = false;

        if (_segmentRestorePaused)
            paused = _pausedBeforeSegment;

        var cb = _onSegmentComplete;
        _onSegmentComplete = null;

        if (invokeCallback)
            cb?.Invoke();
    }
}
