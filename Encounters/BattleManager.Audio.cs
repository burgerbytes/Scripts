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
    private void TriggerHeroHitReaction(PartyMemberRuntime pm)
    {
        if (!enableHeroHitReaction) return;
        if (pm == null) return;

        // 1) Animator flinch (optional if animator missing)
        if (pm.animator != null && !string.IsNullOrEmpty(heroHitTriggerName))
        {
            pm.animator.ResetTrigger(heroHitTriggerName); // helps if multiple hits occur quickly
            pm.animator.SetTrigger(heroHitTriggerName);
        }

        // 2) White flash (optional; requires component on hero prefab)
        if (enableHeroHitFlash && pm.avatarGO != null)
        {
            var flash = pm.avatarGO.GetComponentInChildren<HeroHitFlash>(true);
            if (flash != null)
                flash.Flash();
        }

        // 3) Hit SFX (optional)
        PlayHeroHitSfx();

        if (logHitReaction)
            Debug.Log($"[Battle][HitReaction] hero={pm.name} trigger='{heroHitTriggerName}' flash={enableHeroHitFlash}", pm.avatarGO);
    }
    private void EnsureHeroHitSfxSource()
    {
        if (_heroHitSfxSource != null) return;

        // Create a dedicated 2D audio source for hit SFX so it doesn't interfere with battle music.
        _heroHitSfxSource = gameObject.AddComponent<AudioSource>();
        _heroHitSfxSource.playOnAwake = false;
        _heroHitSfxSource.loop = false;
        _heroHitSfxSource.spatialBlend = 0f; // 2D
        _heroHitSfxSource.volume = 1f;
    }
    private void PlayHeroHitSfx()
    {
        if (heroHitSfx == null) return;

        EnsureHeroHitSfxSource();

        if (randomizeHeroHitPitch)
            _heroHitSfxSource.pitch = UnityEngine.Random.Range(heroHitPitchRange.x, heroHitPitchRange.y);
        else
            _heroHitSfxSource.pitch = 1f;

        _heroHitSfxSource.PlayOneShot(heroHitSfx, heroHitSfxVolume);
    }
private void EnsureBattleMusicSource()
{
    if (battleMusicSource == null)
    {
        battleMusicSource = GetComponent<AudioSource>();
        if (battleMusicSource == null)
            battleMusicSource = gameObject.AddComponent<AudioSource>();
    }

    battleMusicSource.playOnAwake = false;
    battleMusicSource.spatialBlend = 0f; // 2D
    battleMusicSource.loop = loopBattleMusic;
    battleMusicSource.volume = battleMusicVolume;

    if (battleMusicClip != null && battleMusicSource.clip != battleMusicClip)
        battleMusicSource.clip = battleMusicClip;
}
private void StartBattleMusic()
{
    if (battleMusicClip == null && battleMusicSource == null) return;

    EnsureBattleMusicSource();

    if (battleMusicSource.clip == null) return;

    // If it's already playing, don't restart it.
    if (battleMusicSource.isPlaying)
    {
        // Ensure volume/loop reflect current inspector values.
        battleMusicSource.loop = loopBattleMusic;
        battleMusicSource.volume = battleMusicVolume;
        return;
    }

    // Fade-in or instant play.
    if (battleMusicFadeSeconds <= 0f)
    {
        battleMusicSource.volume = battleMusicVolume;
        battleMusicSource.Play();
        return;
    }

    if (_battleMusicFadeRoutine != null)
        StopCoroutine(_battleMusicFadeRoutine);

    _battleMusicFadeRoutine = StartCoroutine(FadeMusicRoutine(battleMusicSource, 0f, battleMusicVolume, battleMusicFadeSeconds, playIfStopped: true));
}
private void StopBattleMusic()
{
    if (battleMusicSource == null) return;

    if (!battleMusicSource.isPlaying)
        return;

    if (battleMusicFadeSeconds <= 0f)
    {
        battleMusicSource.Stop();
        return;
    }

    if (_battleMusicFadeRoutine != null)
        StopCoroutine(_battleMusicFadeRoutine);

    _battleMusicFadeRoutine = StartCoroutine(FadeMusicRoutine(battleMusicSource, battleMusicSource.volume, 0f, battleMusicFadeSeconds, playIfStopped: false, stopAtEnd: true));
}
private IEnumerator FadeMusicRoutine(AudioSource src, float from, float to, float duration, bool playIfStopped, bool stopAtEnd = false)
{
    if (src == null) yield break;

    if (playIfStopped && !src.isPlaying)
        src.Play();

    float t = 0f;
    duration = Mathf.Max(0.0001f, duration);

    // Set the starting volume explicitly.
    src.volume = from;

    while (t < duration)
    {
        t += Time.deltaTime;
        float a = Mathf.Clamp01(t / duration);
        src.volume = Mathf.Lerp(from, to, a);
        yield return null;
    }

    src.volume = to;

    if (stopAtEnd && Mathf.Approximately(to, 0f))
        src.Stop();
}
private bool TryStartHeroBattleMusicStems()
{
    if (!useHeroBattleMusicStems) return false;
    if (_party == null || _party.Count == 0) return false;

    // Clear any prior runtime bookkeeping (without forcing stop if already playing).
    _heroStemStoppedForDead.Clear();

    // Pick root
    Transform root = battleMusicStemRoot != null ? battleMusicStemRoot : this.transform;

    // Gather clips for living heroes
    var stemsToPlay = new List<(HeroStats hero, AudioClip clip)>();
    for (int i = 0; i < _party.Count; i++)
    {
        var pm = _party[i];
        if (pm == null || pm.stats == null || pm.IsDead) continue;

        var hs = pm.stats;
        AudioClip clip = null;
        try { clip = hs.BattleMusicStemClip; } catch { clip = null; }

        if (clip != null)
            stemsToPlay.Add((hs, clip));
    }

    if (stemsToPlay.Count == 0)
        return false;

    // Stop generic track if it was playing (stems take over).
    if (battleMusicSource != null && battleMusicSource.isPlaying)
        battleMusicSource.Stop();

    // Clean up any old stems first
    StopHeroBattleMusicStemsImmediate();

    // Compute per-stem volume
    float perStemVolume = battleMusicVolume;
    if (normalizeStemVolume && stemsToPlay.Count > 1)
        perStemVolume = battleMusicVolume / Mathf.Sqrt(stemsToPlay.Count);

    // Schedule all stems to start at the same DSP time
    double dspStart = AudioSettings.dspTime + 0.075;

    for (int i = 0; i < stemsToPlay.Count; i++)
    {
        var (hero, clip) = stemsToPlay[i];

        var src = root.gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = 0f; // 2D
        src.loop = loopBattleMusic;
        src.clip = clip;
        src.volume = (battleMusicFadeSeconds > 0f) ? 0f : perStemVolume;

        _heroMusicStemSources[hero] = src;

        // Start all stems sample-synced
        src.PlayScheduled(dspStart);

        // Fade-in if desired
        if (battleMusicFadeSeconds > 0f)
            StartCoroutine(FadeMusicRoutine(src, 0f, perStemVolume, battleMusicFadeSeconds, playIfStopped: false));
    }

    return true;
}
private void StartBattleMusicForEncounter()
{
    // Prefer hero stems. If none are configured, fall back to single clip.
    if (TryStartHeroBattleMusicStems())
        return;

    StartBattleMusic();
}
private void StopAllBattleMusic()
{
    // Stop hero stems first (if any), then stop generic track.
    StopHeroBattleMusicStems();
    StopBattleMusic();
}
private void StopHeroBattleMusicStems()
{
    if (_heroMusicStemSources.Count == 0)
        return;

    // If we have a fade duration, fade all sources down and stop.
    if (battleMusicFadeSeconds > 0f)
    {
        if (_heroStemFadeAllRoutine != null)
            StopCoroutine(_heroStemFadeAllRoutine);

        _heroStemFadeAllRoutine = StartCoroutine(FadeOutAndStopAllHeroStemsRoutine(battleMusicFadeSeconds));
    }
    else
    {
        StopHeroBattleMusicStemsImmediate();
    }
}
private void StopHeroBattleMusicStemsImmediate()
{
    if (_heroStemFadeAllRoutine != null)
    {
        StopCoroutine(_heroStemFadeAllRoutine);
        _heroStemFadeAllRoutine = null;
    }

    foreach (var kvp in _heroMusicStemSources)
    {
        var src = kvp.Value;
        if (src == null) continue;
        try { src.Stop(); } catch { }
        Destroy(src);
    }

    _heroMusicStemSources.Clear();
    _heroStemStoppedForDead.Clear();
}
    private AudioClip ResolveVictoryJingleClip(HeroStats killer)
    {
        // Prefer a hero-level override if it exists, else fall back to BaseClassDef field, else default.
        AudioClip clip = null;

        if (killer != null)
        {
            // Try property "VictoryJingleClip" (recommended).
            try
            {
                var prop = killer.GetType().GetProperty("VictoryJingleClip", BindingFlags.Public | BindingFlags.Instance);
                if (prop != null)
                    clip = prop.GetValue(killer, null) as AudioClip;
            }
            catch { /* ignore */ }

            // Try field "victoryJingleClip" or "victoryJingleClipOverride".
            if (clip == null)
            {
                try
                {
                    var f = killer.GetType().GetField("victoryJingleClip", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                    if (f != null)
                        clip = f.GetValue(killer) as AudioClip;
                }
                catch { /* ignore */ }
            }
            if (clip == null)
            {
                try
                {
                    var f = killer.GetType().GetField("victoryJingleClipOverride", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                    if (f != null)
                        clip = f.GetValue(killer) as AudioClip;
                }
                catch { /* ignore */ }
            }

            // Base class fallback: ClassDefinitionSO.victoryJingleClip (via reflection to avoid hard dependency).
            if (clip == null && killer.BaseClassDef != null)
            {
                try
                {
                    var cf = killer.BaseClassDef.GetType().GetField("victoryJingleClip", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                    if (cf != null)
                        clip = cf.GetValue(killer.BaseClassDef) as AudioClip;
                }
                catch { /* ignore */ }
            }
        }

        if (clip == null)
            clip = defaultVictoryJingle;

        return clip;
    }
private AudioSource GetOrCreateVictoryJingleSource()
{
    if (victoryJingleSource != null)
        return victoryJingleSource;

    // IMPORTANT:
    // Do NOT add this AudioSource to the BattleManager GameObject itself.
    // Some UI/SFX systems call GetComponent<AudioSource>() on shared objects; if we attach here,
    // the victory jingle source can get reused for button clicks / enable/disable sounds.
    // Instead, we create a dedicated hidden child.
    Transform child = transform.Find(VictoryJingleChildName);
    GameObject go;
    if (child != null)
    {
        go = child.gameObject;
    }
    else
    {
        go = new GameObject(VictoryJingleChildName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
    }

    victoryJingleSource = go.GetComponent<AudioSource>();
    if (victoryJingleSource == null)
        victoryJingleSource = go.AddComponent<AudioSource>();

    // Dedicated 2D one-shot style source.
    victoryJingleSource.playOnAwake = false;
    victoryJingleSource.loop = false;
    victoryJingleSource.spatialBlend = 0f; // 2D
    victoryJingleSource.ignoreListenerPause = true;
    victoryJingleSource.ignoreListenerVolume = true;

    return victoryJingleSource;
}
    private IEnumerator PlayVictoryJingleRoutine()
    {
        if (!playVictoryJingle)
            yield break;

        
        if (_victoryJinglePlayedThisEncounter)
            yield break;

        _victoryJinglePlayedThisEncounter = true;
AudioClip clip = ResolveVictoryJingleClip(_victoryKillerHero);
        if (clip == null)
        {
            if (victoryJingleDebugLogs)
                Debug.Log($"[VictoryJingle][SKIP] No clip resolved (killer={( _victoryKillerHero!=null ? _victoryKillerHero.gameObject.name : "<none>" )}). time={Time.time:0.00} rt={Time.realtimeSinceStartup:0.00}", this);
            yield break;
        }

        string heroName = _victoryKillerHero != null ? _victoryKillerHero.gameObject.name : "<none>";
        string baseClass = (_victoryKillerHero != null && _victoryKillerHero.BaseClassDef != null) ? _victoryKillerHero.BaseClassDef.className : "<unknown>";
        string trackName = clip != null ? clip.name : "<null>";

        if (victoryJingleDebugLogs)
        {
            Debug.Log($"[VictoryJingle][WILL] hero={heroName} baseClass={baseClass} track={trackName} clipLen={clip.length:0.00}s time={Time.time:0.00} rt={Time.realtimeSinceStartup:0.00}", this);
        }

        AudioSource src = GetOrCreateVictoryJingleSource();
        src.Stop();
        src.clip = clip;
        src.volume = Mathf.Clamp01(victoryJingleVolume);

        if (randomizeVictoryJinglePitch)
            src.pitch = UnityEngine.Random.Range(victoryJinglePitchRange.x, victoryJinglePitchRange.y);
        else
            src.pitch = 1f;

        float beginTime = Time.time;
        float beginRt = Time.realtimeSinceStartup;

        if (victoryJingleDebugLogs)
            Debug.Log($"[VictoryJingle][BEGIN] hero={heroName} baseClass={baseClass} track={trackName} Time.time={beginTime:0.00} rt={beginRt:0.00}", this);

        src.Play();

        // Wait in realtime so pausing Time.timeScale won't truncate the perceived duration.
        float wait = Mathf.Max(0.01f, clip.length);
        yield return new WaitForSecondsRealtime(wait);

        float endTime = Time.time;
        float endRt = Time.realtimeSinceStartup;

        if (victoryJingleDebugLogs)
            Debug.Log($"[VictoryJingle][END] hero={heroName} track={trackName} Time.time={endTime:0.00} rt={endRt:0.00} elapsedRt={(endRt - beginRt):0.00}", this);
    }
    private void VJLog(string phase, HeroStats hero, AudioClip clip, float? clipLenOverride = null)
    {
        if (!victoryJingleDebugLogs) return;

        string heroName = hero != null ? hero.name : "<null>";
        string baseClass = "<unknown>";
        try
        {
            // Adjust if your HeroStats exposes base class differently
            if (hero != null && hero.BaseClassDef != null)
                baseClass = hero.BaseClassDef.name;
        }
        catch { /* ignore */ }

        string trackName = clip != null ? clip.name : "<null>";
        float len = clipLenOverride ?? (clip != null ? clip.length : 0f);

        Debug.Log(
            $"[VictoryJingle][{phase}] " +
            $"hero={heroName} baseClass={baseClass} track={trackName} clipLen={len:0.000}s " +
            $"t={Time.time:0.000} rt={Time.realtimeSinceStartup:0.000}",
            this
        );
    }

}
