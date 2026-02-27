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
    public PartyMemberSnapshot GetPartyMemberSnapshot(int index)
    {
        if (!IsValidPartyIndex(index))
            return default;

        var m = _party[index];
        var hs = m.stats;

        int hp = hs != null ? hs.CurrentHp : 0;
        int maxHp = hs != null ? hs.MaxHp : 0;

        int stamina = hs != null ? Mathf.RoundToInt(hs.CurrentStamina) : 0;
        int maxStamina = hs != null ? hs.MaxStamina : 0;

        int shield = hs != null ? hs.Shield : 0;

        return new PartyMemberSnapshot
        {
            Name = string.IsNullOrEmpty(m.name) ? $"Ally {index + 1}" : m.name,
            HP = hp,
            MaxHP = maxHp,
            Stamina = stamina,
            MaxStamina = maxStamina,
            IsDead = m.IsDead,
            HasActedThisRound = m.hasActedThisRound,
            Shield = shield,
            IsBlocking = shield > 0,

            IsHidden = hs != null && hs.IsHidden,
            IsStunned = hs != null && hs.IsStunned,
            IsTripleBladeEmpowered = hs != null && hs.IsTripleBladeEmpoweredThisTurn,
            IsBleeding = hs != null && hs.IsBleeding,
HasBlockPreview = (shield <= 0) && (_previewPartyTargetIndex == index) && _awaitingPartyTarget && _pendingAbility != null && _pendingActorIndex == index && _pendingAbility.targetType == AbilityTargetType.Self && _pendingAbility.shieldAmount > 0,
            BlockPreviewAmount = ((_previewPartyTargetIndex == index) && _awaitingPartyTarget && _pendingAbility != null && _pendingActorIndex == index) ? Mathf.Max(0, _pendingAbility.shieldAmount) : 0
        };
    }
    private void BeginPlayerTurnSaveState()
    {
        _saveStates.Clear();
        _previewEnemyTarget = null;
        _previewPartyTargetIndex = -1;
        HideConfirmText();
        SetUndoButtonEnabled(false);

        PushSaveStateSnapshot(); // Turn start baseline
    }
    private void PushSaveStateSnapshot()
    {
        var s = new BattleSaveState();

        for (int i = 0; i < PartyCount; i++)
        {
            var pm = _party[i];
            var hs = pm != null ? pm.stats : null;
            if (hs == null) continue;

            s.heroes.Add(new HeroRuntimeSnapshot
            {
                partyIndex = i,
                hp = hs.CurrentHp,
                stamina = hs.CurrentStamina,
                shield = hs.Shield,
                hidden = hs.IsHidden,
                bleedStacks = hs.BleedStacks,
                hasActedThisRound = pm.hasActedThisRound
            });
        }

        if (resourcePool != null)
        {
            s.resources = new ResourcePoolSnapshot
            {
                attack = resourcePool.Attack,
                defense = resourcePool.Defense,
                magic = resourcePool.Magic,
                wild = resourcePool.Wild
            };
        }

        for (int i = 0; i < _encounterMonsters.Count; i++)
        {
            var m = _encounterMonsters[i];
            if (m == null) continue;

            s.monsters.Add(new MonsterRuntimeSnapshot
            {
                instanceId = m.GetInstanceID(),
                isActive = m.gameObject.activeSelf && !m.IsDead,
                hp = m.CurrentHp,
                bleedStacks = m.BleedStacks,
                position = m.transform.position,
                rotation = m.transform.rotation
            });
        }
        s.intents.Clear();
        for (int i = 0; i < _plannedIntents.Count; i++)
        {
            var it = _plannedIntents[i];
            if (it.enemy == null) continue;

            s.intents.Add(new EnemyIntentSnapshot
            {
                type = it.type,
                enemyInstanceId = it.enemy.GetInstanceID(),
                targetPartyIndex = it.targetPartyIndex,
                attackIndex = it.attackIndex,
                damage = it.damage,
                isAoe = it.isAoe,
                stunsTarget = it.stunsTarget,
                stunPlayerPhases = it.stunPlayerPhases,
                appliesBleed = it.appliesBleed,
                bleedStacks = it.bleedStacks,
                appliesCorrosion = it.appliesCorrosion,
                    corrosionIconCount = Mathf.Max(1, it.corrosionIconCount),
                    isSummon = it.isSummon,
                    summonCount = Mathf.Max(1, it.summonCount),
                    maxSummonsPerBattle = it.maxSummonsPerBattle
                });
        }

        _saveStates.Add(s);
    }
    private void ApplySaveStateSnapshot(BattleSaveState s)
    {
        if (s == null) return;

        ClearEnemyTargetPreview();
        _previewPartyTargetIndex = -1;
        HideConfirmText();
        CancelPendingAbility();

        if (resourcePool != null)
            resourcePool.SetAmounts(s.resources.attack, s.resources.defense, s.resources.magic, s.resources.wild);

        for (int i = 0; i < s.heroes.Count; i++)
        {
            var h = s.heroes[i];
            if (!IsValidPartyIndex(h.partyIndex)) continue;

            var pm = _party[h.partyIndex];
            if (pm == null || pm.stats == null) continue;

            pm.stats.SetRuntimeState(h.hp, h.stamina, h.shield, h.hidden);
            pm.stats.SetBleedStacks(h.bleedStacks);
            pm.hasActedThisRound = h.hasActedThisRound;
        }

        var map = new Dictionary<int, Monster>(_encounterMonsters.Count);
        for (int i = 0; i < _encounterMonsters.Count; i++)
        {
            var m = _encounterMonsters[i];
            if (m == null) continue;
            map[m.GetInstanceID()] = m;
        }

        _activeMonsters.Clear();
        _encounterMonsters.Clear();
        _summonedEnemyQueue.Clear();
        NotifyEnemySummonQueueChanged();

        for (int i = 0; i < s.monsters.Count; i++)
        {
            var ms = s.monsters[i];
            if (!map.TryGetValue(ms.instanceId, out var m) || m == null) continue;

            m.transform.position = ms.position;
            m.transform.rotation = ms.rotation;

            if (ms.isActive)
            {
                m.gameObject.SetActive(true);
                m.SetCurrentHp(ms.hp);
                m.SetBleedStacks(ms.bleedStacks);
                if (!m.IsDead)
                    _activeMonsters.Add(m);
            }
            else
            {
                m.SetCurrentHp(ms.hp);
                m.SetBleedStacks(ms.bleedStacks);
                if (m.IsDead || !ms.isActive)
                    m.gameObject.SetActive(false);
            }
        }

        _plannedIntents.Clear();
        if (s.intents != null)
        {
            for (int i = 0; i < s.intents.Count; i++)
            {
                var it = s.intents[i];
                if (!map.TryGetValue(it.enemyInstanceId, out var em) || em == null) continue;
                if (!em.gameObject.activeSelf || em.IsDead) continue;

                _plannedIntents.Add(new EnemyIntent
                {
                    type = it.type,
                    category = ComputeIntentCategory(it.damage, it.isAoe, it.stunsTarget, it.appliesBleed, it.appliesCorrosion, it.isSummon, it.isConsume),
                    enemy = em,
                    targetPartyIndex = it.targetPartyIndex,
                    attackIndex = it.attackIndex,
                    damage = it.damage,
                    isAoe = it.isAoe,
                    stunsTarget = it.stunsTarget,
                    stunPlayerPhases = it.stunPlayerPhases,
                    appliesBleed = it.appliesBleed,
                    bleedStacks = it.bleedStacks,
                    appliesCorrosion = it.appliesCorrosion
                });
            }
        }
        OnEnemyIntentsPlanned?.Invoke(new List<EnemyIntent>(_plannedIntents));

        NotifyPartyChanged();
    }
    public void UndoLastSaveState()
    {
        if (!IsPlayerPhase || _resolving)
            return;

        if (_saveStates == null || _saveStates.Count <= 1)
        {
            SetUndoButtonEnabled(false);
            return;
        }

        _saveStates.RemoveAt(_saveStates.Count - 1);

        BattleSaveState s = _saveStates[_saveStates.Count - 1];
        ApplySaveStateSnapshot(s);

        if (_saveStates.Count <= 1)
            SetUndoButtonEnabled(false);
    }

}
