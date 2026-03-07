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
    private Action<QuickAbilityIconButtonUI, HeroStats, AbilityDefinitionSO> _onClick;
    private Action<QuickAbilityIconButtonUI, HeroStats, AbilityDefinitionSO> _onHoldDetails;

    private bool _pointerDown;
    private bool _didHold;
    private Coroutine _holdRoutine;

    public void BindForHero(
        HeroStats hero,
        AbilityDefinitionSO ability,
        ResourcePool resourcePool,
        Action<QuickAbilityIconButtonUI, HeroStats, AbilityDefinitionSO> onClick,
        Action<QuickAbilityIconButtonUI, HeroStats, AbilityDefinitionSO> onHoldDetails,
        Func<HeroStats, AbilityDefinitionSO, bool> canUseExtraPredicate = null,
        bool showCostText = true
    )
    {
        _hero = hero;
        _ability = ability;
        _resourcePool = resourcePool;
        _onClick = onClick;
        _onHoldDetails = onHoldDetails;
        _canUseExtraPredicate = canUseExtraPredicate;

        if (iconImage != null)
            iconImage.sprite = (ability != null) ? ability.icon : null;

        if (costText != null)
        {
            costText.gameObject.SetActive(showCostText);
            if (showCostText)
            {
                costText.richText = true;
                costText.text = BuildCostStringStatic(_hero, ability, _resourcePool, attackSpriteIndex, defenseSpriteIndex, magicSpriteIndex, wildSpriteIndex);
            }
        }
    }

    public bool IsUsableNow()
    {
        if (_hero == null || _ability == null || _resourcePool == null) return false;

        ResourceCost effectiveCost = ComputeEffectiveCost();

        if (_ability.spendAllAttackResources && effectiveCost.attack <= 0)
            return false;

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
            _onClick?.Invoke(this, _hero, _ability);
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
            _onHoldDetails?.Invoke(this, _hero, _ability);
        }

        _holdRoutine = null;
    }

    public static string BuildCostStringStatic(AbilityDefinitionSO ability, ResourcePool pool)
    {
        return BuildCostStringStatic(null, ability, pool, 2, 0, 1, 3);
    }

    public static string BuildCostStringStatic(HeroStats hero, AbilityDefinitionSO ability, ResourcePool pool)
    {
        return BuildCostStringStatic(hero, ability, pool, 2, 0, 1, 3);
    }

    private static string BuildCostStringStatic(
        HeroStats hero,
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

        if (hero != null && HeroStats.IsRampingBasicAttackAbility(ability))
            cost.attack = System.Math.Max(0L, cost.attack) + System.Math.Max(0L, hero.GetRampingBasicAttackAdditionalAtkCost(ability));

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

    private ResourceCost ComputeEffectiveCost()
    {
        if (_ability == null) return default;

        ResourceCost effectiveCost = _ability.cost;

        if (_ability.spendAllAttackResources)
        {
            long atk = _resourcePool != null ? _resourcePool.Attack : 0;
            effectiveCost.attack = atk <= 0 ? 0 : atk;
        }

        if (_hero != null && HeroStats.IsRampingBasicAttackAbility(_ability))
            effectiveCost.attack = System.Math.Max(0L, effectiveCost.attack) + System.Math.Max(0L, _hero.GetRampingBasicAttackAdditionalAtkCost(_ability));

        return effectiveCost;
    }
}
