using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Tracks per-hero performance within a single battle so we can award bonus XP
/// without bloating BattleManager.
/// </summary>
[DisallowMultipleComponent]
public class BattlePerformanceTracker : MonoBehaviour
{
    [Serializable]
    public class HeroSummary
    {
        public HeroStats hero;
        public Sprite portrait;
        public string heroName;
        public int startLevel;
        public int startXp;
        public int startXpToNextLevel;
        public int baseXp;
        public int bonusXp;
        public int totalXp;
        public int damageDealt;
        public int damageTaken;
        public List<string> bonusReasons;
    }

    private sealed class HeroPerf
    {
        public HeroStats hero;
        public int maxHpAtStart;
        public int levelAtStart;
        public int xpAtStart;
        public int xpToNextAtStart;
        public int damageDealt;
        public int damageTaken;
        public int baseXp;
        public bool usedAnyAbility;
        public bool usedOnlyAttackSkills = true;
        public bool usedNoDamagingAbilities = true;
    }

    private readonly Dictionary<HeroStats, HeroPerf> _perfByHero = new();

    public void BeginBattle(IReadOnlyList<HeroStats> heroes)
    {
        _perfByHero.Clear();
        if (heroes == null) return;

        for (int i = 0; i < heroes.Count; i++)
        {
            var h = heroes[i];
            if (h == null) continue;

            _perfByHero[h] = new HeroPerf
            {
                hero = h,
                maxHpAtStart = Mathf.Max(1, h.MaxHp),
                levelAtStart = h.Level,
                xpAtStart = Mathf.Max(0, h.XP),
                xpToNextAtStart = Mathf.Max(1, h.XPToNextLevel),
                damageDealt = 0,
                damageTaken = 0,
                baseXp = 0,
                usedAnyAbility = false,
                usedOnlyAttackSkills = true,
                usedNoDamagingAbilities = true,
            };
        }
    }

    public void RecordAbilityUse(HeroStats hero, AbilityDefinitionSO ability)
    {
        if (hero == null || ability == null) return;
        if (!_perfByHero.TryGetValue(hero, out var p) || p == null) return;

        p.usedAnyAbility = true;

        bool isDamaging = ability.baseDamage > 0;
        if (isDamaging)
            p.usedNoDamagingAbilities = false;

        // "Attack skill" heuristic: consumes Attack, does damage, and does not consume Magic.
        // This keeps the rule simple and data-driven.
        bool isAttackSkill = isDamaging && ability.cost.attack > 0 && ability.cost.magic <= 0;
        if (!isAttackSkill)
            p.usedOnlyAttackSkills = false;
    }

    public void RecordDamageDealt(HeroStats hero, int amount)
    {
        if (hero == null) return;
        if (amount <= 0) return;
        if (!_perfByHero.TryGetValue(hero, out var p) || p == null) return;

        p.damageDealt += Mathf.Max(0, amount);
    }

    public void RecordDamageTaken(HeroStats hero, int amount)
    {
        if (hero == null) return;
        if (amount <= 0) return;
        if (!_perfByHero.TryGetValue(hero, out var p) || p == null) return;

        p.damageTaken += Mathf.Max(0, amount);
    }

    public void RecordBaseXpGained(HeroStats hero, int amount)
    {
        if (hero == null) return;
        if (amount <= 0) return;
        if (!_perfByHero.TryGetValue(hero, out var p) || p == null) return;

        p.baseXp += amount;
    }

    /// <summary>
    /// Computes bonus XP (and totals) for each hero and returns a summary list.
    ///
    /// IMPORTANT: This method does NOT mutate hero progression (no GainXP calls).
    /// This is intentional so the results panel can animate XP from the pre-battle
    /// state to the post-battle state.
    /// </summary>
    public List<HeroSummary> ComputeSummaries(IReadOnlyList<HeroStats> heroes)
    {
        var result = new List<HeroSummary>();
        if (heroes == null || heroes.Count == 0) return result;

        // Find most damage dealt among tracked heroes.
        int topDamage = -1;
        for (int i = 0; i < heroes.Count; i++)
        {
            var h = heroes[i];
            if (h == null) continue;
            if (_perfByHero.TryGetValue(h, out var p) && p != null)
                topDamage = Mathf.Max(topDamage, p.damageDealt);
        }

        for (int i = 0; i < heroes.Count; i++)
        {
            var h = heroes[i];
            if (h == null) continue;

            _perfByHero.TryGetValue(h, out var p);
            p ??= new HeroPerf
            {
                hero = h,
                maxHpAtStart = Mathf.Max(1, h.MaxHp),
                levelAtStart = h.Level,
                xpAtStart = Mathf.Max(0, h.XP),
                xpToNextAtStart = Mathf.Max(1, h.XPToNextLevel),
            };

            int bonus = 0;
            var reasons = new List<string>();

            // No damage taken
            if (p.damageTaken <= 0)
            {
                bonus += 1;
                reasons.Add("No damage taken");
            }

            // Most damage (ties allowed)
            if (topDamage >= 0 && p.damageDealt == topDamage && topDamage > 0)
            {
                bonus += 1;
                reasons.Add("Most damage");
            }

            // Damage sponge
            if (p.damageTaken >= Mathf.CeilToInt(p.maxHpAtStart * 0.9f) && h.CurrentHp > 0)
            {
                bonus += 1;
                reasons.Add("Damage sponge");
            }

            // Aggressive: only attack skills (and used at least one ability)
            if (p.usedAnyAbility && p.usedOnlyAttackSkills)
            {
                bonus += 1;
                reasons.Add("Aggressive");
            }

            // Tactician: no damaging abilities (and used at least one ability)
            if (p.usedAnyAbility && p.usedNoDamagingAbilities)
            {
                bonus += 1;
                reasons.Add("Tactician");
            }

            result.Add(new HeroSummary
            {
                hero = h,
                portrait = h.Portrait,
                heroName = h.name,
                startLevel = p.levelAtStart,
                startXp = p.xpAtStart,
                startXpToNextLevel = p.xpToNextAtStart,
                baseXp = p.baseXp,
                bonusXp = bonus,
                totalXp = p.baseXp + bonus,
                damageDealt = p.damageDealt,
                damageTaken = p.damageTaken,
                bonusReasons = reasons
            });
        }

        return result;
    }

    /// <summary>
    /// Applies the XP recorded in the provided summaries. Call this after the results
    /// panel finishes animating (or the player clicks Continue).
    /// </summary>
    public void ApplySummaries(IEnumerable<HeroSummary> summaries)
    {
        if (summaries == null) return;

        foreach (var s in summaries)
        {
            if (s == null || s.hero == null) continue;
            if (s.totalXp <= 0) continue;
            s.hero.GainXP(s.totalXp);
        }
    }
}
