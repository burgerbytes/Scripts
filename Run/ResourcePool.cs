// PATH: Assets/Scripts/Run/ResourcePool.cs
using System;
using UnityEngine;

public class ResourcePool : MonoBehaviour
{
    public static ResourcePool Instance { get; private set; }

    [SerializeField] private long attack;
    [SerializeField] private long defense;
    [SerializeField] private long magic;
    [SerializeField] private long wild;

    [Tooltip("If true, Wild may substitute for missing Attack/Defense/Magic when paying costs.")]
    [SerializeField] private bool allowWildSubstitution = true;

    public event Action OnChanged;

    public long Attack => attack;
    public long Defense => defense;
    public long Magic => magic;
    public long Wild => wild;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void ResetForNewRun(long startAttack = 0, long startDefense = 0, long startMagic = 0, long startWild = 0)
    {
        attack = Math.Max(0, startAttack);
        defense = Math.Max(0, startDefense);
        magic = Math.Max(0, startMagic);
        wild = Math.Max(0, startWild);
        OnChanged?.Invoke();
    }

    public void Add(long addAttack, long addDefense, long addMagic, long addWild)
    {
        if (addAttack > 0) attack += addAttack;
        if (addDefense > 0) defense += addDefense;
        if (addMagic > 0) magic += addMagic;
        if (addWild > 0) wild += addWild;
        OnChanged?.Invoke();
    }

    public bool CanAfford(ResourceCost cost)
    {
        return ComputePayment(cost, out _);
    }

    public bool TrySpend(ResourceCost cost)
    {
        if (!ComputePayment(cost, out var pay))
            return false;

        attack = Math.Max(0, attack - pay.attack);
        defense = Math.Max(0, defense - pay.defense);
        magic = Math.Max(0, magic - pay.magic);
        wild = Math.Max(0, wild - pay.wild);

        OnChanged?.Invoke();
        return true;
    }

    private bool ComputePayment(ResourceCost cost, out ResourceCost payment)
    {
        long needA = Math.Max(0, cost.attack);
        long needD = Math.Max(0, cost.defense);
        long needM = Math.Max(0, cost.magic);
        long needW = Math.Max(0, cost.wild);

        // First, pay explicit wild
        if (wild < needW)
        {
            payment = default;
            return false;
        }

        long availWild = wild - needW;

        // If no substitution allowed, must have exact pools.
        if (!allowWildSubstitution)
        {
            if (attack < needA || defense < needD || magic < needM)
            {
                payment = default;
                return false;
            }

            payment = new ResourceCost(needA, needD, needM, needW);
            return true;
        }

        // Substitution allowed: cover deficits in A/D/M with remaining wild.
        long useA = Math.Min(attack, needA);
        long useD = Math.Min(defense, needD);
        long useM = Math.Min(magic, needM);

        long deficitA = needA - useA;
        long deficitD = needD - useD;
        long deficitM = needM - useM;

        long totalDeficit = deficitA + deficitD + deficitM;

        if (availWild < totalDeficit)
        {
            payment = default;
            return false;
        }

        payment = new ResourceCost(
            attack: useA,
            defense: useD,
            magic: useM,
            wild: needW + totalDeficit
        );

        return true;
    }
}
