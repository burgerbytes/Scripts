using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Plays a bass loop immediately when the character selection panel opens, and keeps hero "stem" loops
/// sample-synced to the bass using PlayScheduled. Stems fade in/out based on which hero symbols are
/// currently present in the midrow of any selection reel. The most recently changed hero can receive
/// a subtle volume boost.
/// </summary>
public class StartupCharacterSelectionMusicSync : MonoBehaviour
{
    [Serializable]
    public class Stem
    {
        [Tooltip("Hero symbol id (must match ReelSymbolSO.id or whatever the selection reel reports).")]
        public string heroId;

        [Tooltip("Audio clip for this hero stem (loopable and same length as bass).")]
        public AudioClip clip;

        [Range(0f, 1f)]
        public float baseVolume = 0.75f;
    }

    [Header("Bass (always on)")]
    [SerializeField] private AudioClip bassClip;
    [SerializeField] [Range(0f, 1f)] private float bassVolume = 0.85f;

    [Header("Stems (per hero)")]
    [SerializeField] private List<Stem> stems = new List<Stem>();

    [Header("Behavior")]
    [Tooltip("Hero id that represents 'no hero selected' (ignored when computing active heroes).")]
    [SerializeField] private string nullHeroId = "NULL";

    [Tooltip("Seconds to fade stems in/out.")]
    [SerializeField] private float fadeSeconds = 0.25f;

    [Tooltip("Most recently changed hero gets multiplied by this factor (subtle emphasis).")]
    [SerializeField] private float mostRecentBoost = 1.15f;

    [Tooltip("If true, logs debug info when starting / updating stems.")]
    [SerializeField] private bool logDebug = true;

    private AudioSource _bassSource;
    private readonly Dictionary<string, AudioSource> _stemSources = new Dictionary<string, AudioSource>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> _stemTarget = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

    private bool _started;
    private double _scheduledStartDsp;
    private string _mostRecentHeroId;

    // Legacy compatibility (some older controllers still call these)
    public void OnSelectionPanelOpened() => Begin();
    public void OnSelectionPanelClosed() => StopAndReset();
    public void OnSlotMidrowSymbolChanged(int slotIndex, string symbolId)
    {
        // Legacy: if someone calls this, just treat it as "this hero was most recent".
        if (!string.IsNullOrEmpty(symbolId) && !IsNullHero(symbolId))
            _mostRecentHeroId = symbolId;

        // We cannot infer the full 3-slot state from a single call reliably, so do nothing else here.
        // The active panel should call UpdateActiveHeroes(list,...).
    }
    public void SetActiveSlot(int slotIndex) { /* compatibility no-op */ }

    private void Awake()
    {
        // Build sources early so inspector wiring issues show up fast.
        BuildSourcesIfNeeded();
    }

    private void OnEnable()
    {
        // Safety net: if panel gets enabled without calling Begin, don't auto-start (avoid surprise audio).
        // Begin() should be called explicitly by the panel.
    }

    private void OnDisable()
    {
        StopAndReset();
    }

    public void Begin()
    {
        BuildSourcesIfNeeded();

        if (_bassSource == null)
        {
            Debug.LogWarning("[StartupCharacterSelectionMusicSync] Begin: bass source missing.", this);
            return;
        }

        if (bassClip == null)
        {
            Debug.LogWarning("[StartupCharacterSelectionMusicSync] Begin: Bass Clip is NOT assigned.", this);
            return;
        }

        if (_started)
        {
            // Already running; keep sync.
            return;
        }

        // Diagnostics
        if (logDebug)
        {
            int listenerCount = FindObjectsOfType<AudioListener>(true).Length;
            Debug.Log($"[StartupCharacterSelectionMusicSync] Begin: listenerCount={listenerCount} AudioListener.pause={AudioListener.pause} AudioListener.volume={AudioListener.volume} dspTime={AudioSettings.dspTime:0.000}", this);
        }

        _scheduledStartDsp = AudioSettings.dspTime + 0.10; // small lead time
        _bassSource.clip = bassClip;
        _bassSource.volume = bassVolume;
        _bassSource.loop = true;
        _bassSource.PlayScheduled(_scheduledStartDsp);

        // Start all stems at the exact same dsp time, muted. They will fade in later but remain phase-aligned.
        foreach (var kvp in _stemSources)
        {
            var src = kvp.Value;
            if (src == null) continue;

            var stem = stems.FirstOrDefault(s => IdEquals(s.heroId, kvp.Key));
            src.clip = stem != null ? stem.clip : null;
            src.volume = 0f;
            src.loop = true;

            if (src.clip != null)
                src.PlayScheduled(_scheduledStartDsp);
        }

        _started = true;

        if (logDebug)
            Debug.Log("[StartupCharacterSelectionMusicSync] Begin: scheduled bass + stems (muted).", this);
    }

    public void StopAndReset()
    {
        _started = false;
        _mostRecentHeroId = null;

        if (_bassSource != null)
            _bassSource.Stop();

        foreach (var src in _stemSources.Values)
        {
            if (src != null)
                src.Stop();
        }

        _stemTarget.Clear();
    }

    public void UpdateActiveHeroes(IReadOnlyList<string> midrowHeroIds, string mostRecentHeroId = null)
    {
        if (!_started)
        {
            // If someone calls update before begin, try to begin (won't play if bass clip missing).
            Begin();
        }

        if (midrowHeroIds == null) midrowHeroIds = Array.Empty<string>();

        // Determine active set (distinct, ignoring null hero).
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in midrowHeroIds)
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (IsNullHero(id)) continue;
            active.Add(id.Trim());
        }

        if (!string.IsNullOrWhiteSpace(mostRecentHeroId) && !IsNullHero(mostRecentHeroId))
            _mostRecentHeroId = mostRecentHeroId.Trim();

        if (logDebug)
        {
            string activeStr = active.Count == 0 ? "<none>" : string.Join(",", active);
            Debug.Log($"[StartupCharacterSelectionMusicSync] UpdateActiveHeroes: active={activeStr} mostRecent={_mostRecentHeroId ?? "<null>"}", this);
        }

        // Compute targets for each stem entry (only for configured stems).
        foreach (var stem in stems)
        {
            if (stem == null || string.IsNullOrWhiteSpace(stem.heroId)) continue;

            string id = stem.heroId.Trim();
            float target = 0f;

            if (active.Contains(id))
            {
                target = Mathf.Clamp01(stem.baseVolume);
                if (!string.IsNullOrEmpty(_mostRecentHeroId) && IdEquals(_mostRecentHeroId, id))
                    target = Mathf.Clamp01(target * Mathf.Max(1f, mostRecentBoost));
            }

            _stemTarget[id] = target;

            // Ensure the stem audio source exists for configured stems
            if (!_stemSources.ContainsKey(id))
                CreateStemSource(id);
        }

        // Also fade out any sources that exist but no longer configured in stems.
        foreach (var existing in _stemSources.Keys.ToArray())
        {
            bool configured = stems.Any(s => s != null && IdEquals(s.heroId, existing));
            if (!configured)
                _stemTarget[existing] = 0f;
        }
    }

    private void Update()
    {
        // Smooth fade with unscaled time (UI screens often run at timeScale=0).
        if (_stemSources.Count == 0) return;

        float dt = Time.unscaledDeltaTime;
        float t = fadeSeconds <= 0.0001f ? 1f : Mathf.Clamp01(dt / fadeSeconds);

        foreach (var kvp in _stemSources)
        {
            var id = kvp.Key;
            var src = kvp.Value;
            if (src == null) continue;

            float target = 0f;
            if (_stemTarget.TryGetValue(id, out var tv))
                target = Mathf.Clamp01(tv);

            src.volume = Mathf.MoveTowards(src.volume, target, t);
        }
    }

    private void BuildSourcesIfNeeded()
    {
        if (_bassSource == null)
            _bassSource = CreateSource("SelectionBass");

        // Build stem sources for configured stems
        foreach (var stem in stems)
        {
            if (stem == null || string.IsNullOrWhiteSpace(stem.heroId)) continue;
            string id = stem.heroId.Trim();
            if (!_stemSources.ContainsKey(id))
                CreateStemSource(id);
        }
    }

    private void CreateStemSource(string heroId)
    {
        if (string.IsNullOrWhiteSpace(heroId)) return;
        heroId = heroId.Trim();

        if (_stemSources.ContainsKey(heroId))
            return;

        var src = CreateSource($"SelectionStem_{heroId}");
        _stemSources[heroId] = src;

        // Clip assigned during Begin().
    }

    private AudioSource CreateSource(string name)
    {
        var src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = true;
        src.spatialBlend = 0f;
        src.volume = 0f;

        // Attempt to survive global pauses / volume changes (some projects pause listener in menus).
        src.ignoreListenerPause = true;
        src.ignoreListenerVolume = true;

        src.name = name;
        return src;
    }

    private bool IsNullHero(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return true;
        if (string.IsNullOrWhiteSpace(nullHeroId)) return false;
        return IdEquals(id, nullHeroId);
    }

    private static bool IdEquals(string a, string b)
    {
        return string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
