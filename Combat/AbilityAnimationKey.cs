using UnityEngine;

/// <summary>
/// Stable, shared animation keys for ability casts/attacks.
/// These are NOT animator state names; they are keys resolved by CasterAnimationProfile.
/// </summary>
public enum AbilityAnimationKey
{
    None = 0,

    // Common shared keys
    BasicAttack,
    BasicCast,
    BuffCast,
    DebuffCast,
    HealCast,

    // Rogue / Ninja style
    QuickBlade,
    DaggerTempest,
    Throw,
    Jump,

    // Fighter style
    StrongAttack,
    Guard,
    Taunt,

    // Mage style
    FireSpell,
    WaterSpell,
    ArcaneSpell,

    // Fallback
    Custom
}
