// GUID: bf5e02a1e0e38924facba44bb1cf2fc2
////////////////////////////////////////////////////////////
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// World-space visual reel built from a ring of quads around a cylinder.
///
/// Post-select reel logic:
/// - Each quad is assigned a fixed symbol at build time (strip order around the ring).
/// - Spin does NOT preselect a symbol. The reel simply spins for a random number of steps
///   (at least one full revolution) and stops exactly on a step.
/// - After stopping, you can ask which quad intersects a MidrowPlane (thin collider/renderer bounds) and
///   retrieve that quad's symbol.
/// </summary>
public class Reel3DColumn : MonoBehaviour
{
    [Header("Strip Data")]
    [SerializeField] private ReelStripSO strip;

    [Header("Geometry")]
    [SerializeField] private int quadCount = 18;
    [SerializeField] private float radius = 0.75f;
    [SerializeField] private float quadSizeX = 0.55f;
    [SerializeField] private float quadSizeY = 0.55f;
    [SerializeField] private float quadScaleX = 1f;
    [SerializeField] private float quadScaleY = 1f;

    [SerializeField] private Transform cylinderBody;
    [SerializeField] private bool autoRadiusFromCylinder = true;
    [SerializeField] private float autoRadiusPadding = 0.02f;

    [Header("Orientation")]
    [SerializeField] private Camera viewCamera;
    [SerializeField] private Vector3 localSpinAxis = Vector3.right;
    [SerializeField] private bool billboardToCamera = false;

    [Header("Icon Facing / Thickness")]
    [SerializeField] private bool doubleSidedIcons = true;
    [SerializeField] private float doubleSidedSeparation = 0.0015f;

    [Header("Materials")]
    [SerializeField] private Material iconMaterial;

    [Header("Spin")]
    [Tooltip("Degrees per second for the spin animation.")]
    [SerializeField] private float spinDegreesPerSecond = 720f;

    [Tooltip("Minimum amount of time (seconds) this reel must spin before it can stop. 0 = no minimum.")]
    [SerializeField] private float minSpinDurationSeconds = 0f;

    [Tooltip("If true, the reel rotates in the negative direction around localSpinAxis.")]
    [SerializeField] private bool reverseSpinDirection = true;

    [Header("Stop Shake")]
    [SerializeField] private bool enableStopShake = true;
    [SerializeField] private float stopShakeMagnitudeDeg = 10f;
    [SerializeField] private float stopShakeDuration = 0.14f;
    [SerializeField] private float stopShakeFrequency = 24f;
    [SerializeField] private float stopShakeDamping = 18f;

    private Transform _iconsRoot;

    private struct QuadPair
    {
        public Transform frontT;
        public MeshRenderer frontMr;
        public Reel3DSymbolQuad front;
        public Reel3DSymbolQuad back;
    }

    private readonly List<QuadPair> _quads = new();
    private readonly List<ReelSymbolSO> _fixedSymbolOnQuad = new();
    private readonly List<ReelSymbolSO> _currentSymbolOnQuad = new();

    // Reelcraft: temporary per-battle overrides.
    private readonly Dictionary<int, ReelSymbolSO> _tempTransmuteOriginalOnQuad = new();
    private readonly HashSet<int> _doubledQuads = new();

    // Visual helpers (ninja shadow + mage glow)
    private readonly Dictionary<int, MeshRenderer> _shadowRenderers = new();

    [Header("Reelcraft: Twofold Shadow")]
    [Tooltip("Local-space offset for the shadow overlay quad (relative to the front quad transform).")]
    [SerializeField] private Vector3 twofoldShadowLocalOffset = new Vector3(0.085f, -0.06f, -0.001f);

    [Tooltip("How much to shake the selected icon (local units).")]
    [SerializeField] private float reelcraftIconShakeMagnitude = 0.02f;

    [Tooltip("How long to shake the selected icon (seconds).")]
    [SerializeField] private float reelcraftIconShakeDuration = 0.12f;

    [Tooltip("How much to desaturate the doubled copy (0 = no desat, 1 = full gray).")]
    [SerializeField, Range(0f, 1f)] private float twofoldShadowDesaturation = 0.35f;

    [Tooltip("Brightness multiplier for the doubled copy (lower = darker).")]
    [SerializeField, Range(0.5f, 1f)] private float twofoldShadowBrightness = 0.85f;

    [Header("Reelcraft: Twofold Shadow VFX")]
    [Tooltip("Optional: prefab spawned when Twofold Shadow is applied (e.g., a dense white smoke poof).")]
    [SerializeField] private GameObject twofoldShadowSmokePrefab;

    [Tooltip("Local-space offset for the smoke prefab (relative to the front quad transform).")]
    [SerializeField] private Vector3 twofoldShadowSmokeLocalOffset = new Vector3(0.02f, -0.02f, -0.05f);

    [Tooltip("Seconds before the smoke object auto-destroys.")]
    [SerializeField] private float twofoldShadowSmokeLifetime = 0.85f;

    private readonly HashSet<int> _glowingQuads = new();

    private Coroutine _routine;

    // Authoritative reel pose (integer steps). Angle is always step*StepDeg when stopped.
    private int _currentStep;

    // Rotation bookkeeping (avoid Euler wrap/jitter by driving localRotation directly)
    private bool _baseRotationInitialized = false;
    private Quaternion _baseLocalRotation;
    private float _primaryAxisAngleUnwrapped = 0f;

    public bool IsSpinning { get; private set; }

    private bool _isNudging = false;
    public bool IsNudging => _isNudging;

    public ReelStripSO Strip => strip;
    public int QuadCount => quadCount;

    /// <summary>Current spin speed in degrees/second. Can be modified at runtime.</summary>
    public float SpinDegreesPerSecond
    {
        get => spinDegreesPerSecond;
        set => spinDegreesPerSecond = Mathf.Max(1f, value);
    }

    private float _globalWhiteGlow01 = 0f;

    /// <summary>
    /// Sets a simple white "glow" by tinting all reel icon quads toward white.
    /// Shader-agnostic: uses MaterialPropertyBlock and writes both _Color/_BaseColor.
    /// 0 = no tint, 1 = fully white.
    /// </summary>
    public void SetGlobalWhiteGlow(float glow01)
    {
        EnsureBuilt();
        _globalWhiteGlow01 = Mathf.Clamp01(glow01);

        for (int i = 0; i < _quads.Count; i++)
        {
            ApplyGlobalGlowToRenderer(_quads[i].frontMr);
            if (_quads[i].back != null)
                ApplyGlobalGlowToRenderer(_quads[i].back.GetComponent<MeshRenderer>());
        }

        foreach (var kv in _shadowRenderers)
            ApplyGlobalGlowToRenderer(kv.Value);
    }

    private void ApplyGlobalGlowToRenderer(MeshRenderer mr)
    {
        if (mr == null) return;

        Color baseCol = Color.white;
        if (mr.sharedMaterial != null)
        {
            if (mr.sharedMaterial.HasProperty("_BaseColor"))
                baseCol = mr.sharedMaterial.GetColor("_BaseColor");
            else if (mr.sharedMaterial.HasProperty("_Color"))
                baseCol = mr.sharedMaterial.color;
        }

        Color outCol = Color.Lerp(baseCol, Color.white, _globalWhiteGlow01);

        var mpb = new MaterialPropertyBlock();
        mr.GetPropertyBlock(mpb);
        mpb.SetColor("_Color", outCol);
        mpb.SetColor("_BaseColor", outCol);
        mr.SetPropertyBlock(mpb);
    }


    /// <summary>
    /// Nudges the reel by an integer number of steps while stopped.
    /// Used by Reelcraft abilities (e.g., Measured Bash).
    /// </summary>
    public bool TryNudgeSteps(int deltaSteps)
    {
        EnsureBuilt();

        if (IsSpinning)
            return false;

        if (quadCount <= 0)
            return false;

        if (deltaSteps == 0)
            return true;

        _currentStep = Mod(_currentStep + deltaSteps, quadCount);
        SetPrimaryAxisAngle(_currentStep * StepDeg);
        return true;
    }

    /// <summary>
    /// Nudges the reel by an integer number of steps while stopped, but animates the rotation
    /// instead of snapping instantly. Returns false if the reel is spinning or already nudging.
    /// </summary>
    public bool TryNudgeStepsAnimated(int deltaSteps, float durationSeconds = 0.14f, AnimationCurve ease = null)
    {
        EnsureBuilt();

        if (IsSpinning || _isNudging)
            return false;

        if (quadCount <= 0)
            return false;

        if (deltaSteps == 0)
            return true;

        StartCoroutine(NudgeStepsAnimatedRoutine(deltaSteps, durationSeconds, ease));
        return true;
    }

    private IEnumerator NudgeStepsAnimatedRoutine(int deltaSteps, float durationSeconds, AnimationCurve ease)
    {
        _isNudging = true;

        float dur = Mathf.Max(0.01f, durationSeconds);

        // Use unwrapped angles so a +/-1 step nudge never takes the long way around 0/360.
        float startAngle = _currentStep * StepDeg;
        float deltaAngle = deltaSteps * StepDeg;

        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / dur);
            if (ease != null) u = ease.Evaluate(u);

            SetPrimaryAxisAngle(startAngle + deltaAngle * u);
            yield return null;
        }

        // Commit step state (exact) and snap to canonical resting angle.
        _currentStep = Mod(_currentStep + deltaSteps, quadCount);
        SetPrimaryAxisAngle(_currentStep * StepDeg);

        _isNudging = false;
    }


    /// <summary>
    /// Shakes the whole reel object a bit (same style as the reel-upgrade minigame),
    /// then returns it to its original pose.
    /// </summary>
    public IEnumerator ShakeRoutine(float duration = 0.12f, float magnitude = 6f)
    {
        Vector3 basePos = transform.localPosition;
        float st = 0f;
        while (st < Mathf.Max(0.0001f, duration))
        {
            st += Time.deltaTime;
            float x = UnityEngine.Random.Range(-magnitude, magnitude);
            float y = UnityEngine.Random.Range(-magnitude, magnitude);
            transform.localPosition = basePos + new Vector3(x, y, 0f) * 0.01f;
            yield return null;
        }
        transform.localPosition = basePos;
    }

    /// <summary>
    /// Small local-position shake on a specific icon quad (used for Reelcraft visual feedback).
    /// </summary>
    public IEnumerator ShakeIconRoutine(int quadIndex)
    {
        EnsureBuilt();
        if (quadIndex < 0 || quadIndex >= _quads.Count) yield break;

        Transform t = _quads[quadIndex].frontT;
        if (t == null) yield break;

        float dur = Mathf.Max(0.01f, reelcraftIconShakeDuration);
        float mag = Mathf.Max(0f, reelcraftIconShakeMagnitude);

        Vector3 start = t.localPosition;
        float elapsed = 0f;
        while (elapsed < dur)
        {
            elapsed += Time.deltaTime;
            // quick jitter, decaying
            float k = 1f - Mathf.Clamp01(elapsed / dur);
            Vector2 r = UnityEngine.Random.insideUnitCircle * (mag * k);
            t.localPosition = start + new Vector3(r.x, r.y, 0f);
            yield return null;
        }

        t.localPosition = start;
    }

    /// <summary>
    /// Spawns an optional smoke poof prefab at the given icon quad. Used for Twofold Shadow.
    /// </summary>
    public GameObject SpawnTwofoldShadowSmoke(int quadIndex)
    {
        EnsureBuilt();
        if (twofoldShadowSmokePrefab == null) return null;
        if (quadIndex < 0 || quadIndex >= _quads.Count) return null;

        Transform iconT = _quads[quadIndex].frontT;
        if (iconT == null) return null;

        GameObject go = Instantiate(twofoldShadowSmokePrefab, iconT);
        go.transform.localPosition = twofoldShadowSmokeLocalOffset;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        float life = Mathf.Max(0.05f, twofoldShadowSmokeLifetime);
        Destroy(go, life);
        return go;
    }

    /// <summary>
    /// Temporarily hide/show the front renderer for a quad (helps VFX obscure the symbol swap).
    /// </summary>
    public void SetFrontQuadVisible(int quadIndex, bool visible)
    {
        EnsureBuilt();
        if (quadIndex < 0 || quadIndex >= _quads.Count) return;
        MeshRenderer mr = _quads[quadIndex].frontMr;
        if (mr != null) mr.enabled = visible;
    }

    public int GetMultiplierForQuad(int quadIndex)
    {
        return _doubledQuads.Contains(quadIndex) ? 2 : 1;
    }

    public ReelSymbolSO GetSymbolOnQuad(int quadIndex)
    {
        EnsureBuilt();
        if (quadIndex < 0 || quadIndex >= _currentSymbolOnQuad.Count) return null;
        return _currentSymbolOnQuad[quadIndex];
    }

    public bool SetQuadTemporarilyTransmutedTo(ReelSymbolSO newSymbol, int quadIndex)
    {
        EnsureBuilt();
        if (newSymbol == null) return false;
        if (quadIndex < 0 || quadIndex >= _currentSymbolOnQuad.Count) return false;

        if (!_tempTransmuteOriginalOnQuad.ContainsKey(quadIndex))
            _tempTransmuteOriginalOnQuad[quadIndex] = _currentSymbolOnQuad[quadIndex];

        _currentSymbolOnQuad[quadIndex] = newSymbol;
        ApplySymbolToQuad(quadIndex, newSymbol);
        return true;
    }

    public void RestoreAllTemporaryTransmutes()
    {
        EnsureBuilt();
        foreach (var kv in _tempTransmuteOriginalOnQuad)
        {
            int qi = kv.Key;
            ReelSymbolSO original = kv.Value;
            if (qi >= 0 && qi < _currentSymbolOnQuad.Count)
            {
                _currentSymbolOnQuad[qi] = original;
                ApplySymbolToQuad(qi, original);
            }
        }
        _tempTransmuteOriginalOnQuad.Clear();

        ClearAllGlow();
    }

    public bool MarkQuadDoubled(int quadIndex, bool enableShadowVisual)
    {
        EnsureBuilt();
        if (quadIndex < 0 || quadIndex >= _currentSymbolOnQuad.Count) return false;

        _doubledQuads.Add(quadIndex);
        if (enableShadowVisual)
            EnsureShadowForQuad(quadIndex);
        return true;
    }

    public void ClearAllDoubles()
    {
        _doubledQuads.Clear();
        foreach (var kv in _shadowRenderers)
        {
            if (kv.Value != null)
                Destroy(kv.Value.gameObject);
        }
        _shadowRenderers.Clear();
    }

    public void SetGlowForTransmutableQuads(Func<ReelSymbolSO, bool> canTransmute)
    {
        EnsureBuilt();
        ClearAllGlow();
        if (canTransmute == null) return;

        for (int i = 0; i < _quads.Count; i++)
        {
            ReelSymbolSO sym = (i >= 0 && i < _currentSymbolOnQuad.Count) ? _currentSymbolOnQuad[i] : null;
            if (!canTransmute(sym))
                continue;

            SetQuadGlow(i, true);
        }
    }

    /// <summary>
    /// Highlights ONLY the current midrow icon (the one intersecting midrowPlane) if it is transmutable.
    /// This matches the Reelcraft UX where only midrow results are clickable.
    /// </summary>
    public void SetGlowForTransmutableMidrow(GameObject midrowPlane, Func<ReelSymbolSO, bool> canTransmute)
    {
        EnsureBuilt();
        ClearAllGlow();
        if (midrowPlane == null || canTransmute == null) return;

        int qi;
        ReelSymbolSO sym = GetMidrowSymbolByIntersection(midrowPlane, out qi);
        if (qi < 0) return;
        if (!canTransmute(sym)) return;

        SetQuadGlow(qi, true);
    }

    public void ClearAllGlow()
    {
        EnsureBuilt();
        foreach (int qi in _glowingQuads)
            SetQuadGlow(qi, false);
        _glowingQuads.Clear();
    }

    public float MinSpinDurationSeconds
    {
        get => minSpinDurationSeconds;
        set => minSpinDurationSeconds = Mathf.Max(0f, value);
    }

    private float StepDeg => 360f / Mathf.Max(1, quadCount);
    private float StepDir => reverseSpinDirection ? -1f : 1f;

    private void Awake()
    {
        if (viewCamera == null)
            viewCamera = Camera.main;

        EnsureBuilt();

        // Start aligned so quad 0 is in the window.
        SetPrimaryAxisAngle(_currentStep * StepDeg);

        if (billboardToCamera)
            FaceQuadsToCamera();
    }

    private void LateUpdate()
    {
        if (billboardToCamera)
            FaceQuadsToCamera();
    }

    public void EnsureBuilt()
    {
        if (_quads.Count > 0)
            return;

        if (cylinderBody == null)
        {
            Debug.LogError($"[{nameof(Reel3DColumn)}] Cylinder Body is not assigned on {name}.");
            return;
        }

        _iconsRoot = transform.Find("IconRing");
        if (_iconsRoot == null)
        {
            _iconsRoot = new GameObject("IconRing").transform;
            _iconsRoot.SetParent(transform, false);
        }

        for (int i = _iconsRoot.childCount - 1; i >= 0; i--)
            Destroy(_iconsRoot.GetChild(i).gameObject);

        _quads.Clear();
        _fixedSymbolOnQuad.Clear();
        _currentSymbolOnQuad.Clear();

        float usedRadius = radius;
        if (autoRadiusFromCylinder)
        {
            var mr = cylinderBody.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                Vector3 ext = mr.bounds.extents;
                float approx = Mathf.Max(ext.x, ext.y, ext.z);
                float scaleMag = Mathf.Max(0.0001f, cylinderBody.lossyScale.magnitude / Mathf.Sqrt(3f));
                usedRadius = (approx / scaleMag) + autoRadiusPadding;
            }
        }

        Vector3 center = cylinderBody.localPosition;

        Vector3 axis = localSpinAxis.normalized;
        if (axis.sqrMagnitude < 0.0001f) axis = Vector3.right;

        // Deterministic "front" direction for quad 0:
        Vector3 toCamWorld = viewCamera != null
            ? (viewCamera.transform.position - transform.position)
            : (-transform.forward);

        Vector3 toCamOnPlane = Vector3.ProjectOnPlane(toCamWorld, transform.TransformDirection(axis));
        if (toCamOnPlane.sqrMagnitude < 1e-6f)
            toCamOnPlane = Vector3.ProjectOnPlane(-transform.forward, transform.TransformDirection(axis));

        Vector3 toCamLocal = transform.InverseTransformDirection(toCamOnPlane).normalized;
        if (toCamLocal.sqrMagnitude < 1e-6f) toCamLocal = Vector3.forward;

        Vector3 u = Vector3.ProjectOnPlane(toCamLocal, axis).normalized;
        if (u.sqrMagnitude < 1e-6f) u = Vector3.forward;

        Vector3 v = Vector3.Cross(axis, u).normalized;
        if (v.sqrMagnitude < 1e-6f) v = Vector3.up;

        float stepDeg = StepDeg;

        float sx = Mathf.Max(0.0001f, quadSizeX) * Mathf.Max(0.0001f, quadScaleX);
        float sy = Mathf.Max(0.0001f, quadSizeY) * Mathf.Max(0.0001f, quadScaleY);
        float sz = Mathf.Max(0.0001f, Mathf.Min(sx, sy));
        Vector3 quadScale = new Vector3(sx, sy, sz);

        for (int i = 0; i < quadCount; i++)
        {
            GameObject holder = new GameObject($"Quad_{i:D2}");
            holder.transform.SetParent(_iconsRoot, false);

            float angRad = (i * stepDeg) * Mathf.Deg2Rad;
            Vector3 offset = (Mathf.Cos(angRad) * u + Mathf.Sin(angRad) * v) * usedRadius;

            Vector3 outward = offset.normalized;
            if (outward.sqrMagnitude < 0.0001f)
                outward = u;

            Quaternion faceOut = Quaternion.LookRotation(-outward, axis);
            GameObject frontGO = new GameObject("Front");

            frontGO.transform.SetParent(holder.transform, false);
            frontGO.transform.localPosition = (center + offset) + outward * doubleSidedSeparation;
            frontGO.transform.localRotation = faceOut;
            frontGO.transform.localScale = quadScale;

            // Required render components for Reel3DSymbolQuad
            if (frontGO.GetComponent<MeshFilter>() == null) frontGO.AddComponent<MeshFilter>();
            if (frontGO.GetComponent<MeshRenderer>() == null) frontGO.AddComponent<MeshRenderer>();

            var frontQuad = frontGO.AddComponent<Reel3DSymbolQuad>();
            var frontMr = frontGO.GetComponent<MeshRenderer>();
            if (iconMaterial != null) frontMr.sharedMaterial = iconMaterial;

            // Reelcraft selection: add a collider + click target so we can raycast quads.
            // (Safe: small/cheap collider per quad.)
            var bc = frontGO.AddComponent<BoxCollider>();
            bc.size = new Vector3(1f, 1f, 0.05f);
            bc.center = new Vector3(0f, 0f, 0.02f);

            var clickTarget = frontGO.AddComponent<Reel3DQuadClickTarget>();
            clickTarget.Column = this;
            clickTarget.QuadIndex = i;

            Reel3DSymbolQuad backQuad = null;

            if (doubleSidedIcons)
            {
                GameObject backGO = new GameObject("Back");
                backGO.transform.SetParent(holder.transform, false);
                backGO.transform.localPosition = (center + offset) - outward * doubleSidedSeparation;
                backGO.transform.localRotation = faceOut * Quaternion.Euler(0f, 180f, 0f);
                backGO.transform.localScale = quadScale;

                // Required render components for Reel3DSymbolQuad
                if (backGO.GetComponent<MeshFilter>() == null) backGO.AddComponent<MeshFilter>();
                if (backGO.GetComponent<MeshRenderer>() == null) backGO.AddComponent<MeshRenderer>();

                backQuad = backGO.AddComponent<Reel3DSymbolQuad>();
                var backMr = backGO.GetComponent<MeshRenderer>();
                if (iconMaterial != null) backMr.sharedMaterial = iconMaterial;
            }

            _quads.Add(new QuadPair
            {
                frontT = frontGO.transform,
                frontMr = frontMr,
                front = frontQuad,
                back = backQuad
            });

            _fixedSymbolOnQuad.Add(null);
            _currentSymbolOnQuad.Add(null);
        }

        AssignFixedSymbolsFromStrip();
    }

    private void AssignFixedSymbolsFromStrip()
    {
        if (strip == null || strip.symbols == null || strip.symbols.Count == 0)
        {
            for (int i = 0; i < _quads.Count; i++)
            {
                _fixedSymbolOnQuad[i] = null;
                _currentSymbolOnQuad[i] = null;
                _quads[i].front?.Clear();
                _quads[i].back?.Clear();
            }
            return;
        }

        int n = strip.symbols.Count;
        for (int qi = 0; qi < _quads.Count; qi++)
        {
            ReelSymbolSO sym = strip.symbols[Mod(qi, n)];
            _fixedSymbolOnQuad[qi] = sym;
            _currentSymbolOnQuad[qi] = sym;

            ApplySymbolToQuad(qi, sym);
        }
    }

    private void ApplySymbolToQuad(int quadIndex, ReelSymbolSO sym)
    {
        if (quadIndex < 0 || quadIndex >= _quads.Count) return;

        _quads[quadIndex].front.SetSymbol(sym);
        if (_quads[quadIndex].back != null)
            _quads[quadIndex].back.SetSymbol(sym);

        // If this quad is "doubled" (ninja), keep the shadow sprite in sync and keep the subtle gray tint.
        if (_doubledQuads.Contains(quadIndex) && _shadowRenderers.TryGetValue(quadIndex, out var mr) && mr != null)
        {
            var quad = mr.GetComponent<Reel3DSymbolQuad>();
            if (quad != null)
                quad.SetSymbol(sym);

            ApplyTwofoldShadowTint(_quads[quadIndex].frontMr, mr);
        }
    }

    /// <summary>
    /// Applies a "double" look: same symbol/material, but slightly desaturated + slightly darker (opaque).
    /// This is intentionally NOT a black shadow.
    /// </summary>
    private void ApplyTwofoldShadowTint(MeshRenderer baseMr, MeshRenderer shadowMr)
    {
        if (baseMr == null || shadowMr == null) return;

        Color baseColor = Color.white;

        // Try from shared material (most common)
        if (baseMr.sharedMaterial != null)
        {
            if (baseMr.sharedMaterial.HasProperty("_Color"))
                baseColor = baseMr.sharedMaterial.color;
            else if (baseMr.sharedMaterial.HasProperty("_BaseColor"))
                baseColor = baseMr.sharedMaterial.GetColor("_BaseColor");
        }

        // Slight desaturation toward gray
        float gray = (baseColor.r + baseColor.g + baseColor.b) / 3f;

        float desat = Mathf.Clamp01(twofoldShadowDesaturation);
        float bright = Mathf.Clamp(twofoldShadowBrightness, 0.5f, 1f);

        Color outCol = new Color(
            Mathf.Lerp(baseColor.r, gray, desat),
            Mathf.Lerp(baseColor.g, gray, desat),
            Mathf.Lerp(baseColor.b, gray, desat),
            1f
        );

        outCol *= bright;
        outCol.a = 1f;

        var mpb = new MaterialPropertyBlock();
        shadowMr.GetPropertyBlock(mpb);
        mpb.SetColor("_Color", outCol);
        mpb.SetColor("_BaseColor", outCol);
        shadowMr.SetPropertyBlock(mpb);
    }

    /// <summary>
    /// Spins for a random number of steps (>= minFullRotations full turns) and stops exactly on a step.
    /// Enforces minSpinDurationSeconds by increasing steps if needed.
    /// </summary>
    public void SpinRandom(System.Random rng, int minFullRotations = 1)
    {
        EnsureBuilt();

        if (rng == null)
            rng = new System.Random(unchecked(Environment.TickCount * 31 + (int)(Time.realtimeSinceStartup * 1000f)));

        int full = Mathf.Max(1, minFullRotations);
        int extra = rng.Next(0, Mathf.Max(1, quadCount)); // 0..quadCount-1
        int stepsForward = full * Mathf.Max(1, quadCount) + extra;

        SpinSteps(stepsForward);
    }

    /// <summary>Spins forward in the configured spin direction by the given number of steps.</summary>
    public void SpinSteps(int stepsForward)
    {
        if (stepsForward <= 0)
            stepsForward = Mathf.Max(1, quadCount);

        // ✅ Enforce a minimum spin time by increasing the step count.
        float speed = Mathf.Max(1f, spinDegreesPerSecond); // deg/sec
        float minDur = Mathf.Max(0f, minSpinDurationSeconds);

        if (minDur > 0f)
        {
            float minDeg = speed * minDur;                       // degrees we must travel to last minDur seconds
            int minSteps = Mathf.CeilToInt(minDeg / StepDeg);    // steps needed to reach minDeg (must be whole steps)
            stepsForward = Mathf.Max(stepsForward, minSteps);
        }

        if (_routine != null)
            StopCoroutine(_routine);

        _routine = StartCoroutine(SpinByStepsRoutine(stepsForward));
    }

    private IEnumerator SpinByStepsRoutine(int stepsForward)
    {
        IsSpinning = true;

        float totalDeg = stepsForward * StepDeg;
        // NOTE: spin speed can be adjusted at runtime by changing SpinDegreesPerSecond.
        float sign = StepDir;

        float traveled = 0f;
        float startAngle = GetPrimaryAxisAngle();
        int startStep = _currentStep;

        while (traveled < totalDeg)
        {
            traveled += Mathf.Max(1f, spinDegreesPerSecond) * Time.deltaTime;
            float clamped = Mathf.Min(traveled, totalDeg);

            float ang = startAngle + sign * clamped;
            SetPrimaryAxisAngle(ang);

            yield return null;
        }

        // Commit step state (exact)
        int stepDelta = stepsForward * (reverseSpinDirection ? -1 : 1);
        _currentStep = Mod(startStep + stepDelta, quadCount);

        SetPrimaryAxisAngle(_currentStep * StepDeg);

        if (enableStopShake)
            yield return StopShakeRoutine(_currentStep * StepDeg);

        // Return to exact resting step angle after shake
        SetPrimaryAxisAngle(_currentStep * StepDeg);

        IsSpinning = false;
        _routine = null;
    }

    private IEnumerator StopShakeRoutine(float baseAngle)
    {
        float t = 0f;

        float dur = Mathf.Max(0.0001f, stopShakeDuration);
        float mag = Mathf.Max(0f, stopShakeMagnitudeDeg);
        float freq = Mathf.Max(0f, stopShakeFrequency);
        float damp = Mathf.Max(0f, stopShakeDamping);

        while (t < dur)
        {
            t += Time.deltaTime;

            float envelope = Mathf.Exp(-damp * t);
            float wiggle = Mathf.Sin(t * freq * Mathf.PI * 2f) * mag * envelope;

            SetPrimaryAxisAngle(baseAngle + wiggle);
            yield return null;
        }

        SetPrimaryAxisAngle(baseAngle);
    }

    public ReelSymbolSO GetMidrowSymbolByIntersection(GameObject midrowPlane, out int quadIndex)
    {
        quadIndex = -1;
        if (midrowPlane == null)
            return null;

        if (!TryGetBounds(midrowPlane, out Bounds planeBounds))
            return null;

        quadIndex = FindIntersectingQuadIndex(planeBounds);
        if (quadIndex < 0 || quadIndex >= _currentSymbolOnQuad.Count)
            return null;

        return _currentSymbolOnQuad[quadIndex];
    }

    public ReelSymbolSO GetMidrowSymbolAndMultiplier(GameObject midrowPlane, out int quadIndex, out int multiplier)
    {
        ReelSymbolSO s = GetMidrowSymbolByIntersection(midrowPlane, out quadIndex);
        multiplier = (quadIndex >= 0) ? GetMultiplierForQuad(quadIndex) : 1;
        return s;
    }

    private void EnsureShadowForQuad(int quadIndex)
    {
        if (_shadowRenderers.ContainsKey(quadIndex)) return;
        if (quadIndex < 0 || quadIndex >= _quads.Count) return;

        // Attach to the front quad transform so it stays aligned to the icon.
        var baseFront = _quads[quadIndex].frontT;
        if (baseFront == null) return;

        // Clone the existing front quad so we inherit mesh/material/shader setup (avoids pink missing-material squares).
        GameObject shadowGO = Instantiate(baseFront.gameObject, baseFront);
        shadowGO.name = "Shadow";

        // Remove click/physics from the shadow clone.
        var col = shadowGO.GetComponent<Collider>();
        if (col != null) Destroy(col);
        var ct = shadowGO.GetComponent<Reel3DQuadClickTarget>();
        if (ct != null) Destroy(ct);

        shadowGO.transform.localRotation = Quaternion.identity;
        shadowGO.transform.localScale = Vector3.one;
        shadowGO.transform.localPosition = twofoldShadowLocalOffset;

        var quad = shadowGO.GetComponent<Reel3DSymbolQuad>();
        if (quad == null) quad = shadowGO.AddComponent<Reel3DSymbolQuad>();

        var mr = shadowGO.GetComponent<MeshRenderer>();
        if (mr == null) mr = shadowGO.AddComponent<MeshRenderer>();
        _shadowRenderers[quadIndex] = mr;

        // Ensure we inherit the same materials as the base front quad (some SetSymbol implementations overwrite materials).
        var baseMr = baseFront.GetComponent<MeshRenderer>();
        if (baseMr != null)
            mr.sharedMaterials = baseMr.sharedMaterials;

        // Initialize to current symbol.
        ReelSymbolSO sym = (quadIndex < _currentSymbolOnQuad.Count) ? _currentSymbolOnQuad[quadIndex] : null;
        quad.SetSymbol(sym);

        // Subtle gray "double" tint (not a black shadow)
        ApplyTwofoldShadowTint(baseMr, mr);
    }

    private void SetQuadGlow(int quadIndex, bool on)
    {
        if (quadIndex < 0 || quadIndex >= _quads.Count) return;
        var mr = _quads[quadIndex].frontMr;
        if (mr == null) return;

        var mpb = new MaterialPropertyBlock();
        mr.GetPropertyBlock(mpb);

        // "Soft glow" that works even without emission: tint the icon brighter + a bit bluish.
        // (We also set emission best-effort in case the shader supports it.)
        if (on)
        {
            mpb.SetColor("_Color", new Color(0.65f, 0.85f, 1f, 1f));
            mpb.SetColor("_BaseColor", new Color(0.65f, 0.85f, 1f, 1f));
            mpb.SetColor("_EmissionColor", new Color(0.08f, 0.25f, 0.5f, 1f));
            _glowingQuads.Add(quadIndex);
        }
        else
        {
            mpb.SetColor("_Color", Color.white);
            mpb.SetColor("_BaseColor", Color.white);
            mpb.SetColor("_EmissionColor", Color.black);
        }

        mr.SetPropertyBlock(mpb);
    }

    private int FindIntersectingQuadIndex(Bounds planeBounds)
    {
        EnsureBuilt();

        Vector3 p = planeBounds.center;

        // Prefer the *closest* intersecting quad (some bounds may overlap multiple quads depending on scale/PPU).
        int bestIntersect = -1;
        float bestIntersectDist = float.PositiveInfinity;

        for (int i = 0; i < _quads.Count; i++)
        {
            var q = _quads[i];
            if (q.frontMr == null) continue;

            if (!q.frontMr.bounds.Intersects(planeBounds))
                continue;

            // Use transform position (stable) rather than bounds center (can be inflated by render bounds).
            var t = q.frontT != null ? q.frontT : q.frontMr.transform;
            float d = (t.position - p).sqrMagnitude;
            if (d < bestIntersectDist)
            {
                bestIntersectDist = d;
                bestIntersect = i;
            }
        }

        if (bestIntersect >= 0)
            return bestIntersect;

        // Fallback: choose nearest quad by position (covers cases where renderer bounds don't intersect).
        int best = -1;
        float bestDist = float.PositiveInfinity;

        for (int i = 0; i < _quads.Count; i++)
        {
            var t = _quads[i].frontT;
            if (t == null) continue;

            float d = (t.position - p).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }

        return best;
    }

    private static bool TryGetBounds(GameObject go, out Bounds b)
    {
        b = default;

        var col = go.GetComponent<Collider>();
        if (col != null)
        {
            b = col.bounds;
            return true;
        }

        var r = go.GetComponent<Renderer>();
        if (r != null)
        {
            b = r.bounds;
            return true;
        }

        return false;
    }

    private void FaceQuadsToCamera()
    {
        if (viewCamera == null) return;
        if (_iconsRoot == null) _iconsRoot = transform.Find("IconRing");
        if (_iconsRoot == null) return;

        Vector3 camPos = viewCamera.transform.position;
        Vector3 up = transform.TransformDirection(localSpinAxis.normalized);

        foreach (Transform holder in _iconsRoot)
        {
            Vector3 toCam = (camPos - holder.position);
            if (toCam.sqrMagnitude < 1e-6f) continue;

            holder.rotation = Quaternion.LookRotation(toCam.normalized, up);
        }
    }

    
private float GetPrimaryAxisAngle()
{
    // We track the unwrapped angle ourselves (in degrees) to avoid Euler wrap-around
    // causing apparent direction flips or aliasing.
    return _primaryAxisAngleUnwrapped;
}

private void SetPrimaryAxisAngle(float ang)
{
    EnsureBuilt();

    if (!_baseRotationInitialized)
    {
        _baseLocalRotation = transform.localRotation;
        _baseRotationInitialized = true;
    }

    _primaryAxisAngleUnwrapped = ang;

    Vector3 a = localSpinAxis;
    if (a.sqrMagnitude < 0.0001f) a = Vector3.right;
    a.Normalize();

    // Apply rotation around the configured local spin axis without Euler angle wrapping.
    transform.localRotation = _baseLocalRotation * Quaternion.AngleAxis(ang, a);
}
private static int Mod(int x, int m)
    {
        if (m <= 0) return 0;
        int r = x % m;
        return r < 0 ? r + m : r;
    }

    public void SetStrip(ReelStripSO newStrip, bool rebuildNow = true)
    {
        strip = newStrip;

        if (!rebuildNow)
            return;

        _quads.Clear();
        _fixedSymbolOnQuad.Clear();

        if (_iconsRoot != null)
        {
            for (int i = _iconsRoot.childCount - 1; i >= 0; i--)
                Destroy(_iconsRoot.GetChild(i).gameObject);
        }

        EnsureBuilt();
        SetPrimaryAxisAngle(_currentStep * StepDeg);

        if (billboardToCamera)
            FaceQuadsToCamera();
    }

    /// <summary>
    /// Returns the symbol currently assigned to a specific quad index.
    /// </summary>
    public ReelSymbolSO GetSymbolAtQuadIndex(int quadIndex)
    {
        EnsureBuilt();

        if (quadIndex < 0 || quadIndex >= _fixedSymbolOnQuad.Count)
            return null;

        return _fixedSymbolOnQuad[quadIndex];
    }

    /// <summary>
    /// Replaces the symbol on a specific quad index IN PLACE (no rebuild, no rotation changes).
    /// Updates both the authoritative mapping and the visible quad(s).
    /// </summary>
    public void ReplaceSymbolAtQuadIndex(int quadIndex, ReelSymbolSO newSymbol)
    {
        EnsureBuilt();

        if (quadIndex < 0 || quadIndex >= _fixedSymbolOnQuad.Count)
        {
            Debug.LogWarning($"[{nameof(Reel3DColumn)}] ReplaceSymbolAtQuadIndex out of range: {quadIndex} (count={_fixedSymbolOnQuad.Count})", this);
            return;
        }

        _fixedSymbolOnQuad[quadIndex] = newSymbol;

        // Update visible quad(s)
        var qp = _quads[quadIndex];
        if (qp.front != null) qp.front.SetSymbol(newSymbol);
        if (qp.back != null) qp.back.SetSymbol(newSymbol);
    }
}


////////////////////////////////////////////////////////////

