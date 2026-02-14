// PATH: Assets/Scripts/Combat/HeroTeleportVisualOffset.cs
// GUID: (generated)
////////////////////////////////////////////////////////////
using UnityEngine;

/// <summary>
/// Lets an animation clip "teleport" the hero's visible sprite rig to the currently-selected enemy target mid-animation,
/// then restore it after the stab (or at animation end).
///
/// Intended for Ninja basic attack:
/// - Reel back
/// - Disappear
/// - Teleport (Animation Event -> TeleportToTarget)
/// - Stab (Impact Event -> AnimatorImpactEvents.AttackImpact)
/// - Return (Animation Event -> RestoreFromTeleport) OR (AttackFinished -> auto-restore)
/// </summary>
[DisallowMultipleComponent]
public class HeroTeleportVisualOffset : MonoBehaviour
{
    [Header("What moves")]
    [Tooltip("Transform that should be moved for the teleport (usually the SpriteRenderer root on the hero avatar). If null, we auto-find the first SpriteRenderer and use its transform.")]
    [SerializeField] private Transform visualRootToMove;

    [Header("Targeting")]
    [Tooltip("Optional override anchor on the enemy (e.g., a 'CenterPoint' child). If not set, we will ask BattleManager for the enemy's visual transform.")]
    [SerializeField] private Transform explicitEnemyAnchor;

    [Tooltip("World-space offset applied relative to the target anchor when teleporting. Useful to place the ninja slightly in front of the enemy before the stab.")]
    [SerializeField] private Vector3 worldOffsetFromTarget = new Vector3(-0.45f, 0f, 0f);

    [Tooltip("If true, the X offset flips automatically based on which side of the target the hero is on.")]
    [SerializeField] private bool autoFlipXOffset = true;

    [Header("Restore")]
    [Tooltip("If true, we restore automatically when AnimatorImpactEvents.AttackFinished fires (even if you forget to add the restore animation event).")]
    [SerializeField] private bool autoRestoreOnAttackFinished = true;

    [Header("Debug")]
    [SerializeField] private bool logDebug = false;

    private BattleManager _bm;
    private Vector3 _originalWorldPos;
    private bool _hasOriginal;
    private bool _teleported;

    private void Awake()
    {
        if (visualRootToMove == null)
        {
            var sr = GetComponentInChildren<SpriteRenderer>(true);
            if (sr != null) visualRootToMove = sr.transform;
            else visualRootToMove = transform;
        }

        _bm = FindFirstObjectByType<BattleManager>();

        // If AnimatorImpactEvents exists on the same rig, we can piggyback its AttackFinished event by
        // simply having an animation event call RestoreFromTeleport(). Auto-restore is a safety net.
    }

    private void OnDisable()
    {
        // Safety: don't leave the rig offset if the object disables mid-attack.
        if (_teleported) RestoreFromTeleport();
    }

    /// <summary>
    /// Animation Event: call this on the teleport frame (right when the ninja reappears).
    /// </summary>
    public void TeleportToTarget()
    {
        if (visualRootToMove == null) return;

        Transform anchor = ResolveEnemyAnchor();
        if (anchor == null)
        {
            if (logDebug) Debug.Log("[HeroTeleportVisualOffset] TeleportToTarget: no enemy anchor found (no selected target?).", this);
            return;
        }

        if (!_hasOriginal)
        {
            _originalWorldPos = visualRootToMove.position;
            _hasOriginal = true;
        }

        Vector3 offset = worldOffsetFromTarget;

        if (autoFlipXOffset)
        {
            // If we're to the right of the target, flip the X offset so we still land "in front".
            float dir = Mathf.Sign(_originalWorldPos.x - anchor.position.x);
            // dir > 0 means hero starts right of enemy -> land on right side -> invert offset.x
            if (dir > 0f) offset.x = -offset.x;
        }

        visualRootToMove.position = anchor.position + offset;
        _teleported = true;

        if (logDebug) Debug.Log($"[HeroTeleportVisualOffset] TeleportToTarget -> moved '{visualRootToMove.name}' to {visualRootToMove.position} (anchor={anchor.name})", this);
    }

    /// <summary>
    /// Animation Event: call this near the end of the clip (after the stab / after the ninja disappears again).
    /// </summary>
    public void RestoreFromTeleport()
    {
        if (visualRootToMove == null) return;
        if (!_hasOriginal) return;

        visualRootToMove.position = _originalWorldPos;
        _teleported = false;
        _hasOriginal = false;

        if (logDebug) Debug.Log($"[HeroTeleportVisualOffset] RestoreFromTeleport -> restored '{visualRootToMove.name}' to {_originalWorldPos}", this);
    }

    /// <summary>
    /// Optional Animation Event: if you want the clip to explicitly decide when the safety restore happens.
    /// </summary>
    public void AttackFinished_AutoRestore()
    {
        if (!autoRestoreOnAttackFinished) return;
        if (_teleported) RestoreFromTeleport();
    }

    private Transform ResolveEnemyAnchor()
    {
        if (explicitEnemyAnchor != null) return explicitEnemyAnchor;

        if (_bm == null) _bm = FindFirstObjectByType<BattleManager>();
        if (_bm == null) return null;

        return _bm.GetSelectedEnemyVisualTransform();
    }
}
