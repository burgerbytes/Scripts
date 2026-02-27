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
    public bool TryHandlePartySlotClickForPendingAbility(int partyIndex)
    {
        if (logFlow)
            Debug.Log($"[Battle][AbilityTarget] Party slot clicked. partyIndex={partyIndex} pendingActorIndex={_pendingActorIndex} awaitingPartyTarget={_awaitingPartyTarget} pendingAbility={(_pendingAbility != null ? _pendingAbility.abilityName : "<null>")}");

        if (!IsPlayerPhase) return false;
        if (_resolving) return true; // consume to prevent UI spam while resolving

        if (_pendingAbility == null) return false;
        if (!_awaitingPartyTarget) return false;

        bool selfOnly = _pendingAbility.targetType == AbilityTargetType.Self;

        if (selfOnly && partyIndex != _pendingActorIndex)
        {
            if (_previewPartyTargetIndex == _pendingActorIndex)
            {
                if (logFlow) Debug.Log("[Battle][AbilityTarget] Clicked different party slot -> cancel pending ability.", this);
                _previewPartyTargetIndex = -1;
                _selectedPartyTargetIndex = -1;
                HideConfirmText();
                CancelPendingAbility();
                NotifyPartyChanged();
            }
            return true;
        }

        if (_previewPartyTargetIndex != partyIndex)
        {
            if (_previewPartyTargetIndex != -1 && _previewPartyTargetIndex != partyIndex)
            {
                if (logFlow) Debug.Log("[Battle][AbilityTarget] Clicked different party target -> cancel pending ability.", this);
                _previewPartyTargetIndex = -1;
                _selectedPartyTargetIndex = -1;
                HideConfirmText();
                CancelPendingAbility();
                NotifyPartyChanged();
                return true;
            }

            _previewPartyTargetIndex = partyIndex;
            ShowConfirmText();
            NotifyPartyChanged();
            return true;
        }

        if (logFlow) Debug.Log("[Battle][AbilityTarget] Party target clicked again. Committing pending ability.", this);
        _selectedPartyTargetIndex = partyIndex;
        _previewPartyTargetIndex = -1;
        HideConfirmText();
        AbilityCastState.RaiseTargetConfirmed();
        // Resume windup animation (if it was being held).
        ResumePendingWindupHold();
        StartCoroutine(ResolvePendingAbility());
        NotifyPartyChanged();
        return true;
    }
    private void BeginPendingWindupHoldIfNeeded(PartyMemberRuntime actor, AbilityDefinitionSO ability)
    {
        if (!enableWindupHoldWhileTargeting) return;
        if (actor == null || ability == null) return;

        // Only do this for abilities that are awaiting a target.
        if (!(ability.targetType == AbilityTargetType.Enemy ||
              ability.targetType == AbilityTargetType.Ally ||
              ability.targetType == AbilityTargetType.Self))
            return;

        // Abilities that intentionally play no animation should skip.
        if (IsNoAnimAbility(ability)) return;

        Animator anim = actor.animator;
        if (anim == null && actor.avatarGO != null)
            anim = actor.avatarGO.GetComponentInChildren<Animator>(true);
        if (anim == null) return;

        var profile = anim.GetComponentInParent<CasterAnimationProfile>();
        string actorClassName = GetActorClassName(actor.stats);

        string animationKey = ability.GetAnimationKeyString();

        // Resolve animator state to play (same logic as ResolvePendingAbility, but without applying effects).
        string stateToPlay = profile != null
            ? profile.ResolveAttackState(animationKey, actorClassName, abilityNameFallback: ability.name)
            : null;

        if (string.IsNullOrWhiteSpace(stateToPlay) && !string.IsNullOrWhiteSpace(animationKey))
        {
            int hash = Animator.StringToHash(animationKey);
            if (anim.HasState(0, hash))
                stateToPlay = animationKey;
        }

        if (string.IsNullOrWhiteSpace(stateToPlay) && !string.IsNullOrWhiteSpace(actorClassName))
        {
            string classBasic = $"{actorClassName.ToLowerInvariant()}_basic_attack";
            int hash = Animator.StringToHash(classBasic);
            if (anim.HasState(0, hash))
                stateToPlay = classBasic;
        }

        if (string.IsNullOrWhiteSpace(stateToPlay))
            stateToPlay = "fighter_basic_attack";        // Windup hold is always enabled while awaiting a target (data-driven pause point still optional).
        float holdNorm = -1f;
        if (profile != null)
        {
            bool _unusedEnable;
            profile.ResolveWindupHold(animationKey, actorClassName, abilityNameFallback: ability.name, out _unusedEnable, out holdNorm);
        }
        if (holdNorm < 0f) holdNorm = defaultWindupHoldNormalizedTime;
        holdNorm = Mathf.Clamp(holdNorm, 0f, 0.95f);

        // Stop previous hold if any.
        CancelPendingWindupHold(resetAnimatorToDefault: false);

        _windupAnimator = anim;
        _windupStateName = stateToPlay;
        _windupActorIndex = _pendingActorIndex;
        _windupActive = true;

        // Play immediately, then freeze when we reach hold point.
        anim.speed = 1f;
        anim.CrossFadeInFixedTime(stateToPlay, 0.05f, 0, 0f);

        _windupHoldRoutine = StartCoroutine(WindupHoldRoutine(anim, stateToPlay, holdNorm));
    }
    private IEnumerator WindupHoldRoutine(Animator anim, string stateName, float holdNormalizedTime)
    {
        if (anim == null || string.IsNullOrWhiteSpace(stateName))
            yield break;

        int hash = Animator.StringToHash(stateName);

        // Wait until we actually enter the state (or a short timeout).
        float timeout = 0.5f;
        while (timeout > 0f)
        {
            var st = anim.GetCurrentAnimatorStateInfo(0);
            if (st.shortNameHash == hash || st.fullPathHash == hash)
                break;
            timeout -= Time.deltaTime;
            yield return null;
        }

        while (true)
        {
            if (!_windupActive) yield break;
            if (anim == null) yield break;

            var st = anim.GetCurrentAnimatorStateInfo(0);
            float t = st.normalizedTime;
            // normalizedTime can exceed 1 on looping states
            t = t - Mathf.Floor(t);

            if (t >= holdNormalizedTime)
                break;

            yield return null;
        }

        // Freeze at windup hold point.
        if (anim != null)
        {
            // Capture the exact held pose so cancel/reverse can start from THIS frame.
            var st = anim.GetCurrentAnimatorStateInfo(0);
            float t = st.normalizedTime;
            t = t - Mathf.Floor(t);
            _windupHeldNormalizedTime = Mathf.Clamp01(t);

            // Force the animator to the held frame before freezing to avoid a 1-frame overshoot.
            anim.Play(stateName, 0, _windupHeldNormalizedTime);
            anim.Update(0f);

            anim.speed = 0f;
        }
    }
    private void ResumePendingWindupHold()
    {
        if (_windupAnimator != null)
            _windupAnimator.speed = 1f;

        _windupActive = false;

        if (_windupHoldRoutine != null)
        {
            StopCoroutine(_windupHoldRoutine);
            _windupHoldRoutine = null;
        }
    }
    private void CancelPendingWindupHold(bool resetAnimatorToDefault)
    {
        _windupActive = false;

        if (_windupHoldRoutine != null)
        {
            StopCoroutine(_windupHoldRoutine);
            _windupHoldRoutine = null;
        }

        if (_windupAnimator != null)
        {
            _windupAnimator.speed = 1f;
            if (resetAnimatorToDefault)
            {
                // Rebind snaps back to default state (usually Idle) safely without requiring a state name.
                _windupAnimator.Rebind();
                _windupAnimator.Update(0f);
            }
        }

        _windupHeldNormalizedTime = 0f;

        if (_windupReverseRoutine != null)
        {
            StopCoroutine(_windupReverseRoutine);
            _windupReverseRoutine = null;
        }

        _windupAnimator = null;
        _windupStateName = null;
        _windupActorIndex = -1;
    }
    private void ReversePendingWindupToIdle()
    {
        if (_windupAnimator == null || string.IsNullOrWhiteSpace(_windupStateName))
        {
            // Nothing to reverse; just make sure we aren't stuck frozen.
            CancelPendingWindupHold(resetAnimatorToDefault: true);
            return;
        }

        // Stop the hold routine so it doesn't fight us.
        _windupActive = false;
        if (_windupHoldRoutine != null)
        {
            StopCoroutine(_windupHoldRoutine);
            _windupHoldRoutine = null;
        }

        if (_windupReverseRoutine != null)
        {
            StopCoroutine(_windupReverseRoutine);
            _windupReverseRoutine = null;
        }

        float startNorm = _windupHeldNormalizedTime;
        if (startNorm <= 0f && _windupAnimator != null)
        {
            var st = _windupAnimator.GetCurrentAnimatorStateInfo(0);
            float t = st.normalizedTime;
            t = t - Mathf.Floor(t);
            startNorm = Mathf.Clamp01(t);
        }

        Animator anim = _windupAnimator;
        string stateName = _windupStateName;

        // Clear tracking immediately; coroutine has its own copies.
        _windupAnimator = null;
        _windupStateName = null;
        _windupActorIndex = -1;
        _windupHeldNormalizedTime = 0f;

        _windupReverseRoutine = StartCoroutine(ReverseWindupToIdleRoutine_Manual(anim, stateName, startNorm));
    }
    private IEnumerator ReverseWindupToIdleRoutine_Manual(Animator anim, string stateName, float startNormalized)
    {
        if (anim == null || string.IsNullOrWhiteSpace(stateName))
            yield break;

        // Force pose at the starting point (the held frame).
        float t = Mathf.Clamp01(startNormalized);

        // Freeze time; we will drive the pose manually.
        float prevSpeed = anim.speed;
        anim.speed = 0f;

        anim.Play(stateName, 0, t);
        anim.Update(0f);

        // Estimate clip length for consistent reverse speed.
        float clipLen = 0.25f; // fallback
        try
        {
            var clips = anim.GetCurrentAnimatorClipInfo(0);
            if (clips != null && clips.Length > 0 && clips[0].clip != null)
                clipLen = Mathf.Max(0.05f, clips[0].clip.length);
        }
        catch { /* ignore */ }

        while (t > 0f)
        {
            // Step backwards in normalized time.
            t -= Time.deltaTime / clipLen;
            if (t < 0f) t = 0f;

            anim.Play(stateName, 0, t);
            anim.Update(0f);

            yield return null;
        }

        // Restore normal speed and return to default controller state (usually Idle).
        anim.speed = (prevSpeed == 0f) ? 1f : prevSpeed;
        anim.Rebind();
        anim.Update(0f);
    }
    public void BeginAbilityUseFromMenu(HeroStats hero, AbilityDefinitionSO ability)
    {
        if (logFlow)
            Debug.Log($"[Battle][Ability] BeginAbilityUseFromMenu. hero={(hero != null ? hero.name : "<null>")} ability={(ability != null ? ability.abilityName : "<null>")}");
        if (!IsPlayerPhase || _resolving) return;
        if (hero == null || ability == null) return;

        int actorIndex = GetPartyIndexForHero(hero);
        if (!IsValidPartyIndex(actorIndex)) return;

        PartyMemberRuntime actor = _party[actorIndex];
        if (actor.IsDead) return;

        // Ensure only one hero shows the casting aura at a time.
        ClearCastingAura();


        // Ability unlock rules (Starter Choice / level unlock).
        HeroStats gateHero = actor.stats != null ? actor.stats : hero;
        if (gateHero != null && !gateHero.IsAbilityUnlocked(ability))
        {
            if (logFlow) Debug.Log($"[Battle][Ability] Blocked: {actor.name} tried to use locked ability '{ability.abilityName}'.", this);
            return;
        }

        if (_pendingAction != PlayerActionType.None) return;

        ResourceCost cost = GetEffectiveCost(actor.stats, ability);

        if (ability != null && ability.baseDamage > 0 && actor.stats != null && !actor.stats.CanCommitDamageAttackThisTurn())
        {
            if (logFlow) Debug.Log($"[Battle][Ability] Blocked: {actor.name} has reached their attack limit for this turn.", this);
            return;
        }

        // Once-per-turn abilities (per hero)
        if (actor.stats != null && !actor.stats.CanUseAbilityThisTurn(ability))
        {
            if (logFlow) Debug.Log($"[Battle][Ability] Blocked: {actor.name} already used '{ability.abilityName}' this player turn.", this);
            return;
        }

        if (logFlow)
            Debug.Log($"[Battle][Ability] Pending set. actorIndex={actorIndex} ability={ability.abilityName} targetType={ability.targetType} shieldAmount={ability.shieldAmount} healAmount={ability.healAmount} baseDamage={ability.baseDamage} cost={cost}");

        _pendingActorIndex = actorIndex;
        _pendingAbility = ability;
        _selectedEnemyTarget = null;
        _previewPartyTargetIndex = -1;
        _selectedPartyTargetIndex = -1;
        HideConfirmText();
        ClearEnemyTargetPreview();

        if (AbilityCastState.Instance != null)
            AbilityCastState.Instance.BeginCast(hero, ability);

        // Visual feedback on the hero prefab while the ability is pending.
        SetCastingAura(actorIndex, enableHeroCastingAura);


        _impactFired = false;
        _attackFinished = false;

        _pendingAction = PlayerActionType.Ability1;
        if (ability.targetType == AbilityTargetType.Enemy)
        {
            _awaitingEnemyTarget = true;
            ClearEnemyTargetPreview();
            _selectedEnemyTarget = null;
            _previewEnemyTarget = null;
            if (logFlow) Debug.Log($"[Battle][AbilityTarget] Awaiting ENEMY target for {ability.abilityName}");

            // Start windup immediately while awaiting target.
            BeginPendingWindupHoldIfNeeded(actor, ability);
        }
        else if ((ability.targetType == AbilityTargetType.Self || ability.targetType == AbilityTargetType.Ally) && (ability.shieldAmount > 0 || ability.healAmount > 0))
        {
            _awaitingEnemyTarget = false;
            _awaitingPartyTarget = true;
            ClearEnemyTargetPreview();
            _selectedEnemyTarget = null;
            _previewEnemyTarget = null;
            if (logFlow)
            {
                string mode = (ability.targetType == AbilityTargetType.Self) ? "SELF" : "ALLY";
                Debug.Log($"[Battle][AbilityTarget] Awaiting {mode} confirm for {ability.abilityName} (ally/self ability)");
            }

            // Start windup immediately while awaiting target.
            BeginPendingWindupHoldIfNeeded(actor, ability);
        }
        else
        {
            _awaitingEnemyTarget = false;
            if (logFlow) Debug.Log($"[Battle][Ability] No target required. Resolving immediately for {ability.abilityName}");
            StartCoroutine(ResolvePendingAbility());
        }

        NotifyPartyChanged();
    }
    private IEnumerator ResolvePendingAbility()
    {
        if (logFlow)
            Debug.Log($"[Battle][Resolve] ResolvePendingAbility ENTER. pendingAbility={(_pendingAbility != null ? _pendingAbility.abilityName : "<null>")} pendingActorIndex={_pendingActorIndex} selectedEnemyTarget={(_selectedEnemyTarget != null ? _selectedEnemyTarget.name : "<null>")} awaitingEnemyTarget={_awaitingEnemyTarget} awaitingPartyTarget={_awaitingPartyTarget}", this);

        if (_pendingAbility == null || !IsValidPartyIndex(_pendingActorIndex))
        {
            if (logFlow) Debug.Log("[Battle][Resolve] Cancel: pending ability or actor invalid.", this);
            CancelPendingAbility();
            yield break;
        }

        AbilityDefinitionSO ability = _pendingAbility;
        if (ability == null)
        {
            if (logFlow) Debug.Log("[Battle][Resolve] Cancel: ability is null.", this);
            CancelPendingAbility();
            yield break;
        }

        if (logFlow)
            Debug.Log($"[Battle][Resolve] Confirmed/casting ability: name={ability.name} abilityName={ability.abilityName} targetType={ability.targetType} shieldAmount={ability.shieldAmount} baseDamage={ability.baseDamage} isDamaging={ability.isDamaging} inflictsFocusRune={ability.inflictsFocusRune}", this);

        PartyMemberRuntime actor = _party[_pendingActorIndex];
        HeroStats actorStats = actor.stats;
        if (actorStats == null || actor.IsDead)
        {
            if (logFlow) Debug.Log("[Battle][Resolve] Cancel: actorStats missing or actor dead.", this);
            CancelPendingAbility();
            yield break;
        }

        if (performanceTracker != null)
            performanceTracker.RecordAbilityUse(actorStats, ability);

        Monster enemyTarget = _selectedEnemyTarget;

        if (ability.targetType == AbilityTargetType.Enemy)
        {
            if (enemyTarget == null || enemyTarget.IsDead)
            {
                if (logFlow) Debug.Log("[Battle][Resolve] Abort: Enemy target required but not selected (or dead). Returning to awaiting target.", this);
                _awaitingEnemyTarget = true;
                yield break;
            }
        }

        if (ability.targetType == AbilityTargetType.Ally && ability.shieldAmount > 0)
        {
            if (!IsValidPartyIndex(_selectedPartyTargetIndex) || _party[_selectedPartyTargetIndex] == null || _party[_selectedPartyTargetIndex].IsDead)
            {
                if (logFlow) Debug.Log("[Battle][Resolve] Abort: Ally target required but not selected (or dead). Returning to awaiting party target.", this);
                _awaitingPartyTarget = true;
                yield break;
            }
        }

        PushSaveStateSnapshot();

        ResourceCost cost = GetEffectiveCost(actorStats, ability);

        int bonusDamageFromSpentAtk = 0;
        if (ability != null && (ability.spendAllAttackResources || ability.name == "Heavy Strike"))
        {
            // Cost.attack was set to current ResourcePool ATK in GetEffectiveCost().
            long spentAtk = cost.attack;
            // Clamp to int range for damage math.
            long rawBonus = spentAtk * (long)Mathf.Max(0, ability.bonusDamagePerAttackResource);
            if (rawBonus > int.MaxValue) rawBonus = int.MaxValue;
            bonusDamageFromSpentAtk = (int)rawBonus;
            if (logFlow) Debug.Log($"[Battle][HeavyStrike] spendAllAttackResources=true spentAtk={spentAtk} bonusPerAtk={ability.bonusDamagePerAttackResource} bonusDamage={bonusDamageFromSpentAtk}", this);
        }
        // Spend resources (special-case: spend ALL ATK for abilities like Heavy Strike).
        // ResourcePool.TrySpend may treat WILD as a flexible payment source; for "spend all ATK" we must force ATK to zero.
        bool isHeavyStrike = (ability != null) && (ability.spendAllAttackResources || ability.name == "Heavy Strike");
        long heavyStrikeSpentAtk = 0;

        if (isHeavyStrike)
        {
            if (resourcePool == null)
            {
                Debug.Log($"[Battle][HeavyStrike][Cancel] Missing resourcePool.", this);
                CancelPendingAbility();
                yield break;
            }

            long atkBefore = resourcePool.Attack;
            long defBefore = resourcePool.Defense;
            long magBefore = resourcePool.Magic;
            long wildBefore = resourcePool.Wild;

            heavyStrikeSpentAtk = Math.Max(0L, atkBefore);
            if (heavyStrikeSpentAtk <= 0)
            {
                Debug.Log($"[Battle][HeavyStrike][Cancel] No ATK to spend. attack={atkBefore}", this);
                CancelPendingAbility();
                yield break;
            }

            // Force ATK to 0 up-front so it cannot be paid via WILD or left partially unspent.
            resourcePool.SetAmounts(0, defBefore, magBefore, wildBefore);

            // Spend remaining costs (with attack cost zeroed so we don't double-spend).
            var remainingCost = cost;
            remainingCost.attack = 0;

            if (!resourcePool.TrySpend(remainingCost))
            {
                // Revert if spending the remaining cost fails.
                resourcePool.SetAmounts(atkBefore, defBefore, magBefore, wildBefore);
                Debug.Log($"[Battle][HeavyStrike][Cancel] Could not pay remainingCost={remainingCost}. Reverted resources.", this);
                CancelPendingAbility();
                yield break;
            }

            Debug.Log($"[Battle][HeavyStrike][Spend] spentAtk={heavyStrikeSpentAtk} bonusPerAtk={ability.bonusDamagePerAttackResource} bonusDamage={bonusDamageFromSpentAtk} poolAfter(atk={resourcePool.Attack},def={resourcePool.Defense},mag={resourcePool.Magic},wild={resourcePool.Wild})", this);
        }
        else
        {
            if (resourcePool == null || !resourcePool.TrySpend(cost))
            {
                if (logFlow) Debug.Log($"[Battle][Resolve] Cancel: insufficient resources or missing resourcePool. cost={cost}", this);
                CancelPendingAbility();
                yield break;
            }
        }
// Mark once-per-turn ability usage only after the cast is truly committed (cost successfully spent).
        actorStats.RegisterAbilityUsedThisTurn(ability);

        if (logFlow) Debug.Log($"[Battle][Resolve] Resources spent. cost={cost}. Proceeding to apply ability effects.", this);
        _resolving = true;

        // ============================
        // Combo (chaining): handled during damage application so each cast can spin and potentially queue more casts.
        // ============================

        Animator anim = actor.animator;
        if (anim == null && actor.avatarGO != null)
            anim = actor.avatarGO.GetComponentInChildren<Animator>(true);

        _impactFired = false;
        _attackFinished = false;
        bool useImpactSync = false;
        string stateToPlay = null;

        if (anim != null)
        {
            var profile = anim.GetComponentInParent<CasterAnimationProfile>();
            // OPTION B (preferred): drive animation from a stable Ability "animation key" instead of the
            // player-facing ability name. This scales cleanly as more classes share abilities.
            //
            // - If the AbilityDefinitionSO has a field/property named "animationKey" (case-insensitive), we'll use it.
            // - Otherwise we fall back to legacy behavior using ability.name/ability.abilityName.
            // - The CasterAnimationProfile can optionally scope a mapping to a className.

            string actorClassName = GetActorClassName(actorStats);
            // Prefer the explicit animation key on the ability asset.
            // Leave blank to fall back to legacy name-based mapping.
            string animationKey = (ability != null)
                ? ability.GetAnimationKeyString()
                : null;

            // Some abilities intentionally play no cast animation.
            if (IsNoAnimAbility(ability))
            {
                useImpactSync = false;
                stateToPlay = null;
                if (logFlow) Debug.Log($"[Battle][Resolve] {ability.abilityName}: no animation and no impact sync.", this);
            }
            else
            {
                stateToPlay = profile != null
                    ? profile.ResolveAttackState(animationKey, actorClassName, abilityNameFallback: ability.name)
                    : null;

                // If a mapping wasn't found but the ability explicitly provided an animationKey,
                // try playing a state with the same name directly. This prevents a missing
                // CasterAnimationProfile mapping from silently falling back to a basic attack.
                if (string.IsNullOrWhiteSpace(stateToPlay) && !string.IsNullOrWhiteSpace(animationKey))
                {
                    int hash = Animator.StringToHash(animationKey);
                    if (anim.HasState(0, hash))
                    {
                        stateToPlay = animationKey;
                        if (logFlow) Debug.Log($"[Battle][Resolve] No profile mapping for animationKey='{animationKey}', but Animator has a state with that name. Using it directly.", this);
                    }
                    else
                    {
                        if (logFlow) Debug.LogWarning($"[Battle][Resolve] No profile mapping for animationKey='{animationKey}', and Animator does not have a state named '{animationKey}'. Falling back.", this);
                    }
                }

                // Next, prefer a class-scoped basic attack instead of always fighter_basic_attack.
                if (string.IsNullOrWhiteSpace(stateToPlay) && !string.IsNullOrWhiteSpace(actorClassName))
                {
                    string classBasic = $"{actorClassName.ToLowerInvariant()}_basic_attack";
                    int hash = Animator.StringToHash(classBasic);
                    if (anim.HasState(0, hash))
                        stateToPlay = classBasic;
                }

                // If we still didn't find anything, retain the prior default behavior.
                if (string.IsNullOrWhiteSpace(stateToPlay))
                    stateToPlay = "fighter_basic_attack";

                useImpactSync = true;
            }

            // If this is a heal/shield targeting Self/Ally, default to syncing the effect
            // to the impact event (if the animation clip has one).
            if ((ability.targetType == AbilityTargetType.Self || ability.targetType == AbilityTargetType.Ally) &&
                (ability.healAmount > 0 || ability.shieldAmount > 0))
            {
                useImpactSync = true;
            }

	        // For combo enemy-target abilities, we play the attack animation PER CAST inside the combo loop.
	        // This avoids the first cast playing once here and then subsequent casts having no animation.
	        bool deferAttackAnimToComboLoop = (ability != null && ability.hasCombo && ability.targetType == AbilityTargetType.Enemy);
	        if (!deferAttackAnimToComboLoop && !string.IsNullOrWhiteSpace(stateToPlay))
	        {
	            if (logFlow) Debug.Log($"[Battle][Resolve] Playing animation state '{stateToPlay}'. useImpactSync={useImpactSync}", this);

	            // If we already started this exact state during target selection (windup hold),
	            // do NOT restart it from time=0 on cast; just continue from the held frame.
	            bool startedDuringTargeting =
	                (_windupAnimator == anim) &&
	                (_windupActorIndex == _pendingActorIndex) &&
	                string.Equals(_windupStateName, stateToPlay, StringComparison.Ordinal);

	            if (startedDuringTargeting)
	            {
	                if (logFlow) Debug.Log($"[Battle][Resolve] Windup hold already started state '{stateToPlay}' during targeting. Continuing without restart.", this);
	                anim.speed = 1f; // ensure unfrozen
	                // Clear windup tracking now that we're committing the cast.
	                CancelPendingWindupHold(resetAnimatorToDefault: false);
	            }
	            else
	            {
	                anim.Play(stateToPlay, 0, 0f);
	            }
	        }
            else
            {
                if (logFlow) Debug.Log($"[Battle][Resolve] No animation played for ability '{ability.abilityName}'.", this);
            }
        }
        else
        {
            if (logFlow) Debug.Log("[Battle][Resolve] No animator found on actor; skipping animation.", this);
        }

        // Support ability impact sync (heal/shield)
        bool isSupportAbility =
            (ability.targetType == AbilityTargetType.Self || ability.targetType == AbilityTargetType.Ally) &&
            (ability.healAmount > 0 || ability.shieldAmount > 0);

        if (isSupportAbility && useImpactSync && anim != null)
        {
            if (logFlow) Debug.Log("[Battle][Resolve] Support ability: waiting for AttackImpact animation event...", this);

            yield return null;

            float elapsed = 0f;
            const float failSafeSeconds = 3.0f;
            while (!_impactFired && elapsed < failSafeSeconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (logFlow) Debug.Log($"[Battle][Resolve] Support impact wait finished. impactFired={_impactFired} elapsed={elapsed:0.000}s", this);
        }

        // ============================
        // Enemy-target abilities
        // ============================
        if (ability.targetType == AbilityTargetType.Enemy && enemyTarget != null)
        {
	            // Wait for impact sync for enemy-target abilities too (even if non-damaging).
	            // For combo abilities, impact sync is handled per-cast inside the combo loop.
	            if (useImpactSync && anim != null && !(ability != null && ability.hasCombo))
            {
                if (logFlow) Debug.Log("[Battle][Resolve] Waiting for AttackImpact animation event...", this);

                yield return null;

                float elapsed = 0f;
                const float failSafeSeconds = 3.0f;
                while (!_impactFired && elapsed < failSafeSeconds)
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                if (logFlow) Debug.Log($"[Battle][Resolve] Done waiting for impact. impactFired={_impactFired} elapsed={elapsed:0.000}s", this);

            // ============================
            // Mage Dart VFX (sprite animation on target)
            // Uses the same "spawn SpellEffectEntity prefab on target and wait for it to finish" structure as BoD Consume.
            // ============================
            if (IsMageDartAbility(ability, actorStats))
            {
                if (logFlow) Debug.Log($"[Battle][VFX] Dart -> spawning target effect. caster={actorStats.name} target={enemyTarget.name}", this);
                yield return SpawnSpellEffectPrefabOnMonsterRoutine(mageDartEffectPrefab, enemyTarget, mageDartEffectVerticalOffset);
            }

            }

            // Taunt: force this enemy to target the casting fighter on its next intent, and immediately
            // update any already-planned intent for this enemy so UI + execution reflect the new target.
            if (IsTauntAbility(ability))
            {
                // Force this enemy to target the casting hero (typically the Fighter) immediately.
                int tauntCasterIndex = _pendingActorIndex;
                // _party is a list of PartyMemberRuntime, so we must locate the index by matching stats.
                if (tauntCasterIndex < 0 && actorStats != null && _party != null)
                {
                    for (int i = 0; i < _party.Count; i++)
                    {
                        if (_party[i] != null && _party[i].stats == actorStats)
                        {
                            tauntCasterIndex = i;
                            break;
                        }
                    }
                }

                if (tauntCasterIndex >= 0)
                {
                    enemyTarget.SetForcedTargetPartyIndex(tauntCasterIndex);

                    // If intents were already planned for the upcoming enemy phase, retarget them now
                    // so the UI updates immediately (player sees the taunt right away).
                    if (_plannedIntents.Count == 0)
                        PlanEnemyIntents();

                    RetargetPlannedIntentsForEnemy(enemyTarget, tauntCasterIndex);

                    // Broadcast updated intents for UI listeners.
                    OnEnemyIntentsPlanned?.Invoke(new List<EnemyIntent>(_plannedIntents));
                }

                // Taunt also grants the caster block (shield) even though this is an enemy-target ability.
                if (ability.shieldAmount > 0 && actorStats != null)
                {
                    if (logFlow) Debug.Log($"[Battle][Taunt] Granting block to caster. amount={ability.shieldAmount} caster={actorStats.name} shieldBefore={actorStats.Shield}", this);
                    actorStats.AddShield(ability.shieldAmount);
                    if (logFlow) Debug.Log($"[Battle][Taunt] Block granted. caster={actorStats.name} shieldAfter={actorStats.Shield}", this);
                }

                NotifyPartyChanged();
            }

            // Non-damaging abilities (isDamaging == false) should NEVER apply any damage by default.
            // This makes utility abilities like Taunt/Focus Rune safe even if the caster has high Attack.
            bool doesDamage = (ability != null && ability.isDamaging);

            if (!doesDamage && logFlow)
                Debug.Log($"[Battle][Resolve] Non-damaging ability -> skipping damage application. ability={ability.abilityName}", this);

            int shownDamage = 0;
            int dealt = 0;
            int totalBaseDamage = 0;

            if (doesDamage)
            {
                // Consume "next attack" bonus damage ONCE for the whole ability.
                int passiveBonusOnce = (actorStats != null) ? actorStats.ConsumeBonusDamageNextAttackIfDamaging(ability) : 0;

                // Combo chaining: each cast performs its own bonus one-reel spin (does NOT consume SpinsRemaining).
                // If the spin lands on the trigger type, we queue additional casts based on the resource gain amount.
                // This can chain until a max total cast cap is reached.

                int maxTotalCasts = 1;
                if (ability != null && ability.hasCombo)
                {
                    maxTotalCasts = (ability.comboMaxTotalCasts > 0)
                        ? ability.comboMaxTotalCasts
                        : (1 + Mathf.Max(0, ability.comboMaxExtraCasts));
                }

                int castsRemaining = 1;
                int castsExecuted = 0;

                // Current target can change during combo chaining if the ability requests random retargets.
                Monster currentTarget = enemyTarget;
                bool randomizeNextTarget = false;

                while (castsRemaining > 0)
                {
                    int hitIndex = castsExecuted;
                    castsRemaining--;

	                    // Play the attack animation for EACH combo cast (including the first).
	                    // Restart from time=0 so repeated casts don't get ignored by the Animator.
	                    if (ability != null && ability.hasCombo && anim != null && !string.IsNullOrWhiteSpace(stateToPlay))
	                    {
	                        _impactFired = false;
	                        if (logFlow) Debug.Log($"[Battle][Combo] Playing per-cast animation '{stateToPlay}' hitIndex={hitIndex}.", this);
	                        anim.Play(stateToPlay, 0, 0f);

	                        // Give Animator a frame to evaluate transitions/state.
	                        yield return null;

	                        if (useImpactSync)
	                        {
	                            float elapsed = 0f;
	                            const float failSafeSeconds = 2.0f;
	                            while (!_impactFired && elapsed < failSafeSeconds)
	                            {
	                                elapsed += Time.deltaTime;
	                                yield return null;
	                            }
	                        }
	                    }

                    // Ensure we always have a valid target when chaining.
                    if (currentTarget == null || currentTarget.IsDead)
                        currentTarget = GetRandomLivingEnemy(exclude: null);
                    if (currentTarget == null || currentTarget.IsDead)
                        break;

                    // Each cast's combo spin (including the first cast).
                    if (ability != null && ability.hasCombo && reelSpinSystem != null)
                    {
                        float speedMult = Mathf.Clamp(
                            ability.comboSpinSpeedMultiplierStart + ability.comboSpinSpeedMultiplierStep * hitIndex,
                            0.1f,
                            Mathf.Max(0.1f, ability.comboSpinSpeedMultiplierMax));

                        yield return StartCoroutine(reelSpinSystem.MomentumSpinAndInstantCollect(_pendingActorIndex, speedMult));

                        var spin = reelSpinSystem.LastInstantSpinResult;
                        if (spin.valid && actorStats != null)
                        {
                            // Ensure symbol-landed passives fire for this bonus spin.
                            actorStats.NotifyReelSymbolLanded(spin.symbol, spin.resourceType, spin.amount, spin.multiplier);

                            // Chain: landing on trigger type queues additional casts based on the gained amount.
                            if (spin.resourceType == ability.comboTriggerType)
                            {
                                int extra = Mathf.Max(0, spin.total);
                                if (extra > 0)
                                {
                                    // Cap to max total casts.
                                    int remainingCap = Mathf.Max(0, maxTotalCasts - (castsExecuted + 1) - castsRemaining);
                                    if (remainingCap > 0)
                                        castsRemaining += Mathf.Min(extra, remainingCap);
                                }

                                // If requested, randomize the NEXT target whenever the trigger lands.
                                if (ability.comboRandomizeNextEnemyTargetOnTrigger)
                                    randomizeNextTarget = true;
                            }
                            else
                            {
                                randomizeNextTarget = false;
                            }
                        }
                    }

                    // First cast gets one-time bonuses (spent-ATK bonus, next-attack passive bonus).
                    int passiveBonusThisHit = (castsExecuted == 0) ? passiveBonusOnce : 0;
                    int spentAtkBonusThisHit = (castsExecuted == 0) ? bonusDamageFromSpentAtk : 0;

                    totalBaseDamage =
                        Mathf.Max(0, actorStats.Attack) +
                        Mathf.Max(0, ability.baseDamage) +
                        Mathf.Max(0, passiveBonusThisHit) +
                        Mathf.Max(0, spentAtkBonusThisHit);

                    // Damage numbers should show computed formula damage, not clamped HP lost.
                    var target = currentTarget;

                    shownDamage = target.CalculateDamageFromAbility(
                        abilityBaseDamage: totalBaseDamage,
                        classAttackModifier: 1f,
                        element: ability.element,
                        abilityTags: ability.tags);

                    if (isHeavyStrike)
                    {
                        Debug.Log($"[Battle][HeavyStrike][Damage] caster={actorStats.name} target={(enemyTarget!=null?enemyTarget.name:"<null>")} spentAtk={heavyStrikeSpentAtk} bonusDamage={spentAtkBonusThisHit} totalBaseDamage={totalBaseDamage} shownDamage={shownDamage}", this);
                    }

                    dealt = target.TakeDamageFromAbility(
                        abilityBaseDamage: totalBaseDamage,
                        classAttackModifier: 1f,
                        element: ability.element,
                        abilityTags: ability.tags);

                    if (debugEnemyHpBarDrop && target != null)
                    {
                        Debug.Log($"[Battle][HpBarDrop] After TakeDamageFromAbility target={target.name} dealt={dealt} hpNow={target.CurrentHp}/{target.MaxHp} instance={target.GetInstanceID()}", this);

                        var hpBar = target.GetComponentInChildren<MonsterHpBar>(true);
                        if (hpBar == null)
                        {
                            Debug.LogWarning($"[Battle][HpBarDrop] No MonsterHpBar found under target={target.name} instance={target.GetInstanceID()}", this);
                        }
                        else
                        {
                            Debug.Log($"[Battle][HpBarDrop] Found hpBar={hpBar.name} barInstance={hpBar.GetInstanceID()} barBoundMonster={(hpBar != null ? (hpBar.GetComponentInParent<Monster>() != null ? hpBar.GetComponentInParent<Monster>().GetInstanceID().ToString() : "none") : "none")}", this);

                            hpBar.ForceDebugDumpVisual("BattleManager BEFORE ClearPreview/Refresh");
                            hpBar.ClearPreview();

                            hpBar.ForceDebugDumpVisual("BattleManager AFTER ClearPreview");
                            hpBar.RefreshNow("BattleManager post-damage");

                            hpBar.ForceDebugDumpVisual("BattleManager AFTER RefreshNow");
                        }
                    }

                    if (performanceTracker != null)
                        performanceTracker.RecordDamageDealt(actorStats, dealt);

                    if (shownDamage > 0)
                        SpawnDamageNumber(target.transform.position, shownDamage);

                    // Optional monster reaction animations (hit/block) for Animator-driven monsters.
                    var enemyAnim = target != null ? target.GetComponentInChildren<MonsterAnimationDriver>(true) : null;
                    if (enemyAnim != null && !target.IsDead)
                    {
                        if (shownDamage <= 0 || dealt <= 0)
                            enemyAnim.PlayBlock();
                        else
                            enemyAnim.PlayHit();
                    }

                    actorStats.ApplyOnHitEffectsTo(target);

                    if (totalBaseDamage > 0)
                        actorStats.RegisterDamageAttackCommitted();

                    // Bloodlust (passive): whenever this hero deals damage, spin ONLY their reel once and instantly collect that reel's payout.
                    // Uses the same "momentum" spin helper (does not consume spinsRemaining and does not touch normal pending payout state).
                    if (dealt > 0 && actorStats != null && actorStats.HasAbilityUnlocked("Bloodlust") && reelSpinSystem != null)
                    {
                        if (logFlow) Debug.Log($"[Battle][Bloodlust] Triggered. caster={actorStats.name} dealt={dealt} -> reelIndex={_pendingActorIndex}", this);
                        yield return StartCoroutine(reelSpinSystem.MomentumSpinAndInstantCollect(_pendingActorIndex));
                    }

                    // If the enemy died from this hit, handle death once and stop applying further hits.
                    if (target != null && target.IsDead)
                    {
                        int xpAward = (target != null) ? target.XpReward : 5;
                        if (performanceTracker != null)
                            performanceTracker.RecordBaseXpGained(actorStats, xpAward);
                        else
                            actorStats.GainXP(xpAward);

                        // Momentum: if this ability killed the enemy, immediately spin ONLY the caster's reel once and cash it out.
                        if (ability != null && ability.momentumOnKill && reelSpinSystem != null)
                            yield return StartCoroutine(reelSpinSystem.MomentumSpinAndInstantCollect(_pendingActorIndex));

                        RecordVictoryKillerFromPendingActor("Ability kill");
                        HandleMonsterKilled(target);

                        // If we still have casts remaining, pick a new living target and continue.
                        if (castsRemaining > 0 && castsExecuted + 1 < maxTotalCasts)
                        {
                            currentTarget = GetRandomLivingEnemy(exclude: null);
                            if (currentTarget == null) break;
                        }
                        else
                        {
                            break;
                        }
                    }

                    // Apply requested random retargeting for the NEXT cast when the trigger lands.
                    if (randomizeNextTarget && castsRemaining > 0)
                    {
                        currentTarget = GetRandomLivingEnemy(exclude: currentTarget);
                        randomizeNextTarget = false;
                    }
                    castsExecuted++;

                    // Safety: stop if we reached max total casts.
                    if (castsExecuted >= maxTotalCasts)
                        break;

                    // NOTE: Combo chains are bounded by castsRemaining/maxTotalCasts,
                    // so we don't need an additional "handledDeath" early-exit here.
                } // end combo-casts loop
            }
            else
            {
                if (logFlow) Debug.Log($"[Battle][Resolve] Non-damaging enemy ability '{ability.abilityName}': skipping damage math.", this);
            }

// ---------------- Status Infliction (Monster) ----------------
            if (ability.inflictsFocusRune && enemyTarget != null && !enemyTarget.IsDead)
            {
                if (logFlow) Debug.Log($"[Battle][Status] Applying FocusRune via ability='{ability.abilityName}' to monster='{enemyTarget.name}'", this);
                enemyTarget.SetFocusRune(true);
            }

            // Death check ALWAYS (not gated)
            if (enemyTarget.IsDead)
            {
                int xpAward = (enemyTarget != null) ? enemyTarget.XpReward : 5;
                if (performanceTracker != null)
                    performanceTracker.RecordBaseXpGained(actorStats, xpAward);
                else
                    actorStats.GainXP(xpAward);

                

                // Momentum: if this ability killed the enemy, immediately spin ONLY the caster's reel once and cash it out.
                if (ability != null && ability.momentumOnKill && reelSpinSystem != null)
                    yield return StartCoroutine(reelSpinSystem.MomentumSpinAndInstantCollect(_pendingActorIndex));
                    
                RecordVictoryKillerFromPendingActor("Ability kill");
                HandleMonsterKilled(enemyTarget);
            }
        }


        // ============================
        // Sabotage (Enemy Ability Debuff)
        // ============================
        // If configured, pick a random enemy attack and mark it sabotaged for the rest of the battle.
        // Whenever the enemy uses that attack, it takes self-damage equal to current sabotage stacks.
        if (ability != null && ability.targetType == AbilityTargetType.Enemy)
        {
            bool doSabotage = false;
            int stacksToApply = 0;
            try { doSabotage = ability.inflictsSabotage; stacksToApply = ability.sabotageStacks; }
            catch { doSabotage = false; stacksToApply = 0; }

            if (doSabotage && enemyTarget != null && !enemyTarget.IsDead)
            {
                int stacks = Mathf.Max(1, stacksToApply);
                int chosenIdx = enemyTarget.ApplySabotageToRandomAttack(stacks);
                if (logFlow)
                    Debug.Log($"[Battle][Sabotage] Applied to monster='{enemyTarget.name}' +{stacks} stacks. chosenAttackIndex={chosenIdx} totalStacks={enemyTarget.SabotageStacks}", this);
            }
        }

        // ============================
        // Shield (Self/Ally)
        // ============================
        if (ability.shieldAmount > 0 && (ability.targetType == AbilityTargetType.Self || ability.targetType == AbilityTargetType.Ally))
        {
            HeroStats targetStats = actorStats;
            string targetName = actorStats.name;

            if (ability.targetType == AbilityTargetType.Ally)
            {
                if (IsValidPartyIndex(_selectedPartyTargetIndex) && _party[_selectedPartyTargetIndex] != null)
                {
                    targetStats = _party[_selectedPartyTargetIndex].stats;
                    targetName = _party[_selectedPartyTargetIndex].name;
                }
            }

            if (targetStats != null)
            {
                if (logFlow) Debug.Log($"[Battle][Shield] Applying shield. amount={ability.shieldAmount} target={targetName} shieldBefore={targetStats.Shield}", this);
                targetStats.AddShield(ability.shieldAmount);
                if (logFlow) Debug.Log($"[Battle][Shield] Shield applied. target={targetName} shieldAfter={targetStats.Shield}", this);
            }
        }

        // ============================
        // Heal (Self/Ally)
        // ============================
        if (ability.healAmount > 0 && (ability.targetType == AbilityTargetType.Self || ability.targetType == AbilityTargetType.Ally))
        {
            HeroStats targetStats = actorStats;
            GameObject targetGO = actor != null ? actor.avatarGO : null;
            string targetName = actorStats != null ? actorStats.name : "<null>";

            if (ability.targetType == AbilityTargetType.Ally)
            {
                if (IsValidPartyIndex(_selectedPartyTargetIndex) && _party[_selectedPartyTargetIndex] != null)
                {
                    targetStats = _party[_selectedPartyTargetIndex].stats;
                    targetGO = _party[_selectedPartyTargetIndex].avatarGO;
                    targetName = _party[_selectedPartyTargetIndex].name;
                }
            }

            if (targetStats != null)
            {
                int before = targetStats.CurrentHp;
                targetStats.Heal(ability.healAmount);
                int healed = Mathf.Max(0, targetStats.CurrentHp - before);

                if (logFlow) Debug.Log($"[Battle][Heal] Applied. amount={ability.healAmount} healed={healed} target={targetName} hpNow={targetStats.CurrentHp}/{targetStats.MaxHp}", this);

                if (healed > 0)
                {
                    Vector3 pos = GetHeroCenterWorldPosition(targetStats, targetGO != null ? targetGO.transform : (targetStats != null ? targetStats.transform : null));
                    SpawnHealNumber(pos, healed);
                    SpawnHealVfx(GetHeroCenterPointTransform(targetStats, targetStats != null ? targetStats.transform : null));
                }
            }
        }

        // ---------------- Status Cleansing (Bleeding / Stunned) ----------------
        if (ability.targetType == AbilityTargetType.Self || ability.targetType == AbilityTargetType.Ally)
        {
            bool hasConfiguredCleansing = (ability.removesStatusEffects != null && ability.removesStatusEffects.Count > 0);
            bool isFirstAid = (ability.name == "First Aid" || ability.abilityName == "First Aid");

            if (hasConfiguredCleansing || isFirstAid)
            {
                HeroStats cleanseTargetStats = actorStats;
                GameObject cleanseTargetGO = actor != null ? actor.avatarGO : null;
                string cleanseTargetName = actor != null ? actor.name : (actorStats != null ? actorStats.name : "<null>");

                if (ability.targetType == AbilityTargetType.Ally)
                {
                    if (IsValidPartyIndex(_selectedPartyTargetIndex) && _party[_selectedPartyTargetIndex] != null)
                    {
                        cleanseTargetStats = _party[_selectedPartyTargetIndex].stats;
                        cleanseTargetGO = _party[_selectedPartyTargetIndex].avatarGO;
                        cleanseTargetName = _party[_selectedPartyTargetIndex].name;
                    }
                }

                ApplyStatusCleansingToHero(ability, cleanseTargetStats, cleanseTargetName, cleanseTargetGO, forceBleedForFirstAid: isFirstAid);
            }
        }

        bool wasHiddenBeforeCast = actorStats.IsHidden;

        if (ability.name == "Conceal")
        {
            actorStats.SetHidden(true);
        }
        else if (wasHiddenBeforeCast)
        {
            bool keepHidden = false;

            if (ability.name == "Backstab" && ability.targetType == AbilityTargetType.Enemy && enemyTarget != null && enemyTarget.IsDead)
                keepHidden = true;

            if (!keepHidden)
                actorStats.SetHidden(false);
        }

        ApplyPartyHiddenVisuals();

        actor.hasActedThisRound = true;

        _resolving = false;

        if (AbilityCastState.Instance != null)
            AbilityCastState.Instance.ClearCast();

        CancelPendingAbility();
        NotifyPartyChanged();

        if (_saveStates != null && _saveStates.Count > 1)
            SetUndoButtonEnabled(true);
    }
    private static bool IsNoAnimAbility(AbilityDefinitionSO ability)
    {
        if (ability == null) return false;
        string n = null;
        try { n = string.IsNullOrWhiteSpace(ability.name) ? ability.abilityName : ability.name; } catch { n = null; }
        if (string.IsNullOrWhiteSpace(n)) return false;
        n = n.Trim();

        // These are intentionally “instant” (no cast animation / no impact sync).
        return string.Equals(n, "Conceal", StringComparison.OrdinalIgnoreCase)
            || string.Equals(n, "Block", StringComparison.OrdinalIgnoreCase)
            || string.Equals(n, "Aegis", StringComparison.OrdinalIgnoreCase);
    }
    private void CancelPendingAbility()
    {
        if (logFlow)
            Debug.Log($"[Battle][Cancel] CancelPendingAbility. pendingAbility={(_pendingAbility != null ? _pendingAbility.abilityName : "<null>")} pendingActorIndex={_pendingActorIndex} awaitingEnemyTarget={_awaitingEnemyTarget} awaitingPartyTarget={_awaitingPartyTarget}", this);

        // Turn off any active casting aura before we wipe pending indices.
        ClearCastingAura();

        // If the player cancels while targeting, play the caster windup back in reverse to idle.
        ReversePendingWindupToIdle();

        _pendingAction = PlayerActionType.None;
        _pendingAbility = null;
        _pendingActorIndex = -1;
        _awaitingEnemyTarget = false;
        _awaitingPartyTarget = false;
        _selectedEnemyTarget = null;
        _previewPartyTargetIndex = -1;
        _selectedPartyTargetIndex = -1;
        HideConfirmText();
        ClearEnemyTargetPreview();
        UpdateEnemyTargetIndicators();
        _impactFired = false;
        _attackFinished = false;

        if (AbilityCastState.Instance != null)
            AbilityCastState.Instance.ClearCast();

        OnPendingAbilityCleared?.Invoke();
    }
    private void SetCastingAura(int partyIndex, bool enabled)
    {
        if (!enableHeroCastingAura) return;
        if (!IsValidPartyIndex(partyIndex)) return;

        PartyMemberRuntime pm = _party[partyIndex];
        if (pm == null || pm.avatarGO == null) return;

        // Prefer a component on the avatar root; fallback to children.
        var aura = pm.avatarGO.GetComponent<HeroCastingAura>();
        if (aura == null)
            aura = pm.avatarGO.GetComponentInChildren<HeroCastingAura>(true);

        if (aura == null)
        {
            if (logFlow) Debug.Log($"[Battle][Aura] No HeroCastingAura found on avatarGO for partyIndex={partyIndex} ({pm.avatarGO.name}).", this);
            return;
        }

        if (enabled)
        {
            _castingAuraPartyIndex = partyIndex;
            aura.BeginCasting();
        }
        else
        {
            aura.EndCasting();
            if (_castingAuraPartyIndex == partyIndex)
                _castingAuraPartyIndex = -1;
        }
    }
    private void ClearCastingAura()
    {
        if (_castingAuraPartyIndex < 0) return;
        SetCastingAura(_castingAuraPartyIndex, false);
        _castingAuraPartyIndex = -1;
    }
    private static bool IsTauntAbility(AbilityDefinitionSO a)
    {
        if (a == null) return false;
        // Support both the display field and the ScriptableObject asset name.
        return string.Equals(a.abilityName, "Taunt", System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(a.name, "Taunt", System.StringComparison.OrdinalIgnoreCase);
    }
    private void RecordVictoryKillerFromPendingActor(string reason)
    {
        var hs = GetHeroAtPartyIndex(_pendingActorIndex);
        if (hs == null)
            return;

        _victoryKillerHero = hs;

        if (victoryJingleDebugLogs)
        {
            string heroName = hs != null ? hs.gameObject.name : "<null>";
            string baseClass = (hs != null && hs.BaseClassDef != null) ? hs.BaseClassDef.className : "<unknown>";
            Debug.Log($"[VictoryJingle][KILLER] Recorded killer from pending actor. hero={heroName} baseClass={baseClass} reason={reason} time={Time.time:0.00} rt={Time.realtimeSinceStartup:0.00}", this);
        }
    }

}
