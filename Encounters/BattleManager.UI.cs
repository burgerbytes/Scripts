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
    private bool ShouldOfferEvolutionPanel(HeroStats hero)
    {
        if (hero == null) return false;

        // IMPORTANT: only offer evolution when the hero actually reached the evolution threshold.
        // This prevents the evolution panel from popping for non-mapped classes and accidentally
        // running the reel-upgrade minigame early.
        if (!hero.HasPendingEvolution)
        {
            Debug.Log($"[Evolution][Gate] hero='{hero.name}' HasPendingEvolution=false -> false", this);
            return false;
        }

        if (hero.AdvancedClassDef != null)
        {
            Debug.Log($"[Evolution][Gate] hero='{hero.name}' already advanced='{hero.AdvancedClassDef.className}' -> false", this);
            return false;
        }

        bool mappingFound = TryGetLevel5EvolutionData(
            hero,
            out _,
            out _,
            out _,
            out _,
            out _);

        // Legacy fallback: allow Fighter/Ninja evolution even if the mapping list isn't wired
        // or the base class SO reference changed.
        bool legacyFighterNinjaMage =
            hero.BaseClassDef != null &&
            !string.IsNullOrEmpty(hero.BaseClassDef.className) &&
            (
                string.Equals(hero.BaseClassDef.className, "Fighter", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(hero.BaseClassDef.className, "Ninja", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(hero.BaseClassDef.className, "Mage", StringComparison.OrdinalIgnoreCase)
            );

        bool ok = mappingFound || legacyFighterNinjaMage;

        Debug.Log($"[Evolution][Gate] hero='{hero.name}' pending={hero.HasPendingEvolution} mappingFound={mappingFound} legacyFighterNinjaMage={legacyFighterNinjaMage} -> {ok}", this);
        return ok;
    }
private IEnumerator FadeOutAndStopAllHeroStemsRoutine(float seconds)
{
    // Snapshot (sources may be modified while fading)
    var sources = new List<AudioSource>(_heroMusicStemSources.Values);

    for (int i = 0; i < sources.Count; i++)
    {
        var src = sources[i];
        if (src == null) continue;

        // Fade to zero, then stop.
        StartCoroutine(FadeMusicRoutine(src, src.volume, 0f, seconds, playIfStopped: false, stopAtEnd: true));
    }

    // Wait out the fade, then clean up components.
    yield return new WaitForSeconds(Mathf.Max(0f, seconds) + 0.02f);

    StopHeroBattleMusicStemsImmediate();
    _heroStemFadeAllRoutine = null;
}
private void FadeOutHeroStemIfNeeded(HeroStats hero)
{
    if (!useHeroBattleMusicStems) return;
    if (hero == null) return;
    if (_heroStemStoppedForDead.Contains(hero)) return;

    if (_heroMusicStemSources.TryGetValue(hero, out var src) && src != null)
    {
        _heroStemStoppedForDead.Add(hero);

        float fade = Mathf.Max(0f, heroStemFadeOutSeconds);

        if (fade <= 0f)
        {
            src.Stop();
            Destroy(src);
            _heroMusicStemSources.Remove(hero);
            return;
        }

        StartCoroutine(FadeOutAndStopSingleStemRoutine(hero, src, fade));
    }
}
private IEnumerator FadeOutAndStopSingleStemRoutine(HeroStats hero, AudioSource src, float seconds)
{
    if (src == null) yield break;

    // Fade out and stop.
    yield return FadeMusicRoutine(src, src.volume, 0f, seconds, playIfStopped: false, stopAtEnd: true);

    if (src != null)
        Destroy(src);

    if (hero != null)
        _heroMusicStemSources.Remove(hero);
}
private void LayoutHeroStatusIcons(Transform statusIconRoot)
{
    if (statusIconRoot == null) return;

    // Collect active icon children (SpriteRenderer) excluding the stack label object.
    List<Transform> icons = new List<Transform>(8);

    for (int i = 0; i < statusIconRoot.childCount; i++)
    {
        Transform child = statusIconRoot.GetChild(i);
        if (child == null || !child.gameObject.activeSelf) continue;

        // Exclude the legacy stack label container if it's directly under the root.
        if (string.Equals(child.name, "Stacks", StringComparison.OrdinalIgnoreCase))
            continue;

        // Only layout actual icon sprites.
        var sr = child.GetComponent<SpriteRenderer>();
        if (sr != null)
            icons.Add(child);
    }

    // Centered horizontal row.
    int count = icons.Count;
    if (count > 0)
    {
        float startX = -(count - 1) * 0.5f * statusIconHorizontalSpacing;
        for (int i = 0; i < count; i++)
        {
            float x = startX + i * statusIconHorizontalSpacing;
            icons[i].localPosition = new Vector3(x, 0f, 0f);
        }
    }

    // Apply stack-count tuning if a TMP label exists (common legacy: "_StatusIcon/Stacks").
    Transform stacksTf = statusIconRoot.Find("Stacks");
    if (stacksTf != null)
    {
        stacksTf.localPosition = statusStackTextLocalOffset;
        stacksTf.localScale = Vector3.one * statusStackTextScale;

        TMP_Text tmp = stacksTf.GetComponent<TMP_Text>();
        if (tmp == null)
            tmp = stacksTf.GetComponentInChildren<TMP_Text>(true);

        if (tmp != null && statusStackTextFontSize > 0f)
            tmp.fontSize = statusStackTextFontSize;
    }

    // Also, if any icon has its own embedded TMP count label, apply the same tuning there too.
    for (int i = 0; i < icons.Count; i++)
    {
        var tmp = icons[i].GetComponentInChildren<TMP_Text>(true);
        if (tmp == null) continue;

        // Try to move a child named "Stacks"/"Count" if present, otherwise leave as-is.
        Transform labelTf = icons[i].Find("Stacks");
        if (labelTf == null) labelTf = icons[i].Find("Count");
        if (labelTf != null)
        {
            labelTf.localPosition = statusStackTextLocalOffset;
            labelTf.localScale = Vector3.one * statusStackTextScale;
        }

        if (statusStackTextFontSize > 0f)
            tmp.fontSize = statusStackTextFontSize;
    }
}
    private ItemOptionSO BuildRuntimeSkipOption()
    {
        ItemOptionSO skip = ScriptableObject.CreateInstance<ItemOptionSO>();
        skip.optionName = "Skip";
        skip.description = "Skip this reward and start the battle.";
        skip.pros = Array.Empty<string>();
        skip.cons = Array.Empty<string>();
        skip.item = null;
        skip.quantity = 0;
        skip.icon = null;
        return skip;
    }
    private static HeroStats[] BuildPartyStatsArray(List<PartyMemberRuntime> party)
    {
        // IMPORTANT: Preserve party indices so dropdown selections map back to _party reliably.
        // (We may have null slots if a party member is missing.)
        if (party == null || party.Count == 0) return System.Array.Empty<HeroStats>();

        var arr = new HeroStats[party.Count];
        for (int i = 0; i < party.Count; i++)
            arr[i] = party[i] != null ? party[i].stats : null;

        return arr;
    }

}
