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
    private static IntentCategory ComputeIntentCategory(int damage, bool isAoe, bool stunsTarget, bool appliesBleed, bool appliesCorrosion, bool isSummon, bool isConsume)
    {
        if (isSummon) return IntentCategory.Summon;
        if (isConsume) return IntentCategory.SelfBuff;


        bool hasStatus = stunsTarget || appliesBleed || appliesCorrosion;

        if (isAoe)
        {
            if (damage > 0) return IntentCategory.StatusAndAoe;
            return IntentCategory.Aoe;
        }

        if (damage > 0)
        {
            return hasStatus ? IntentCategory.DamageAndStatus : IntentCategory.Normal;
        }

        return hasStatus ? IntentCategory.StatusDebuffOnly : IntentCategory.Normal;
    }
    private IEnumerator EnemyPhaseRoutine()
    {
        SetState(BattleState.EnemyPhase);

        CancelPendingAbility();

        if (_plannedIntents.Count == 0)
            PlanEnemyIntents();

        // Snapshot intents so we can safely clear the live list used for UI rendering.
        var intentsToExecute = new List<EnemyIntent>(_plannedIntents);

        // Broadcast the snapshot BEFORE clearing (some listeners depend on the planned list).
        OnEnemyIntentsPlanned?.Invoke(new List<EnemyIntent>(_plannedIntents));

        _plannedIntents.Clear();
        NotifyPartyChanged();

        Debug.Log($"[EnemyPhase] intentsToExecute.Count={intentsToExecute.Count}", this);

        for (int i = 0; i < intentsToExecute.Count; i++)
        {
            var intent = intentsToExecute[i];

            if (intent.enemy == null)
            {
                Debug.LogWarning("[EnemyPhase] intent.enemy is NULL. Skipping intent.", this);
                continue;
            }

            bool summoned = intent.enemy.isSummonedMonster;

            if (intent.enemy.IsDead)
            {
                if (summoned)
                    Debug.LogWarning($"[Summon][EXEC] Summoned enemy '{intent.enemy.name}' is dead. Skipping intent.", intent.enemy);
                continue;
            }

            if (summoned)
                Debug.Log($"[Summon][EXEC] ENTER intent[{i}] enemy={intent.enemy.name} type={intent.type} atkIdx={intent.attackIndex} target={intent.targetPartyIndex} aoe={intent.isAoe}", intent.enemy);

            // Summon intent
            if (intent.type == IntentType.Summon || intent.isSummon)
            {
                if (summoned)
                    Debug.Log($"[Summon][EXEC] (Summoner is summoned) executing SUMMON intent. enemy={intent.enemy.name} atkIdx={intent.attackIndex}", intent.enemy);
                else
                    Debug.Log($"[SUMMON][EXEC] Executing summon intent. Enemy={intent.enemy.name} atkIdx={intent.attackIndex}", intent.enemy);

                ExecuteMonsterSummonIntent(intent);
                yield return new WaitForSeconds(0.15f);
                continue;
            }

            // Consume (self-buff) intent
            if (intent.type == IntentType.SelfBuff || intent.isConsume)
            {
                yield return ExecuteMonsterConsumeIntentRoutine(intent);
                yield return new WaitForSeconds(0.15f);
                continue;
            }

            // Build target list
            List<int> targets = new List<int>();

            if (intent.isAoe)
            {
                for (int p = 0; p < PartyCount; p++)
                {
                    if (!IsValidPartyIndex(p)) continue;
                    var pm = _party[p];
                    if (pm == null || pm.stats == null || pm.IsDead) continue;
                    targets.Add(p);
                }
            }
            else
            {
                int targetIdx = intent.targetPartyIndex;

                // If invalid/dead, choose a fallback living target (DO NOT break the whole enemy phase).
                if (!IsValidPartyIndex(targetIdx) || _party[targetIdx] == null || _party[targetIdx].stats == null || _party[targetIdx].IsDead)
                    targetIdx = GetRandomLivingTargetIndex();

                if (!IsValidPartyIndex(targetIdx) || _party[targetIdx] == null || _party[targetIdx].stats == null || _party[targetIdx].IsDead)
                {
                    if (summoned)
                        Debug.LogWarning($"[Summon][EXEC] Summoned enemy '{intent.enemy.name}' had NO VALID TARGET. originalTarget={intent.targetPartyIndex}. Skipping intent.", intent.enemy);
                    else
                        Debug.LogWarning($"[EnemyPhase] Enemy '{intent.enemy.name}' had NO VALID TARGET. originalTarget={intent.targetPartyIndex}. Skipping intent.", intent.enemy);
                    continue;
                }

                targets.Add(targetIdx);
            }

            if (targets.Count == 0)
            {
                if (summoned)
                    Debug.LogWarning($"[Summon][EXEC] Summoned enemy '{intent.enemy.name}' resolved zero targets. Skipping.", intent.enemy);
                continue;
            }

            // Choose a lunge target transform (use the first target)
            Transform lungeTarget = null;
            var firstHero = _party[targets[0]];
            if (firstHero != null && firstHero.animator != null)
                lungeTarget = firstHero.animator.transform;
            else if (firstHero != null && firstHero.avatarGO != null)
                lungeTarget = firstHero.avatarGO.transform;

            if (lungeTarget == null)
            {
                if (summoned)
                    Debug.LogWarning($"[Summon][EXEC] Summoned enemy '{intent.enemy.name}' has null lunge target transform. Skipping intent.", intent.enemy);
                else
                    Debug.LogWarning($"[EnemyPhase] Enemy '{intent.enemy.name}' has null lunge target transform. Skipping intent.", intent.enemy);
                continue;
            }

            if (summoned)
                Debug.Log($"[Summon][EXEC] START ATTACK enemy={intent.enemy.name} targets={targets.Count}", intent.enemy);

                        // Do the enemy lunge animation, then apply results.
            // Resolve (passive): queue reel spins for heroes that are attacked by this intent (can't yield inside callback).
            var resolveSpinQueue = new List<int>();

            yield return EnemyLungeAttack(intent.enemy, lungeTarget, intent.attackIndex, () =>
            {
                if (summoned)
                    Debug.Log($"[Summon][APPLY] enemy={intent.enemy.name} applying effects to {targets.Count} targets", intent.enemy);

                for (int t = 0; t < targets.Count; t++)
                {
                    int partyIndex = targets[t];
                    if (!IsValidPartyIndex(partyIndex)) continue;

                    var heroPm = _party[partyIndex];
                    if (heroPm == null || heroPm.stats == null || heroPm.IsDead) continue;

                    var hs = heroPm.stats;

                    // Conceal/Hidden: single-target attacks miss; AoE still hits.
                    if (hs.IsHidden && !intent.isAoe)
                    {
                        if (summoned)
                            Debug.Log($"[Summon][APPLY] enemy={intent.enemy.name} MISSED hidden hero partyIndex={partyIndex} hero={hs.name}", hs);
                        continue;
                    }

                    int raw = intent.damage > 0 ? intent.damage : intent.enemy.GetDamage();
                    raw = Mathf.Max(0, raw);

                    if (summoned)
                        Debug.Log($"[Summon][APPLY] enemy={intent.enemy.name} -> hero={hs.name} rawDamage={raw} bleed={intent.appliesBleed} stun={intent.stunsTarget} corrosion={intent.appliesCorrosion}", hs);

                    if (raw > 0)
                    {
                        hs.TakeDamage(raw);
                        TriggerHeroHitReaction(heroPm);
                    }

                    if (intent.appliesBleed && intent.bleedStacks > 0)
                        ApplyBleedStacksToHero(hs, intent.bleedStacks);

                    if (intent.stunsTarget && intent.stunPlayerPhases > 0)
                        hs.StunForNextPlayerPhases(intent.stunPlayerPhases);

                    if (intent.appliesCorrosion && intent.corrosionIconCount > 0 && reelSpinSystem != null)
                    {
                        for (int c = 0; c < intent.corrosionIconCount; c++)
                            reelSpinSystem.ApplyCorrosionToReel(partyIndex);
                    }

                    // Resolve (passive): whenever this hero is attacked by an enemy intent, spin their reel once.
                    if (reelSpinSystem != null && hs.HasAbilityUnlocked("Resolve"))
                    {
                        if (!resolveSpinQueue.Contains(partyIndex))
                            resolveSpinQueue.Add(partyIndex);

                        if (logFlow) Debug.Log($"[Battle][ResolvePassive] Queued Resolve spin. target={hs.name} partyIndex={partyIndex}", hs);
                    }
                }
            });


            // If any heroes died from this attack, stop their battle music stems.
            CheckAndHandleNewlyDeadHeroesForStems();
            // Execute queued Resolve spins AFTER the lunge + damage application completes.
            if (reelSpinSystem != null && resolveSpinQueue.Count > 0)
            {
                for (int r = 0; r < resolveSpinQueue.Count; r++)
                {
                    int ri = resolveSpinQueue[r];
                    if (!IsValidPartyIndex(ri)) continue;

                    var pm = _party[ri];
                    if (pm == null || pm.stats == null || pm.IsDead) continue;

                    yield return StartCoroutine(reelSpinSystem.MomentumSpinAndInstantCollect(ri));
                }
            }
if (summoned)
                Debug.Log($"[Summon][EXEC] FINISHED intent enemy={intent.enemy.name}", intent.enemy);

            // Small pacing delay so multiple enemies don’t feel instantaneous
            yield return new WaitForSeconds(0.12f);

            if (_state == BattleState.BattleEnd) yield break;
            if (IsPartyDefeated())
            {
                Debug.Log("[BattleManager] Party defeated (enemy phase).", this);
                SetState(BattleState.BattleEnd);
                yield break;
            }
        }

        // Plan next-turn intents so the player sees them during the upcoming PlayerPhase.
        // This also ensures newly-summoned monsters get an intent immediately.
        if (_state != BattleState.BattleEnd)
        {
            PlanEnemyIntents();
            Debug.Log($"[EnemyPhase] Planned next-turn intents. count={_plannedIntents.Count}", this);
            OnEnemyIntentsPlanned?.Invoke(new List<EnemyIntent>(_plannedIntents));
            NotifyPartyChanged();
        }

        _enemyTurnRoutine = null;
        if (_state != BattleState.BattleEnd)
            SetState(BattleState.PlayerPhase);
    }
    private void PlanEnemyIntents()
    {
        _plannedIntents.Clear();

        for (int i = 0; i < _activeMonsters.Count; i++)
        {
            Monster m = _activeMonsters[i];
            if (m == null || m.IsDead) continue;

            int targetIdx = -1;

            // Taunt support: if the monster has a forced target, use it (if alive) and clear it immediately.
            if (m != null && m.TryGetForcedTargetPartyIndex(out int forcedIdx))
            {
                bool validForced = IsValidPartyIndex(forcedIdx) && _party != null && forcedIdx < _party.Count && _party[forcedIdx] != null && !_party[forcedIdx].IsDead;
                if (validForced)
                    targetIdx = forcedIdx;

                // One-shot: clear regardless so stale taunts don't persist.
                m.ClearForcedTargetPartyIndex();
            }

            if (targetIdx < 0)
                targetIdx = GetRandomLivingTargetIndex();

            if (targetIdx < 0) continue;

            ChooseMonsterAttackForIntent(m,
                out int attackIndex,
                out int damage,
                out bool isAoe,
                out bool stunsTarget,
                out int stunPlayerPhases,
                out bool appliesBleed,
                out int bleedStacks,
                out bool appliesCorrosion,
                out int corrosionIconCount,
                out bool isSummon,
                out int summonCount,
                out int maxSummonsPerBattle,
                out bool isConsume);
            Debug.Log(
                $"[SUMMON][PLAN] Monster={m.name} " +
                $"atkIdx={attackIndex} isSummon={isSummon} " +
                $"summonCount={summonCount} maxPerBattle={maxSummonsPerBattle}",
                m
            );

            _plannedIntents.Add(new EnemyIntent
            {
                type = isSummon ? IntentType.Summon : (isConsume ? IntentType.SelfBuff : (isAoe ? IntentType.AoEAttack : IntentType.Attack)),
                category = ComputeIntentCategory(damage, isAoe, stunsTarget, appliesBleed, appliesCorrosion, isSummon, isConsume),
                enemy = m,
                targetPartyIndex = isConsume ? -1 : targetIdx,

                attackIndex = attackIndex,
                damage = damage,
                isAoe = isAoe,

                stunsTarget = stunsTarget,
                stunPlayerPhases = stunPlayerPhases,

                appliesBleed = appliesBleed,
                bleedStacks = bleedStacks,

                appliesCorrosion = appliesCorrosion,
                corrosionIconCount = corrosionIconCount,

                isSummon = isSummon,
                summonCount = summonCount,
                maxSummonsPerBattle = maxSummonsPerBattle,

                isConsume = isConsume,
                consumeVictimInstanceId = 0,
                consumeHealAmount = 0
            });



        }

        OnEnemyIntentsPlanned?.Invoke(new List<EnemyIntent>(_plannedIntents));
        NotifyPartyChanged();
        Debug.Log($"[SUMMON][PLAN] PlanEnemyIntents END. _plannedIntents.Count={_plannedIntents.Count}", this);

    }
    private void ChooseMonsterAttackForIntent(Monster m,
        out int attackIndex,
        out int damage,
        out bool isAoe,
        out bool stunsTarget,
        out int stunPlayerPhases,
        out bool appliesBleed,
        out int bleedStacks,
        out bool appliesCorrosion,
        out int corrosionIconCount,
        out bool isSummon,
        out int summonCount,
        out int maxSummonsPerBattle,
        out bool isConsume)
    {
        attackIndex = -1;
        damage = 0;
        isAoe = false;
        stunsTarget = false;
        stunPlayerPhases = 1;
        appliesBleed = false;
        bleedStacks = 0;
        appliesCorrosion = false;
        corrosionIconCount = 1;

        isSummon = false;
        summonCount = 1;
        maxSummonsPerBattle = 1;

        isConsume = false;

        if (m == null) return;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        object attacksObj = null;
        var t = m.GetType();

        var fiAttacks = t.GetField("attacks", flags);
        if (fiAttacks != null)
            attacksObj = fiAttacks.GetValue(m);

        System.Array attacksArray = attacksObj as System.Array;
        int count = attacksArray != null ? attacksArray.Length : 0;

        if (count <= 0)
        {
            try { damage = m.GetDamage(); } catch { damage = 0; }

            try
            {
                var pi = t.GetProperty("IsDefaultAttackAoE", flags);
                if (pi != null) isAoe = (bool)pi.GetValue(m, null);
            }
            catch { isAoe = false; }

            try
            {
                stunsTarget = m.DefaultAttackStunsTarget;
                stunPlayerPhases = m.DefaultAttackStunPlayerPhases;
            }
            catch { stunsTarget = false; stunPlayerPhases = 1; }

            return;
        }

        // Pick an attack. If we roll a summon attack that has no remaining uses, re-roll a few times.
        object atk = null;
        Type atkType = null;

        const int MAX_REROLL_ATTEMPTS = 8;
        int attempts = 0;

        while (attempts < MAX_REROLL_ATTEMPTS)
        {
            attempts++;
            attackIndex = UnityEngine.Random.Range(0, count);
            atk = attacksArray.GetValue(attackIndex);
            if (atk == null) continue;

            atkType = atk.GetType();

            bool candidateIsSummon = ReadBool(atk, atkType, "isSummon", false);
            bool candidateIsConsume = ReadBool(atk, atkType, "isConsume", false);

            // Sacrifice gating:
            // If the rolled ability requires a Pawn sacrifice but there are no Pawn allies available,
            // use the authored backupAbilityId if provided; otherwise reroll.
            bool candidateIsSacrifice = ReadBool(atk, atkType, "isSacrifice", false) || candidateIsConsume;
            if (candidateIsSacrifice)
            {
                bool onlySummoned = candidateIsConsume ? ReadBool(atk, atkType, "consumeOnlySummoned", true) : false;
                if (!HasEligiblePawnSacrificeTarget(m, onlySummoned))
                {
                    string backupId = ReadString(atk, atkType, "backupAbilityId", "");
                    int backupIdx = FindAttackIndexById(attacksArray, backupId);

                    if (backupIdx >= 0)
                    {
                        var backupAtk = attacksArray.GetValue(backupIdx);
                        if (backupAtk != null)
                        {
                            var backupType = backupAtk.GetType();
                            bool backupIsSummon = ReadBool(backupAtk, backupType, "isSummon", false);

                            // If the backup is a summon, ensure it has remaining uses.
                            if (!backupIsSummon || m.CanUseSummonAttack(backupIdx, ReadInt(backupAtk, backupType, "maxSummonsPerBattle", 1)))
                            {
                                attackIndex = backupIdx;
                                atk = backupAtk;
                                atkType = backupType;
                                break;
                            }
                        }
                    }

                    // No valid backup found -> reroll
                    atk = null;
                    atkType = null;
                    continue;
                }
            }

            if (!candidateIsSummon)
                break;

            int candidateMax = ReadInt(atk, atkType, "maxSummonsPerBattle", 1);
            if (m.CanUseSummonAttack(attackIndex, candidateMax))
                break;

            atk = null;
            atkType = null;
        }

        if (atk == null || atkType == null) return;
        damage = ReadInt(atk, atkType, "damage", 0);
        isAoe = ReadBool(atk, atkType, "isAoe", false);

        stunsTarget = ReadBool(atk, atkType, "stunsTarget", false);
        stunPlayerPhases = Mathf.Max(1, ReadInt(atk, atkType, "stunPlayerPhases", 1));

        appliesBleed = ReadBool(atk, atkType, "appliesBleed", false);
        if (!appliesBleed) appliesBleed = ReadBool(atk, atkType, "bleedsTarget", false);

        bleedStacks = Mathf.Max(0, ReadInt(atk, atkType, "bleedStacks", 0));
        if (bleedStacks == 0) bleedStacks = Mathf.Max(0, ReadInt(atk, atkType, "bleedAmount", 0));

        appliesCorrosion = ReadBool(atk, atkType, "appliesCorrosion", false);
        if (!appliesCorrosion) appliesCorrosion = ReadBool(atk, atkType, "corrodesReel", false);

        corrosionIconCount = Mathf.Max(1, ReadInt(atk, atkType, "corrosionIconCount", 1));

        // Summon support (optional attack behavior).
        isSummon = ReadBool(atk, atkType, "isSummon", false);
        isConsume = ReadBool(atk, atkType, "isConsume", false);
        summonCount = Mathf.Max(1, ReadInt(atk, atkType, "summonCount", 1));
        maxSummonsPerBattle = ReadInt(atk, atkType, "maxSummonsPerBattle", 1);

        if (isSummon)
        {
            // Summon attacks don't deal damage by default; they are their own intent category.
            damage = 0;
            isAoe = false;

            stunsTarget = false;
            stunPlayerPhases = 1;
            appliesBleed = false;
            bleedStacks = 0;
            appliesCorrosion = false;
            corrosionIconCount = 1;
            Debug.Log(
                $"[SUMMON][CHOOSE] Monster={m.name} selected SUMMON attack " +
                $"atkIdx={attackIndex} count={summonCount} max={maxSummonsPerBattle}",
                m
            );
        }

        // Consume support (optional attack behavior).
        if (isConsume)
        {
            // Consume is a self-buff; it does not deal damage directly.
            damage = 0;
            isAoe = false;

            stunsTarget = false;
            stunPlayerPhases = 1;
            appliesBleed = false;
            bleedStacks = 0;
            appliesCorrosion = false;
            corrosionIconCount = 1;

            // Ensure this isn't treated as a summon.
            isSummon = false;
            summonCount = 0;
            maxSummonsPerBattle = 0;
        }

        if (corrosionIconCount == 1) corrosionIconCount = Mathf.Max(1, ReadInt(atk, atkType, "corrosionCount", 1));
        if (corrosionIconCount == 1) corrosionIconCount = Mathf.Max(1, ReadInt(atk, atkType, "corrodeCount", 1));
    }
private IEnumerator ExecuteMonsterConsumeIntentRoutine(EnemyIntent intent)
{
    if (intent.enemy == null) yield break;

    // Pull authored consume settings from the attack definition.
    bool onlySummoned = true;
    float mult = 1f;
    bool canOverheal = false;

    if (intent.enemy.TryGetAttack(intent.attackIndex, out var atk) && atk != null)
    {
        onlySummoned = atk.consumeOnlySummoned;
        mult = Mathf.Max(0f, atk.consumeHealMultiplier);
        canOverheal = atk.consumeCanOverheal;
    }

    Monster victim = ChooseConsumeVictim(intent.enemy, onlySummoned);
    if (victim == null)
        yield break;

    // VISUALS:
    // - Caster plays CAST (not Spell).
    // - A separate SpellEffect prefab spawns on the victim and plays SPELL.
    // - Gameplay effects resolve AFTER the spell visual completes.
    MonsterAnimationDriver casterAnim = intent.enemy.GetComponentInChildren<MonsterAnimationDriver>(true);
    if (casterAnim != null)
    {
        casterAnim.ResetCastRelease();
        casterAnim.PlayCast();

        // Prefer an animation event for release timing (cast->spell handoff). Falls back to a short delay.
        if (casterAnim.waitForCastReleaseEvent)
        {
            float elapsed = 0f;
            const float failSafeSeconds = 2.0f;
            while (!casterAnim.CastReleaseFired && elapsed < failSafeSeconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }
        else
        {
            // Small delay so the Cast trigger is visually perceived before the effect spawns.
            yield return new WaitForSeconds(0.1f);
        }
    }
    else
    {
        // No animation driver; keep legacy behavior for gameplay, and just delay slightly for readability.
        yield return new WaitForSeconds(0.05f);
    }

    // Spawn Spell effect on the victim (CenterPoint if present).
    yield return SpawnSpellEffectOnTargetRoutine(victim);

    // GAMEPLAY RESOLUTION (unchanged from prior behavior)
    int healAmount = Mathf.RoundToInt(victim.MaxHp * mult);

    // Kill the victim (treat as lethal damage).
    int lethalIncoming = victim.CurrentHp + Mathf.Max(0, victim.Defense) + 9999;
    victim.TakeDamage(lethalIncoming);

    if (victim.IsDead)
        HandleMonsterKilled(victim);

    // Heal the caster.
    intent.enemy.Heal(healAmount, canOverheal);

    NotifyPartyChanged();
}
private void ExecuteMonsterConsumeIntent(EnemyIntent intent)
{
    StartCoroutine(ExecuteMonsterConsumeIntentRoutine(intent));
}
    private void ExecuteMonsterSummonIntent(EnemyIntent intent)
    {
        if (intent.enemy == null) return;

        if (!TryGetSummonAttackData(intent.enemy, intent.attackIndex, out GameObject prefab, out int count, out int maxPerBattle))
            return;

        if (!intent.enemy.CanUseSummonAttack(intent.attackIndex, maxPerBattle))
            return;

        // NOTE:
        // Summon intents bypass the normal "lunge + attack" execution path (which triggers animations).
        // Fire the authored animation cue (Attack/Spell/Cast) here so Summon attacks can animate.
        PlayMonsterAnimationCue(intent.enemy, intent.attackIndex);

        int spawnCount = Mathf.Max(1, count);

        for (int i = 0; i < spawnCount; i++)
        {
            Debug.Log(
                $"[SUMMON][SPAWN] Spawning {spawnCount} monster(s) " +
                $"for {intent.enemy.name}",
                intent.enemy
            );

            if (_activeMonsters.Count >= Mathf.Max(1, maxActiveEnemiesOnScreen))
            {
                EnqueueSummonedEnemy(prefab);
                continue;
            }

            SpawnSummonedEnemy(prefab);
        }

        intent.enemy.RegisterSummonAttackUse(intent.attackIndex);
        NotifyPartyChanged();
    }
    private void EnqueueSummonedEnemy(GameObject prefab)
    {
        if (prefab == null) return;
        _summonedEnemyQueue.Enqueue(prefab);
        NotifyEnemySummonQueueChanged();
        Debug.Log($"[SUMMON][QUEUE] Enqueued summon prefab='{prefab.name}'. queueCount={_summonedEnemyQueue.Count}", this);
    }
    private void TrySpawnQueuedSummonsToFillCap()
    {
        int cap = Mathf.Max(1, maxActiveEnemiesOnScreen);
        bool spawnedAny = false;

        while (_activeMonsters.Count < cap && _summonedEnemyQueue.Count > 0)
        {
            GameObject prefab = _summonedEnemyQueue.Dequeue();
            NotifyEnemySummonQueueChanged();

            if (prefab == null) continue;

            Debug.Log($"[SUMMON][QUEUE] Dequeued summon prefab='{prefab.name}'. remaining={_summonedEnemyQueue.Count}", this);
            SpawnSummonedEnemy(prefab);
            spawnedAny = true;
        }

        if (spawnedAny)
            NotifyPartyChanged();
    }
    private void SpawnSummonedEnemy(GameObject prefab)
    {
        if (prefab == null) return;

        Vector3 pos = GetSummonSpawnPosition();
        GameObject go = Instantiate(prefab, pos, Quaternion.identity);
        // If the monster prefab defines a visual CenterPoint, align it to the intended summon position.
        AlignMonsterToWorldPositionUsingCenterPoint(go, pos);

        Monster summoned = go.GetComponentInChildren<Monster>(true);
        if (summoned == null)
        {
            Debug.LogWarning($"[BattleManager][Summon] Summon prefab '{prefab.name}' did not have a Monster component in children.", this);
            Destroy(go);
            return;
        }

        summoned.gameObject.SetActive(true);
        summoned.ResetSummonTrackingForBattle();

        summoned.isSummonedMonster = true;
        Debug.Log($"[SUMMON][SPAWN] Marked summoned monster as isSummonedMonster=true name={summoned.name}", summoned);
        _activeMonsters.Add(summoned);
        if (!_encounterMonsters.Contains(summoned)) _encounterMonsters.Add(summoned);

        // If the summon spawns dead for some reason, remove it.
        if (summoned.IsDead)
        {
            summoned.gameObject.SetActive(false);
            _activeMonsters.Remove(summoned);
        }
    }
    private void NotifyEnemySummonQueueChanged()
    {
        OnEnemySummonQueueChanged?.Invoke(EnemySummonQueueCount);
    }
    private bool TryGetSummonAttackData(Monster m, int attackIndex, out GameObject summonPrefab, out int summonCount, out int maxSummonsPerBattle)
    {
        Debug.Log(
            $"[SUMMON][DATA] Reading summon data. " +
            $"Monster={m.name} atkIdx={attackIndex}",
            m
        );

        summonPrefab = null;
        summonCount = 1;
        maxSummonsPerBattle = 1;

        if (m == null) return false;
        if (attackIndex < 0) return false;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var t = m.GetType();

        var fiAttacks = t.GetField("attacks", flags);
        if (fiAttacks == null) return false;

        var attacksObj = fiAttacks.GetValue(m);
        var attacksArray = attacksObj as Array;
        if (attacksArray == null) return false;
        if (attackIndex >= attacksArray.Length) return false;

        var atk = attacksArray.GetValue(attackIndex);
        if (atk == null) return false;

        var atkType = atk.GetType();
        bool isSummon = ReadBool(atk, atkType, "isSummon", false);
        if (!isSummon) return false;

        // Prefab field/property
        var fiPrefab = atkType.GetField("summonPrefab", flags);
        if (fiPrefab != null && typeof(GameObject).IsAssignableFrom(fiPrefab.FieldType))
            summonPrefab = fiPrefab.GetValue(atk) as GameObject;

        var piPrefab = atkType.GetProperty("summonPrefab", flags);
        if (summonPrefab == null && piPrefab != null && typeof(GameObject).IsAssignableFrom(piPrefab.PropertyType))
            summonPrefab = piPrefab.GetValue(atk, null) as GameObject;

        if (summonPrefab == null)
        {
            Debug.LogWarning($"[BattleManager][Summon] Monster '{m.name}' used a summon attack but summonPrefab was null (attackIndex={attackIndex}).", this);
            return false;
        }

        summonCount = Mathf.Max(1, ReadInt(atk, atkType, "summonCount", 1));
        maxSummonsPerBattle = ReadInt(atk, atkType, "maxSummonsPerBattle", 1);

        return true;
    }
    private Vector3 GetSummonSpawnPosition()
    {
        if (monsterSpawnPoints != null && monsterSpawnPoints.Length > 0)
        {
            // Pick the first spawn point that isn't already occupied by a live monster.
            for (int i = 0; i < monsterSpawnPoints.Length; i++)
            {
                var sp = monsterSpawnPoints[i];
                if (sp == null) continue;

                bool occupied = false;
                for (int j = 0; j < _activeMonsters.Count; j++)
                {
                    var m = _activeMonsters[j];
                    if (m == null || m.IsDead) continue;

                    // IMPORTANT:
                    // Monsters may be CenterPoint-aligned, meaning the monster root transform.position
                    // will NOT equal the spawn point position. Use the monster's CenterPoint (if present)
                    // when determining whether a spawn point is occupied.
                    Vector3 monsterPos = GetMonsterWorldPositionForSpawnOccupancy(m);
                    if (Vector3.SqrMagnitude(monsterPos - sp.position) < 0.01f)
                    {
                        occupied = true;
                        break;
                    }
                }

                if (!occupied)
                    return sp.position;
            }

            var last = monsterSpawnPoints[monsterSpawnPoints.Length - 1];
            if (last != null)
                return last.position + new Vector3(UnityEngine.Random.Range(-0.35f, 0.35f), 0f, UnityEngine.Random.Range(-0.35f, 0.35f));
        }

        return Vector3.zero;
    }
    private void RemoveEnemyIntentsForMonster(Monster dead)
    {
        if (dead == null) return;
        if (_plannedIntents == null || _plannedIntents.Count == 0) return;

        bool removedAny = false;
        for (int i = _plannedIntents.Count - 1; i >= 0; i--)
        {
            var intent = _plannedIntents[i];
            if (intent.enemy == null || intent.enemy == dead || intent.enemy.IsDead)
            {
                _plannedIntents.RemoveAt(i);
                removedAny = true;
            }
        }

        if (removedAny)
        {
            OnEnemyIntentsPlanned?.Invoke(new List<EnemyIntent>(_plannedIntents));
            NotifyPartyChanged();
        }
    }

}
