using System.Collections.Generic;
using UnityEngine;

public class ReelDisableManager : MonoBehaviour
{
    [Header("Reel Roots to Disable")]
    [Tooltip("Top-level GameObjects for any reel systems (character select, battle, rewards, etc.)")]
    [SerializeField] private List<GameObject> reelRoots = new List<GameObject>();

    private readonly List<bool> _previousStates = new List<bool>();

    private void Awake()
    {
        _previousStates.Clear();
        foreach (var go in reelRoots)
        {
            _previousStates.Add(go != null && go.activeSelf);
        }
    }

    public void DisableReels()
    {
        for (int i = 0; i < reelRoots.Count; i++)
        {
            var go = reelRoots[i];
            if (go == null) continue;

            _previousStates[i] = go.activeSelf;
            go.SetActive(false);
        }
    }

    public void EnableReels()
    {
        for (int i = 0; i < reelRoots.Count; i++)
        {
            var go = reelRoots[i];
            if (go == null) continue;

            go.SetActive(_previousStates[i]);
        }
    }
}
