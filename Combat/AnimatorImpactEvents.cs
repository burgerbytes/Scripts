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

    [Header("AttackImpact Camera Focus")]
    [SerializeField] private bool enableImpactCameraFocus = true;
    [SerializeField] private Transform cameraFocusAnchor;
    [SerializeField, Range(0.5f, 1.0f)] private float impactZoomMultiplier = 0.85f;
    [SerializeField] private float impactZoomInDuration = 0.08f;
    [SerializeField] private float impactZoomHoldDuration = 0f;
    [SerializeField] private float impactZoomOutDuration = 0.12f;

    [Header("AttackImpact Animation Pause")]
    [SerializeField] private bool pauseAnimatorAtMaxZoom = true;
    [SerializeField, Range(0f, 1f)] private float pausedAnimatorSpeed = 0f;
    [SerializeField, Min(0f)] private float pausedAnimatorSeconds = 1f;
    [SerializeField] private Animator animatorToSlow;

    [Header("Debug")]
    [SerializeField] private bool logMissingClips = false;

    private BattleManager _bm;
    private AudioSource _audioSource;
    private CameraFocusController _cameraFocus;

    private bool _hasPendingOverride;
    private ImpactSfxType _pendingOverride;

    private void Awake()
    {
        _bm = FindObjectOfType<BattleManager>();

        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
            _audioSource = GetComponentInParent<AudioSource>();

        if (animatorToSlow == null)
            animatorToSlow = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);
    }

    public void SetImpactSfx(ImpactSfxType type)
    {
        _hasPendingOverride = true;
        _pendingOverride = type;
    }

    public void SetImpactSfxMelee()        => SetImpactSfx(ImpactSfxType.Melee);
    public void SetImpactSfxBlock()        => SetImpactSfx(ImpactSfxType.Block);
    public void SetImpactSfxFire()         => SetImpactSfx(ImpactSfxType.FireMagic);
    public void SetImpactSfxIce()          => SetImpactSfx(ImpactSfxType.IceMagic);
    public void SetImpactSfxThunder()      => SetImpactSfx(ImpactSfxType.ThunderMagic);
    public void SetImpactSfxWater()        => SetImpactSfx(ImpactSfxType.WaterMagic);
    public void SetImpactSfxWind()         => SetImpactSfx(ImpactSfxType.WindMagic);
    public void SetImpactSfxEarth()        => SetImpactSfx(ImpactSfxType.EarthMagic);
    public void SetImpactSfxAtkBuff()      => SetImpactSfx(ImpactSfxType.AtkBuff);
    public void SetImpactSfxDefBuff()      => SetImpactSfx(ImpactSfxType.DefBuff);
    public void SetImpactSfxCharge()       => SetImpactSfx(ImpactSfxType.Charge);
    public void SetImpactSfxPoison()       => SetImpactSfx(ImpactSfxType.Poison);
    public void SetImpactSfxHealing()      => SetImpactSfx(ImpactSfxType.HealingMagic);

    public void ClearImpactOverride() => _hasPendingOverride = false;

    public void AttackImpact()
    {
        TriggerImpactCameraFocus();

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

        var teleport = GetComponent<HeroTeleportVisualOffset>();
        if (teleport == null)
            teleport = GetComponentInChildren<HeroTeleportVisualOffset>(true);

        teleport?.AttackFinished_AutoRestore();
    }

    private void TriggerImpactCameraFocus()
    {
        if (!enableImpactCameraFocus)
            return;

        if (_cameraFocus == null && Camera.main != null)
            _cameraFocus = Camera.main.GetComponentInParent<CameraFocusController>();

        if (_cameraFocus == null)
            _cameraFocus = FindObjectOfType<CameraFocusController>();

        if (_cameraFocus == null)
            return;

        Transform focusTarget = ResolveCameraFocusTarget();
        Transform attackerRoot = transform.root;
        Transform defenderRoot = null;

        if (_bm != null && _bm.TryGetImpactCameraContext(out Transform bmFocusTarget, out Transform bmAttackerRoot, out Transform bmDefenderRoot))
        {
            if (bmFocusTarget != null) focusTarget = bmFocusTarget;
            if (bmAttackerRoot != null) attackerRoot = bmAttackerRoot;
            defenderRoot = bmDefenderRoot;
        }

        Animator pauseTarget = pauseAnimatorAtMaxZoom ? ResolveAnimatorToSlow() : null;

        var keepVisibleRoots = new System.Collections.Generic.List<Transform>(2);
        if (attackerRoot != null) keepVisibleRoots.Add(attackerRoot);
        if (defenderRoot != null && defenderRoot != attackerRoot) keepVisibleRoots.Add(defenderRoot);

        _cameraFocus.FocusZoomTo(
            focusTarget,
            impactZoomMultiplier,
            impactZoomInDuration,
            impactZoomHoldDuration,
            impactZoomOutDuration,
            pauseTarget,
            pausedAnimatorSpeed,
            pausedAnimatorSeconds,
            keepVisibleRoots);
    }

    private Transform ResolveCameraFocusTarget()
    {
        if (cameraFocusAnchor != null)
            return cameraFocusAnchor;

        Transform root = transform.root != null ? transform.root : transform;

        if (root.name == "CenterPoint")
            return root;

        Transform center = FindChildRecursive(root, "CenterPoint");
        if (center != null)
            return center;

        return root;
    }

    private static Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null)
                continue;

            if (child.name == childName)
                return child;

            Transform nested = FindChildRecursive(child, childName);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private Animator ResolveAnimatorToSlow()
    {
        if (animatorToSlow != null)
            return animatorToSlow;

        animatorToSlow = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);
        return animatorToSlow;
    }

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
