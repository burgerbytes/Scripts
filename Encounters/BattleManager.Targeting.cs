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
    public int GetPartyIndexForHeroStats(HeroStats hero)
    {
        if (hero == null || _party == null) return -1;
        for (int i = 0; i < _party.Count; i++)
        {
            if (_party[i] != null && _party[i].stats == hero)
                return i;
        }
        return -1;
    }
    public int GetIncomingDamagePreviewForPartyIndex(int index)
    {
        if (!IsValidPartyIndex(index)) return 0;

        var hs = _party[index].stats;
        if (hs == null || hs.CurrentHp <= 0) return 0;

        // Predict HP loss by simulating how shields + defense will reduce incoming damage.
        int predictedHpLoss = 0;
        int remainingShield = Mathf.Max(0, hs.Shield);
        int defense = Mathf.Max(0, hs.Defense);

        for (int i = 0; i < _plannedIntents.Count; i++)
        {
            var intent = _plannedIntents[i];
            if (intent.enemy == null || intent.enemy.IsDead) continue;// Conceal/Hidden: single-target attacks miss, but AoE still hits.
            // Mirror the runtime resolution rules (see EnemyAttack resolution).
            bool hitsThisHero = intent.isAoe || intent.targetPartyIndex == index;
            if (!hitsThisHero) continue;

            if (hs.IsHidden && !intent.isAoe)
                continue;

            int raw = intent.damage > 0 ? intent.damage : intent.enemy.GetDamage();
            raw = Mathf.Max(0, raw);
            if (raw <= 0) continue;

            // Shield absorbs first (shared across all hits in the preview).
            int absorbed = Mathf.Min(remainingShield, raw);
            remainingShield -= absorbed;
            int afterShield = raw - absorbed;

            // Defense mitigation happens per-hit (matches HeroStats.TakeDamage()).
            int hpLoss = Mathf.Max(0, afterShield - defense);
            predictedHpLoss += hpLoss;
        }

        // Add bleed tick preview (applies at start of the player's turn).
        try
        {
            if (hs.IsBleeding)
            {
                int stacks = hs.BleedStacks;
                int appliedTurn = hs.BleedAppliedOnPlayerTurn;
                if (stacks > 0 && appliedTurn != PlayerTurnNumber)
                {
                    int raw = stacks;
                    int hpLoss = Mathf.Max(0, raw - defense);
                    predictedHpLoss += hpLoss;
                }
            }
        }
        catch { }

        return Mathf.Max(0, predictedHpLoss);
    }
    public void SelectEnemyTarget(Monster target)
    {

        if (logFlow)
            Debug.Log($"[Battle][AbilityTarget] Enemy clicked. target={(target != null ? target.name : "<null>")} awaitingEnemyTarget={_awaitingEnemyTarget}");

        if (!IsPlayerPhase || _resolving) return;
        if (!_awaitingEnemyTarget) return;
        if (target == null) return;
        if (target.IsDead) return;

        if (_previewEnemyTarget != target)
        {
            // IMPORTANT: do NOT set _previewEnemyTarget here; SetEnemyTargetPreview() needs the old value
            // so it can clear the previous target's preview correctly.
            SetEnemyTargetPreview(target);
            ShowConfirmText();

            if (logFlow)
                Debug.Log($"[Battle][AbilityTarget] Preview target set to {target.name}. Click again to confirm.");
            return;
        }

        _selectedEnemyTarget = target;
        _selectedFieldObjectTarget = null;
        _awaitingEnemyTarget = false;

        ClearEnemyTargetPreview();

        HideConfirmText();

        if (logFlow)
            Debug.Log($"[Battle][AbilityTarget] Target confirmed: {target.name}. Resolving ability.");

        AbilityCastState.RaiseTargetConfirmed();
        // Resume windup animation (if it was being held).
        ResumePendingWindupHold();
        StartCoroutine(ResolvePendingAbility());

    }
    private Transform GetEnemyVisualTransform(Monster enemy)
    {
        if (enemy == null) return null;

        var sr = enemy.GetComponentInChildren<SpriteRenderer>(true);
        if (sr != null) return sr.transform;

        if (enemy.transform.childCount > 0) return enemy.transform.GetChild(0);

        return enemy.transform;
    }
    private void SetEnemyTargetPreview(Monster target)
    {
        if (_previewEnemyTarget != null && _previewEnemyTarget != target)
        {
            var oldBar = _previewEnemyTarget.GetComponentInChildren<MonsterHpBar>(true);
            if (oldBar != null) oldBar.ClearPreview();
        }

        _previewEnemyTarget = target;

        if (monsterInfoController != null) monsterInfoController.Show(target);

        if (target == null || _pendingAbility == null) return;
        if (!IsValidPartyIndex(_pendingActorIndex)) return;

        var actor = _party[_pendingActorIndex];
        if (actor == null || actor.stats == null || actor.IsDead) return;

        // NEW: Non-damaging abilities should show 0 predicted damage (no preview drop).
        // Also: don't preview-consume or include "next attack" bonus for non-damaging abilities.
        if (_pendingAbility.targetType == AbilityTargetType.Enemy && !_pendingAbility.isDamaging)
        {
            var bar0 = target.GetComponentInChildren<MonsterHpBar>(true);
            if (bar0 != null)
                bar0.SetDamagePreview(target.CurrentHp); // no change

            UpdateEnemyTargetIndicators();
            NotifyPartyChanged();
            return;
        }

        int previewPassiveBonus = 0;
        if (actor.stats != null && _pendingAbility != null && _pendingAbility.targetType == AbilityTargetType.Enemy)
        {
            // Preview should include the "next attack" bonus even when baseDamage is 0,
            // because your runtime damage model is: Attack + baseDamage (+ bonus).
            // BUT only if the ability is damaging (handled above).
            int baseNoBonus = Mathf.Max(0, actor.stats.Attack) + Mathf.Max(0, _pendingAbility.baseDamage);
            if (baseNoBonus > 0)
                previewPassiveBonus = actor.stats.BonusDamageNextAttack;
        }

        int previewBonusFromSpentAtk = 0;
        // Heavy Strike preview: include bonus damage based on CURRENT ATK in the pool (without spending it).
        // This mirrors ResolvePendingAbility logic where spend-all-ATK is forced and bonusDamageFromSpentAtk is added into totalBaseDamage.
        bool previewSpendAllAtk = false;
        try
        {
            previewSpendAllAtk = (_pendingAbility != null && (_pendingAbility.spendAllAttackResources || string.Equals(_pendingAbility.name, "Heavy Strike", StringComparison.OrdinalIgnoreCase)));
        }
        catch { previewSpendAllAtk = false; }

        if (previewSpendAllAtk && resourcePool != null && _pendingAbility != null)
        {
            long atkInPool = Math.Max(0L, resourcePool.Attack);
            int perAtk = Mathf.Max(0, _pendingAbility.bonusDamagePerAttackResource);
            long raw = atkInPool * (long)perAtk;
            if (raw > int.MaxValue) raw = int.MaxValue;
            previewBonusFromSpentAtk = (int)raw;
        }

        int totalBaseDamage =
            Mathf.Max(0, actor.stats.Attack) +
            Mathf.Max(0, _pendingAbility.baseDamage) +
            Mathf.Max(0, previewPassiveBonus) +
            Mathf.Max(0, previewBonusFromSpentAtk);

        int predictedDamage = 0;

        // Optional micro-optimization: if total base is 0, skip CalculateDamageFromAbility.
        if (totalBaseDamage > 0)
        {
            predictedDamage = target.CalculateDamageFromAbility(
                abilityBaseDamage: totalBaseDamage,
                classAttackModifier: 1f,
                element: _pendingAbility.element,
                abilityTags: _pendingAbility.tags);
        }

        int previewHp = Mathf.Max(0, target.CurrentHp - predictedDamage);

        var bar = target.GetComponentInChildren<MonsterHpBar>(true);
        if (bar != null)
            bar.SetDamagePreview(previewHp);

        UpdateEnemyTargetIndicators();
        NotifyPartyChanged(); // lets PartyHUD refresh ally target indicators
    }
    private void ClearEnemyTargetPreview()
    {
        if (_previewEnemyTarget != null)
        {
            var bar = _previewEnemyTarget.GetComponentInChildren<MonsterHpBar>(true);
            if (bar != null) bar.ClearPreview();
        }
        _previewEnemyTarget = null;

        UpdateEnemyTargetIndicators();
        NotifyPartyChanged();
    }
    private TargetIndicatorUI GetOrCreateEnemyTargetIndicator(Monster m)
    {
        if (m == null) return null;

        if (_enemyTargetIndicators.TryGetValue(m, out var cached) && cached != null)
            return cached;

        // If the prefab already has an indicator wired, use it.
        var existing = m.GetComponentInChildren<TargetIndicatorUI>(true);
        if (existing != null)
        {
            _enemyTargetIndicators[m] = existing;
            return existing;
        }

        // Option A: Spawn at runtime if a prefab is provided.
        if (enemyTargetIndicatorPrefab == null)
            return null;

        RectTransform parent = null;

        // Prefer attaching to the HP bar object so offsets are intuitive.
        var hpBar = m.GetComponentInChildren<MonsterHpBar>(true);
        if (hpBar != null)
        {
            parent = hpBar.GetComponent<RectTransform>();
            if (parent == null)
                parent = hpBar.transform.parent as RectTransform;
        }

        // Fallback: any canvas under the monster.
        if (parent == null)
        {
            var canvas = m.GetComponentInChildren<Canvas>(true);
            if (canvas != null)
                parent = canvas.transform as RectTransform;
        }

        if (parent == null)
            return null;

        TargetIndicatorUI spawned = Instantiate(enemyTargetIndicatorPrefab, parent);
        spawned.name = "TargetIndicator";
        spawned.transform.SetAsLastSibling();
        spawned.Configure(enemyTargetIndicatorOffset, enemyTargetIndicatorScale);
        spawned.SetVisible(false);

        _enemyTargetIndicators[m] = spawned;
        _spawnedEnemyTargetIndicators.Add(m);
        return spawned;
    }
    private void RemoveEnemyTargetIndicatorForMonster(Monster m)
    {
        if (m == null) return;
        if (_enemyTargetIndicators == null) return;

        if (_enemyTargetIndicators.TryGetValue(m, out var indicator))
        {
            _enemyTargetIndicators.Remove(m);
            if (_spawnedEnemyTargetIndicators.Contains(m))
            {
                _spawnedEnemyTargetIndicators.Remove(m);
                if (indicator != null && indicator.gameObject != null)
                    Destroy(indicator.gameObject);
            }
        }
    }
    private void CleanupEnemyTargetIndicators()
    {
        if (_enemyTargetIndicators == null || _enemyTargetIndicators.Count == 0)
            return;

        foreach (var kvp in _enemyTargetIndicators)
        {
            if (!_spawnedEnemyTargetIndicators.Contains(kvp.Key))
                continue;

            var indicator = kvp.Value;
            if (indicator != null && indicator.gameObject != null)
                Destroy(indicator.gameObject);
        }
        _enemyTargetIndicators.Clear();
        _spawnedEnemyTargetIndicators.Clear();
    }
    private void UpdateEnemyTargetIndicators()
    {
        // Optional, purely visual.
        // Show indicator only while awaiting an enemy target, and only on the current preview target.
        bool shouldShow = _awaitingEnemyTarget && _previewEnemyTarget != null;

        for (int i = 0; i < _activeMonsters.Count; i++)
        {
            Monster m = _activeMonsters[i];
            if (m == null) continue;

            var indicator = GetOrCreateEnemyTargetIndicator(m);
            if (indicator == null) continue;

            indicator.SetVisible(shouldShow && m == _previewEnemyTarget);
        }
    }
    private void RetargetPlannedIntentsForEnemy(Monster enemy, int newTargetPartyIndex)
    {
        if (enemy == null) return;
        if (_plannedIntents == null || _plannedIntents.Count == 0) return;

        for (int i = 0; i < _plannedIntents.Count; i++)
        {
            var intent = _plannedIntents[i];
            if (intent.enemy != enemy) continue;

            // Only meaningful for single-target attack intents.
            if (intent.isAoe) continue;
            if (intent.type == IntentType.Summon || intent.type == IntentType.SelfBuff) continue;

            intent.targetPartyIndex = newTargetPartyIndex;
            _plannedIntents[i] = intent;
        }
    }
    private bool HasEligiblePawnSacrificeTarget(Monster caster, bool onlySummoned)
    {
        if (caster == null) return false;
        if (_activeMonsters == null) return false;

        for (int i = 0; i < _activeMonsters.Count; i++)
        {
            var m = _activeMonsters[i];
            if (m == null || m == caster || m.IsDead) continue;
            if (!m.IsPawn) continue;
            if (onlySummoned && !m.isSummonedMonster) continue;
            return true;
        }
        return false;
    }
private IEnumerator SpawnSpellEffectOnTargetRoutine(Monster target)
{
    if (spellEffectPrefab == null || target == null)
        yield break;

    Transform anchor = GetMonsterCenterPointTransform(target.transform);
    Vector3 pos = (anchor != null ? anchor.position : target.transform.position) + Vector3.up * spellEffectVerticalOffset;

    // Parent to the anchor if available so it follows motion.
    Transform parent = anchor != null ? anchor : null;

    GameObject go = Instantiate(spellEffectPrefab, pos, Quaternion.identity, parent);

    SpellEffectEntity effect = go.GetComponentInChildren<SpellEffectEntity>(true);
    if (effect == null)
    {
        // No controller; destroy with a conservative fallback so we don't leak objects.
        Destroy(go, 2.0f);
        yield break;
    }

    bool finished = false;
    effect.Play(() => finished = true);

    float elapsed = 0f;
    const float failSafeSeconds = 5.0f;
    while (!finished && elapsed < failSafeSeconds)
    {
        elapsed += Time.deltaTime;
        yield return null;
    }
}
    private int GetFirstAlivePartyIndex()
    {
        for (int i = 0; i < PartyCount; i++)
            if (!_party[i].IsDead) return i;
        return 0;
    }
    private int GetRandomLivingTargetIndex()
    {
        List<int> living = new List<int>(PartyCount);
        for (int i = 0; i < PartyCount; i++)
            if (!_party[i].IsDead) living.Add(i);

        if (living.Count == 0) return -1;
        return living[UnityEngine.Random.Range(0, living.Count)];
    }
    private bool IsValidPartyIndex(int index) => _party != null && index >= 0 && index < _party.Count;

    private int GetPartyIndexForHero(HeroStats hero)
    {
        if (hero == null || _party == null) return -1;
        for (int i = 0; i < _party.Count; i++)
            if (_party[i] != null && _party[i].stats == hero) return i;
        return -1;
    }
    public HeroStats GetHeroAtPartyIndex(int index)
    {
        if (!IsValidPartyIndex(index)) return null;
        return _party[index].stats;
    }
    public Transform GetSelectedEnemyVisualTransform()
    {
        if (_selectedEnemyTarget == null)
            return null;

        // If your monsters have a CenterPoint transform, prefer that:
        var center = _selectedEnemyTarget.transform.Find("CenterPoint");
        if (center != null)
            return center;

        return _selectedEnemyTarget.transform;
    }

}

