using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI for per-hero Reelcraft abilities.
///
/// Expected wiring (minimal):
/// - Put this panel under a Canvas (it can be disabled by default).
/// - Assign titleText, descriptionText (optional).
/// - Assign buttons relevant to the abilities you want visible.
///
/// This script does not spawn UI - it's a simple controller you can hook to prefab buttons.
/// </summary>
public class ReelcraftPanelUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private ReelcraftController reelcraft;
    [SerializeField] private BattleManager battleManager;
    [SerializeField] private ReelSpinSystem reelSpinSystem;

    [Header("Text")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text descriptionText;

    [Header("Fighter - Measured Bash")]
    [SerializeField] private Button MeasuredBashUpButton;
    [SerializeField] private Button MeasuredBashDownButton;

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
        if (reelcraft != null)
            reelcraft.OnReelcraftUsed += HandleReelcraftUsed;

        if (reelSpinSystem != null)
        {
            reelSpinSystem.OnReelPhaseChanged += HandleReelPhaseChanged;
            reelSpinSystem.OnPendingPayoutChanged += HandlePendingChanged;
        }
    }

    private void OnDisable()
    {
        if (reelcraft != null)
            reelcraft.OnReelcraftUsed -= HandleReelcraftUsed;

        if (reelSpinSystem != null)
        {
            reelSpinSystem.OnReelPhaseChanged -= HandleReelPhaseChanged;
            reelSpinSystem.OnPendingPayoutChanged -= HandlePendingChanged;
        }
    }

    private void HandleReelcraftUsed(int partyIndex)
    {
        // If the currently displayed hero uses Reelcraft, close the panel immediately
        // and lock out all buttons (once-per-battle).
        if (!gameObject.activeSelf) return;
        if (partyIndex != _partyIndex) return;

        DisableAllButtons();
        Hide();
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
        // IMPORTANT:
        // Some prefabs may already have persistent OnClick handlers wired in the Inspector.
        // If we add listeners in code without clearing, clicks can fire multiple times.
        // That can cause "Measured Bash Down moves by 2" (-1 twice).
        if (MeasuredBashUpButton != null)
        {
            MeasuredBashUpButton.onClick.RemoveAllListeners();
            MeasuredBashUpButton.onClick.AddListener(() => OnMeasuredBash(+1));
        }

        if (MeasuredBashDownButton != null)
        {
            MeasuredBashDownButton.onClick.RemoveAllListeners();
            MeasuredBashDownButton.onClick.AddListener(() => OnMeasuredBash(-1));
        }

        // Deprecated buttons (older prefab). Clear listeners anyway so they can't double-fire.
        if (transmuteAtkToMagicButton != null)
        {
            transmuteAtkToMagicButton.onClick.RemoveAllListeners();
            transmuteAtkToMagicButton.onClick.AddListener(() => OnTransmuteDeprecated(ReelSpinSystem.ResourceType.Attack));
        }

        if (transmuteDefToMagicButton != null)
        {
            transmuteDefToMagicButton.onClick.RemoveAllListeners();
            transmuteDefToMagicButton.onClick.AddListener(() => OnTransmuteDeprecated(ReelSpinSystem.ResourceType.Defend));
        }

        if (transmuteWildToMagicButton != null)
        {
            transmuteWildToMagicButton.onClick.RemoveAllListeners();
            transmuteWildToMagicButton.onClick.AddListener(() => OnTransmuteDeprecated(ReelSpinSystem.ResourceType.Wild));
        }

        if (transmuteSelectButton != null)
        {
            transmuteSelectButton.onClick.RemoveAllListeners();
            transmuteSelectButton.onClick.AddListener(OnTransmuteSelect);
        }

        if (twofoldShadowButton != null)
        {
            twofoldShadowButton.onClick.RemoveAllListeners();
            twofoldShadowButton.onClick.AddListener(OnTwofoldShadow);
        }
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

    private void DisableAllButtons()
    {
        if (MeasuredBashUpButton != null) MeasuredBashUpButton.interactable = false;
        if (MeasuredBashDownButton != null) MeasuredBashDownButton.interactable = false;
        if (transmuteSelectButton != null) transmuteSelectButton.interactable = false;
        if (transmuteAtkToMagicButton != null) transmuteAtkToMagicButton.interactable = false;
        if (transmuteDefToMagicButton != null) transmuteDefToMagicButton.interactable = false;
        if (transmuteWildToMagicButton != null) transmuteWildToMagicButton.interactable = false;
        if (twofoldShadowButton != null) twofoldShadowButton.interactable = false;
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
        SetGroupVisible(MeasuredBashUpButton, MeasuredBashDownButton, archetype == ReelcraftController.ReelcraftArchetype.Fighter);
        if (transmuteSelectButton != null)
            SetGroupVisible(transmuteSelectButton, archetype == ReelcraftController.ReelcraftArchetype.Mage);

        // Hide deprecated 3-button UI if present (avoid confusion)
        SetGroupVisible(transmuteAtkToMagicButton, transmuteDefToMagicButton, transmuteWildToMagicButton, false);
        SetGroupVisible(twofoldShadowButton, archetype == ReelcraftController.ReelcraftArchetype.Ninja);

        // Interactables
        if (MeasuredBashUpButton != null) MeasuredBashUpButton.interactable = canUse && archetype == ReelcraftController.ReelcraftArchetype.Fighter;
        if (MeasuredBashDownButton != null) MeasuredBashDownButton.interactable = canUse && archetype == ReelcraftController.ReelcraftArchetype.Fighter;

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
                return "Measured Bash: Nudge your reel up/down by 1 symbol.";
            case ReelcraftController.ReelcraftArchetype.Mage:
                return "Arcane Transmutation: Click a glowing icon to permanently transmute it into Magic for this battle (NULL works too).";
            case ReelcraftController.ReelcraftArchetype.Ninja:
                return "Twofold Shadow: Double the resources from your reel's landed symbol.";
            default:
                return "No Reelcraft available for this class.";
        }
    }

    private void OnMeasuredBash(int dir)
    {
        if (reelcraft == null) return;

        bool ok = reelcraft.TryMeasuredBash(_partyIndex, dir);
        if (logFlow)
            Debug.Log($"[ReelcraftPanel] MeasuredBash dir={dir} ok={ok}", this);

        // Panel will auto-close via OnReelcraftUsed, but keep this safe in case
        // the event isn't wired for some reason.
        if (ok)
        {
            DisableAllButtons();
            Hide();
            return;
        }

        Refresh();
    }

    private void OnTransmuteSelect()
    {
        if (reelcraft == null) return;

        bool ok = reelcraft.BeginArcaneTransmutationSelect(_partyIndex);
        if (logFlow)
            Debug.Log($"[ReelcraftPanel] TransmuteSelect ok={ok}", this);

        if (ok)
        {
            // Keep panel open while selecting; buttons will disable/close on OnReelcraftUsed.
            DisableAllButtons();
        }
        else
        {
            Refresh();
        }
    }

    private void OnTwofoldShadow()
    {
        if (reelcraft == null) return;

        bool ok = reelcraft.TryTwofoldShadow(_partyIndex);
        if (logFlow)
            Debug.Log($"[ReelcraftPanel] TwofoldShadow ok={ok}", this);

        if (ok)
        {
            DisableAllButtons();
            Hide();
            return;
        }

        Refresh();
    }

    private void OnTransmuteDeprecated(ReelSpinSystem.ResourceType from)
    {
        // Deprecated path (older prefab). We leave it here for safety, but it should be hidden by Refresh().
        if (reelcraft == null) return;
        if (logFlow)
            Debug.LogWarning("[ReelcraftPanel] Deprecated Transmute button clicked. Use the single Transmute button.", this);
    }

    private static void SetGroupVisible(Button a, Button b, bool visible)
    {
        if (a != null) a.gameObject.SetActive(visible);
        if (b != null) b.gameObject.SetActive(visible);
    }

    private static void SetGroupVisible(Button a, bool visible)
    {
        if (a != null) a.gameObject.SetActive(visible);
    }

    private static void SetGroupVisible(Button a, Button b, Button c, bool visible)
    {
        if (a != null) a.gameObject.SetActive(visible);
        if (b != null) b.gameObject.SetActive(visible);
        if (c != null) c.gameObject.SetActive(visible);
    }
}
