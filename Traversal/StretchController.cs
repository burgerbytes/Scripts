// PATH: Assets/Scripts/StretchController.cs
using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// NEW MODEL (Jan 2026):
/// Stretch progression is NOT distance/time-based anymore.
/// A stretch is a fixed number of battle "stops" (battlesPerStretch).
///
/// Loop:
/// - StartNewStretch(...) -> enters Running and immediately requests a battle
/// - When a battle is completed -> CompleteBattleAndAdvance()
///     - If battlesCompleted >= battlesPerStretch -> EnterRestArea (campfire)
///     - Else -> optional pan phase -> request next battle
///
/// Compatibility:
/// - Keeps OnDistanceChanged(current,target), but now reports battles progress.
/// - Keeps SetEncounterActive(...) for older systems; it is now a soft "battle active" flag.
/// - Keeps CanResumeRunningVisuals() for legacy visuals gating.
/// </summary>
public class StretchController : MonoBehaviour
{
    public enum StretchState
    {
        Running,
        RestArea
    }

    [Header("Stretch (Battle Stops)")]
    [Tooltip("How many battles occur before arriving at the campfire.")]
    [SerializeField] private int battlesPerStretch = 3;

    [Tooltip("If true, the next battle begins immediately when a stretch starts.")]
    [SerializeField] private bool startBattleImmediatelyOnStretchStart = true;

    [Header("Travel / Pan (Optional Cosmetic Phase)")]
    [Tooltip("If true, we run a short 'travel' delay between battles. Replace with a real camera pan later.")]
    [SerializeField] private bool useTravelDelayBetweenBattles = true;

    [Tooltip("Seconds to wait between battles to simulate panning/travel. (Replace with camera pan later.)")]
    [SerializeField] private float travelDelaySeconds = 0.75f;

    [Header("Refs (optional)")]
    [SerializeField] private HeroStats heroStats;
    [SerializeField] private Animator playerAnimator;
    [SerializeField] private ScrollingBackground scrollingBackground;

    [Header("Player State Names (Legacy Visuals)")]
    [SerializeField] private string playerRunStateName = "Run";
    [SerializeField] private string playerIdleStateName = "Idle";

    [Header("Player Completion Animation (Rest)")]
    [Tooltip("If true, triggers an Animator Trigger when the stretch is completed (e.g., 'Rest').")]
    [SerializeField] private bool triggerRestOnComplete = true;

    [Tooltip("Animator Trigger parameter to fire on completion when Trigger Rest On Complete is true.")]
    [SerializeField] private string restTriggerName = "Rest";

    [Tooltip("Optional: state name to crossfade to on completion (e.g., Rest). Used as a reliable fallback even if the Trigger isn't wired.")]
    [SerializeField] private string restStateName = "Rest";

    [Tooltip("If true, will also CrossFade to Rest State Name on completion (recommended).")]
    [SerializeField] private bool crossfadeToRestStateOnComplete = true;

    [Header("Debug")]
    [SerializeField] private bool logStateTransitions = false;

    // --- Events ---
    // Kept for compatibility: now means (battlesCompleted, battlesPerStretch).
    public event Action<float, float> OnDistanceChanged;

    public event Action OnStretchCompleted;
    public event Action<StretchState> OnStateChanged;

    /// <summary>
    /// Fired when the stretch wants the next battle to begin.
    /// Hook your future BattleManager/Encounter system to this.
    /// </summary>
    public event Action OnBattleRequested;

    /// <summary>
    /// Fired when travel/pan begins (between battles).
    /// </summary>
    public event Action<int, int> OnTravelStarted; // (battlesCompleted, battlesPerStretch)

    /// <summary>
    /// Fired when travel/pan ends (right before requesting next battle).
    /// </summary>
    public event Action<int, int> OnTravelEnded; // (battlesCompleted, battlesPerStretch)

    // --- Internal state ---
    private int _battlesCompleted;
    private bool _battleActive;     // Legacy replacement for "encounterActive"
    private bool _traveling;
    private StretchState _state = StretchState.Running;
    private Coroutine _travelRoutine;

    // --- Public properties (compatibility + convenience) ---
    public int BattlesPerStretch => Mathf.Max(1, battlesPerStretch);
    public int BattlesCompleted => _battlesCompleted;

    // Compatibility: these used to be distance. Now they reflect battle progress.
    public float TargetDistance => BattlesPerStretch;
    public float CurrentDistance => _battlesCompleted;
    public float Progress01 => (BattlesPerStretch <= 0) ? 1f : Mathf.Clamp01((float)_battlesCompleted / BattlesPerStretch);

    public bool EncounterActive => _battleActive; // legacy name, same meaning (battle active)
    public StretchState State => _state;
    public bool IsRestArea => _state == StretchState.RestArea;

    /// <summary>
    /// "Running" now means: we are in the stretch phase and not at campfire.
    /// We may still be traveling or in a battle.
    /// </summary>
    public bool IsRunning => _state == StretchState.Running;

    /// <summary>
    /// Stretch completes when battlesCompleted >= battlesPerStretch.
    /// </summary>
    public bool IsCompleted => _battlesCompleted >= BattlesPerStretch;

    private void Awake()
    {
        if (heroStats == null)
            heroStats = FindFirstObjectByType<HeroStats>();
        // playerAnimator and scrollingBackground intentionally not auto-found here.
    }

    private void OnDisable()
    {
        if (_travelRoutine != null)
            StopCoroutine(_travelRoutine);
        _travelRoutine = null;
    }

    /// <summary>
    /// Called by encounter logic (legacy) to mark battle active/inactive.
    /// In the new system, your BattleManager should set this true at battle start and false at battle end.
    /// </summary>
    public void SetEncounterActive(bool isActive)
    {
        _battleActive = isActive;

        // Optional: if battle just ended, you may prefer to auto-advance here.
        // We DO NOT auto-advance to avoid double-advancing when BattleManager also calls CompleteBattleAndAdvance().
    }

    /// <summary>
    /// Starts a new stretch.
    /// Compatibility: old code passes a target distance float; we interpret it as battlesPerStretch.
    /// </summary>
    public void StartNewStretch(float newTargetDistance)
    {
        // Interpret the old "distance" input as a count of battles.
        int newBattles = Mathf.Max(1, Mathf.RoundToInt(newTargetDistance));
        battlesPerStretch = newBattles;

        _battlesCompleted = 0;
        _battleActive = false;
        _traveling = false;

        if (_travelRoutine != null)
            StopCoroutine(_travelRoutine);
        _travelRoutine = null;

        SetState(StretchState.Running);

        // Legacy visuals: "running" can just mean "in-stretch".
        if (scrollingBackground != null)
            scrollingBackground.SetPaused(false);

        if (playerAnimator != null)
            playerAnimator.CrossFadeInFixedTime(playerRunStateName, 0.05f, 0);

        PushProgressEvent();

        if (startBattleImmediatelyOnStretchStart)
            RequestNextBattle();
    }

    /// <summary>
    /// New explicit API: start a new stretch by battle count.
    /// </summary>
    public void StartNewStretchBattles(int newBattlesPerStretch)
    {
        StartNewStretch(newBattlesPerStretch);
    }

    /// <summary>
    /// Call this when a battle ends in victory and rewards (if any) have been handled.
    /// This advances the stretch.
    /// </summary>
    public void CompleteBattleAndAdvance()
    {
        if (_state != StretchState.Running)
            return;

        if (_battleActive)
        {
            // If someone forgets to clear battleActive, we still advance but warn in logs.
            if (logStateTransitions)
                Debug.Log("[StretchController] CompleteBattleAndAdvance called while battleActive=true. Clearing.");
            _battleActive = false;
        }

        _battlesCompleted = Mathf.Clamp(_battlesCompleted + 1, 0, BattlesPerStretch);
        PushProgressEvent();

        if (IsCompleted)
        {
            CompleteStretch();
            return;
        }

        // Travel/pan between battles
        BeginTravelThenRequestBattle();
    }

    private void BeginTravelThenRequestBattle()
    {
        if (_travelRoutine != null)
            StopCoroutine(_travelRoutine);

        _travelRoutine = StartCoroutine(TravelRoutine());
    }

    private IEnumerator TravelRoutine()
    {
        _traveling = true;
        OnTravelStarted?.Invoke(_battlesCompleted, BattlesPerStretch);

        // Pause scrolling background if you want the "pan" to be separate from scrolling.
        // For now, we keep it paused during travel to reduce visual noise.
        if (scrollingBackground != null)
            scrollingBackground.SetPaused(true);

        if (playerAnimator != null)
            playerAnimator.CrossFadeInFixedTime(playerIdleStateName, 0.05f, 0);

        if (useTravelDelayBetweenBattles && travelDelaySeconds > 0f)
            yield return new WaitForSeconds(travelDelaySeconds);
        else
            yield return null;

        _traveling = false;
        OnTravelEnded?.Invoke(_battlesCompleted, BattlesPerStretch);

        // Resume background if desired.
        if (scrollingBackground != null)
            scrollingBackground.SetPaused(false);

        if (playerAnimator != null)
            playerAnimator.CrossFadeInFixedTime(playerRunStateName, 0.05f, 0);

        RequestNextBattle();

        _travelRoutine = null;
    }

    private void RequestNextBattle()
    {
        if (_state != StretchState.Running)
            return;

        if (IsCompleted)
            return;

        // Mark battle as active only once the battle system actually begins.
        // However, many systems prefer StretchController to reflect "battle is coming now",
        // so it's safe to set this true here if you want.
        // We keep it false here and let BattleManager set it true at battle start.

        OnBattleRequested?.Invoke();
    }

    private void CompleteStretch()
    {
        // Snap progress
        _battlesCompleted = Mathf.Max(_battlesCompleted, BattlesPerStretch);
        PushProgressEvent();

        EnterRestArea();
        OnStretchCompleted?.Invoke();
    }

    public void EnterRestArea()
    {
        SetState(StretchState.RestArea);

        if (scrollingBackground != null)
            scrollingBackground.SetPaused(true);

        if (playerAnimator != null)
        {
            bool playedCompletion = false;

            if (triggerRestOnComplete && !string.IsNullOrWhiteSpace(restTriggerName))
            {
                playerAnimator.ResetTrigger(restTriggerName);
                playerAnimator.SetTrigger(restTriggerName);
                playedCompletion = true;
            }

            if (crossfadeToRestStateOnComplete && !string.IsNullOrWhiteSpace(restStateName))
            {
                playerAnimator.CrossFadeInFixedTime(restStateName, 0.05f, 0);
                playedCompletion = true;
            }

            if (!playedCompletion)
            {
                playerAnimator.CrossFadeInFixedTime(playerIdleStateName, 0.05f, 0);
            }
        }
    }

    private void SetState(StretchState newState)
    {
        if (_state == newState)
            return;

        _state = newState;

        if (logStateTransitions)
            Debug.Log($"[StretchController] State -> {_state}");

        OnStateChanged?.Invoke(_state);
    }

    private void PushProgressEvent()
    {
        OnDistanceChanged?.Invoke(_battlesCompleted, BattlesPerStretch);
    }

    /// <summary>
    /// Convenience used by other systems to decide if they should resume running visuals.
    /// In the new system, "running visuals" only make sense when:
    /// - we're in Running state
    /// - not traveling
    /// - not completed
    /// - not currently in battle
    /// </summary>
    public bool CanResumeRunningVisuals()
    {
        return _state == StretchState.Running && !_traveling && !_battleActive && !IsCompleted;
    }
}
