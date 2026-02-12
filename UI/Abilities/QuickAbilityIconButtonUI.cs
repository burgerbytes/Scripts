// GUID: 4b0c6a1e6f0a4f2fb2e7a9b8d24b2c10
////////////////////////////////////////////////////////////
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class QuickAbilityIconButtonUI : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Header("UI")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text costText;

    [Header("Hold Behavior")]
    [SerializeField] private float holdSeconds = 0.35f;

    [Header("Cost Sprite Indices (match TMP Sprite Asset table)")]
    [SerializeField] private int attackSpriteIndex = 2;
    [SerializeField] private int defenseSpriteIndex = 0;
    [SerializeField] private int magicSpriteIndex = 1;
    [SerializeField] private int wildSpriteIndex = 3;

    private HeroStats _hero;
    private AbilityDefinitionSO _ability;
    private ResourcePool _resourcePool;

    private Func<HeroStats, AbilityDefinitionSO, bool> _canUseExtraPredicate;
    private Action<HeroStats, AbilityDefinitionSO> _onClickCast;
    private Action<HeroStats, AbilityDefinitionSO> _onHoldDetails;

    private bool _pointerDown;
    private bool _didHold;
    private Coroutine _holdRoutine;

    public void BindForHero(
        HeroStats hero,
        AbilityDefinitionSO ability,
        ResourcePool resourcePool,
        Action<HeroStats, AbilityDefinitionSO> onClickCast,
        Action<HeroStats, AbilityDefinitionSO> onHoldDetails,
        Func<HeroStats, AbilityDefinitionSO, bool> canUseExtraPredicate = null
    )
    {
        _hero = hero;
        _ability = ability;
        _resourcePool = resourcePool;
        _onClickCast = onClickCast;
        _onHoldDetails = onHoldDetails;
        _canUseExtraPredicate = canUseExtraPredicate;

        if (iconImage != null)
            iconImage.sprite = (ability != null) ? ability.icon : null;

        if (costText != null)
        {
            costText.richText = true;
            costText.text = BuildCostStringStatic(ability, _resourcePool, attackSpriteIndex, defenseSpriteIndex, magicSpriteIndex, wildSpriteIndex);
        }
    }

    public bool IsUsableNow()
    {
        if (_hero == null || _ability == null || _resourcePool == null) return false;

        ResourceCost effectiveCost = _ability.cost;

        if (_ability.spendAllAttackResources)
        {
            long atk = _resourcePool.Attack;
            if (atk <= 0) return false;
            effectiveCost.attack = atk;
        }

        if (!_resourcePool.CanAfford(effectiveCost))
            return false;

        if (_canUseExtraPredicate != null && !_canUseExtraPredicate.Invoke(_hero, _ability))
            return false;

        return true;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!enabled || _ability == null) return;

        _pointerDown = true;
        _didHold = false;

        if (_holdRoutine != null)
            StopCoroutine(_holdRoutine);

        _holdRoutine = StartCoroutine(HoldTimer());
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!_pointerDown) return;

        _pointerDown = false;

        if (_holdRoutine != null)
        {
            StopCoroutine(_holdRoutine);
            _holdRoutine = null;
        }

        if (_didHold)
            return;

        if (IsUsableNow())
            _onClickCast?.Invoke(_hero, _ability);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!_pointerDown) return;

        _pointerDown = false;

        if (_holdRoutine != null)
        {
            StopCoroutine(_holdRoutine);
            _holdRoutine = null;
        }
    }

    private IEnumerator HoldTimer()
    {
        float t = 0f;
        while (_pointerDown && t < holdSeconds)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (_pointerDown)
        {
            _didHold = true;
            _onHoldDetails?.Invoke(_hero, _ability);
        }

        _holdRoutine = null;
    }

    public static string BuildCostStringStatic(AbilityDefinitionSO ability, ResourcePool pool)
    {
        return BuildCostStringStatic(ability, pool, 2, 0, 1, 3);
    }

    private static string BuildCostStringStatic(
        AbilityDefinitionSO ability,
        ResourcePool pool,
        int atkSprite,
        int defSprite,
        int magSprite,
        int wildSprite
    )
    {
        if (ability == null) return "";

        ResourceCost cost = ability.cost;

        if (pool != null && ability.spendAllAttackResources)
        {
            long atk = pool.Attack;
            cost.attack = atk < 0 ? 0 : atk;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        void AddPart(int spriteIdx, long amount)
        {
            if (amount <= 0) return;
            if (sb.Length > 0) sb.Append(" ");
            sb.Append($"<sprite={spriteIdx}>");
            sb.Append(amount);
        }

        AddPart(defSprite, cost.defense);
        AddPart(magSprite, cost.magic);
        AddPart(atkSprite, cost.attack);
        AddPart(wildSprite, cost.wild);

        return sb.ToString();
    }
}
