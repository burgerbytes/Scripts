using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ReelcraftPanelUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private ReelcraftController reelcraft;
    [SerializeField] private BattleManager battleManager;
    [SerializeField] private ReelSpinSystem reelSpinSystem;

    [Header("Text")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text descriptionText;

    [Header("Fighter - Steel Nudge")]
    [SerializeField] private Button steelNudgeUpButton;
    [SerializeField] private Button steelNudgeDownButton;

    [Header("Mage - Arcane Transmutation")]
    [SerializeField] private Button transmuteSelectButton;

    // Back-compat (older prefab): if these are wired, we hide/ignore them.
    [SerializeField] private Button transmuteAtkToMagicButton;
    [SerializeField] private Button transmuteDefToMagicButton;
    [SerializeField] private Button transmuteWildToMagicButton;

    [Header("Ninja - Twofold Shadow")]
    [SerializeField] private Button twofoldShadowButton;

    [Header("Debug")]
    [SerializeField] private bool logFlow = true;

    private int _partyIndex = -1;
    private HeroStats _hero;

    private void Awake()
    {
        if (reelcraft == null)
            reelcraft = FindFirstObjectByType<ReelcraftController>();
        if (battleManager == null)
            battleManager = FindFirstObjectByType<BattleManager>();
        if (reelSpinSystem == null)
            reelSpinSystem = FindFirstObjectByType<ReelSpinSystem>();

        WireButtons();

        // Default hidden (matches your intended flow)
        gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (reelSpinSystem != null)
        {
            reelSpinSystem.OnReelPhaseChanged += HandleReelPhaseChanged;
            reelSpinSystem.OnPendingPayoutChanged += HandlePendingChanged;
        }
    }

    private void OnDisable()
    {
        if (reelSpinSystem != null)
        {
            reelSpinSystem.OnReelPhaseChanged -= HandleReelPhaseChanged;
            reelSpinSystem.OnPendingPayoutChanged -= HandlePendingChanged;
        }
    }

    private void HandleReelPhaseChanged(bool inReelPhase)
    {
        // If reel phase ends, hide this panel immediately.
        if (!inReelPhase)
            Hide();
    }

    private void HandlePendingChanged(int a, int d, int m, int w)
    {
        // Keep button enabled/disabled state up to date.
        if (gameObject.activeSelf)
            Refresh();
    }

    private void WireButtons()
    {
        if (steelNudgeUpButton != null)
            steelNudgeUpButton.onClick.AddListener(() => OnSteelNudge(+1));
        if (steelNudgeDownButton != null)
            steelNudgeDownButton.onClick.AddListener(() => OnSteelNudge(-1));

        if (transmuteAtkToMagicButton != null)
            transmuteAtkToMagicButton.onClick.AddListener(() => OnTransmuteDeprecated(ReelSpinSystem.ResourceType.Attack));
        if (transmuteDefToMagicButton != null)
            transmuteDefToMagicButton.onClick.AddListener(() => OnTransmuteDeprecated(ReelSpinSystem.ResourceType.Defend));
        if (transmuteWildToMagicButton != null)
            transmuteWildToMagicButton.onClick.AddListener(() => OnTransmuteDeprecated(ReelSpinSystem.ResourceType.Wild));

        if (transmuteSelectButton != null)
            transmuteSelectButton.onClick.AddListener(OnTransmuteSelect);

        if (twofoldShadowButton != null)
            twofoldShadowButton.onClick.AddListener(OnTwofoldShadow);
    }

    public void ShowForHero(int partyIndex)
    {
        if (battleManager == null)
            battleManager = FindFirstObjectByType<BattleManager>();
        if (battleManager == null) return;

        _partyIndex = partyIndex;
        _hero = battleManager.GetHeroAtPartyIndex(partyIndex);

        if (reelSpinSystem != null && !reelSpinSystem.InReelPhase)
            return;

        gameObject.SetActive(true);
        Refresh();
    }

    public void Hide()
    {
        _partyIndex = -1;
        _hero = null;
        if (gameObject != null)
            gameObject.SetActive(false);
    }

    public void Refresh()
    {
        if (_partyIndex < 0 || battleManager == null) return;
        if (reelcraft == null) return;

        bool canUse = reelcraft.CanUse(_partyIndex);
        bool used = reelcraft.HasUsed(_partyIndex);

        var archetype = reelcraft.GetArchetype(_hero);

        // Title / description
        if (titleText != null)
        {
            string heroName = _hero != null ? _hero.name : $"Party {_partyIndex}";
            titleText.text = used ? $"{heroName} — Reelcraft (USED)" : $"{heroName} — Reelcraft";
        }

        if (descriptionText != null)
            descriptionText.text = BuildDescription(archetype, canUse, used);

        // Toggle groups
        SetGroupVisible(steelNudgeUpButton, steelNudgeDownButton, archetype == ReelcraftController.ReelcraftArchetype.Fighter);
        if (transmuteSelectButton != null)
            SetGroupVisible(transmuteSelectButton, archetype == ReelcraftController.ReelcraftArchetype.Mage);

        // Hide deprecated 3-button UI if present (avoid confusion)
        SetGroupVisible(transmuteAtkToMagicButton, transmuteDefToMagicButton, transmuteWildToMagicButton, false);
        SetGroupVisible(twofoldShadowButton, archetype == ReelcraftController.ReelcraftArchetype.Ninja);

        // Interactables
        if (steelNudgeUpButton != null) steelNudgeUpButton.interactable = canUse && archetype == ReelcraftController.ReelcraftArchetype.Fighter;
        if (steelNudgeDownButton != null) steelNudgeDownButton.interactable = canUse && archetype == ReelcraftController.ReelcraftArchetype.Fighter;

        if (transmuteSelectButton != null) transmuteSelectButton.interactable = canUse && archetype == ReelcraftController.ReelcraftArchetype.Mage;

        if (twofoldShadowButton != null) twofoldShadowButton.interactable = canUse && archetype == ReelcraftController.ReelcraftArchetype.Ninja;
    }

    private bool HasPending(ReelSpinSystem.ResourceType type)
    {
        if (reelSpinSystem == null) return false;
        reelSpinSystem.GetPendingPayout(out int a, out int d, out int m, out int w);
        switch (type)
        {
            case ReelSpinSystem.ResourceType.Attack: return a > 0;
            case ReelSpinSystem.ResourceType.Defend: return d > 0;
            case ReelSpinSystem.ResourceType.Magic: return m > 0;
            case ReelSpinSystem.ResourceType.Wild: return w > 0;
        }
        return false;
    }

    private static string BuildDescription(ReelcraftController.ReelcraftArchetype archetype, bool canUse, bool used)
    {
        if (used) return "Reelcraft can only be used once per battle.";
        if (!canUse) return "Reelcraft is only available during the reel phase after a spin lands.";

        switch (archetype)
        {
            case ReelcraftController.ReelcraftArchetype.Fighter:
                return "Steel Nudge: Nudge your reel up/down by 1 symbol.";
            case ReelcraftController.ReelcraftArchetype.Mage:
                return "Arcane Transmutation: Click a glowing icon to permanently transmute it into Magic for this battle (NULL works too).";
            case ReelcraftController.ReelcraftArchetype.Ninja:
                return "Twofold Shadow: Double the resources from your reel's landed symbol.";
            default:
                return "No Reelcraft available for this class.";
        }
    }

    private void OnSteelNudge(int dir)
    {
        if (reelcraft == null) return;

        bool ok = reelcraft.TrySteelNudge(_partyIndex, dir);
        if (logFlow)
            Debug.Log($"[ReelcraftPanel] SteelNudge dir={dir} ok={ok}", this);

        Refresh();
    }

    private void OnTransmuteSelect()
    {
        if (reelcraft == null) return;

        bool ok = reelcraft.BeginArcaneTransmutationSelect(_partyIndex);
        if (logFlow)
            Debug.Log($"[ReelcraftPanel] TransmuteSelect ok={ok}", this);

        // Keep panel open so player can click an icon.
        Refresh();
    }

    // Deprecated: old behavior converted pending resources. Kept only so existing prefabs don't NRE.
    private void OnTransmuteDeprecated(ReelSpinSystem.ResourceType from)
    {
        if (logFlow)
            Debug.LogWarning($"[ReelcraftPanel] Deprecated transmute button pressed (from={from}). Please wire transmuteSelectButton instead.", this);

        Refresh();
    }

    private void OnTwofoldShadow()
    {
        if (reelcraft == null) return;

        bool ok = reelcraft.TryTwofoldShadow(_partyIndex);
        if (logFlow)
            Debug.Log($"[ReelcraftPanel] TwofoldShadow ok={ok}", this);

        Refresh();
    }

    private static void SetGroupVisible(Button a, Button b, bool visible)
    {
        if (a != null) a.gameObject.SetActive(visible);
        if (b != null) b.gameObject.SetActive(visible);
    }

    private static void SetGroupVisible(Button a, Button b, Button c, bool visible)
    {
        if (a != null) a.gameObject.SetActive(visible);
        if (b != null) b.gameObject.SetActive(visible);
        if (c != null) c.gameObject.SetActive(visible);
    }

    private static void SetGroupVisible(Button a, bool visible)
    {
        if (a != null) a.gameObject.SetActive(visible);
    }
}
