using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using System.Runtime.CompilerServices;

// Project specific namespaces
using SlotsAndSorcery.VFX;

public partial class BattleManager : MonoBehaviour
{
    private void BeginRunAndBattle()
    {
        if (_runStarted) return;

        _runStarted = true;
        StartNewRun();
        StartBattle();
    }
    public void StartNewRun()
    {
        _startupRewardHandled = false;

        CleanupExistingEncounter();
        DestroyPartyAvatars();

        if (resourcePool != null)
            resourcePool.ResetForNewRun(0, 0, 0, 0);

        _party = new List<PartyMemberRuntime>(partySize);

        int count = Mathf.Clamp(partySize, 1, 3);
        for (int i = 0; i < count; i++)
        {
            PartyMemberRuntime m = new PartyMemberRuntime();
            m.name = $"Ally {i + 1}";

            GameObject prefab = (partyMemberPrefabs != null && i < partyMemberPrefabs.Length) ? partyMemberPrefabs[i] : null;
            if (prefab == null)
            {
                Debug.LogError($"[BattleManager] Missing party prefab for slot {i}. Assign Party Member Prefabs size 3.");
                _party.Add(m);
                continue;
            }

            Transform spawn = (partySpawnPoints != null && i < partySpawnPoints.Length) ? partySpawnPoints[i] : null;
            Vector3 pos = spawn != null ? spawn.position : Vector3.zero;

            GameObject go = Instantiate(prefab, pos, Quaternion.identity, partyRoot);

            // Align the hero so that its prefab child 'CenterPoint' sits exactly on the spawn point.
            // This makes partySpawnPoints represent the intended visual center for VFX/UI alignment.
            if (spawn != null)
            {
                AlignHeroToSpawnPointUsingCenterPoint(go, spawn);
            }

            m.avatarGO = go;
            m.animator = go.GetComponentInChildren<Animator>(true);
            m.stats = go.GetComponentInChildren<HeroStats>(true);

            if (m.stats == null)
                Debug.LogError($"[BattleManager] Party prefab slot {i} has no HeroStats component.");

            if (m.stats != null)
            {
                m.stats.ResetForNewRun();
            }

            _party.Add(m);
        }
        
        // Startup selection data is one-shot.
        StartupPartySelectionData.Clear();

if (reelSpinSystem != null)
        {
            var heroes = new List<HeroStats>();
            for (int i = 0; i < _party.Count; i++)
            {
                if (_party[i]?.stats != null)
                    heroes.Add(_party[i].stats);
            }

            reelSpinSystem.ConfigureFromParty(heroes);
        ConfigureReelSpinSystemCashoutHooks();
        }

        InitializeBattleGridForParty();

        _activePartyIndex = GetFirstAlivePartyIndex();
        OnActivePartyMemberChanged?.Invoke(_activePartyIndex);
        NotifyPartyChanged();
        PartyReady?.Invoke();
    }
private bool TryRunLevel5EvolutionNow()
{
    if (_party == null || _party.Count == 0) return false;

    bool any = false;

    for (int i = 0; i < _party.Count; i++)
    {
        var pm = _party[i];
        var hs = pm != null ? pm.stats : null;
        if (hs == null)
        {
            Debug.LogWarning($"[Evolution] TryRunLevel5EvolutionNow partyIndex={i} heroStats=NULL. Skipping.", this);
            continue;
        }

        // Evolve exactly once, when the hero first reaches Level 5+ and has not yet been evolved.
        if (hs.Level < 5) continue;
        if (hs.AdvancedClassDef != null) continue;

        if (!TryGetLevel5EvolutionData(hs,
            out var advancedPrefab,
            out var advancedClassDef,
            out var advancedReelStripTemplate,
            out var advancedPortraitOverride,
            out var advancedWorldSpriteOverride))
        {
            var baseDef = hs.BaseClassDef;
            Debug.LogWarning($"[Evolution] No level5EvolutionMappings entry found for hero='{hs.name}' baseClass='{(baseDef != null ? baseDef.className : "NULL")}'. Skipping evolution.");
            continue;
        }

        var baseClassName = hs.BaseClassDef != null ? hs.BaseClassDef.className : "NULL";
        Debug.Log($"[Evolution] Level 5 evolution triggered for hero='{hs.name}' baseClass='{baseClassName}' -> advanced='{(advancedClassDef != null ? advancedClassDef.className : "NULL")}' prefab='{(advancedPrefab != null ? advancedPrefab.name : "NULL")}'.", this);

        bool ok = EvolvePartyMemberToAdvanced(
            partyIndex: i,
            advancedPrefab: advancedPrefab,
            advancedClassDef: advancedClassDef,
            advancedReelStripTemplate: advancedReelStripTemplate,
            advancedPortraitOverride: advancedPortraitOverride,
            advancedWorldSpriteOverride: advancedWorldSpriteOverride);

        if (!ok)
        {
            Debug.LogError($"[Evolution] Level 5 evolution FAILED for partyIndex={i} hero='{hs.name}'. Continuing run to avoid soft-lock.", this);
            continue;
        }

        any = true;
    }

    return any;
}
    public bool TryGetLevel5EvolutionData(
        HeroStats hero,
        out GameObject advancedPrefab,
        out ClassDefinitionSO advancedClassDef,
        out ReelStripSO advancedReelStripTemplate,
        out Sprite advancedPortraitOverride,
        out Sprite advancedWorldSpriteOverride)
    {
        advancedPrefab = null;
        advancedClassDef = null;
        advancedReelStripTemplate = null;
        advancedPortraitOverride = null;
        advancedWorldSpriteOverride = null;

        if (hero == null)
        {
            Debug.LogWarning("[Evolution][Mapping] hero NULL. Cannot resolve.", this);
            return false;
        }
        if (hero.Level < 5)
        {
            Debug.Log($"[Evolution][Mapping] hero='{hero.name}' level={hero.Level} < 5. Not eligible.", this);
            return false;
        }
        if (hero.AdvancedClassDef != null)
        {
            Debug.Log($"[Evolution][Mapping] hero='{hero.name}' already advanced='{hero.AdvancedClassDef.className}'.", this);
            return false;
        }

        var baseDef = hero.BaseClassDef;
        if (baseDef == null)
        {
            Debug.LogWarning($"[Evolution][Mapping] hero='{hero.name}' BaseClassDef=NULL.", this);
            return false;
        }

        EvolutionMapping match = null;
        if (level5EvolutionMappings != null)
        {
            for (int mi = 0; mi < level5EvolutionMappings.Count; mi++)
            {
                var m = level5EvolutionMappings[mi];
                if (m == null) continue;
                if (m.requiredBaseClass == null) continue;

                if (m.requiredBaseClass == baseDef ||
                    (m.requiredBaseClass != null && baseDef != null &&
                     string.Equals(m.requiredBaseClass.className, baseDef.className, StringComparison.OrdinalIgnoreCase)))
                {
                    match = m;
                    break;
                }
            }
        }

        if (match == null) return false;
        if (match.advancedPrefab == null)
        {
            Debug.LogWarning($"[Evolution][Mapping] hero='{hero.name}' matched base='{baseDef.className}' but advancedPrefab=NULL.", this);
            return false;
        }

        advancedPrefab = match.advancedPrefab;
        advancedClassDef = match.advancedClassDef;
        advancedReelStripTemplate = match.advancedReelStripTemplate;
        advancedPortraitOverride = match.advancedPortraitOverride;
        advancedWorldSpriteOverride = match.advancedWorldSpriteOverride;
        Debug.Log($"[Evolution][Mapping] hero='{hero.name}' base='{baseDef.className}' -> prefab='{advancedPrefab.name}' advClass='{(advancedClassDef != null ? advancedClassDef.className : "NULL")}' strip='{(advancedReelStripTemplate != null ? advancedReelStripTemplate.name : "NULL")}' portrait='{(advancedPortraitOverride != null ? advancedPortraitOverride.name : "NULL")}' worldSprite='{(advancedWorldSpriteOverride != null ? advancedWorldSpriteOverride.name : "NULL")}'", this);
        return true;
    }
    public bool EvolvePartyMemberToAdvanced(
        int partyIndex,
        GameObject advancedPrefab,
        ClassDefinitionSO advancedClassDef,
        ReelStripSO advancedReelStripTemplate,
        Sprite advancedPortraitOverride,
        Sprite advancedWorldSpriteOverride)
    {
        Debug.Log(
            $"[Evolution] BattleManager.EvolvePartyMemberToAdvanced BEGIN partyIndex={partyIndex} advancedPrefab='{(advancedPrefab != null ? advancedPrefab.name : "NULL")}' " +
            $"advancedClassDef='{(advancedClassDef != null ? advancedClassDef.className : "NULL")}' advancedStrip='{(advancedReelStripTemplate != null ? advancedReelStripTemplate.name : "NULL")}' " +
            $"portraitOverride='{(advancedPortraitOverride != null ? advancedPortraitOverride.name : "NULL")}' worldSpriteOverride='{(advancedWorldSpriteOverride != null ? advancedWorldSpriteOverride.name : "NULL")}'",
            this
        );

        if (!IsValidPartyIndex(partyIndex))
        {
            Debug.LogError($"[BattleManager] EvolvePartyMemberToAdvanced invalid partyIndex={partyIndex}");
            return false;
        }

        if (advancedPrefab == null)
        {
            Debug.LogError("[BattleManager] EvolvePartyMemberToAdvanced advancedPrefab is NULL.");
            return false;
        }

        PartyMemberRuntime m = _party[partyIndex];
        if (m == null || m.avatarGO == null || m.stats == null)
        {
            Debug.LogError($"[BattleManager] EvolvePartyMemberToAdvanced partyIndex={partyIndex} missing avatar/stats.");
            return false;
        }

        HeroStats oldStats = m.stats;
        List<AbilityDefinitionSO> baseUnlocked = null;
        if (oldStats != null && oldStats.BaseClassDef != null)
            baseUnlocked = oldStats.GetUnlockedAbilitiesFromClassDef(oldStats.BaseClassDef);
        Debug.Log(
            $"[Evolution] Old hero instance='{(m.avatarGO != null ? m.avatarGO.name : "NULL")}' stats='{(oldStats != null ? oldStats.name : "NULL")}' level={(oldStats != null ? oldStats.Level : 0)}",
            this
        );
        Transform parent = (partyRoot != null) ? partyRoot : m.avatarGO.transform.parent;
        Vector3 oldCenterWorld = (oldStats != null ? oldStats.CenterPointWorldPosition : m.avatarGO.transform.position);

        Vector3 pos = m.avatarGO.transform.position;
        Quaternion rot = m.avatarGO.transform.rotation;

        GameObject newGo = Instantiate(advancedPrefab, pos, rot, parent);
        Debug.Log($"[Evolution] Instantiated new advanced prefab GO='{newGo.name}'", this);
        HeroStats newStats = newGo.GetComponentInChildren<HeroStats>(true);
        Animator newAnim = newGo.GetComponentInChildren<Animator>(true);

        if (newStats == null)
        {
            Debug.LogError($"[BattleManager] Advanced prefab '{advancedPrefab.name}' has no HeroStats component.");
            Destroy(newGo);
            return false;
        }

                // Align the new prefab so its CenterPoint stays where the old hero's CenterPoint was.
        // This prevents evolved prefabs with different CenterPoint local offsets from appearing shifted.
        Vector3 newCenterWorld = newStats.CenterPointWorldPosition;
        Vector3 deltaToMatchCenter = oldCenterWorld - newCenterWorld;
        if (deltaToMatchCenter.sqrMagnitude > 0.000001f)
        {
            newGo.transform.position += deltaToMatchCenter;
            Debug.Log($"[Evolution] CenterPoint align: oldCenter={oldCenterWorld} newCenter={newCenterWorld} delta={deltaToMatchCenter} -> newPos={newGo.transform.position}", this);
        }
        else
        {
            Debug.Log($"[Evolution] CenterPoint align not needed (delta ~ 0). center={newCenterWorld}", this);
        }

        // Preserve all runtime progress from the old instance.
        Debug.Log("[Evolution] Copying runtime state oldStats -> newStats", this);
        newStats.CopyRuntimeStateFrom(oldStats);

        // Apply advanced class definition (if not already present).
        if (advancedClassDef != null && newStats.AdvancedClassDef == null)
        {
            Debug.Log($"[Evolution] Applying advanced class def '{advancedClassDef.className}'", this);
            newStats.ApplyClassDefinition(advancedClassDef);
        }
        else
        {
            Debug.Log($"[Evolution] Skipping ApplyClassDefinition (advancedClassDef NULL or already set). currentAdvanced='{(newStats.AdvancedClassDef != null ? newStats.AdvancedClassDef.className : "NULL")}'", this);
        }

        // Swap reel strip to advanced template (if provided).
        if (advancedReelStripTemplate != null)
        {
            Debug.Log($"[Evolution] Replacing reel strip from template '{advancedReelStripTemplate.name}'", this);
            newStats.ReplaceReelStripFromTemplate(advancedReelStripTemplate);
        }
        else
        {
            Debug.Log("[Evolution] No advancedReelStripTemplate provided. Leaving current reel strip as-is.", this);
        }

        // Override portrait (optional).
        if (advancedPortraitOverride != null)
        {
            Debug.Log($"[Evolution] Setting portrait override '{advancedPortraitOverride.name}'", this);
            newStats.SetPortrait(advancedPortraitOverride);
        }
        else
        {
            Debug.Log("[Evolution] No portrait override provided. Leaving portrait as-is.", this);
        }


        // Override world sprite (optional) - useful during early prefab setup.
        if (advancedWorldSpriteOverride != null)
        {
            var srs = newGo.GetComponentsInChildren<SpriteRenderer>(true);
            int changed = 0;
            for (int i = 0; i < srs.Length; i++)
            {
                if (srs[i] == null) continue;
                srs[i].sprite = advancedWorldSpriteOverride;
                changed++;
            }
            Debug.Log($"[Evolution] Applied world sprite override '{advancedWorldSpriteOverride.name}'. spriteRenderersChanged={changed}", this);
        }
        else
        {
            Debug.Log("[Evolution] No world sprite override provided. Leaving SpriteRenderer sprites as-is.", this);
        }

        // Ensure advanced class abilities are available immediately.
        if (advancedClassDef != null)
            newStats.ForceUnlockAllAbilitiesFromClassDef(advancedClassDef, includeStarterChoice: true);
        if (baseUnlocked != null)
        {
            for (int i = 0; i < baseUnlocked.Count; i++)
            {
                var a = baseUnlocked[i];
                if (a == null) continue;
                newStats.IsAbilityUnlocked(a);
            }
        }
        newStats.MarkEvolutionResolved();


        // Destroy old avatar
        Debug.Log($"[Evolution] Destroying old avatar GO='{m.avatarGO.name}'", this);
        Destroy(m.avatarGO);

        // Update runtime party entry
        m.avatarGO = newGo;
        m.animator = newAnim;
        m.stats = newStats;
        _party[partyIndex] = m;

        // Reconfigure reels to reference the new HeroStats instances.
        if (reelSpinSystem != null)
        {
            Debug.Log("[Evolution] Reconfiguring ReelSpinSystem from updated party", this);
            var heroes = new List<HeroStats>(_party.Count);
            for (int i = 0; i < _party.Count; i++)
                if (_party[i] != null && _party[i].stats != null)
                    heroes.Add(_party[i].stats);

            reelSpinSystem.ConfigureFromParty(heroes);
            Debug.Log($"[Evolution] ReelSpinSystem.ConfigureFromParty done. heroes={heroes.Count}", this);
        }
        else
        {
            Debug.Log("[Evolution] reelSpinSystem is NULL. Skipping reel reconfigure.", this);
        }

        NotifyPartyChanged();

        Debug.Log("[Evolution] NotifyPartyChanged called.", this);

        Debug.Log($"[BattleManager] Evolved partyIndex={partyIndex} '{oldStats.name}' -> prefab='{advancedPrefab.name}' class='{(advancedClassDef != null ? advancedClassDef.className : "NULL")}'.");
        return true;
    }

}

