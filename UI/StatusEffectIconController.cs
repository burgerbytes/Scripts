using UnityEngine;

[DisallowMultipleComponent]
public class StatusEffectIconController : MonoBehaviour
{
    [Header("Visuals")]
    [SerializeField] private bool useTransformFromPrefab = true;
    [SerializeField] private Vector3 localOffset = new Vector3(0f, 1.25f, 0f);
    [SerializeField] private Vector3 localScale = new Vector3(0.35f, 0.35f, 0.35f);
    [SerializeField] private int sortingOrder = 50;

    [Header("Sprites")]
    [SerializeField] private Sprite stunnedSprite;
    [SerializeField] private Sprite hiddenSprite;
    [SerializeField] private Sprite tripleBladeEmpoweredSprite;

    [Header("Behavior")]
    [SerializeField] private bool billboardToMainCamera = true;

    private SpriteRenderer _sr;

    private bool _hidden;
    private bool _stunned;
    private bool _tripleBladeEmpowered;

    private void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        if (_sr == null) _sr = gameObject.AddComponent<SpriteRenderer>();

        ApplyTransformDefaults();
        ApplyVisual();
    }

    private void LateUpdate()
    {
        if (!billboardToMainCamera) return;

        var cam = Camera.main;
        if (cam == null) return;

        transform.rotation = cam.transform.rotation;
    }

    public void ConfigureSprites(Sprite hidden, Sprite stunned, Sprite tripleBladeEmpowered)
    {
        // Prefer inspector assignments; only fill if missing.
        if (hiddenSprite == null) hiddenSprite = hidden;
        if (stunnedSprite == null) stunnedSprite = stunned;
        if (tripleBladeEmpoweredSprite == null) tripleBladeEmpoweredSprite = tripleBladeEmpowered;

        ApplyVisual();
    }

    public void SetStates(bool hidden, bool stunned, bool tripleBladeEmpowered)
    {
        if (_hidden == hidden && _stunned == stunned && _tripleBladeEmpowered == tripleBladeEmpowered)
            return;

        _hidden = hidden;
        _stunned = stunned;
        _tripleBladeEmpowered = tripleBladeEmpowered;

        ApplyVisual();
    }

    private void ApplyTransformDefaults()
    {
        transform.localPosition = localOffset;
        transform.localScale = localScale;

        if (_sr != null)
            _sr.sortingOrder = sortingOrder;
    }

    private void ApplyVisual()
    {
        if (_sr == null) _sr = GetComponent<SpriteRenderer>();
        if (_sr == null) return;

        Sprite spriteToUse = null;

        // Priority: Stunned > Hidden > TripleBladeEmpowered
        if (_stunned) spriteToUse = stunnedSprite;
        else if (_hidden) spriteToUse = hiddenSprite;
        else if (_tripleBladeEmpowered) spriteToUse = tripleBladeEmpoweredSprite;

        _sr.sprite = spriteToUse;
        _sr.enabled = (spriteToUse != null);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        _sr = GetComponent<SpriteRenderer>();
        if (_sr != null)
            _sr.sortingOrder = sortingOrder;

        // Keep prefab looking correct in editor.
        transform.localPosition = localOffset;
        transform.localScale = localScale;
    }
#endif
}
