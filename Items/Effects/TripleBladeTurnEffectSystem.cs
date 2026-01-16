using UnityEngine;

public class TripleBladeTurnEffectSystem : MonoBehaviour
{
    [Header("References (optional)")]
    [SerializeField] private BattleManager battleManager;
    [SerializeField] private ReelSpinSystem reelSpinSystem;

    [Header("Triple Blade Settings")]
    [SerializeField] private bool preferItemEffectFlag = true;
    [SerializeField] private string tripleBladeItemName = "Triple Blade";
    [SerializeField] private float attackMultiplierForTurn = 2.0f;
    [SerializeField] private int maxDamageAttacksThisTurn = 1;

    private BattleManager _bm;

    private void Awake()
    {
        if (battleManager == null) battleManager = BattleManager.Instance;
        if (reelSpinSystem == null) reelSpinSystem = FindFirstObjectByType<ReelSpinSystem>();
        _bm = FindObjectOfType<BattleManager>();
    }

    private void OnEnable()
    {
        if (battleManager != null)
            battleManager.OnBattleStateChanged += HandleBattleStateChanged;

        if (reelSpinSystem != null)
            reelSpinSystem.OnSpinLanded += HandleSpinLanded;
    }

    private void OnDisable()
    {
        if (battleManager != null)
            battleManager.OnBattleStateChanged -= HandleBattleStateChanged;

        if (reelSpinSystem != null)
            reelSpinSystem.OnSpinLanded -= HandleSpinLanded;
    }

    private void HandleBattleStateChanged(BattleManager.BattleState state)
    {
        if (state == BattleManager.BattleState.PlayerPhase)
            ResetAllHeroesTurnCombatState();
    }

    private void HandleSpinLanded(ReelSpinSystem.SpinLandedInfo info)
    {
        if (battleManager == null) return;

        if (!info.IsTripleAttack) return;

        Debug.Log($"[TripleBlade] Reels landed TRIPLE ATTACK (attackCount={info.attackCount}, total={(info.symbols != null ? info.symbols.Count : 0)}). Checking equipped heroes...");

        ApplyTripleBladeToEquippedHeroes();
    }

    private void ResetAllHeroesTurnCombatState()
    {
        if (battleManager == null) return;

        for (int i = 0; i < battleManager.PartyCount; i++)
        {
            HeroStats h = battleManager.GetHeroAtPartyIndex(i);
            if (h == null) continue;
            h.ResetTurnCombatState();
        }
    }

    private void ApplyTripleBladeToEquippedHeroes()
    {
        if (battleManager == null) return;

        int appliedCount = 0;

        for (int i = 0; i < battleManager.PartyCount; i++)
        {
            HeroStats h = battleManager.GetHeroAtPartyIndex(i);
            if (h == null)
            {
                Debug.Log($"[TripleBlade] partyIndex {i}: HeroStats NULL");
                continue;
            }

            bool hasTripleBlade = false;

            if (preferItemEffectFlag)
                hasTripleBlade = h.HasEquippedEffect(ItemEffect.TripleBlade);

            if (!hasTripleBlade && !string.IsNullOrWhiteSpace(tripleBladeItemName))
                hasTripleBlade = h.HasEquippedItemName(tripleBladeItemName);

            Debug.Log($"[TripleBlade] partyIndex {i} hero='{h.gameObject.name}' hasTripleBlade={hasTripleBlade}");

            if (!hasTripleBlade) continue;

            h.MultiplyTurnAttack(attackMultiplierForTurn);
            h.ConstrainDamageAttacksThisTurn(maxDamageAttacksThisTurn);

            
            h.SetTripleBladeEmpoweredThisTurn(true);
			appliedCount++;
            var bm = FindObjectOfType<BattleManager>();
            if (_bm != null) _bm.RefreshStatusVisuals();
            Debug.Log($"[TripleBlade] EFFECT ACTIVE on hero '{h.gameObject.name}' this turn: Attack x{attackMultiplierForTurn}, Max damaging attacks = {maxDamageAttacksThisTurn}.");
        }
    }
}
