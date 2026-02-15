using UnityEngine;

/// <summary>
/// Optional marker/helper for hero prefabs to be clickable in-world for ally targeting.
/// Put this on the same GameObject as (or a parent of) your BoxCollider2D/Collider.
/// BattleManager will raycast/OverlapPoint and find this in parents.
/// </summary>
public class HeroTargetClickReceiver : MonoBehaviour
{
    [SerializeField] private HeroStats heroStats;

    public HeroStats HeroStats => heroStats != null ? heroStats : GetComponentInParent<HeroStats>();

    private void Reset()
    {
        heroStats = GetComponentInParent<HeroStats>();
    }

    private void Awake()
    {
        if (heroStats == null)
            heroStats = GetComponentInParent<HeroStats>();
    }
}
