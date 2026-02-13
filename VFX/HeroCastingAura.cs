using UnityEngine;

public class HeroCastingAura : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject auraRoot;     // your disabled child object (e.g., "CastingAura")
    [SerializeField] private Animator auraAnimator;

    [Header("Animator")]
    [SerializeField] private string castStateName = "ChargeAura";
    [SerializeField] private int animatorLayer = 0;

    private bool _initialized;

    private void Awake()
    {
        EnsureInit();
        ForceOff();
    }

    private void EnsureInit()
    {
        if (_initialized) return;

        if (auraRoot == null)
            auraRoot = transform.Find("CastingAura")?.gameObject;

        if (auraAnimator == null && auraRoot != null)
            auraAnimator = auraRoot.GetComponentInChildren<Animator>(true);

        _initialized = true;
    }

    private void ForceOff()
    {
        if (auraRoot != null) auraRoot.SetActive(false);
    }

    // --- BattleManager expects these names ---
    public void BeginCasting() => EnableAura();
    public void EndCasting() => DisableAura();

    // --- Internal implementation ---
    public void EnableAura()
    {
        EnsureInit();
        if (auraRoot == null) return;

        auraRoot.SetActive(true);

        if (auraAnimator != null)
        {
            auraAnimator.Play(castStateName, animatorLayer, 0f);
            auraAnimator.Update(0f);
        }
    }

    public void DisableAura()
    {
        EnsureInit();
        if (auraRoot == null) return;

        if (auraAnimator != null)
            auraAnimator.Rebind(); // optional “hard stop”

        auraRoot.SetActive(false);
    }
}
