using UnityEngine;
using UnityEngine.UI;

public class HeroEquipUI : MonoBehaviour
{
    [Header("Party Binding")]
    [Tooltip("Party index this UI row represents (0, 1, 2...)")]
    public int partyIndex;

    [Header("Equip UI")]
    [Tooltip("Root of this hero's equipment grid (EquipGrid1)")]
    public Transform equipGridRoot;

    [Tooltip("Image used to display the hero portrait (child under EquipGrid)")]
    public Image portraitImage;

    private HeroStats hero;

    private void OnEnable()
    {
        BattleManager.PartyReady += Bind;
    }

    private void OnDisable()
    {
        BattleManager.PartyReady -= Bind;
    }

    private void Start()
    {
        // Fallback in case PartyReady already fired
        Bind();
    }

    private void Bind()
    {
        if (hero != null) return;

        var bm = BattleManager.Instance;
        if (bm == null) return;

        hero = bm.GetHeroAtPartyIndex(partyIndex);
        if (hero == null)
        {
            Debug.LogWarning($"[HeroEquipUI] No hero at party index {partyIndex}", this);
            return;
        }

        // 🔗 Wire EquipGrid to HeroStats
        hero.SetEquipGridRoot(equipGridRoot);
        hero.RefreshEquipSlotsFromGrid();

        // 🖼️ Apply portrait from HERO PREFAB DATA
        if (portraitImage != null && hero.Portrait != null)
        {
            portraitImage.sprite = hero.Portrait;
            portraitImage.enabled = true;
        }

        Debug.Log(
            $"[HeroEquipUI] Bound UI '{name}' -> hero '{hero.name}' " +
            $"(partyIndex {partyIndex}, portrait={(hero.Portrait != null ? hero.Portrait.name : "NULL")})"
        );
    }
}
