using UnityEngine;
using UnityEngine.UI;

public class TurnSimulator : MonoBehaviour
{
    [SerializeField] private ReelSpinSystem reels;

    [Header("Optional UI Controls")]
    [Tooltip("Optional: wire this to the 'Spin' button.")]
    [SerializeField] private Button spinButton;

    private int _turn = 0;

    private void Awake()
    {
        if (spinButton != null)
            spinButton.onClick.AddListener(HandleSpinClicked);
    }

    private void OnDestroy()
    {
        if (spinButton != null)
            spinButton.onClick.RemoveListener(HandleSpinClicked);
    }

    private void Update()
    {
        // Debug helper:
        // Press Y to start a new turn (resets spins to the per-turn limit and clears pending payouts).
        if (Input.GetKeyDown(KeyCode.Y))
        {
            _turn++;
            if (reels != null)
                reels.BeginTurn();

            Debug.Log($"Turn {_turn}: began new turn (spins reset).");
        }

        // Press T to spin (ReelSpinSystem enforces the per-turn limit).
        if (Input.GetKeyDown(KeyCode.T))
        {
            HandleSpinClicked();
        }
    }

    private void HandleSpinClicked()
    {
        if (reels != null)
            reels.TrySpin();
    }
}
