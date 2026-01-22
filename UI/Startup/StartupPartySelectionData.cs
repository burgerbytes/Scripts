// GUID: 16aa818ea5fae1d4cb17ad2118df528e
////////////////////////////////////////////////////////////
using UnityEngine;

/// <summary>
/// Static handoff from Startup selection UI to runtime systems.
/// Keeps selection state (chosen party member prefabs + chosen starting abilities) during the menu flow.
/// </summary>
public static class StartupPartySelectionData
{
    // Slot order matches party slots (0..N-1).
    private static GameObject[] _chosenPrefabs = new GameObject[3];
    private static AbilityDefinitionSO[] _startingAbilities = new AbilityDefinitionSO[3];

    /// <summary>Ensure internal arrays are at least the requested party size.</summary>
    public static void EnsureCapacity(int partySize)
    {
        partySize = Mathf.Clamp(partySize, 1, 3);

        if (_chosenPrefabs == null || _chosenPrefabs.Length != partySize)
            _chosenPrefabs = new GameObject[partySize];

        if (_startingAbilities == null || _startingAbilities.Length != partySize)
            _startingAbilities = new AbilityDefinitionSO[partySize];
    }

    public static void Clear()
    {
        if (_chosenPrefabs != null)
        {
            for (int i = 0; i < _chosenPrefabs.Length; i++)
                _chosenPrefabs[i] = null;
        }

        if (_startingAbilities != null)
        {
            for (int i = 0; i < _startingAbilities.Length; i++)
                _startingAbilities[i] = null;
        }
    }

    // ---- Party Prefabs ----

    /// <summary>Sets the chosen party member prefab for a slot.</summary>
    public static void SetPartyMemberPrefab(int slot, GameObject prefab)
    {
        if (_chosenPrefabs == null) EnsureCapacity(3);
        if (slot < 0 || slot >= _chosenPrefabs.Length) return;
        _chosenPrefabs[slot] = prefab;
    }

    // Aliases for reflection-based callers (older controller variants)
    public static void SetChosenPrefab(int slot, GameObject prefab) => SetPartyMemberPrefab(slot, prefab);
    public static void SetChosenHeroPrefab(int slot, GameObject prefab) => SetPartyMemberPrefab(slot, prefab);
    public static void SetHeroPrefab(int slot, GameObject prefab) => SetPartyMemberPrefab(slot, prefab);
    public static void SetSelectedPrefab(int slot, GameObject prefab) => SetPartyMemberPrefab(slot, prefab);
    public static void SetSlotPrefab(int slot, GameObject prefab) => SetPartyMemberPrefab(slot, prefab);
    public static void SetHeroAt(int slot, GameObject prefab) => SetPartyMemberPrefab(slot, prefab);

    public static GameObject GetPartyMemberPrefab(int slot)
    {
        if (_chosenPrefabs == null) return null;
        if (slot < 0 || slot >= _chosenPrefabs.Length) return null;
        return _chosenPrefabs[slot];
    }

    public static GameObject[] GetChosenPartyPrefabs(int partySize)
    {
        EnsureCapacity(partySize);
        var result = new GameObject[_chosenPrefabs.Length];
        for (int i = 0; i < result.Length; i++)
            result[i] = _chosenPrefabs[i];
        return result;
    }

    // ---- Starting Abilities ----

    public static void SetStartingAbility(int slot, AbilityDefinitionSO ability)
    {
        if (_startingAbilities == null) EnsureCapacity(3);
        if (slot < 0 || slot >= _startingAbilities.Length) return;
        _startingAbilities[slot] = ability;
    }

    public static AbilityDefinitionSO GetStartingAbility(int slot)
    {
        if (_startingAbilities == null) return null;
        if (slot < 0 || slot >= _startingAbilities.Length) return null;
        return _startingAbilities[slot];
    }
}

////////////////////////////////////////////////////////////
