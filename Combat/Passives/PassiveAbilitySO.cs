using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Data-driven, always-on passive ability.
///
/// Author passives as ScriptableObjects with one or more rules:
///   WHEN a trigger fires (e.g., ReelSymbolLanded)
///   IF optional filters match (e.g., resourceType == Attack)
///   THEN apply one or more effects (e.g., +1 bonus damage on next attack)
///
/// This lets you create new passives without writing new C# classes.
/// </summary>
[CreateAssetMenu(menuName = "Combat/Passive Ability", fileName = "Passive_")]
public class PassiveAbilitySO : AbilityDefinitionSO
{
    // Optional VFX/UI helpers cached at runtime.
    private static ScreenDimmer _screenDimmer;
    private static PassivePresentationDirector _presentation;

    public enum PassiveTriggerType
    {
        ReelSymbolLanded,
        // Add more trigger types here as the game grows.
    }

    public enum PassiveEffectType
    {
        AddBonusDamageNextAttack,
        AddShield,
        DimScreen,
        PlayProcPresentation,
        // Add more effect types here as the game grows.
    }

    [Serializable]
    public class PassiveEffect
    {
        public PassiveEffectType effectType;

        [Tooltip("Generic magnitude used by most effects (e.g., +1 bonus damage).")]
        public int amount = 1;

        [Tooltip("Generic float magnitude used by some effects (e.g., DimScreen alpha).")]
        [Range(0f, 1f)]
        public float floatValue = 0.55f;

        [Header("Presentation (optional)")]
        [Tooltip("If effectType is PlayProcPresentation, this prefab will be instantiated at the hero.")]
        public GameObject vfxPrefab;

        [Tooltip("If effectType is PlayProcPresentation, duration in seconds to hold dim + zoom before restoring.")]
        [Min(0f)]
        public float durationSeconds = 1.0f;

        [Tooltip("If effectType is PlayProcPresentation, camera zoom multiplier for orthographic cameras (<1 zooms in).")]
        [Range(0.5f, 1.0f)]
        public float zoomMultiplier = 0.85f;

        [Tooltip("If true, clamps the resulting value so it never goes below 0 (where applicable).")]
        public bool clampNonNegative = true;
    }

    [Serializable]
    public class PassiveRule
    {
        [Header("When")]
        public PassiveTriggerType trigger;

        [Header("Filters (optional)")]
        [Tooltip("If enabled, only fires when the reel symbol payout type matches.")]
        public bool filterByResourceType = false;

        public ReelSpinSystem.ResourceType resourceType;

        [Tooltip("If enabled, only fires when the ReelSymbolSO.id matches this string (case-sensitive).")]
        public bool filterBySymbolId = false;

        public string symbolIdEquals;

        [Tooltip("If enabled, only fires when the reel payout amount is at least this value.")]
        public bool filterByMinAmount = false;

        public int minAmount = 1;

        [Header("Then")]
        public List<PassiveEffect> effects = new List<PassiveEffect>();
    }

    [Header("Debug")]
    [SerializeField] private bool logDebug = true;

    [Header("Passive Rules")]
    [Tooltip("Each rule listens for a trigger and applies effects when filters match.")]
    [SerializeField] private List<PassiveRule> rules = new List<PassiveRule>();

    // Cache per-hero delegates so we can unsubscribe cleanly.
    [NonSerialized] private readonly Dictionary<int, Action<ReelSymbolSO, ReelSpinSystem.ResourceType, int, int>> _reelHandlers
        = new Dictionary<int, Action<ReelSymbolSO, ReelSpinSystem.ResourceType, int, int>>(8);

    protected virtual void OnEnable()
    {
        // Enforce passive kind.
        kind = AbilityKind.Passive;
        if (string.IsNullOrWhiteSpace(abilityName))
            abilityName = name;
    }

    /// <summary>
    /// Called by HeroStats when the hero instance enables.
    /// </summary>
    public virtual void Register(HeroStats hero)
    {
        if (hero == null || hero.Events == null) return;

        // Subscribe only if we actually have rules that use this trigger.
        if (!HasTrigger(PassiveTriggerType.ReelSymbolLanded))
            return;

        int id = hero.GetInstanceID();
        if (_reelHandlers.ContainsKey(id))
            return;

        Action<ReelSymbolSO, ReelSpinSystem.ResourceType, int, int> handler = (symbol, resourceType, amount, multiplier) =>
        {
            ApplyReelSymbolLandedRules(hero, symbol, resourceType, amount, multiplier);
        };

        _reelHandlers.Add(id, handler);
        hero.Events.OnReelSymbolLanded += handler;
    }

    /// <summary>
    /// Called by HeroStats when the hero instance disables.
    /// </summary>
    public virtual void Unregister(HeroStats hero)
    {
        if (hero == null || hero.Events == null) return;

        int id = hero.GetInstanceID();
        if (!_reelHandlers.TryGetValue(id, out var handler))
            return;

        hero.Events.OnReelSymbolLanded -= handler;
        _reelHandlers.Remove(id);
    }

    private bool HasTrigger(PassiveTriggerType t)
    {
        if (rules == null) return false;
        for (int i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            if (r == null) continue;
            if (r.trigger == t) return true;
        }
        return false;
    }

    private void ApplyReelSymbolLandedRules(HeroStats hero, ReelSymbolSO symbol, ReelSpinSystem.ResourceType resourceType, int amount, int multiplier)
    {
        if (logDebug && resourceType == ReelSpinSystem.ResourceType.Attack)
        {
            string symId = symbol != null ? (!string.IsNullOrEmpty(symbol.id) ? symbol.id : symbol.name) : "<null>";
            Debug.Log($"[Passive][BattleRhythm] ATK symbol landed hero='{hero.name}' symbol='{symId}' amount={amount} mult={multiplier}");
        }

        if (hero == null) return;
        if (rules == null) return;

        for (int i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (rule == null) continue;
            if (rule.trigger != PassiveTriggerType.ReelSymbolLanded) continue;

            if (rule.filterByResourceType && rule.resourceType != resourceType)
                continue;

            if (rule.filterBySymbolId)
            {
                string want = rule.symbolIdEquals ?? string.Empty;
                string got = (symbol != null) ? (symbol.id ?? string.Empty) : string.Empty;
                if (got != want) continue;
            }

            if (rule.filterByMinAmount && amount < rule.minAmount)
                continue;

            if (logDebug)
                {
                    string symId2 = symbol != null ? (!string.IsNullOrEmpty(symbol.id) ? symbol.id : symbol.name) : "<null>";
                    Debug.Log($"[Passive] Proc passive='{this.name}' hero='{hero.name}' trigger=ReelSymbolLanded type={resourceType} symbol='{symId2}' amount={amount} mult={multiplier}");
                }

                ApplyEffects(hero, rule.effects);
        }
    }

    private void ApplyEffects(HeroStats hero, List<PassiveEffect> effects)
    {
        if (hero == null) return;
        if (effects == null) return;

        for (int j = 0; j < effects.Count; j++)
        {
            var e = effects[j];
            if (e == null) continue;

                        if (logDebug)
            {
                Debug.Log($"[Passive]   Effect start passive='{this.name}' hero='{hero.name}' effect={e.effectType} amount={e.amount} float={e.floatValue} dur={e.durationSeconds} zoom={e.zoomMultiplier}");
            }

            switch (e.effectType)
            {
                case PassiveEffectType.AddBonusDamageNextAttack:
                {
                    int amt = e.amount;
                    if (e.clampNonNegative) amt = Mathf.Max(0, amt);
                    if (amt == 0) break;
                    //hero.AddBonusDamageNextAttack(amt);
                    break;
                }

                case PassiveEffectType.AddShield:
                {
                    int amt = e.amount;
                    if (e.clampNonNegative) amt = Mathf.Max(0, amt);
                    if (amt == 0) break;
                    hero.AddShield(amt);
                    break;
                }

                case PassiveEffectType.DimScreen:
                {
                    float a = Mathf.Clamp01(e.floatValue);
                    var dimmer = GetScreenDimmer();
                    if (dimmer != null)
                    {
                        dimmer.DimScreenTo(a);
                    }
                    else
                    {
                        // Keep this as a warning (not error) so you notice missing scene wiring.
                        Debug.LogWarning($"[Passives] DimScreen effect fired but no ScreenDimmer exists in scene. passive='{abilityName}'.");
                    }
                    break;
                }

                case PassiveEffectType.PlayProcPresentation:
                {
                    // Convenience effect: dim + camera punch-in + optional VFX at the same time.
                    // Designed for Battle Rhythm, but generic enough to reuse for other procs.
                    var presenter = GetPresentationDirector();
                    if (presenter != null)
                    {
                        presenter.PlayProcPresentation(
                            hero,
                            dimAlpha: Mathf.Clamp01(e.floatValue),
                            zoomMultiplier: Mathf.Clamp(e.zoomMultiplier, 0.5f, 1.0f),
                            durationSeconds: Mathf.Max(0f, e.durationSeconds),
                            vfxPrefab: e.vfxPrefab
                        );
                    }
                    else
                    {
                        Debug.LogWarning($"[Passives] PlayProcPresentation fired but no PassivePresentationDirector exists in scene. passive='{abilityName}'.");
                    }
                    break;
                }

                default:
                    Debug.LogWarning($"[Passives] Unhandled effectType='{e.effectType}' on passive='{abilityName}'.");
                    break;
            }
        }
    }

    private static ScreenDimmer GetScreenDimmer()
    {
        if (_screenDimmer != null) return _screenDimmer;

#if UNITY_2023_1_OR_NEWER
        _screenDimmer = UnityEngine.Object.FindFirstObjectByType<ScreenDimmer>();
#else
        _screenDimmer = UnityEngine.Object.FindObjectOfType<ScreenDimmer>();
#endif
        return _screenDimmer;
    }

    private static PassivePresentationDirector GetPresentationDirector()
    {
        if (_presentation != null) return _presentation;

#if UNITY_2023_1_OR_NEWER
        _presentation = UnityEngine.Object.FindFirstObjectByType<PassivePresentationDirector>();
#else
        _presentation = UnityEngine.Object.FindObjectOfType<PassivePresentationDirector>();
#endif
        return _presentation;
    }
}
