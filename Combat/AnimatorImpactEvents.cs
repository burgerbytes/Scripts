// PATH: Assets/Scripts/Combat/AnimatorImpactEvents.cs
// GUID: f6b70521b03faa34aae1b74cbc6b0046
////////////////////////////////////////////////////////////
using UnityEngine;
using UnityEngine.Serialization;
using System.Reflection;

public class AnimatorImpactEvents : MonoBehaviour
{
    public enum ImpactSfxType
    {
        Melee,
        Block,
        AttackMagic,
        HealingMagic,

        FireMagic,
        IceMagic,
        ThunderMagic,
        WaterMagic,
        WindMagic,
        EarthMagic,

        AtkBuff,
        DefBuff,
        Charge,
        Poison
    }

    [Header("Impact SFX Clips")]
    [FormerlySerializedAs("attackImpactSfx")]
    [SerializeField] private AudioClip meleeImpactSfx;
    [SerializeField] private AudioClip blockSfx;
    [SerializeField] private AudioClip attackMagicImpactSfx;
    [SerializeField] private AudioClip healingMagicImpactSfx;

    [Header("Elemental Magic")]
    [SerializeField] private AudioClip fireMagicSfx;
    [SerializeField] private AudioClip iceMagicSfx;
    [SerializeField] private AudioClip thunderMagicSfx;
    [SerializeField] private AudioClip waterMagicSfx;
    [SerializeField] private AudioClip windMagicSfx;
    [SerializeField] private AudioClip earthMagicSfx;

    [Header("Buff / Status")]
    [SerializeField] private AudioClip atkBuffSfx;
    [SerializeField] private AudioClip defBuffSfx;
    [SerializeField] private AudioClip chargeSfx;
    [SerializeField] private AudioClip poisonSfx;

    [Header("Audio Settings")]
    [FormerlySerializedAs("attackImpactVolume")]
    [SerializeField, Range(0f, 1f)] private float impactVolume = 1f;

    [SerializeField] private bool randomizePitch = true;
    [SerializeField] private Vector2 pitchRange = new Vector2(0.95f, 1.05f);

    [Header("Fallback")]
    [SerializeField] private ImpactSfxType fallbackType = ImpactSfxType.Melee;

    [Header("Debug")]
    [SerializeField] private bool logMissingClips = false;

    private BattleManager _bm;
    private AudioSource _audioSource;

    private bool _hasPendingOverride;
    private ImpactSfxType _pendingOverride;

    private void Awake()
    {
        _bm = FindObjectOfType<BattleManager>();

        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
            _audioSource = GetComponentInParent<AudioSource>();
    }

    // ============================
    // PUBLIC API (CALL BEFORE ANIM)
    // ============================

    public void SetImpactSfx(ImpactSfxType type)
    {
        _hasPendingOverride = true;
        _pendingOverride = type;
    }

    // Convenience helpers (optional but nice)
    public void SetImpactSfxMelee()        => SetImpactSfx(ImpactSfxType.Melee);
    public void SetImpactSfxBlock()        => SetImpactSfx(ImpactSfxType.Block);
    public void SetImpactSfxFire()          => SetImpactSfx(ImpactSfxType.FireMagic);
    public void SetImpactSfxIce()           => SetImpactSfx(ImpactSfxType.IceMagic);
    public void SetImpactSfxThunder()       => SetImpactSfx(ImpactSfxType.ThunderMagic);
    public void SetImpactSfxWater()         => SetImpactSfx(ImpactSfxType.WaterMagic);
    public void SetImpactSfxWind()          => SetImpactSfx(ImpactSfxType.WindMagic);
    public void SetImpactSfxEarth()         => SetImpactSfx(ImpactSfxType.EarthMagic);
    public void SetImpactSfxAtkBuff()       => SetImpactSfx(ImpactSfxType.AtkBuff);
    public void SetImpactSfxDefBuff()       => SetImpactSfx(ImpactSfxType.DefBuff);
    public void SetImpactSfxCharge()        => SetImpactSfx(ImpactSfxType.Charge);
    public void SetImpactSfxPoison()        => SetImpactSfx(ImpactSfxType.Poison);
    public void SetImpactSfxHealing()       => SetImpactSfx(ImpactSfxType.HealingMagic);

    public void ClearImpactOverride() => _hasPendingOverride = false;

    // ============================
    // ANIMATION EVENTS
    // ============================

    public void AttackImpact()
    {
        if (_bm != null)
            _bm.NotifyAttackImpact();

        ImpactSfxType type = _hasPendingOverride ? _pendingOverride : fallbackType;
        _hasPendingOverride = false;

        PlayImpactSfx(type);
    }

    public void AttackFinished()
    {
        if (_bm != null)
            _bm.NotifyAttackFinished();

        // Safety: if this actor is using a mid-clip teleport offset (e.g., Ninja basic attack),
        // restore the visual rig at the end of the attack so we never get stuck offset.
        var teleport = GetComponent<HeroTeleportVisualOffset>();
        if (teleport == null)
            teleport = GetComponentInChildren<HeroTeleportVisualOffset>(true);

        teleport?.AttackFinished_AutoRestore();
    }

    // ============================
    // INTERNAL
    // ============================

    private void PlayImpactSfx(ImpactSfxType type)
    {
        AudioClip clip = GetClip(type);

        if (clip == null)
        {
            if (logMissingClips)
                Debug.LogWarning($"[AnimatorImpactEvents] Missing SFX for {type}", this);
            return;
        }

        if (_audioSource != null)
        {
            float originalPitch = _audioSource.pitch;

            if (randomizePitch)
                _audioSource.pitch = Random.Range(pitchRange.x, pitchRange.y);

            _audioSource.PlayOneShot(clip, impactVolume);

            if (randomizePitch)
                _audioSource.pitch = originalPitch;
        }
        else
        {
            AudioSource.PlayClipAtPoint(clip, transform.position, impactVolume);
        }
    }

    private AudioClip GetClip(ImpactSfxType type)
    {
        switch (type)
        {
            case ImpactSfxType.Melee:        return meleeImpactSfx;
            case ImpactSfxType.Block:        return blockSfx;
            case ImpactSfxType.AttackMagic:  return attackMagicImpactSfx;
            case ImpactSfxType.HealingMagic: return healingMagicImpactSfx;

            case ImpactSfxType.FireMagic:    return fireMagicSfx;
            case ImpactSfxType.IceMagic:     return iceMagicSfx;
            case ImpactSfxType.ThunderMagic: return thunderMagicSfx;
            case ImpactSfxType.WaterMagic:   return waterMagicSfx;
            case ImpactSfxType.WindMagic:    return windMagicSfx;
            case ImpactSfxType.EarthMagic:   return earthMagicSfx;

            case ImpactSfxType.AtkBuff:      return atkBuffSfx;
            case ImpactSfxType.DefBuff:      return defBuffSfx;
            case ImpactSfxType.Charge:       return chargeSfx;
            case ImpactSfxType.Poison:       return poisonSfx;
        }

        return null;
    }
}


////////////////////////////////////////////////////////////
