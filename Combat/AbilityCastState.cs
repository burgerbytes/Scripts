using UnityEngine;

public class AbilityCastState : MonoBehaviour
{
    public static AbilityCastState Instance { get; private set; }

    public bool HasPendingCast => CurrentAbility != null;

    public HeroStats CurrentCaster { get; private set; }
    public AbilityDefinitionSO CurrentAbility { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        // DontDestroyOnLoad(gameObject);
    }

    public void BeginCast(HeroStats caster, AbilityDefinitionSO ability)
    {
        CurrentCaster = caster;
        CurrentAbility = ability;

        Debug.Log(
            $"[AbilityCastState] BeginCast: caster={(caster != null ? caster.name : "null")}, ability={(ability != null ? ability.name : "null")}",
            this
        );
    }

    public void ClearCast()
    {
        Debug.Log("[AbilityCastState] ClearCast", this);
        CurrentCaster = null;
        CurrentAbility = null;
    }
}
