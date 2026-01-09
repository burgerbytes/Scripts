using TMPro;
using UnityEngine;

/// <summary>
/// Simple UI helper to display how many reel spins remain this player turn.
/// Wire the TMP_Text in the inspector, or leave it null to auto-find on this GameObject.
/// </summary>
public class SpinsRemainingUI : MonoBehaviour
{
    [Header("References (optional)")]
    [SerializeField] private ReelSpinSystem reelSpinSystem;
    [SerializeField] private TMP_Text spinsText;

    [Header("Format")]
    [SerializeField] private string prefix = "Spins: ";

    private void Awake()
    {
        if (spinsText == null)
            spinsText = GetComponent<TMP_Text>();

        if (reelSpinSystem == null)
            reelSpinSystem = FindFirstObjectByType<ReelSpinSystem>();
    }

    private void OnEnable()
    {
        if (reelSpinSystem != null)
            reelSpinSystem.OnSpinsRemainingChanged += HandleChanged;

        // Force an initial paint
        HandleChanged(reelSpinSystem != null ? reelSpinSystem.SpinsRemaining : 0);
    }

    private void OnDisable()
    {
        if (reelSpinSystem != null)
            reelSpinSystem.OnSpinsRemainingChanged -= HandleChanged;
    }

    private void HandleChanged(int remaining)
    {
        if (spinsText == null) return;
        spinsText.text = $"{prefix}{remaining}";
    }
}
