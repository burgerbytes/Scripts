using UnityEngine;

/// <summary>
/// Worn Buckler:
/// - At the start of every player turn (PlayerPhase), grant +1 Block to the equipped hero.
/// Break logic is handled elsewhere (HeroStats damage hook).
/// </summary>
public class WornBucklerTurnEffectSystem : MonoBehaviour
{
    [Header("References (optional)")]
    [SerializeField] private BattleManager battleManager;

    [Header("Worn Buckler Settings")]
    [SerializeField] private int blockPerTurn = 1;

    private const string WORN_BUCKLER_NAME = "Worn Buckler";

    private void Awake()
    {
        Debug.Log("[WornBuckler] Awake", this);

        if (battleManager == null)
            battleManager = BattleManager.Instance;

        if (battleManager == null)
            Debug.LogError("[WornBuckler] BattleManager.Instance is NULL in Awake.", this);
    }

    private void OnEnable()
    {
        Debug.Log("[WornBuckler] OnEnable - subscribing", this);

        if (battleManager == null)
            battleManager = BattleManager.Instance;

        if (battleManager == null)
        {
            Debug.LogError("[WornBuckler] Cannot subscribe: BattleManager is NULL.", this);
            return;
        }

        // prevent accidental double-subscribe if component is toggled
        battleManager.OnBattleStateChanged -= HandleBattleStateChanged;
        battleManager.OnBattleStateChanged += HandleBattleStateChanged;
    }

    private void OnDisable()
    {
        if (battleManager != null)
            battleManager.OnBattleStateChanged -= HandleBattleStateChanged;
    }

    // IMPORTANT: signature matches your BattleManager example.
    private void HandleBattleStateChanged(BattleManager.BattleState state)
    {
        // Proves the handler is firing (remove later if noisy)
        Debug.Log($"[WornBuckler] OnBattleStateChanged state={state}", this);

        if (state != BattleManager.BattleState.PlayerPhase)
            return;

        ApplyWornBucklerBlock();
    }

    private void ApplyWornBucklerBlock()
    {
        if (battleManager == null) return;
        if (blockPerTurn <= 0) return;

        int partyCount = battleManager.PartyCount;
        for (int i = 0; i < partyCount; i++)
        {
            HeroStats h = battleManager.GetHeroAtPartyIndex(i);
            if (h == null) continue;

            // Prefer enum effect check if you have it; name fallback prevents wiring mistakes.
            bool hasBuckler =
                h.HasEquippedEffect(ItemEffect.WornBuckler) ||
                h.HasEquippedItemName(WORN_BUCKLER_NAME);

            if (!hasBuckler) continue;

            // Use whichever your project uses for Block. If your HeroStats uses AddBlock instead, swap it.
            h.AddShield(blockPerTurn);

            Debug.Log($"[WornBuckler] Granted +{blockPerTurn} Block to hero='{h.name}' at turn start.", this);
        }
    }
}
