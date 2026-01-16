public enum IntentCategory
{
    Normal,            // Damage only, single target
    StatusDebuffOnly,  // No damage, applies a debuff/status
    DamageAndStatus,   // Damage + status
    Aoe,               // AoE with no damage
    StatusAndAoe,      // AoE damage (may also apply status)
    SelfBuff           // Buffs self
}
