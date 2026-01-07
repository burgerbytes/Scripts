// PATH: Assets/Scripts/Run/ResourceCost.cs
using System;

[Serializable]
public struct ResourceCost
{
    public long attack;
    public long defense;
    public long magic;
    public long wild;

    public ResourceCost(long attack, long defense, long magic, long wild)
    {
        this.attack = attack;
        this.defense = defense;
        this.magic = magic;
        this.wild = wild;
    }
}
