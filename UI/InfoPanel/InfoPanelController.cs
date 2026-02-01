using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class InfoPanelController : MonoBehaviour
{
    private const string TAG = "[InfoPanel]";

    [Header("Wiring")]
    [Tooltip("Root object that contains BG + Panel. If null, uses this GameObject.")]
    [SerializeField] private GameObject infoPanelRoot;

    [Tooltip("Background button object (BG). Clicking it closes the panel.")]
    [SerializeField] private Button backgroundButton;

    [Header("Content UI")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private Image iconImage;

    [Header("Debug")]
    [SerializeField] private bool logFlow = true;

    [SerializeField] private ReelDisableManager reelDisableManager;
    public bool IsOpen => (infoPanelRoot != null ? infoPanelRoot.activeInHierarchy : gameObject.activeInHierarchy);

    private void Awake()
    {
        if (infoPanelRoot == null) infoPanelRoot = gameObject;

        // Auto-find BG Button if not assigned (expects a child named "BG" like your hierarchy).
        if (backgroundButton == null)
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

        if (!infoPanelRoot.activeSelf)
        {
            infoPanelRoot.SetActive(true);
            reelDisableManager?.DisableReels();
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
        }
    }

    private void OnBackgroundClicked()
    {
        if (logFlow) Debug.Log($"{TAG} BG CLICK -> close", this);
        Close();
    }

    private void OnDisable()
    {
        reelDisableManager?.EnableReels();
    }
}
