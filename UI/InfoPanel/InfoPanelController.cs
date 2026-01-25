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

    [Header("Debug")]
    [SerializeField] private bool logFlow = true;

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

    public void Open()
    {
        if (infoPanelRoot == null) infoPanelRoot = gameObject;

        if (!infoPanelRoot.activeSelf)
        {
            infoPanelRoot.SetActive(true);
            if (logFlow) Debug.Log($"{TAG} OPEN (root enabled)", this);
        }
        else
        {
            if (logFlow) Debug.Log($"{TAG} OPEN requested (already open)", this);
        }
    }

    public void Close()
    {
        if (infoPanelRoot == null) infoPanelRoot = gameObject;

        if (infoPanelRoot.activeSelf)
        {
            infoPanelRoot.SetActive(false);
            if (logFlow) Debug.Log($"{TAG} CLOSE (root disabled)", this);
        }
        else
        {
            if (logFlow) Debug.Log($"{TAG} CLOSE requested (already closed)", this);
        }
    }

    private void OnBackgroundClicked()
    {
        if (logFlow) Debug.Log($"{TAG} BG CLICK -> close", this);
        Close();
    }
}