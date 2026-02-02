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
        SyncInternalLists();
    }

    private void OnValidate()
    {
        // Keep lists in sync in-editor when reelRoots changes.
        SyncInternalLists();
    }

    private void SyncInternalLists()
    {
        int n = (reelRoots != null) ? reelRoots.Count : 0;

        // Resize counts/states to match.
        while (_disableCounts.Count < n) _disableCounts.Add(0);
        while (_originalStates.Count < n) _originalStates.Add(false);
        while (_disableCounts.Count > n) _disableCounts.RemoveAt(_disableCounts.Count - 1);
        while (_originalStates.Count > n) _originalStates.RemoveAt(_originalStates.Count - 1);

        // Initialize originals to current active states (safe default).
        for (int i = 0; i < n; i++)
        {
            var go = reelRoots[i];
            _originalStates[i] = (go != null && go.activeSelf);
            // Do not reset _disableCounts here; runtime state should persist.
        }
    }

    /// <summary>
    /// Disables all configured reel roots. Safe to call multiple times (nested).
    /// </summary>
    public void DisableReels()
    {
        if (reelRoots == null) return;
        SyncInternalLists();

        for (int i = 0; i < reelRoots.Count; i++)
        {
            var go = reelRoots[i];
            if (go == null) continue;

            // First disable captures original state.
            if (_disableCounts[i] <= 0)
            {
                _originalStates[i] = go.activeSelf;
                go.SetActive(false);
                _disableCounts[i] = 1;
            }
            else
            {
                _disableCounts[i]++;
                // Already disabled.
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
        SyncInternalLists();

        for (int i = 0; i < reelRoots.Count; i++)
        {
            var go = reelRoots[i];
            if (go == null) continue;

            if (_disableCounts[i] <= 0)
            {
                // No-op: nothing to release.
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

