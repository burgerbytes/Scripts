using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Shared shell for the game's reusable info panel.
///
/// Design intent:
/// - ONE panel shell lives under MainCanvas/InfoPanelController.
/// - Heroes, monsters, items, and future subjects all feed data into this shell.
/// - Type-specific presenters can still exist, but should behave like launchers/providers,
///   not standalone panel frameworks.
///
/// Current hierarchy compatibility:
/// - Works with the existing generic Info tab.
/// - Works with the existing Monster Reel tab if wired.
/// - Optionally works with a Status tab if/when a generic/shared status root is added.
/// - Safely ignores any optional tabs that are not wired yet.
/// </summary>
public class InfoPanelController : MonoBehaviour
{
    private const string TAG = "[InfoPanel]";

    [Header("Wiring")]
    [Tooltip("Root object that contains BG + Panel. If null, uses this GameObject.")]
    [SerializeField] private GameObject infoPanelRoot;

    [Tooltip("Background button object (BG). Clicking it closes the panel (outside the panel bounds).")]
    [SerializeField] private Button backgroundButton;

    [Tooltip("Optional: PartyHUD reference for single-panel locking. If null, we auto-find one at runtime.")]
    [SerializeField] private PartyHUD partyHUD;

    [Header("Generic Content UI")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private Image iconImage;

    [Header("Tab UI")]
    [Tooltip("Optional root containing tab buttons. If null, tabs are ignored.")]
    [SerializeField] private GameObject tabBarRoot;
    [SerializeField] private Button infoTabButton;
    [SerializeField] private Button reelTabButton;
    [SerializeField] private Button statusTabButton;

    [Header("Tab Roots")]
    [Tooltip("Root containing the shared generic info content.")]
    [SerializeField] private GameObject infoTabRoot;
    [Tooltip("Root containing the monster reel UI.")]
    [SerializeField] private GameObject monsterReelTabRoot;
    [Tooltip("Optional shared status root. Safe to leave null until the hierarchy is updated.")]
    [SerializeField] private GameObject statusTabRoot;

    [Header("Tab Presenters")]
    [SerializeField] private MonsterReelPanelUI monsterReelPanelUI;
    [SerializeField] private MonsterStatusTabUI monsterStatusTabUI;

    [Header("Optional")]
    [Tooltip("If assigned, reels will be disabled while the InfoPanel is open.")]
    [SerializeField] private ReelDisableManager reelDisableManager;

    [Header("Raycast Safety")]
    [Tooltip("If true, BG will NOT receive raycasts over the Panel area (prevents BG from blocking tab clicks).")]
    [SerializeField] private bool preventBackgroundRaycastsOverPanel = true;

    [Tooltip("If true and logFlow is enabled, clicking will dump the top UI raycast hits to the console.")]
    [SerializeField] private bool debugRaycastOnClick = false;

    [Tooltip("If true, non-interactive content graphics (title/body/icon) will have Raycast Target disabled so they don't block tab button clicks.")]
    [SerializeField] private bool disableContentRaycastTargets = true;

    [Header("Debug")]
    [SerializeField] private bool logFlow = false;

    public bool IsOpen => (infoPanelRoot != null ? infoPanelRoot.activeInHierarchy : gameObject.activeInHierarchy);

    private enum Mode
    {
        Generic,
        Monster,
        Hero,
        Item
    }

    private enum ActiveTab
    {
        Info,
        Reel,
        Status
    }

    private Mode _mode = Mode.Generic;
    private ActiveTab _activeTab = ActiveTab.Info;

    private Monster _currentMonster;
    private HeroStats _currentHero;
    private ItemSO _currentItem;

    private InfoPanelPresentation _lastPresentation;
    private RectTransform _panelRect;
    private GraphicRaycaster _raycaster;

    private void Awake()
    {
        if (infoPanelRoot == null)
            infoPanelRoot = gameObject;

        if (partyHUD == null)
            partyHUD = FindFirstObjectByType<PartyHUD>(FindObjectsInactive.Include);

        if (backgroundButton == null && infoPanelRoot != null)
        {
            Transform t = infoPanelRoot.transform.Find("BG");
            if (t != null)
                backgroundButton = t.GetComponent<Button>();
        }

        if (monsterReelPanelUI == null && monsterReelTabRoot != null)
            monsterReelPanelUI = monsterReelTabRoot.GetComponentInChildren<MonsterReelPanelUI>(true);

        if (monsterStatusTabUI == null && statusTabRoot != null)
            monsterStatusTabUI = statusTabRoot.GetComponentInChildren<MonsterStatusTabUI>(true);

        if (backgroundButton != null)
        {
            backgroundButton.onClick.RemoveListener(OnBackgroundClicked);
            backgroundButton.onClick.AddListener(OnBackgroundClicked);
        }

        if (infoTabButton != null)
        {
            infoTabButton.onClick.RemoveListener(OnInfoTabClicked);
            infoTabButton.onClick.AddListener(OnInfoTabClicked);
        }

        if (reelTabButton != null)
        {
            reelTabButton.onClick.RemoveListener(OnReelTabClicked);
            reelTabButton.onClick.AddListener(OnReelTabClicked);
        }

        if (statusTabButton != null)
        {
            statusTabButton.onClick.RemoveListener(OnStatusTabClicked);
            statusTabButton.onClick.AddListener(OnStatusTabClicked);
        }

        _raycaster = GetComponentInParent<GraphicRaycaster>();
        CachePanelRectAndInstallBackgroundFilter();

        if (disableContentRaycastTargets)
            DisableRaycastTargetsOnContent();

        if (infoPanelRoot != null)
            infoPanelRoot.SetActive(false);

        ApplyPresentation(InfoPanelContentFactory.BuildGenericPresentation("Info", "Select something to inspect."), openPanel: false);
        ShowInfoTab();
    }

    private void OnDestroy()
    {
        if (backgroundButton != null)
            backgroundButton.onClick.RemoveListener(OnBackgroundClicked);
        if (infoTabButton != null)
            infoTabButton.onClick.RemoveListener(OnInfoTabClicked);
        if (reelTabButton != null)
            reelTabButton.onClick.RemoveListener(OnReelTabClicked);
        if (statusTabButton != null)
            statusTabButton.onClick.RemoveListener(OnStatusTabClicked);
    }

    private void OnDisable()
    {
        reelDisableManager?.EnableReels();
        if (partyHUD != null && partyHUD.GetCurrentOpenPanel() == PartyHUD.UIPanelType.InfoPanel)
            partyHUD.NotifyPanelClosed(PartyHUD.UIPanelType.InfoPanel);
    }

    private void Update()
    {
        if (!logFlow || !debugRaycastOnClick || !IsOpen)
            return;

        if (Input.GetMouseButtonDown(0))
            DumpTopRaycastHits("MouseDown");
    }

    public void Show(InfoPanelData data)
    {
        _mode = Mode.Generic;
        _currentMonster = null;
        _currentHero = null;
        _currentItem = null;

        ApplyPresentation(InfoPanelContentFactory.BuildGenericPresentation(data.title, data.body, data.image), openPanel: true);
    }

    public void Show(IInfoPanelContentProvider provider)
    {
        if (provider == null)
        {
            Show(new InfoPanelData { title = "Info", body = "No provider was supplied.", image = null });
            return;
        }

        ApplyPresentation(provider.BuildInfoPanelPresentation(), openPanel: true);
    }

    public void ShowPresentation(InfoPanelPresentation presentation)
    {
        ApplyPresentation(presentation, openPanel: true);
    }

    public void ShowMonster(Monster monster)
    {
        if (monster == null)
        {
            Show(new InfoPanelData { title = "Unknown Monster", body = "No monster data was provided.", image = null });
            return;
        }

        _mode = Mode.Monster;
        _currentMonster = monster;
        _currentHero = null;
        _currentItem = null;

        ApplyPresentation(InfoPanelContentFactory.BuildMonsterPresentation(monster), openPanel: true);
    }

    public void ShowMonster(Monster monster, InfoPanelData dataFromContent)
    {
        if (monster == null)
        {
            Show(dataFromContent);
            return;
        }

        _mode = Mode.Monster;
        _currentMonster = monster;
        _currentHero = null;
        _currentItem = null;

        ApplyPresentation(InfoPanelContentFactory.BuildMonsterPresentation(monster, dataFromContent), openPanel: true);
    }

    public void ShowHero(HeroStats hero)
    {
        if (hero == null)
        {
            Show(new InfoPanelData { title = "Unknown Hero", body = "No hero data was provided.", image = null });
            return;
        }

        _mode = Mode.Hero;
        _currentHero = hero;
        _currentMonster = null;
        _currentItem = null;

        ApplyPresentation(InfoPanelContentFactory.BuildHeroPresentation(hero), openPanel: true);
    }

    public void ShowHero(HeroStats hero, InfoPanelData dataFromContent)
    {
        if (hero == null)
        {
            Show(dataFromContent);
            return;
        }

        _mode = Mode.Hero;
        _currentHero = hero;
        _currentMonster = null;
        _currentItem = null;

        ApplyPresentation(InfoPanelContentFactory.BuildHeroPresentation(hero, dataFromContent), openPanel: true);
    }

    public void ShowItem(ItemSO item)
    {
        if (item == null)
        {
            Show(new InfoPanelData { title = "Unknown Item", body = "No item data was provided.", image = null });
            return;
        }

        _mode = Mode.Item;
        _currentItem = item;
        _currentMonster = null;
        _currentHero = null;

        ApplyPresentation(InfoPanelContentFactory.BuildItemPresentation(item), openPanel: true);
    }

    public void ShowItem(ItemSO item, InfoPanelData dataFromContent)
    {
        if (item == null)
        {
            Show(dataFromContent);
            return;
        }

        _mode = Mode.Item;
        _currentItem = item;
        _currentMonster = null;
        _currentHero = null;

        ApplyPresentation(InfoPanelContentFactory.BuildItemPresentation(item, dataFromContent), openPanel: true);
    }

    public void Open()
    {
        if (infoPanelRoot == null)
            infoPanelRoot = gameObject;

        if (partyHUD != null && !partyHUD.NotifyPanelOpened(PartyHUD.UIPanelType.InfoPanel))
        {
            if (logFlow)
                Debug.Log($"{TAG} Open blocked by PartyHUD single-panel lock.", this);
            return;
        }

        if (!IsOpen)
        {
            infoPanelRoot.SetActive(true);
            reelDisableManager?.DisableReels();
            CachePanelRectAndInstallBackgroundFilter();

            if (logFlow)
                Debug.Log($"{TAG} Open", this);
        }
    }

    public void Close()
    {
        if (infoPanelRoot == null)
            infoPanelRoot = gameObject;

        if (!IsOpen)
            return;

        infoPanelRoot.SetActive(false);
        reelDisableManager?.EnableReels();
        partyHUD?.NotifyPanelClosed(PartyHUD.UIPanelType.InfoPanel);

        if (monsterStatusTabUI != null)
            monsterStatusTabUI.ShowForMonster(null);

        if (logFlow)
            Debug.Log($"{TAG} Close", this);
    }

    private void ApplyPresentation(InfoPanelPresentation presentation, bool openPanel)
    {
        if (presentation == null)
            presentation = InfoPanelContentFactory.BuildGenericPresentation("Info", "No presentation data was provided.");

        _lastPresentation = presentation;
        ShowCore(presentation.Info);

        bool showInfoTab = presentation.ShowInfoTab;
        bool showReelTab = presentation.ShowReelTab && monsterReelTabRoot != null && monsterReelPanelUI != null && _currentMonster != null && MonsterReelPanelUI.HasDisplayableReelStrip(_currentMonster);
        bool showStatusTab = presentation.ShowStatusTab && statusTabRoot != null;

        ApplyTabVisibility(showInfoTab, showReelTab, showStatusTab, $"ApplyPresentation({presentation.SubjectKind})");

        ActiveTab desiredDefault = ActiveTab.Info;
        if (presentation.DefaultTab == InfoPanelDefaultTab.Reel && showReelTab)
            desiredDefault = ActiveTab.Reel;
        else if (presentation.DefaultTab == InfoPanelDefaultTab.Status && showStatusTab)
            desiredDefault = ActiveTab.Status;

        SetActiveTab(desiredDefault);

        if (openPanel)
            Open();
    }

    private void ShowCore(InfoPanelData data)
    {
        if (titleText != null)
            titleText.text = data.title ?? string.Empty;

        if (bodyText != null)
            bodyText.text = data.body ?? string.Empty;

        if (iconImage != null)
        {
            iconImage.sprite = data.image;
            iconImage.enabled = data.image != null;
        }
    }

    private void OnBackgroundClicked() => Close();
    private void OnInfoTabClicked() => SetActiveTab(ActiveTab.Info);
    private void OnReelTabClicked() => SetActiveTab(ActiveTab.Reel);
    private void OnStatusTabClicked() => SetActiveTab(ActiveTab.Status);

    private void SetActiveTab(ActiveTab tab)
    {
        bool canShowReel = _lastPresentation != null && _lastPresentation.ShowReelTab && monsterReelTabRoot != null && monsterReelPanelUI != null && _currentMonster != null && MonsterReelPanelUI.HasDisplayableReelStrip(_currentMonster);
        bool canShowStatus = _lastPresentation != null && _lastPresentation.ShowStatusTab && statusTabRoot != null;

        if (tab == ActiveTab.Reel && !canShowReel)
            tab = ActiveTab.Info;
        if (tab == ActiveTab.Status && !canShowStatus)
            tab = ActiveTab.Info;

        _activeTab = tab;

        if (infoTabRoot != null)
            infoTabRoot.SetActive(tab == ActiveTab.Info);

        if (monsterReelTabRoot != null)
            monsterReelTabRoot.SetActive(tab == ActiveTab.Reel && canShowReel);

        if (statusTabRoot != null)
            statusTabRoot.SetActive(tab == ActiveTab.Status && canShowStatus);

        if (tab == ActiveTab.Reel && canShowReel)
            monsterReelPanelUI.ShowForMonster(_currentMonster);

        if (monsterStatusTabUI != null)
        {
            if (tab == ActiveTab.Status && canShowStatus && _currentMonster != null)
                monsterStatusTabUI.ShowForMonster(_currentMonster);
            else
                monsterStatusTabUI.ShowForMonster(null);
        }
    }

    private void ApplyTabVisibility(bool infoEnabled, bool reelEnabled, bool statusEnabled, string caller)
    {
        bool anyTabsEnabled = infoEnabled || reelEnabled || statusEnabled;

        if (tabBarRoot != null)
            tabBarRoot.SetActive(anyTabsEnabled);

        if (infoTabButton != null)
            infoTabButton.gameObject.SetActive(infoEnabled);

        if (reelTabButton != null)
            reelTabButton.gameObject.SetActive(reelEnabled);

        if (statusTabButton != null)
            statusTabButton.gameObject.SetActive(statusEnabled);

        if (logFlow)
            Debug.Log($"{TAG} ApplyTabVisibility caller={caller} info={infoEnabled} reel={reelEnabled} status={statusEnabled}", this);

        if (!anyTabsEnabled)
        {
            if (infoTabRoot != null) infoTabRoot.SetActive(true);
            if (monsterReelTabRoot != null) monsterReelTabRoot.SetActive(false);
            if (statusTabRoot != null) statusTabRoot.SetActive(false);
        }
    }

    private void ShowInfoTab() => SetActiveTab(ActiveTab.Info);

    private void DisableRaycastTargetsOnContent()
    {
        if (titleText != null) titleText.raycastTarget = false;
        if (bodyText != null) bodyText.raycastTarget = false;
        if (iconImage != null) iconImage.raycastTarget = false;

        if (infoTabRoot == null)
            return;

        Transform tabBarTransform = tabBarRoot != null ? tabBarRoot.transform : null;

        foreach (TMP_Text tmp in infoTabRoot.GetComponentsInChildren<TMP_Text>(true))
        {
            if (tabBarTransform != null && tmp.transform.IsChildOf(tabBarTransform))
                continue;
            tmp.raycastTarget = false;
        }

        foreach (Image image in infoTabRoot.GetComponentsInChildren<Image>(true))
        {
            if (tabBarTransform != null && image.transform.IsChildOf(tabBarTransform))
                continue;
            if (image.GetComponentInParent<Button>() != null)
                continue;
            image.raycastTarget = false;
        }
    }

    private void CachePanelRectAndInstallBackgroundFilter()
    {
        if (tabBarRoot != null && tabBarRoot.transform.parent != null)
            _panelRect = tabBarRoot.transform.parent as RectTransform;

        if (_panelRect == null && infoPanelRoot != null)
        {
            Transform panelTransform = infoPanelRoot.transform.Find("BG/Panel");
            if (panelTransform != null)
                _panelRect = panelTransform as RectTransform;
        }

        if (!preventBackgroundRaycastsOverPanel || backgroundButton == null || _panelRect == null)
            return;

        InfoPanelBackgroundRaycastFilter filter = backgroundButton.GetComponent<InfoPanelBackgroundRaycastFilter>();
        if (filter == null)
            filter = backgroundButton.gameObject.AddComponent<InfoPanelBackgroundRaycastFilter>();

        filter.SetPanelRect(_panelRect);
    }

    private void DumpTopRaycastHits(string reason)
    {
        if (_raycaster == null || EventSystem.current == null)
            return;

        PointerEventData ped = new PointerEventData(EventSystem.current)
        {
            position = Input.mousePosition
        };

        List<RaycastResult> results = new List<RaycastResult>();
        _raycaster.Raycast(ped, results);

        StringBuilder sb = new StringBuilder();
        sb.Append($"{TAG} Raycast dump ({reason}) at {ped.position}: hits={results.Count}\n");
        for (int i = 0; i < Mathf.Min(results.Count, 12); i++)
        {
            RaycastResult result = results[i];
            Graphic g = result.gameObject != null ? result.gameObject.GetComponent<Graphic>() : null;
            bool raycastTarget = g != null && g.raycastTarget;
            sb.Append($"  {i:00}: {GetPath(result.gameObject)} depth={result.depth} sortingLayer={result.sortingLayer} sortingOrder={result.sortingOrder} raycastTarget={raycastTarget}\n");
        }

        Debug.Log(sb.ToString(), this);
    }

    private static string GetPath(GameObject go)
    {
        if (go == null)
            return "<null>";

        Transform t = go.transform;
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }

        return path;
    }
}

public class InfoPanelBackgroundRaycastFilter : MonoBehaviour, ICanvasRaycastFilter
{
    private RectTransform _panelRect;

    public void SetPanelRect(RectTransform panelRect)
    {
        _panelRect = panelRect;
    }

    public bool IsRaycastLocationValid(Vector2 sp, Camera eventCamera)
    {
        if (_panelRect == null)
            return true;

        bool overPanel = RectTransformUtility.RectangleContainsScreenPoint(_panelRect, sp, eventCamera);
        return !overPanel;
    }
}
