using System.Collections;
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
/// - Works with the existing Monster Reel / Abilities tab if wired.
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

    [Tooltip("Optional in-panel close button. If left null, we auto-find common names.")]
    [SerializeField] private Button closeButton;

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
    [SerializeField] private Button extraTabButton;

    [Header("Tab Roots")]
    [Tooltip("Root containing the shared generic info content.")]
    [SerializeField] private GameObject infoTabRoot;
    [Tooltip("Root containing the shared abilities / reel UI.")]
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


    [Header("Rendering")]
    [Tooltip("If true, the info panel root gets its own nested Canvas so it renders above world-space combat and grid visuals.")]
    [SerializeField] private bool useDedicatedPanelCanvas = true;

    [Tooltip("Sorting layer used by the dedicated info panel canvas.")]
    [SerializeField] private string dedicatedCanvasSortingLayerName = "UI";

    [Tooltip("Sorting order used by the dedicated info panel canvas.")]
    [SerializeField] private int dedicatedCanvasSortingOrder = 5100;

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
    private Coroutine _reelTabRefreshRoutine;

    private void Awake()
    {
        if (infoPanelRoot == null)
            infoPanelRoot = gameObject;

        if (partyHUD == null)
            partyHUD = FindFirstObjectByType<PartyHUD>(FindObjectsInactive.Include);

        AutoWireButtons();
        AutoWirePresenters();

        WireButtonListeners();

        EnsureDedicatedPanelCanvas();
        _raycaster = GetComponentInParent<GraphicRaycaster>();
        CachePanelRectAndInstallBackgroundFilter();

        if (disableContentRaycastTargets)
            DisableRaycastTargetsOnContent();

        if (infoPanelRoot != null)
            infoPanelRoot.SetActive(false);

        ApplyPresentation(InfoPanelContentFactory.BuildGenericPresentation("Info", "Select something to inspect."), openPanel: false);
        ShowInfoTab();
    }


    private void EnsureDedicatedPanelCanvas()
    {
        if (!useDedicatedPanelCanvas)
            return;

        if (infoPanelRoot == null)
            infoPanelRoot = gameObject;

        EnsureDedicatedCanvasOnTarget(infoPanelRoot, "panel-root", dedicatedCanvasSortingOrder);

        if (infoPanelRoot != gameObject)
            EnsureDedicatedCanvasOnTarget(gameObject, "controller-root", Mathf.Max(0, dedicatedCanvasSortingOrder - 1));
    }

    private void EnsureDedicatedCanvasOnTarget(GameObject target, string label, int sortingOrder)
    {
        if (target == null)
        {
            if (logFlow)
                Debug.LogWarning($"{TAG} Dedicated canvas skipped for {label}: target is NULL.", this);
            return;
        }

        Canvas parentCanvas = target.transform.parent != null ? target.transform.parent.GetComponentInParent<Canvas>(true) : GetComponentInParent<Canvas>(true);
        Canvas ownCanvas = target.GetComponent<Canvas>();
        bool addedCanvas = false;
        if (ownCanvas == null)
        {
            ownCanvas = target.AddComponent<Canvas>();
            addedCanvas = true;
        }

        ownCanvas.overrideSorting = true;
        if (!string.IsNullOrWhiteSpace(dedicatedCanvasSortingLayerName))
            ownCanvas.sortingLayerName = dedicatedCanvasSortingLayerName;
        ownCanvas.sortingOrder = sortingOrder;

        if (parentCanvas != null)
        {
            ownCanvas.renderMode = parentCanvas.renderMode;
            ownCanvas.worldCamera = parentCanvas.worldCamera;
            ownCanvas.planeDistance = parentCanvas.planeDistance;
            ownCanvas.pixelPerfect = parentCanvas.pixelPerfect;
            ownCanvas.additionalShaderChannels = parentCanvas.additionalShaderChannels;
        }
        else
        {
            ownCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }

        GraphicRaycaster raycaster = target.GetComponent<GraphicRaycaster>();
        bool addedRaycaster = false;
        if (raycaster == null)
        {
            raycaster = target.AddComponent<GraphicRaycaster>();
            addedRaycaster = true;
        }

        if (ReferenceEquals(target, infoPanelRoot))
            _raycaster = raycaster;

        Canvas[] localCanvases = target.GetComponents<Canvas>();
        if (logFlow)
        {
            Debug.Log(
                $"{TAG} Dedicated canvas ensured on '{target.name}' ({label}) | addedCanvas={addedCanvas} addedRaycaster={addedRaycaster} activeSelf={target.activeSelf} activeInHierarchy={target.activeInHierarchy} parentCanvas={(parentCanvas != null ? parentCanvas.name : "<none>")} renderMode={ownCanvas.renderMode} worldCamera={(ownCanvas.worldCamera != null ? ownCanvas.worldCamera.name : "<none>")} layer={ownCanvas.sortingLayerName} order={ownCanvas.sortingOrder} localCanvasCount={localCanvases.Length}",
                target);
        }
    }

    private void OnEnable()
    {
        AutoWireButtons();
        AutoWirePresenters();
        WireButtonListeners();
        EnsureDedicatedPanelCanvas();
    }

    private void OnDestroy()
    {
        if (_reelTabRefreshRoutine != null) StopCoroutine(_reelTabRefreshRoutine);
        if (backgroundButton != null)
            backgroundButton.onClick.RemoveListener(OnBackgroundClicked);
        if (closeButton != null)
            closeButton.onClick.RemoveListener(OnCloseButtonClicked);
        if (infoTabButton != null)
            infoTabButton.onClick.RemoveListener(OnInfoTabClicked);
        if (reelTabButton != null)
            reelTabButton.onClick.RemoveListener(OnReelTabClicked);
        if (statusTabButton != null)
            statusTabButton.onClick.RemoveListener(OnStatusTabClicked);
        if (extraTabButton != null)
            extraTabButton.onClick.RemoveAllListeners();
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
            EnsureDedicatedPanelCanvas();
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

        if (monsterReelPanelUI != null)
            monsterReelPanelUI.ClearCurrentSelection();

        if (logFlow)
            Debug.Log($"{TAG} Close", this);
    }

    public void ClosePanel() => Close();
    public void HidePanel() => Close();
    public void ShowInfoTabFromButton() => SetActiveTab(ActiveTab.Info);
    public void ShowReelTabFromButton() => SetActiveTab(ActiveTab.Reel);
    public void ShowStatusTabFromButton() => SetActiveTab(ActiveTab.Status);

    public void RewireButtonsNow()
    {
        AutoWireButtons();
        AutoWirePresenters();
        WireButtonListeners();
        EnsureDedicatedPanelCanvas();
    }

    private void ApplyPresentation(InfoPanelPresentation presentation, bool openPanel)
    {
        if (presentation == null)
            presentation = InfoPanelContentFactory.BuildGenericPresentation("Info", "No presentation data was provided.");

        _lastPresentation = presentation;
        ShowCore(presentation.Info);

        bool showInfoTab = presentation.ShowInfoTab;
        bool showReelTab = presentation.ShowReelTab && CanShowReelTabForCurrentSubject();

        // Temporarily force Status off so we can isolate and validate the Reel tab path.
        bool showStatusTab = false;

        ApplyTabVisibility(showInfoTab, showReelTab, showStatusTab, $"ApplyPresentation({presentation.SubjectKind})");

        // Temporarily prefer Reel whenever it is available so the panel opens directly
        // into the monster abilities/reel view for debugging.
        ActiveTab desiredDefault = showReelTab ? ActiveTab.Reel : ActiveTab.Info;

        if (logFlow)
            Debug.Log($"{TAG} ApplyPresentation defaultTab={desiredDefault} showInfoTab={showInfoTab} showReelTab={showReelTab} showStatusTab={showStatusTab}", this);

        SetActiveTab(desiredDefault);

        if (openPanel)
            Open();
    }

    private bool CanShowReelTabForCurrentSubject()
    {
        if (monsterReelTabRoot == null || monsterReelPanelUI == null)
            return false;

        switch (_mode)
        {
            case Mode.Monster:
                return MonsterReelPanelUI.HasDisplayableReelStrip(_currentMonster);
            case Mode.Hero:
                return MonsterReelPanelUI.HasDisplayableHeroAbilities(_currentHero);
            default:
                return false;
        }
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
    private void OnCloseButtonClicked() => Close();

    private void OnInfoTabClicked()
    {
        if (logFlow)
            Debug.Log($"{TAG} Info tab click", this);
        SetActiveTab(ActiveTab.Info);
    }

    private void OnReelTabClicked()
    {
        if (logFlow)
            Debug.Log($"{TAG} Reel tab click", this);
        SetActiveTab(ActiveTab.Reel);
    }

    private void OnStatusTabClicked()
    {
        if (logFlow)
            Debug.Log($"{TAG} Status tab click", this);
        SetActiveTab(ActiveTab.Status);
    }

    private void SetActiveTab(ActiveTab tab)
    {
        AutoWirePresenters();

        ActiveTab requestedTab = tab;
        bool canShowReel = _lastPresentation != null && _lastPresentation.ShowReelTab && CanShowReelTabForCurrentSubject();
        bool canShowStatus = _lastPresentation != null && _lastPresentation.ShowStatusTab && statusTabRoot != null && _currentMonster != null;

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

        if (logFlow)
        {
            Debug.Log($"{TAG} SetActiveTab requested={requestedTab} actual={tab} mode={_mode} canShowReel={canShowReel} canShowStatus={canShowStatus} monster={(_currentMonster != null ? _currentMonster.name : "<null>")} hero={(_currentHero != null ? _currentHero.name : "<null>")}", this);
            Debug.Log($"{TAG} TAB ROOT STATES => InfoRoot={(infoTabRoot != null && infoTabRoot.activeSelf)} | ReelRoot={(monsterReelTabRoot != null && monsterReelTabRoot.activeSelf)} | StatusRoot={(statusTabRoot != null && statusTabRoot.activeSelf)}", this);
            Debug.Log($"{TAG} TAB BUTTON STATES => InfoBtn={(infoTabButton != null && infoTabButton.gameObject.activeSelf)} | ReelBtn={(reelTabButton != null && reelTabButton.gameObject.activeSelf)} | StatusBtn={(statusTabButton != null && statusTabButton.gameObject.activeSelf)} | ExtraBtn={(extraTabButton != null && extraTabButton.gameObject.activeSelf)}", this);
        }

        if (tab == ActiveTab.Reel && canShowReel && monsterReelPanelUI != null)
        {
            if (logFlow)
                Debug.Log($"{TAG} ReelPanelUI state before init => activeSelf={monsterReelPanelUI.gameObject.activeSelf} inHierarchy={monsterReelPanelUI.gameObject.activeInHierarchy} reelRootActive={(monsterReelTabRoot != null && monsterReelTabRoot.activeInHierarchy)}", monsterReelPanelUI);

            monsterReelPanelUI.gameObject.SetActive(true);

            if (logFlow)
                Debug.Log($"{TAG} ReelPanelUI state after SetActive(true) => activeSelf={monsterReelPanelUI.gameObject.activeSelf} inHierarchy={monsterReelPanelUI.gameObject.activeInHierarchy}", monsterReelPanelUI);

            if (_mode == Mode.Monster)
            {
                if (logFlow)
                    Debug.Log($"{TAG} Initializing reel tab for monster '{(_currentMonster != null ? _currentMonster.name : "<null>")}'", this);
                monsterReelPanelUI.ShowForMonster(_currentMonster);
            }
            else if (_mode == Mode.Hero)
            {
                if (logFlow)
                    Debug.Log($"{TAG} Initializing reel tab for hero '{(_currentHero != null ? _currentHero.name : "<null>")}'", this);
                monsterReelPanelUI.ShowForHero(_currentHero);
            }

            StartDeferredReelRefresh();
        }
        else if (tab != ActiveTab.Reel && monsterReelPanelUI != null)
        {
            monsterReelPanelUI.ClearCurrentSelection();
        }

        if (monsterStatusTabUI != null)
        {
            if (tab == ActiveTab.Status && canShowStatus && _currentMonster != null)
                monsterStatusTabUI.ShowForMonster(_currentMonster);
            else
                monsterStatusTabUI.ShowForMonster(null);
        }
    }


    private void StartDeferredReelRefresh()
    {
        if (_reelTabRefreshRoutine != null)
            StopCoroutine(_reelTabRefreshRoutine);

        _reelTabRefreshRoutine = StartCoroutine(DeferredReelRefresh());
    }

    private IEnumerator DeferredReelRefresh()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();
        yield return null;

        if (_activeTab != ActiveTab.Reel || monsterReelPanelUI == null)
            yield break;

        if (logFlow)
            Debug.Log($"{TAG} Deferred reel refresh running. reelRootActive={(monsterReelTabRoot != null && monsterReelTabRoot.activeInHierarchy)} panelActive={monsterReelPanelUI.gameObject.activeInHierarchy}", monsterReelPanelUI);

        monsterReelPanelUI.gameObject.SetActive(true);
        monsterReelPanelUI.RefreshCurrentSubject();
        monsterReelPanelUI.ForceRebuildVisibleState();

        if (logFlow)
            Debug.Log($"{TAG} Deferred reel refresh finished.", monsterReelPanelUI);

        _reelTabRefreshRoutine = null;
    }

    private void ApplyTabVisibility(bool infoEnabled, bool reelEnabled, bool statusEnabled, string caller)
    {
        bool anyTabsEnabled = infoEnabled || reelEnabled || statusEnabled;

        if (tabBarRoot != null)
            tabBarRoot.SetActive(anyTabsEnabled);

        SetTabButtonVisible(infoTabButton, infoEnabled);
        SetTabButtonVisible(reelTabButton, reelEnabled);
        SetTabButtonVisible(statusTabButton, statusEnabled);
        SetTabButtonVisible(extraTabButton, false);

        if (logFlow)
            Debug.Log($"{TAG} ApplyTabVisibility caller={caller} info={infoEnabled} reel={reelEnabled} status={statusEnabled} extra={false}", this);

        if (!anyTabsEnabled)
        {
            if (infoTabRoot != null) infoTabRoot.SetActive(true);
            if (monsterReelTabRoot != null) monsterReelTabRoot.SetActive(false);
            if (statusTabRoot != null) statusTabRoot.SetActive(false);
        }
    }

    private void SetTabButtonVisible(Button button, bool visible)
    {
        if (button == null)
            return;

        GameObject go = button.gameObject;
        go.SetActive(visible);
        button.interactable = visible;

        CanvasGroup cg = go.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.alpha = visible ? 1f : 0f;
            cg.interactable = visible;
            cg.blocksRaycasts = visible;
        }

        Graphic[] graphics = go.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
            graphics[i].raycastTarget = visible;
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

    private void AutoWireButtons()
    {
        if (infoPanelRoot == null)
            infoPanelRoot = gameObject;

        if (backgroundButton == null && infoPanelRoot != null)
        {
            Transform bg = infoPanelRoot.transform.Find("BG");
            if (bg != null)
            {
                backgroundButton = bg.GetComponent<Button>();
                if (backgroundButton == null)
                {
                    Transform bgButtonChild = bg.Find("Button");
                    if (bgButtonChild != null)
                        backgroundButton = bgButtonChild.GetComponent<Button>();
                }

                if (backgroundButton == null)
                    backgroundButton = bg.GetComponentInChildren<Button>(true);
            }
        }

        Transform panel = infoPanelRoot != null ? infoPanelRoot.transform.Find("BG/Panel") : null;
        Transform tabBar = tabBarRoot != null ? tabBarRoot.transform : (panel != null ? FindChildByTrimmedName(panel, "TabBarRoot") : null);

        if (tabBarRoot == null && tabBar != null)
            tabBarRoot = tabBar.gameObject;

        if (infoTabButton == null && tabBar != null)
            infoTabButton = FindButtonByName(tabBar, "InfoTabButton") ?? FindButtonByName(tabBar, "Info Tab Button");

        if (reelTabButton == null && tabBar != null)
            reelTabButton = FindButtonByName(tabBar, "ReelTabButton") ?? FindButtonByName(tabBar, "AbilitiesTabButton") ?? FindButtonByName(tabBar, "AbilityTabButton");

        if (statusTabButton == null && tabBar != null)
            statusTabButton = FindButtonByName(tabBar, "StatusTabButton") ?? FindButtonByName(tabBar, "Status Tab Button");

        if (extraTabButton == null && tabBar != null)
            extraTabButton = FindButtonByName(tabBar, "ExtraTabButton") ?? FindButtonByName(tabBar, "Extra Tab Button");

        if (closeButton == null && infoPanelRoot != null)
        {
            closeButton = FindButtonByName(infoPanelRoot.transform, "CloseButton")
                       ?? FindButtonByName(infoPanelRoot.transform, "Close Button")
                       ?? FindButtonByName(infoPanelRoot.transform, "Close")
                       ?? FindButtonByName(infoPanelRoot.transform, "XButton");
        }
    }

    private void WireButtonListeners()
    {
        if (backgroundButton != null)
        {
            backgroundButton.onClick.RemoveListener(OnBackgroundClicked);
            backgroundButton.onClick.AddListener(OnBackgroundClicked);
        }

        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(OnCloseButtonClicked);
            closeButton.onClick.AddListener(OnCloseButtonClicked);
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
    }

    private void AutoWirePresenters()
    {
        Transform panel = infoPanelRoot != null ? infoPanelRoot.transform.Find("BG/Panel") : null;

        if (infoTabRoot == null && panel != null)
        {
            Transform t = FindChildByTrimmedName(panel, "InfoTabRoot");
            if (t != null) infoTabRoot = t.gameObject;
        }

        if (monsterReelTabRoot == null && panel != null)
        {
            Transform t = FindChildByTrimmedName(panel, "MonsterReelTabRoot");
            if (t != null) monsterReelTabRoot = t.gameObject;
        }

        if (statusTabRoot == null && panel != null)
        {
            Transform t = FindChildByTrimmedName(panel, "StatusTabRoot");
            if (t != null) statusTabRoot = t.gameObject;
        }

        if (monsterReelPanelUI == null && monsterReelTabRoot != null)
            monsterReelPanelUI = monsterReelTabRoot.GetComponentInChildren<MonsterReelPanelUI>(true);

        if (monsterStatusTabUI == null && statusTabRoot != null)
            monsterStatusTabUI = statusTabRoot.GetComponentInChildren<MonsterStatusTabUI>(true);
    }

    private static Button FindButtonByName(Transform root, string childName)
    {
        if (root == null || string.IsNullOrWhiteSpace(childName))
            return null;

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t != null && string.Equals(t.name.Trim(), childName.Trim(), System.StringComparison.Ordinal))
                return t.GetComponent<Button>();
        }

        return null;
    }

    private static Transform FindChildByTrimmedName(Transform root, string childName)
    {
        if (root == null || string.IsNullOrWhiteSpace(childName))
            return null;

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t != null && string.Equals(t.name.Trim(), childName.Trim(), System.StringComparison.Ordinal))
                return t;
        }

        return null;
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

////////////////////////////////////////////////////////////


////////////////////////////////////////////////////////////

////////////////////////////////////////////////////////////
