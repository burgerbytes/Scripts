using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls the in-game InfoPanel popover (BG + Panel).
///
/// Updated: obeys PartyHUD single-panel locking so InfoPanel cannot open while another panel is open
/// (AbilityMenu, QuickAbilityMenu, StatsPanel, Reelcraft, etc.).
/// </summary>
public class InfoPanelController : MonoBehaviour
{
    private const string TAG = "[InfoPanel]";

    [Header("Wiring")]
    [Tooltip("Root object that contains BG + Panel. If null, uses this GameObject.")]
    [SerializeField] private GameObject infoPanelRoot;

    [Tooltip("Background button object (BG). Clicking it closes the panel.")]
    [SerializeField] private Button backgroundButton;

    [Tooltip("Optional: PartyHUD reference for single-panel locking. If null, we auto-find one at runtime.")]
    [SerializeField] private PartyHUD partyHUD;

    [Header("Content UI")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private Image iconImage;

    [Header("Debug")]
    [SerializeField] private bool logFlow = false;

    [Header("Optional")]
    [Tooltip("If assigned, reels will be disabled while the InfoPanel is open.")]
    [SerializeField] private ReelDisableManager reelDisableManager;

    public bool IsOpen => (infoPanelRoot != null ? infoPanelRoot.activeInHierarchy : gameObject.activeInHierarchy);

    private void Awake()
    {
        if (infoPanelRoot == null) infoPanelRoot = gameObject;

        if (partyHUD == null)
            partyHUD = FindFirstObjectByType<PartyHUD>();

        // Auto-find BG Button if not assigned (expects a child named "BG" like your hierarchy).
        if (backgroundButton == null && infoPanelRoot != null)
        {
            var t = infoPanelRoot.transform.Find("BG");
            if (t != null) backgroundButton = t.GetComponent<Button>();
        }

        if (backgroundButton != null)
        {
            backgroundButton.onClick.RemoveListener(OnBackgroundClicked);
            backgroundButton.onClick.AddListener(OnBackgroundClicked);
        }
        else if (logFlow)
        {
            Debug.LogWarning($"{TAG} No backgroundButton wired. Add a Button component to BG and assign it.", this);
        }
    }

    private void OnDestroy()
    {
        if (backgroundButton != null)
            backgroundButton.onClick.RemoveListener(OnBackgroundClicked);
    }

    public void Show(InfoPanelData data)
    {
        if (titleText != null) titleText.text = data.title ?? "";
        if (bodyText != null) bodyText.text = data.body ?? "";

        if (iconImage != null)
        {
            iconImage.sprite = data.image;
            iconImage.enabled = (data.image != null);
            iconImage.preserveAspect = true;
        }

        Open();
    }

    public void Open()
    {
        if (infoPanelRoot == null)
            infoPanelRoot = gameObject;

        // Single-panel lock: if ANY other panel is open, do not open InfoPanel.
        // (This prevents InfoPanel from popping while AbilityMenu/QuickAbilityMenu/etc are up.)
        if (partyHUD != null)
        {
            var canOpen = partyHUD.CanOpenPanel(PartyHUD.UIPanelType.InfoPanel);
            if (!canOpen)
            {
                if (logFlow)
                    Debug.Log($"{TAG} Open blocked (another panel open).", this);
                return;
            }
        }

        if (!infoPanelRoot.activeSelf)
        {
            infoPanelRoot.SetActive(true);
            reelDisableManager?.DisableReels();

            // Acquire the lock AFTER we actually open.
            partyHUD?.NotifyPanelOpened(PartyHUD.UIPanelType.InfoPanel);
        }
    }

    public void Close()
    {
        if (infoPanelRoot == null)
            infoPanelRoot = gameObject;

        if (infoPanelRoot.activeSelf)
        {
            infoPanelRoot.SetActive(false);
            reelDisableManager?.EnableReels();

            // Release the lock.
            partyHUD?.NotifyPanelClosed(PartyHUD.UIPanelType.InfoPanel);
        }
    }

    private void OnBackgroundClicked()
    {
        if (logFlow) Debug.Log($"{TAG} BG CLICK -> close", this);
        Close();
    }

    private void OnDisable()
    {
        // Safety: if this GameObject gets disabled externally, make sure we don't leave the lock held.
        reelDisableManager?.EnableReels();
        if (partyHUD != null && partyHUD.GetCurrentOpenPanel() == PartyHUD.UIPanelType.InfoPanel)
            partyHUD.NotifyPanelClosed(PartyHUD.UIPanelType.InfoPanel);
    }
}
