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
    private void TickBleedingAtEndOfPlayerTurn()
    {
        if (_party == null) return;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        for (int i = 0; i < _party.Count; i++)
        {
            var pm = _party[i];
            var hs = pm != null ? pm.stats : null;
            if (hs == null || pm.IsDead) continue;

            int stacks = 0;
            try { stacks = hs.BleedStacks; } catch { stacks = 0; }
            if (stacks <= 0) continue;

            int appliedTurn = -999;
            try
            {
                var pi = hs.GetType().GetProperty("BleedAppliedOnPlayerTurn", flags);
                if (pi != null && pi.PropertyType == typeof(int))
                    appliedTurn = (int)pi.GetValue(hs, null);
                else
                {
                    var fi = hs.GetType().GetField("BleedAppliedOnPlayerTurn", flags) ?? hs.GetType().GetField("bleedAppliedOnPlayerTurn", flags);
                    if (fi != null && fi.FieldType == typeof(int))
                        appliedTurn = (int)fi.GetValue(hs);
                }
            }
            catch { appliedTurn = -999; }

            if (appliedTurn == PlayerTurnNumber)
                continue;

            int dealt = 0;
            try
            {
                var mi = hs.GetType().GetMethod("TickBleedingAtEndOfPlayerTurn", flags, null, Type.EmptyTypes, null);
                if (mi != null && mi.ReturnType == typeof(int))
                {
                    dealt = (int)mi.Invoke(hs, null);
                }
                else
                {
                    var mi2 = hs.GetType().GetMethod("TickBleedingAtTurnStart", flags, null, Type.EmptyTypes, null);
                    if (mi2 != null && mi2.ReturnType == typeof(int))
                        dealt = (int)mi2.Invoke(hs, null);
                    else
                        dealt = 0;
                }
            }
            catch { dealt = 0; }

            if (dealt > 0 && pm.avatarGO != null)
                SpawnDamageNumber(GetHeroCenterWorldPosition(hs, pm.avatarGO.transform), dealt);
        }

        // If any heroes died from bleed ticks, stop their battle music stems.
        CheckAndHandleNewlyDeadHeroesForStems();

        if (IsPartyDefeated())
        {
            Debug.Log("[BattleManager] Party defeated (bleed tick).", this);
            SetState(BattleState.BattleEnd);
        }

        NotifyPartyChanged();
    }
    private void ApplyStatusCleansingToHero(
        AbilityDefinitionSO ability,
        HeroStats targetStats,
        string targetName,
        GameObject targetGO,
        bool forceBleedForFirstAid)
    {
        if (ability == null || targetStats == null) return;

        bool clearBleed = forceBleedForFirstAid;
        bool clearStun = false;

        var list = ability.removesStatusEffects;
        if (list != null)
        {
            for (int i = 0; i < list.Count; i++)
            {
                switch (list[i])
                {
                    case RemovableStatusEffect.Bleeding:
                        clearBleed = true;
                        break;
                    case RemovableStatusEffect.Stunned:
                        clearStun = true;
                        break;
                }
            }
        }

        int removedCount = 0;

        if (clearBleed)
        {
            bool removed = false;
            try { removed = targetStats.ClearBleeding(); } catch { removed = false; }
            if (removed) removedCount++;
            if (logFlow && removed) Debug.Log($"[Battle][Cleanse] Removed BLEEDING from {targetName} via {ability.abilityName}", this);
        }

        if (clearStun)
        {
            bool removed = false;
            try { removed = targetStats.ClearStun(); } catch { removed = false; }
            if (removed) removedCount++;
            if (logFlow && removed) Debug.Log($"[Battle][Cleanse] Removed STUNNED from {targetName} via {ability.abilityName}", this);
        }

        if (removedCount > 0)
            NotifyPartyChanged();
    }
    private static void ApplyBleedStacksToHero(HeroStats hs, int stacksToAdd)
    {
        if (hs == null || stacksToAdd <= 0) return;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var t = hs.GetType();

        var miAdd = t.GetMethod("AddBleedStacks", flags, null, new[] { typeof(int) }, null);
        if (miAdd != null)
        {
            miAdd.Invoke(hs, new object[] { stacksToAdd });
            return;
        }

        var miSet = t.GetMethod("SetBleedStacks", flags, null, new[] { typeof(int) }, null);
        if (miSet != null)
        {
            int current = 0;
            try
            {
                var pi = t.GetProperty("BleedStacks", flags);
                if (pi != null && pi.PropertyType == typeof(int)) current = (int)pi.GetValue(hs, null);
                else
                {
                    var fi = t.GetField("BleedStacks", flags) ?? t.GetField("bleedStacks", flags);
                    if (fi != null && fi.FieldType == typeof(int)) current = (int)fi.GetValue(hs);
                }
            }
            catch { current = 0; }

            miSet.Invoke(hs, new object[] { current + stacksToAdd });
            return;
        }

        try
        {
            var pi = t.GetProperty("BleedStacks", flags);
            if (pi != null && pi.CanWrite && pi.PropertyType == typeof(int))
            {
                int cur = (int)(pi.GetValue(hs, null) ?? 0);
                pi.SetValue(hs, cur + stacksToAdd, null);
                return;
            }
        }
        catch { }

        try
        {
            var fi = t.GetField("BleedStacks", flags) ?? t.GetField("bleedStacks", flags);
            if (fi != null && fi.FieldType == typeof(int))
            {
                int cur = (int)(fi.GetValue(hs) ?? 0);
                fi.SetValue(hs, cur + stacksToAdd);
            }
        }
        catch { }
    }
    private void ApplyPartyHiddenVisuals()
    {
        if (_party == null) return;

        for (int i = 0; i < _party.Count; i++)
        {
            var pm = _party[i];
            if (pm == null || pm.avatarGO == null) continue;

            var hs = pm.stats;
            bool hidden = hs != null && hs.IsHidden;

            var sr = pm.avatarGO.GetComponentInChildren<SpriteRenderer>(true);
            if (sr != null)
                sr.color = hidden ? hiddenTint : Color.white;

            
            
            // Status icons: support multiple simultaneous effects by creating one child icon per effect under _StatusIcon.
            // (Legacy versions used a single StatusEffectIconController on the root, which could only show one sprite at a time.)

            // Status icons should be positioned relative to the hero prefab's CenterPoint (if present).
            // This avoids variance from differing sprite pivots/bounds between heroes.
            Transform centerTf = GetHeroCenterPointTransform(hs, pm.avatarGO.transform);
            Transform desiredParent = (centerTf != null) ? centerTf : pm.avatarGO.transform;

            Transform iconTf = null;

            // First try: look for an existing "_StatusIcon" under the preferred anchor (CenterPoint/root),
            // then fall back to the HeroStats root (legacy setups).
            if (desiredParent != null)
            {
                iconTf = desiredParent.Find("_StatusIcon");
                if (iconTf == null)
                    iconTf = desiredParent.Find("__StatusIcon");
            }

            if (iconTf == null && hs != null)
            {
                iconTf = hs.transform.Find("_StatusIcon");
                if (iconTf == null)
                    iconTf = hs.transform.Find("__StatusIcon");
            }

            if (iconTf == null)
            {
                // Broad fallback: find any existing "_StatusIcon" anywhere under the avatar GO.
                var all = pm.avatarGO.GetComponentsInChildren<Transform>(true);
                for (int t = 0; t < all.Length; t++)
                {
                    if (all[t] != null && all[t].name == "_StatusIcon")
                    {
                        iconTf = all[t];
                        break;
                    }
                }
            }

            // If the ally prefab doesn't include a status icon anchor, create one at runtime.
            if (iconTf == null)
            {
                var go = new GameObject("_StatusIcon");
                go.transform.SetParent(desiredParent != null ? desiredParent : pm.avatarGO.transform, false);
                iconTf = go.transform;
            }
            else
            {
                // Ensure the anchor is parented to the desired parent so offsets are relative to CenterPoint.
                if (desiredParent != null && iconTf.parent != desiredParent)
                    iconTf.SetParent(desiredParent, true);
            }

            // Normalize placement relative to CenterPoint (or root if CenterPoint is missing).
            if (iconTf != null)
            {
                iconTf.localPosition = statusIconLocalOffset;
                iconTf.localScale = Vector3.one * statusIconScale;

                // Root should never render a sprite (children do the rendering).
                var rootSr = iconTf.GetComponent<SpriteRenderer>();
                if (rootSr != null) rootSr.enabled = false;

                int corrosionCount = (reelSpinSystem != null) ? reelSpinSystem.GetCorrosionCountForReel(i) : 0;
                RefreshHeroStatusIcons(iconTf, hs, corrosionCount);
                LayoutHeroStatusIcons(iconTf);
            }
        }
    }
private void RefreshHeroStatusIcons(Transform statusIconRoot, HeroStats hs, int corrosionCount)
{
    if (statusIconRoot == null) return;

    bool hidden = hs != null && hs.IsHidden;
    bool stunned = hs != null && hs.IsStunned;
    bool triple = hs != null && hs.IsTripleBladeEmpoweredThisTurn;

    int attackBoost = (hs != null) ? hs.BonusDamageNextAttack : 0;
    bool attackBoostActive = attackBoost > 0;
    bool bleeding = hs != null && hs.IsBleeding;
    int bleedStacks = (hs != null) ? hs.BleedStacks : 0;

    bool corrosion = corrosionCount > 0;
    int corrosionStacks = Mathf.Max(0, corrosionCount);

    // Disable any legacy root-level "Stacks" label; stacks now live under the Bleeding icon.
    var legacyStacks = statusIconRoot.Find("Stacks");
    if (legacyStacks != null)
        legacyStacks.gameObject.SetActive(false);

    EnsureHeroStatusIcon(statusIconRoot, "Hidden", statusIconHiddenSprite, hidden);
    EnsureHeroStatusIcon(statusIconRoot, "Stunned", statusIconStunnedSprite, stunned);
    EnsureHeroStatusIcon(statusIconRoot, "TripleBlade", statusIconTripleBladeEmpoweredSprite, triple);

    var attackBoostIcon = EnsureHeroStatusIcon(statusIconRoot, "AttackBoost", statusIconAttackBoostSprite, attackBoostActive);
    if (attackBoostIcon != null)
        EnsureHeroStatusStacks(attackBoostIcon, attackBoostActive ? attackBoost : 0);

    var corrosionIcon = EnsureHeroStatusIcon(statusIconRoot, "Corrosion", statusIconCorrosionSprite, corrosion);
    if (corrosionIcon != null)
        EnsureHeroStatusStacks(corrosionIcon, corrosion ? corrosionStacks : 0);

    var bleedIcon = EnsureHeroStatusIcon(statusIconRoot, "Bleeding", statusIconBleedingSprite, bleeding);
    if (bleedIcon != null)
        EnsureHeroStatusStacks(bleedIcon, bleeding ? bleedStacks : 0);
}
private Transform EnsureHeroStatusIcon(Transform root, string childName, Sprite sprite, bool active)
{
    if (root == null) return null;

    // Don't create/show an icon if no sprite is assigned.
    bool shouldShow = active && sprite != null;

    Transform tf = root.Find(childName);
    if (tf == null)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(root, false);
        tf = go.transform;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 50;
    }

    // Keep transforms stable; layout will position in a row.
    tf.localPosition = Vector3.zero;
    tf.localRotation = Quaternion.identity;
    tf.localScale = Vector3.one;

    var iconSr = tf.GetComponent<SpriteRenderer>();
    if (iconSr == null) iconSr = tf.gameObject.AddComponent<SpriteRenderer>();
    iconSr.sprite = sprite;
    iconSr.enabled = shouldShow;

    if (tf.gameObject.activeSelf != shouldShow)
        tf.gameObject.SetActive(shouldShow);

    return tf;
}
private void EnsureHeroStatusStacks(Transform iconTf, int stacks)
{
    if (iconTf == null) return;

    Transform stacksTf = iconTf.Find("Stacks");
    TextMeshPro tmp = null;

    if (stacksTf == null)
    {
        var go = new GameObject("Stacks");
        go.transform.SetParent(iconTf, false);
        stacksTf = go.transform;

        tmp = go.AddComponent<TextMeshPro>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = false;

        var mr = tmp.GetComponent<MeshRenderer>();
        if (mr != null) mr.sortingOrder = 51;
    }
    else
    {
        tmp = stacksTf.GetComponent<TextMeshPro>();
        if (tmp == null) tmp = stacksTf.GetComponentInChildren<TextMeshPro>(true);
    }

    if (stacksTf != null)
    {
        stacksTf.localPosition = statusStackTextLocalOffset;
        stacksTf.localScale = Vector3.one * statusStackTextScale;
    }

    if (tmp != null)
    {
        bool show = stacks > 0;
        tmp.text = show ? stacks.ToString() : "";
        tmp.enabled = show;

        if (statusStackTextFontSize > 0f)
            tmp.fontSize = statusStackTextFontSize;
    }
}
    private void ApplyMonsterStatusVisuals()
    {

        if (_activeMonsters == null) return;

        for (int i = 0; i < _activeMonsters.Count; i++)
        {
            var m = _activeMonsters[i];
            if (m == null) continue;

            // Preferred: show status icons under the monster's HP bar.
            var hpBar = m.GetComponentInChildren<MonsterHpBar>(true);
            if (hpBar != null)
            {
                hpBar.ConfigureStatusSprites(statusIconBleedingSprite, 
                                             statusIconFocusRuneSprite,
                                             statusIconIgnitionSprite,
                                             statusIconStasisSprite,
                                             statusIconSabotagedSprite);
                // MonsterHpBar subscribes to status changes and will refresh automatically,
                // but do an initial refresh so newly-spawned monsters show correct icons immediately.
                // (The call above already refreshes.)
                continue;
            }

            // Fallback (legacy): world-space icon above the monster.
            Transform iconTf = m.transform.Find("_StatusIcon");
            if (iconTf == null)
            {
                var go = new GameObject("_StatusIcon");
                go.transform.SetParent(m.transform, false);
                iconTf = go.transform;
                iconTf.localPosition = new Vector3(0f, 1.2f, 0f);
                iconTf.localScale = Vector3.one;
            }

            var ctrl = iconTf.GetComponent<MonsterStatusEffectIconController>();
            if (ctrl == null)
                ctrl = iconTf.gameObject.AddComponent<MonsterStatusEffectIconController>();

            ctrl.Configure(statusIconBleedingSprite, statusIconSabotagedSprite);

            int stacks = 0;
            try { stacks = m.BleedStacks; } catch { stacks = 0; }
            ctrl.SetBleedStacks(stacks);

            int sab = 0;
            try { sab = m.SabotageStacks; } catch { sab = 0; }
            ctrl.SetSabotageStacks(sab);
        }

    }
    public void RefreshStatusVisuals()
    {
        ApplyPartyHiddenVisuals();
        ApplyMonsterStatusVisuals();
    }
    private void SpawnDamageNumber(Vector3 worldPos, int amount)
    {
        if (amount == 0) return;

        Vector3 jitter = new Vector3(
            UnityEngine.Random.Range(-damageNumberRandomJitter.x, damageNumberRandomJitter.x),
            UnityEngine.Random.Range(-damageNumberRandomJitter.y, damageNumberRandomJitter.y),
            UnityEngine.Random.Range(-damageNumberRandomJitter.z, damageNumberRandomJitter.z)
        );

        Vector3 spawnPos = worldPos + damageNumberWorldOffset + jitter;

        if (damageNumberPrefab != null)
        {
            DamageNumber dn = Instantiate(damageNumberPrefab);
            dn.transform.position = spawnPos;
            TrySetDamageNumberValue(dn, amount);
            return;
        }

        if (!enableRuntimeDamageNumbers)
            return;

        var go = new GameObject($"DamageNumber_{amount}");
        go.transform.position = spawnPos;

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = amount.ToString();
        tmp.fontSize = runtimeDamageNumberFontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.color = Color.white;

        var runtime = go.AddComponent<RuntimeDamageNumber>();
        runtime.Initialize(Camera.main, runtimeDamageNumberLifetime, runtimeDamageNumberRiseDistance);
    }
    private static void TrySetDamageNumberValue(DamageNumber dn, int amount)
    {
        if (dn == null) return;

        string[] names = { "Init", "SetValue", "SetAmount", "SetNumber", "SetDamage", "Initialize", "Setup" };

        Type t = dn.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (string methodName in names)
        {
            MethodInfo miInt = t.GetMethod(methodName, flags, null, new[] { typeof(int) }, null);
            if (miInt != null)
            {
                miInt.Invoke(dn, new object[] { amount });
                return;
            }

            MethodInfo miStr = t.GetMethod(methodName, flags, null, new[] { typeof(string) }, null);
            if (miStr != null)
            {
                miStr.Invoke(dn, new object[] { amount.ToString() });
                return;
            }
        }

        TMP_Text tmp = dn.GetComponent<TMP_Text>();
        if (tmp == null) tmp = dn.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null)
        {
            tmp.text = amount.ToString();
            return;
        }

        dn.gameObject.SendMessage("SetValue", amount, SendMessageOptions.DontRequireReceiver);
    }
    private static void TrySetDamageNumberTextAndColor(DamageNumber dn, string textValue, Color color)
    {
        if (dn == null) return;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        try
        {
            // Most common: private TMP_Text text;
            var f = dn.GetType().GetField("text", flags);
            if (f != null)
            {
                var tmp = f.GetValue(dn) as TMP_Text;
                if (tmp != null)
                {
                    tmp.text = textValue;
                    tmp.color = color;
                    return;
                }
            }

            // Fallback: search any TMP_Text on the object.
            var any = dn.GetComponent<TMP_Text>();
            if (any == null) any = dn.GetComponentInChildren<TMP_Text>(true);
            if (any != null)
            {
                any.text = textValue;
                any.color = color;
            }
        }
        catch
        {
            // best-effort only
        }
    }

}
