// PATH: Assets/Scripts/Encounters/MonsterAnimationDriver.cs
// GUID: 4eabbd0dfb7480c4cae6c91acd5e0fc8
////////////////////////////////////////////////////////////
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Optional Animator-driven monster animation controller.
/// Add this to monsters (e.g., Skeleton) that use sprite-sheet animations.
/// Monsters without this component keep the existing legacy behavior.
/// </summary>
public class MonsterAnimationDriver : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("Animator that drives the monster animation states.")]
    public Animator animator;

    [Tooltip("Optional SpriteRenderer used for flip / tint (not required).")]
    public SpriteRenderer spriteRenderer;

    [Header("Enable")]
    [Tooltip("If true, BattleManager will trigger attack animations during enemy lunges.")]
    public bool useAttackAnimations = true;

    [Tooltip("If true, BattleManager will play death animation and delay monster deactivation.")]
    public bool useDeathAnimation = true;

    [Header("Animator Parameters (defaults assume Triggers)")]
    public string idleTrigger = "Idle";
    public string walkTrigger = "Walk";
    public string hitTrigger = "Hit";
    public string blockTrigger = "Block";
    public string deathTrigger = "Death";
    public string attack1Trigger = "Attack1";
    public string attack2Trigger = "Attack2";
    public string spellTrigger = "Spell";
    public string castTrigger = "Cast";

    [Header("Attack Mapping")]
    [Tooltip("If true, wait for an Animator Event to apply damage on enemy attacks. " +
             "Add an Animation Event calling MonsterAnimationDriver.AnimationEvent_AttackImpact() on the attack clips.")]
    public bool waitForAttackImpactEvent = false;

    [Tooltip("If true, BattleManager can wait for a Cast release animation event before spawning spell VFX. " +
             "Add an Animation Event calling MonsterAnimationDriver.AnimationEvent_CastRelease() on the cast clip.")]
    public bool waitForCastReleaseEvent = true;

    [Tooltip("Mapping from EnemyIntent.attackIndex -> which attack animation to use. " +
             "If array is empty or index out of range, Attack1 is used.")]
    public AttackAnimVariant[] attackVariantByAttackIndex = new AttackAnimVariant[0];

    public enum AttackAnimVariant
    {
        Attack1 = 0,
        Attack2 = 1
    }

    [Header("Timings")]
    [Tooltip("How long BattleManager waits after triggering Death before deactivating the monster. " +
             "Set this to your death clip length.")]
    [Min(0f)]
    public float deathDurationSeconds = 0.8f;

    [Header("AttackImpact Camera Focus")]
    [SerializeField] private bool enableImpactCameraFocus = true;
    [SerializeField] private Transform cameraFocusAnchor;
    [SerializeField, Range(0.5f, 1.0f)] private float impactZoomMultiplier = 0.85f;
    [SerializeField] private float impactZoomInDuration = 0.08f;
    [SerializeField] private float impactZoomHoldDuration = 0f;
    [SerializeField] private float impactZoomOutDuration = 0.12f;

    [Header("AttackImpact Animation Pause")]
    [SerializeField] private bool pauseAnimatorAtMaxZoom = true;
    [SerializeField, Range(0f, 1f)] private float pausedAnimatorSpeed = 0f;
    [SerializeField, Min(0f)] private float pausedAnimatorSeconds = 1f;
    [SerializeField] private Animator animatorToSlow;

    private bool _attackImpactFired;
    private bool _castReleaseFired;
    private CameraFocusController _cameraFocus;

    public bool AttackImpactFired => _attackImpactFired;
    public bool CastReleaseFired => _castReleaseFired;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);

        if (animatorToSlow == null)
            animatorToSlow = animator != null ? animator : GetComponentInChildren<Animator>(true);
    }

    public void ResetAttackImpact()
    {
        _attackImpactFired = false;
    }

    public void ResetCastRelease()
    {
        _castReleaseFired = false;
    }

    public void AnimationEvent_AttackImpact()
    {
        _attackImpactFired = true;
        TriggerImpactCameraFocus();
    }

    public void AnimationEvent_CastRelease()
    {
        _castReleaseFired = true;
    }

    public void PlayIdle()
    {
        FireTrigger(idleTrigger);
    }

    public void PlayWalk()
    {
        FireTrigger(walkTrigger);
    }

    public void PlayHit()
    {
        FireTrigger(hitTrigger);
    }

    public void PlayBlock()
    {
        FireTrigger(blockTrigger);
    }

    public void PlayDeath()
    {
        FireTrigger(deathTrigger);
    }

    public void PlayAttackForAttackIndex(int attackIndex)
    {
        if (!useAttackAnimations)
            return;

        AttackAnimVariant variant = AttackAnimVariant.Attack1;

        if (attackVariantByAttackIndex != null && attackVariantByAttackIndex.Length > 0)
        {
            int idx = Mathf.Clamp(attackIndex, 0, attackVariantByAttackIndex.Length - 1);
            variant = attackVariantByAttackIndex[idx];
        }

        if (variant == AttackAnimVariant.Attack2)
            FireTrigger(attack2Trigger);
        else
            FireTrigger(attack1Trigger);
    }

    public void PlaySpell()
    {
        FireTrigger(spellTrigger);
    }

    public void PlayCast()
    {
        FireTrigger(castTrigger);
    }

    private void TriggerImpactCameraFocus()
    {
        if (!enableImpactCameraFocus)
            return;

        if (_cameraFocus == null && Camera.main != null)
            _cameraFocus = Camera.main.GetComponentInParent<CameraFocusController>();

        if (_cameraFocus == null)
            _cameraFocus = FindObjectOfType<CameraFocusController>();

        if (_cameraFocus == null)
            return;

        Transform focusTarget = ResolveCameraFocusTarget();
        Transform attackerRoot = transform.root;
        Transform defenderRoot = null;

        BattleManager bm = BattleManager.Instance != null ? BattleManager.Instance : FindObjectOfType<BattleManager>();
        if (bm != null && bm.TryGetImpactCameraContext(out Transform bmFocusTarget, out Transform bmAttackerRoot, out Transform bmDefenderRoot))
        {
            if (bmFocusTarget != null) focusTarget = bmFocusTarget;
            if (bmAttackerRoot != null) attackerRoot = bmAttackerRoot;
            defenderRoot = bmDefenderRoot;
        }

        Animator pauseTarget = pauseAnimatorAtMaxZoom ? ResolveAnimatorToSlow() : null;

        List<Transform> keepVisibleRoots = new List<Transform>(2);
        if (attackerRoot != null) keepVisibleRoots.Add(attackerRoot);
        if (defenderRoot != null && defenderRoot != attackerRoot) keepVisibleRoots.Add(defenderRoot);

        _cameraFocus.FocusZoomTo(
            focusTarget,
            impactZoomMultiplier,
            impactZoomInDuration,
            impactZoomHoldDuration,
            impactZoomOutDuration,
            pauseTarget,
            pausedAnimatorSpeed,
            pausedAnimatorSeconds,
            keepVisibleRoots);
    }

    private Transform ResolveCameraFocusTarget()
    {
        if (cameraFocusAnchor != null)
            return cameraFocusAnchor;

        Transform root = transform.root != null ? transform.root : transform;

        if (root.name == "CenterPoint")
            return root;

        Transform center = FindChildRecursive(root, "CenterPoint");
        if (center != null)
            return center;

        return root;
    }

    private static Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null)
                continue;

            if (child.name == childName)
                return child;

            Transform nested = FindChildRecursive(child, childName);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private Animator ResolveAnimatorToSlow()
    {
        if (animatorToSlow != null)
            return animatorToSlow;

        if (animator != null)
            return animator;

        animatorToSlow = GetComponentInChildren<Animator>(true);
        return animatorToSlow;
    }

    private void FireTrigger(string param)
    {
        if (animator == null || string.IsNullOrWhiteSpace(param))
            return;

        animator.ResetTrigger(param);
        animator.SetTrigger(param);
    }
}


////////////////////////////////////////////////////////////
