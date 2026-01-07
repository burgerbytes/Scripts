using UnityEngine;

public class LandmarkController : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private StretchController stretch;
    [SerializeField] private GameObject landmarkPrefab;

    [Header("When to show")]
    [Range(0f, 1f)]
    [SerializeField] private float showAtProgress = 0.8f;

    [Header("World placement")]
    [Tooltip("World X where the landmark starts when it becomes visible (usually off-screen right).")]
    [SerializeField] private float spawnWorldX = 10f;

    [Tooltip("World X where the landmark sits when the stretch completes.")]
    [SerializeField] private float arriveWorldX = 3.5f;

    [Tooltip("World Y position (ground height).")]
    [SerializeField] private float worldY = -1.5f;

    [Tooltip("World Z position (depth/order).")]
    [SerializeField] private float worldZ = 0f;

    [Header("Optional smoothing")]
    [SerializeField] private bool useSmoothDamp = false;
    [SerializeField] private float smoothTime = 0.15f;

    [Header("Editor Preview")]
    [Tooltip("If enabled, ignores StretchController and uses Preview Progress.")]
    [SerializeField] private bool previewInEditor = false;

    [Range(0f, 1f)]
    [SerializeField] private float previewProgress01 = 0f;

    [Header("Read-only Debug")]
    [SerializeField] private float debug_progress01;
    [SerializeField] private bool debug_visible;
    [SerializeField] private float debug_targetX;

    private Transform _instance;
    private float _velX;

    private void Awake()
    {
        if (stretch == null)
            stretch = FindFirstObjectByType<StretchController>();
    }

    private void Start()
    {
        EnsureInstance();
        SetVisible(false);
    }

    private void Update()
    {
        if (_instance == null)
            return;

        float p = GetProgress01();
        debug_progress01 = p;

        if (p < showAtProgress)
        {
            debug_visible = false;
            SetVisible(false);
            return;
        }

        debug_visible = true;
        SetVisible(true);

        if (p >= 1f)
        {
            debug_targetX = arriveWorldX;
            SetX(arriveWorldX);
            return;
        }

        float t = Mathf.InverseLerp(showAtProgress, 1f, p);
        float targetX = Mathf.Lerp(spawnWorldX, arriveWorldX, t);
        debug_targetX = targetX;

        if (useSmoothDamp)
        {
            float newX = Mathf.SmoothDamp(_instance.position.x, targetX, ref _velX, Mathf.Max(0.001f, smoothTime));
            SetX(newX);
        }
        else
        {
            SetX(targetX);
        }
    }

    private float GetProgress01()
    {
        if (previewInEditor)
            return previewProgress01;

        if (stretch == null)
            return 0f;

        return stretch.Progress01;
    }

    private void EnsureInstance()
    {
        if (_instance != null) return;
        if (landmarkPrefab == null) return;

        GameObject go = Instantiate(landmarkPrefab, transform);
        _instance = go.transform;
        _instance.position = new Vector3(spawnWorldX, worldY, worldZ);
    }

    private void SetVisible(bool visible)
    {
        if (_instance == null) return;
        if (_instance.gameObject.activeSelf != visible)
            _instance.gameObject.SetActive(visible);
    }

    private void SetX(float x)
    {
        if (_instance == null) return;
        _instance.position = new Vector3(x, worldY, worldZ);
    }

    private void OnDrawGizmos()
    {
        Vector3 a = new Vector3(spawnWorldX, worldY, worldZ);
        Vector3 b = new Vector3(arriveWorldX, worldY, worldZ);

        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(a, 0.15f);

        Gizmos.color = Color.green;
        Gizmos.DrawSphere(b, 0.15f);

        Gizmos.color = Color.white;
        Gizmos.DrawLine(a, b);
    }
}
