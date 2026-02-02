using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simple enable/disable gate for reel root GameObjects (battle reels, rewards reels, etc.).
///
/// IMPORTANT: This manager supports nested disables via reference counting:
/// - DisableReels() increments a counter per root and deactivates the root on the first disable.
/// - EnableReels() decrements the counter per root and restores the original active state when the counter returns to 0.
///
/// This prevents one UI panel from re-enabling reels that another panel still expects to remain disabled.
/// </summary>
public class ReelDisableManager : MonoBehaviour
{
    [Header("Reel Roots to Disable")]
    [Tooltip("Top-level GameObjects for any reel systems (character select, battle, rewards, etc.)")]
    [SerializeField] private List<GameObject> reelRoots = new List<GameObject>();

    // Per-root reference counts and original active states (captured on first disable).
    private readonly List<int> _disableCounts = new List<int>();
    private readonly List<bool> _originalStates = new List<bool>();

    private void Awake()
    {
        EnsureInternalListSizes();

        // Initialize originals to current active states at startup.
        for (int i = 0; i < reelRoots.Count; i++)
        {
            var go = reelRoots[i];
            _originalStates[i] = (go != null && go.activeSelf);
            _disableCounts[i] = 0;
        }
    }

    private void OnValidate()
    {
        // In-editor only: keep lists sized correctly when reelRoots changes.
        EnsureInternalListSizes();

        // Only update originals while not playing; at runtime originals must remain stable
        // across Disable/Enable calls or reels may never restore correctly.
        if (!Application.isPlaying)
        {
            for (int i = 0; i < reelRoots.Count; i++)
            {
                var go = reelRoots[i];
                _originalStates[i] = (go != null && go.activeSelf);
                _disableCounts[i] = 0;
            }
        }
    }

    private void EnsureInternalListSizes()
    {
        int n = (reelRoots != null) ? reelRoots.Count : 0;

        while (_disableCounts.Count < n) _disableCounts.Add(0);
        while (_originalStates.Count < n) _originalStates.Add(false);
        while (_disableCounts.Count > n) _disableCounts.RemoveAt(_disableCounts.Count - 1);
        while (_originalStates.Count > n) _originalStates.RemoveAt(_originalStates.Count - 1);
    }

    /// <summary>
    /// Disables all configured reel roots. Safe to call multiple times (nested).
    /// </summary>
    public void DisableReels()
    {
        if (reelRoots == null) return;
        EnsureInternalListSizes();

        for (int i = 0; i < reelRoots.Count; i++)
        {
            var go = reelRoots[i];
            if (go == null) continue;

            if (_disableCounts[i] <= 0)
            {
                // First disable captures original state.
                _originalStates[i] = go.activeSelf;
                go.SetActive(false);
                _disableCounts[i] = 1;
            }
            else
            {
                _disableCounts[i]++;
            }
        }
    }

    /// <summary>
    /// Re-enables reel roots back to their original states once all disables are released.
    /// Safe to call even if not previously disabled (it will no-op).
    /// </summary>
    public void EnableReels()
    {
        if (reelRoots == null) return;
        EnsureInternalListSizes();

        for (int i = 0; i < reelRoots.Count; i++)
        {
            var go = reelRoots[i];
            if (go == null) continue;

            if (_disableCounts[i] <= 0)
            {
                _disableCounts[i] = 0;
                continue;
            }

            _disableCounts[i]--;

            if (_disableCounts[i] <= 0)
            {
                _disableCounts[i] = 0;
                go.SetActive(_originalStates[i]);
            }
        }
    }
}
