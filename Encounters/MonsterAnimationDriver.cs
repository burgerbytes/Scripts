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

    private bool _attackImpactFired;
    private bool _castReleaseFired;

    public bool AttackImpactFired => _attackImpactFired;
    public bool CastReleaseFired => _castReleaseFired;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
    }

    public void ResetAttackImpact()
    {
        _attackImpactFired = false;
    }

    public void ResetCastRelease()
    {
        _castReleaseFired = false;
    }

    // Animation Event hook (call this from the attack clip at the impact frame)
    public void AnimationEvent_AttackImpact()
    {
        _attackImpactFired = true;
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
    private void FireTrigger(string param)
    {
        if (animator == null || string.IsNullOrWhiteSpace(param))
            return;

        animator.ResetTrigger(param);
        animator.SetTrigger(param);
    }
}

