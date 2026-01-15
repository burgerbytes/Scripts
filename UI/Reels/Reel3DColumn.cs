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

    private Coroutine _routine;

    // Authoritative reel pose (integer steps). Angle is always step*StepDeg when stopped.
    private int _currentStep;

    public bool IsSpinning { get; private set; }

    public ReelStripSO Strip => strip;
    public int QuadCount => quadCount;

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

            var frontQuad = frontGO.AddComponent<Reel3DSymbolQuad>();
            var frontMr = frontGO.GetComponent<MeshRenderer>();
            if (iconMaterial != null) frontMr.sharedMaterial = iconMaterial;

            Reel3DSymbolQuad backQuad = null;

            if (doubleSidedIcons)
            {
                GameObject backGO = new GameObject("Back");
                backGO.transform.SetParent(holder.transform, false);
                backGO.transform.localPosition = (center + offset) - outward * doubleSidedSeparation;
                backGO.transform.localRotation = faceOut * Quaternion.Euler(0f, 180f, 0f);
                backGO.transform.localScale = quadScale;

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

            _quads[qi].front.SetSymbol(sym);
            if (_quads[qi].back != null)
                _quads[qi].back.SetSymbol(sym);
        }
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
        float speed = Mathf.Max(1f, spinDegreesPerSecond); // deg/sec
        float sign = StepDir;

        float traveled = 0f;
        float startAngle = GetPrimaryAxisAngle();
        int startStep = _currentStep;

        while (traveled < totalDeg)
        {
            traveled += speed * Time.deltaTime;
            float clamped = Mathf.Min(traveled, totalDeg);

            float ang = startAngle + sign * clamped;
            SetPrimaryAxisAngle(ang);

            yield return null;
        }

        // Commit step state (exact)
        int stepDelta = stepsForward * (reverseSpinDirection ? -1 : 1);
        _currentStep = startStep + stepDelta;

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
        if (quadIndex < 0 || quadIndex >= _fixedSymbolOnQuad.Count)
            return null;

        return _fixedSymbolOnQuad[quadIndex];
    }

    private int FindIntersectingQuadIndex(Bounds planeBounds)
    {
        EnsureBuilt();

        for (int i = 0; i < _quads.Count; i++)
        {
            var mr = _quads[i].frontMr;
            if (mr == null) continue;

            if (mr.bounds.Intersects(planeBounds))
                return i;
        }

        Vector3 p = planeBounds.center;
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
        Vector3 e = transform.localEulerAngles;

        Vector3 a = localSpinAxis;
        a = a.sqrMagnitude < 0.0001f ? Vector3.right : a.normalized;

        float ax = Mathf.Abs(a.x), ay = Mathf.Abs(a.y), az = Mathf.Abs(a.z);
        if (ax >= ay && ax >= az) return e.x;
        if (ay >= ax && ay >= az) return e.y;
        return e.z;
    }

    private void SetPrimaryAxisAngle(float ang)
    {
        Vector3 e = transform.localEulerAngles;

        Vector3 a = localSpinAxis;
        a = a.sqrMagnitude < 0.0001f ? Vector3.right : a.normalized;

        float ax = Mathf.Abs(a.x), ay = Mathf.Abs(a.y), az = Mathf.Abs(a.z);
        if (ax >= ay && ax >= az) e.x = ang;
        else if (ay >= ax && ay >= az) e.y = ang;
        else e.z = ang;

        transform.localEulerAngles = e;
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
}
