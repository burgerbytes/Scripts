using UnityEngine;

public class TurnSimulator : MonoBehaviour
{
    [SerializeField] private ReelSpinSystem reels;

    private int _turn = 0;

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
            if (reels != null)
                reels.TrySpin();
        }
    }
}
