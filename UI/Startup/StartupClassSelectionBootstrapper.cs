// PATH: Assets/Scripts/UI/Startup/StartupClassSelectionBootstrapper.cs
// GUID: d48595d4376ce4e4d86910f04e87d7e3
////////////////////////////////////////////////////////////
using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Boots the game into a class selection menu BEFORE BattleManager starts the run.
///
/// Updated to use NewStartupClassSelectionPanel and to call Show() one frame later
/// (so all UI + reels are active and Awake() has run).
/// </summary>
[DefaultExecutionOrder(-1000)]
public class StartupClassSelectionBootstrapper : MonoBehaviour
{
    [Header("Available Hero/Class Prefabs")]
    [Tooltip("Prefabs the player can choose from for each party slot.")]
    [SerializeField] private GameObject[] availablePartyPrefabs;

    [Header("Scene Refs (optional)")]
    [SerializeField] private NewStartupClassSelectionPanel selectionPanel;

    [Tooltip("If null, the first BattleManager found in-scene will be used.")]
    [SerializeField] private BattleManager battleManager;

    [Header("Startup Visibility")]
    [Tooltip("Optional: Root object that contains the in-game 3D reel visuals. This is disabled during class selection.")]
    [SerializeField] private GameObject reels3DRoot;

    [Tooltip("Party size for selection (defaults to BattleManager.partySize if possible).")]
    [SerializeField] private int partySize = 0;

    private static bool s_started; // protects against duplicate bootstrappers
    private bool _startedThisInstance;

    private void Awake()
    {
        if (s_started)
        {
            // If another bootstrapper already ran, kill this one.
            Destroy(gameObject);
            return;
        }

        s_started = true;
        _startedThisInstance = true;

        // Auto-find references if not wired.
        if (battleManager == null)
            battleManager = FindInSceneIncludingInactive<BattleManager>();

        if (selectionPanel == null)
            selectionPanel = FindInSceneIncludingInactive<NewStartupClassSelectionPanel>();

        if (battleManager == null)
        {
            Debug.LogError("[StartupClassSelectionBootstrapper] No BattleManager found in scene. Cannot run class selection.");
            return;
        }

        if (selectionPanel == null)
        {
            Debug.LogError("[StartupClassSelectionBootstrapper] No NewStartupClassSelectionPanel found in scene. Cannot run class selection.");
            return;
        }

        // Hide reels during class selection
        if (reels3DRoot != null)
            reels3DRoot.SetActive(false);
    }

    private void Start()
    {
        // Ensure we only proceed if we were the instance that won the singleton gate.
        if (!_startedThisInstance) return;

        // Call Show one frame later to avoid Awake order issues (panel/reels might not be active yet).
        StartCoroutine(ShowNextFrame());
    }

    private IEnumerator ShowNextFrame()
    {
        yield return null;

        if (battleManager == null || selectionPanel == null)
            yield break;

        // Prefer BattleManager.partySize if inspector value not set.
        int size = partySize > 0 ? partySize : battleManager.PartySize;

        selectionPanel.Show(availablePartyPrefabs, size, OnConfirmed);
    }

    private void OnConfirmed(GameObject[] chosen)
    {
        if (battleManager == null) return;

        // Re-enable reels now that we're leaving selection
        if (reels3DRoot != null)
            reels3DRoot.SetActive(true);

        // Provide chosen prefabs to BattleManager; it will begin the run once it's ready.
        battleManager.SetPartyMemberPrefabs(chosen);

        // Destroy bootstrapper so it doesn't run again if you return to this scene.
        Destroy(gameObject);
    }

    // IMPORTANT: finds inactive objects too
    private static T FindInSceneIncludingInactive<T>() where T : UnityEngine.Object
    {
        var all = Resources.FindObjectsOfTypeAll<T>();
        for (int i = 0; i < all.Length; i++)
        {
            var obj = all[i];
            if (obj == null) continue;

            if (obj is Component c)
            {
                if (c.gameObject != null && c.gameObject.scene.IsValid())
                    return obj;
            }
            else if (obj is GameObject go)
            {
                if (go.scene.IsValid())
                    return obj;
            }
        }
        return null;
    }
}
//////////////////////////////////////////////////////////
