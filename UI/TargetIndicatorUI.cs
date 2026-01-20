using UnityEngine;

/// <summary>
/// Small UI helper that shows/hides a target indicator (e.g., a red sideways triangle)
/// and lets you control its size and position via the inspector.
///
/// Usage:
/// - Create an Image (triangle) under your unit UI (enemy HUD or PartyHUDSlot).
/// - Assign its RectTransform to <see cref="indicatorRect"/>.
/// - Tweak <see cref="anchoredOffset"/> and <see cref="scale"/>.
/// - Call <see cref="SetVisible"/> from code (BattleManager / PartyHUDSlot already do this).
/// </summary>
public class TargetIndicatorUI : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("RectTransform of the indicator graphic (triangle). If null, this object's RectTransform will be used.")]
    [SerializeField] private RectTransform indicatorRect;

    [Header("Layout")]
    [Tooltip("Anchored position offset applied to the indicator rect.")]
    [SerializeField] private Vector2 anchoredOffset = new Vector2(-40f, 0f);

    [Tooltip("Uniform scale multiplier applied to the indicator rect.")]
    [SerializeField] private float scale = 1f;

    [Header("Behavior")]
    [Tooltip("If true, the layout settings will be applied in edit mode when values change.")]
    [SerializeField] private bool applyInEditMode = true;

    private void Reset()
    {
        indicatorRect = GetComponent<RectTransform>();
    }

    private void Awake()
    {
        if (indicatorRect == null)
            indicatorRect = GetComponent<RectTransform>();

        ApplyLayout();
    }

    private void OnValidate()
    {
        if (!applyInEditMode) return;
        if (indicatorRect == null)
            indicatorRect = GetComponent<RectTransform>();

        ApplyLayout();
    }

    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf != visible)
            gameObject.SetActive(visible);
    }

    /// <summary>
    /// Configure layout values at runtime (useful for BattleManager-spawned indicators).
    /// </summary>
    public void Configure(Vector2 offset, float newScale)
    {
        anchoredOffset = offset;
        scale = newScale;
        ApplyLayout();
    }

    private void ApplyLayout()
    {
        if (indicatorRect == null) return;

        indicatorRect.anchoredPosition = anchoredOffset;
        indicatorRect.localScale = Vector3.one * Mathf.Max(0.0001f, scale);
    }
}
