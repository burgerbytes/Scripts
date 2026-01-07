using UnityEngine;

public class TurnSimulator : MonoBehaviour
{
    [SerializeField] private ReelSpinSystem reels;

    private int _turn = 0;

    private void Update()
    {
        // Press T to simulate "start of turn"
        if (Input.GetKeyDown(KeyCode.T))
        {
            _turn++;
            reels.SpinAll();
            Debug.Log($"Turn {_turn}: spun reels.");
        }
    }
}
