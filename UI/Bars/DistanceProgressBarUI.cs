using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI-only: shows stretch distance progress on a filled Image.
/// Attach this under TopStatusBar and wire the fill Image.
/// </summary>
public class DistanceProgressBarUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private StretchController stretch;
    [SerializeField] private Image fillImage;

    [Header("Optional Text")]
    [SerializeField] private TMP_Text progressText;
    [Tooltip("Example: {0:0}/{1:0}m")]
    [SerializeField] private string textFormat = "{0:0}/{1:0}m";

    private void Awake()
    {
        if (stretch == null)
            stretch = FindFirstObjectByType<StretchController>();
    }

    private void OnEnable()
    {
        if (stretch != null)
        {
            stretch.OnDistanceChanged += HandleDistanceChanged;
            // force initial paint
            HandleDistanceChanged(stretch.CurrentDistance, stretch.TargetDistance);
        }
    }

    private void OnDisable()
    {
        if (stretch != null)
            stretch.OnDistanceChanged -= HandleDistanceChanged;
    }

    private void HandleDistanceChanged(float current, float target)
    {
        float t = (target <= 0f) ? 1f : Mathf.Clamp01(current / target);

        if (fillImage != null)
            fillImage.fillAmount = t;

        if (progressText != null)
            progressText.text = string.Format(textFormat, current, target);
    }
}
