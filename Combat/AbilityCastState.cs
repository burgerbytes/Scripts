using System;
using UnityEngine;

public class AbilityCastState : MonoBehaviour
{
    public static AbilityCastState Instance { get; private set; }



    /// <summary>
    /// Fired the moment the player CONFIRMS a target (i.e. the second click / commit),
    /// right before ResolvePendingAbility() begins and animations start.
    /// UI can use this to immediately hide targeting panels, descriptions, etc.
    /// </summary>
    public static event System.Action OnTargetConfirmed;
    
    public static void RaiseTargetConfirmed()
    {
        OnTargetConfirmed?.Invoke();
    }

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
        CurrentCaster = null;
        CurrentAbility = null;
    }
}