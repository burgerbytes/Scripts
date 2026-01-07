// Assets/Scripts/Combat/AnimatorImpactEvents.cs
// Attach this to the SAME GameObject that has the Animator component playing the attack clip.
// Then add an Animation Event on the attack clip that calls AttackImpact() at the exact impact frame.

using UnityEngine;

public class AnimatorImpactEvents : MonoBehaviour
{
    // Animation Event hook (impact frame)
    public void AttackImpact()
    {
        BattleManager bm = FindObjectOfType<BattleManager>();
        if (bm != null) bm.NotifyAttackImpact();
    }

    // Optional Animation Event hook (end of animation)
    public void AttackFinished()
    {
        BattleManager bm = FindObjectOfType<BattleManager>();
        if (bm != null) bm.NotifyAttackFinished();
    }
}
