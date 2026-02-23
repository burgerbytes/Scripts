using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// Controls the in-game InfoPanel popover (BG + Panel).
///
/// ONE place that "panel-like" UI should be displayed.
/// Supports:
/// - Generic InfoPanelData (title/body/icon)
/// - Monster inspection with an optional Monster Reel tab (MonsterReelPanelUI)
///
/// Obeys PartyHUD single-panel locking so InfoPanel cannot open while another panel is open.
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

    [Header("Optional: Tab UI")]
    [Tooltip("Optional root containing tab buttons. If null, tabs are ignored.")]
    [SerializeField] private GameObject tabBarRoot;

    [Tooltip("Tab button that shows the generic info content.")]
    [SerializeField] private Button infoTabButton;

    [Tooltip("Tab button that shows the monster reel content.")]
    [SerializeField] private Button reelTabButton;

    [Tooltip("Root containing the generic title/body/icon UI. If null, uses the fields above directly.")]
    [SerializeField] private GameObject infoTabRoot;

    [Tooltip("Root containing the monster reel UI.")]
    [SerializeField] private GameObject monsterReelTabRoot;

    [Tooltip("Monster reel presenter (lives under monsterReelTabRoot).")]
    [SerializeField] private MonsterReelPanelUI monsterReelPanelUI;

    [Header("Debug")]
    [SerializeField] private bool logFlow = false;

    [Header("Optional")]
    [Tooltip("If assigned, reels will be disabled while the InfoPanel is open.")]
    [SerializeField] private ReelDisableManager reelDisableManager;

    [Header("Raycast Safety")]
    [Tooltip("If true, BG will NOT receive raycasts over the Panel area (prevents BG from blocking tab clicks).")]
    [SerializeField] private bool preventBackgroundRaycastsOverPanel = true;

    [Tooltip("If true and logFlow is enabled, clicking will dump the top UI raycast hits to the console.")]
    [SerializeField] private bool debugRaycastOnClick = true;

    [Tooltip("If true, non-interactive content graphics (title/body/icon) will have Raycast Target disabled so they don't block tab button clicks.")]
    [SerializeField] private bool disableContentRaycastTargets = true;

    public bool IsOpen => (infoPanelRoot != null ? infoPanelRoot.activeInHierarchy : gameObject.activeInHierarchy);

    private Monster _currentMonster;
    private HeroStats _currentHero;

    private enum Mode
    {
        Generic,
        Monster,
        Hero
    }

    private Mode _mode = Mode.Generic;

    // Cached rect we treat as "the panel area" where BG should NOT get clicks.
    private RectTransform _panelRect;
    private GraphicRaycaster _raycaster;

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

        // Tabs (optional)
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

        // Cache a GraphicRaycaster for debugging raycast stacks (same Canvas as this controller, ideally).
        _raycaster = GetComponentInParent<GraphicRaycaster>();

        CachePanelRectAndInstallBackgroundFilter();

        if (disableContentRaycastTargets)
            DisableRaycastTargetsOnContent();

        // Start closed.
        if (infoPanelRoot != null)
            infoPanelRoot.SetActive(false);

        // Default: no tabs visible (generic mode), and show the info root safely.
        ApplyTabVisibility(false, false, "Awake(default)");
        ShowInfoTab(); // safe default
    }

    private void DisableRaycastTargetsOnContent()
    {
        // IMPORTANT: If any large TMP_Text/Image (ex: Title) overlaps the TabBar,
        // it can become the top raycast hit and prevent buttons from receiving clicks.
        // These are display-only, so make them ignore raycasts.

        if (titleText != null)
            titleText.raycastTarget = false;

        if (bodyText != null)
            bodyText.raycastTarget = false;

        if (iconImage != null)
            iconImage.raycastTarget = false;

        // Also disable on any TMP/Image under infoTabRoot EXCEPT the tab bar subtree.
        if (infoTabRoot != null)
        {
            var tabBarT = tabBarRoot != null ? tabBarRoot.transform : null;

            foreach (var tmp in infoTabRoot.GetComponentsInChildren<TMP_Text>(true))
            {
                if (tabBarT != null && tmp.transform.IsChildOf(tabBarT)) continue;
                tmp.raycastTarget = false;
            }

            foreach (var img in infoTabRoot.GetComponentsInChildren<Image>(true))
            {
                if (tabBarT != null && img.transform.IsChildOf(tabBarT)) continue;

                // Leave anything under a Button alone (including its target graphic).
                if (img.GetComponentInParent<Button>() != null) continue;

                img.raycastTarget = false;
            }
        }
    }

    private void CachePanelRectAndInstallBackgroundFilter()
    {
        // Your hierarchy indicates TabBarRoot lives under BG/Panel/TabBarRoot.
        // So "Panel" is the parent of TabBarRoot.
        if (tabBarRoot != null)
        {
            var parent = tabBarRoot.transform.parent;
            if (parent != null)
                _panelRect = parent as RectTransform;
        }

        // Fallback: try to find BG/Panel directly
        if (_panelRect == null && infoPanelRoot != null)
        {
            var panelT = infoPanelRoot.transform.Find("BG/Panel");
            if (panelT != null)
                _panelRect = panelT as RectTransform;
        }

        if (preventBackgroundRaycastsOverPanel && backgroundButton != null && _panelRect != null)
        {
            var filter = backgroundButton.GetComponent<InfoPanelBackgroundRaycastFilter>();
            if (filter == null) filter = backgroundButton.gameObject.AddComponent<InfoPanelBackgroundRaycastFilter>();
            filter.SetPanelRect(_panelRect);
            if (logFlow) Debug.Log($"{TAG} Installed BG raycast filter to ignore clicks over Panel | panelPath={GetPath(_panelRect.gameObject)}", this);
        }
        else
        {
            if (logFlow)
            {
                Debug.Log($"{TAG} BG raycast filter NOT installed (preventBackgroundRaycastsOverPanel={preventBackgroundRaycastsOverPanel}, bg={(backgroundButton!=null)}, panelRect={(_panelRect!=null)})", this);
            }
        }
    }

    private void OnDestroy()
    {
        if (backgroundButton != null)
            backgroundButton.onClick.RemoveListener(OnBackgroundClicked);

        if (infoTabButton != null)
            infoTabButton.onClick.RemoveListener(OnInfoTabClicked);

        if (reelTabButton != null)
            reelTabButton.onClick.RemoveListener(OnReelTabClicked);
    }

    /// <summary>
    /// Generic show: title/body/icon only. No tabs.
    /// </summary>
    public void Show(InfoPanelData data)
    {
        _mode = Mode.Generic;
        _currentMonster = null;

        ShowCore(data);

        // Non-monster panels do not show tabs.
        ApplyTabVisibility(false, false, "Show(Generic)");
        ShowInfoTab();

        Open();
    }

    /// <summary>
    /// Convenience: show monster info using the monster's DisplayName/Description if available.
    /// </summary>

public void ShowHero(HeroStats hero)
{
    if (hero == null)
    {
        if (logFlow) Debug.LogWarning($"{TAG} ShowHero called with null hero. Falling back to generic.", this);
        Show(new InfoPanelData { title = "", body = "", image = null });
        return;
    }

    string body = BuildHeroBody(hero);
    ShowHero(hero, new InfoPanelData
    {
        title = hero.name,
        body = body,
        image = hero.Portrait
    });
}

public void ShowHero(HeroStats hero, InfoPanelData dataFromContent)
{
    if (hero == null)
    {
        if (logFlow) Debug.LogWarning($"{TAG} ShowHero(hero,data) called with null hero. Falling back to generic.", this);
        Show(dataFromContent);
        return;
    }

    _mode = Mode.Hero;
    _currentHero = hero;
    _currentMonster = null;

    // Hero inspection does not use the Reel tab.
    ApplyTabVisibility(false, false, "ShowHero");

    // Ensure the monster reel root is hidden if it exists.
    if (monsterReelTabRoot != null)
        monsterReelTabRoot.SetActive(false);

    // Show the core info and open.
    ShowCore(dataFromContent);
    Open();
}

private string BuildHeroBody(HeroStats hero)
{
    if (hero == null) return "";

    // Keep this fairly short—this panel is meant to be glanceable on mobile.
    // You can expand this later (traits, passives, equipment, etc.).
    string className = "";
    try
    {
        var classDef = hero.AdvancedClassDef != null ? hero.AdvancedClassDef : hero.BaseClassDef;
        if (classDef != null) className = classDef.className;
    }
    catch { /* ignore */ }

    System.Text.StringBuilder sb = new System.Text.StringBuilder();

    if (!string.IsNullOrWhiteSpace(className))
        sb.AppendLine(className);

    sb.AppendLine($"Lv {hero.Level}");
    sb.AppendLine($"HP {hero.CurrentHp}/{hero.MaxHp}  SHD {hero.Shield}");
    sb.AppendLine($"STA {hero.CurrentStamina:0}/{hero.MaxStamina}");
    sb.AppendLine($"ATK {hero.Attack}  DEF {hero.Defense}  SPD {hero.Speed}");
    sb.AppendLine($"G {hero.Gold}");

    return sb.ToString().Trim();
}

    public void ShowMonster(Monster monster)
    {
        if (monster == null)
        {
            Debug.LogWarning($"{TAG} ShowMonster called with null monster. Falling back to generic.", this);
            Show(new InfoPanelData { title = "Unknown", body = "", image = null });
            return;
        }

        InfoPanelData d = new InfoPanelData
        {
            title = !string.IsNullOrWhiteSpace(monster.DisplayName) ? monster.DisplayName : monster.name,
            body = !string.IsNullOrWhiteSpace(monster.Description) ? monster.Description : "",
            image = null
        };

        ShowMonster(monster, d);
    }

    /// <summary>
    /// Monster show: allows tabbing between info + monster reel (if wired).
    /// Monsters ALWAYS show the tab bar (Info tab always available),
    /// but the Reel tab only appears if reel UI is correctly wired.
    /// </summary>
    public void ShowMonster(Monster monster, InfoPanelData dataFromContent)
    {
        _mode = Mode.Monster;
        _currentMonster = monster;

        // Build a reasonable default if InfoPanelContent didn't provide anything.
        InfoPanelData d = dataFromContent;

        if (string.IsNullOrWhiteSpace(d.title) && monster != null && !string.IsNullOrWhiteSpace(monster.DisplayName))
            d.title = monster.DisplayName;

        if (string.IsNullOrWhiteSpace(d.body) && monster != null && !string.IsNullOrWhiteSpace(monster.Description))
            d.body = monster.Description;

        ShowCore(d);

        bool hasReelUI = (monsterReelTabRoot != null && monsterReelPanelUI != null);
        ApplyTabVisibility(true, hasReelUI, $"ShowMonster(hasReelUI={hasReelUI})");

        // Default to info tab.
        ShowInfoTab();

        Open();
    }

    private void ShowCore(InfoPanelData data)
    {
        if (titleText != null) titleText.text = data.title ?? "";
        if (bodyText != null) bodyText.text = data.body ?? "";

        if (iconImage != null)
        {
            iconImage.sprite = data.image;
            iconImage.enabled = (data.image != null);
        }
    }

    public void Open()
    {
        if (infoPanelRoot == null) infoPanelRoot = gameObject;

        // Respect PartyHUD panel locking.
        if (partyHUD != null && !partyHUD.NotifyPanelOpened(PartyHUD.UIPanelType.InfoPanel))
        {
            if (logFlow) Debug.Log($"{TAG} Open blocked by PartyHUD single-panel lock.", this);
            return;
        }

        if (!IsOpen)
        {
            infoPanelRoot.SetActive(true);
            reelDisableManager?.DisableReels();

            // Re-cache panel rect after activation (RectTransforms can be missing until enabled in some setups).
            CachePanelRectAndInstallBackgroundFilter();

            if (logFlow) Debug.Log($"{TAG} Open", this);
        }
    }

    public void Close()
    {
        if (infoPanelRoot == null) infoPanelRoot = gameObject;

        if (IsOpen)
        {
            infoPanelRoot.SetActive(false);
            reelDisableManager?.EnableReels();

            // Release the lock.
            partyHUD?.NotifyPanelClosed(PartyHUD.UIPanelType.InfoPanel);

            if (logFlow) Debug.Log($"{TAG} Close", this);
        }
    }

    private void OnBackgroundClicked()
    {
        if (logFlow) Debug.Log($"{TAG} BG CLICK -> close", this);
        Close();
    }

    private void OnInfoTabClicked() => ShowInfoTab();
    private void OnReelTabClicked() => ShowReelTab();

    /// <summary>
    /// Controls visibility of the tab bar and (optionally) the Reel tab.
    /// Adds detailed debug logs whenever TabBarRoot / buttons are enabled/disabled.
    /// </summary>
    private void ApplyTabVisibility(bool tabsEnabled, bool reelTabEnabled, string caller)
    {
        // ---- TAB BAR ROOT ----
        if (tabBarRoot != null)
        {
            bool before = tabBarRoot.activeSelf;
            tabBarRoot.SetActive(tabsEnabled);
            bool after = tabBarRoot.activeSelf;

            Debug.Log($"{TAG} TabBarRoot SetActive({tabsEnabled}) from '{caller}' | before={before} after={after} | path={GetPath(tabBarRoot)}",
                tabBarRoot);
        }
        else
        {
            Debug.LogWarning($"{TAG} tabBarRoot is NULL in ApplyTabVisibility from '{caller}'. Tabs will not display.", this);
        }

        // ---- TAB BUTTONS ----
        if (infoTabButton != null)
        {
            bool before = infoTabButton.gameObject.activeSelf;
            infoTabButton.gameObject.SetActive(tabsEnabled);
            bool after = infoTabButton.gameObject.activeSelf;

            if (before != after)
                Debug.Log($"{TAG} InfoTabButton SetActive({tabsEnabled}) from '{caller}' | before={before} after={after} | path={GetPath(infoTabButton.gameObject)}",
                    infoTabButton.gameObject);
        }

        if (reelTabButton != null)
        {
            bool desired = tabsEnabled && reelTabEnabled;

            bool before = reelTabButton.gameObject.activeSelf;
            reelTabButton.gameObject.SetActive(desired);
            bool after = reelTabButton.gameObject.activeSelf;

            if (before != after)
                Debug.Log($"{TAG} ReelTabButton SetActive({desired}) from '{caller}' (tabsEnabled={tabsEnabled}, reelTabEnabled={reelTabEnabled}) | before={before} after={after} | path={GetPath(reelTabButton.gameObject)}",
                    reelTabButton.gameObject);
        }

        // ---- SAFE BASELINE FOR CONTENT ROOTS ----
        if (!tabsEnabled)
        {
            if (monsterReelTabRoot != null) monsterReelTabRoot.SetActive(false);
            if (infoTabRoot != null) infoTabRoot.SetActive(true);
        }
        else
        {
            if (!reelTabEnabled && monsterReelTabRoot != null)
                monsterReelTabRoot.SetActive(false);
        }
    }

    private void ShowInfoTab()
    {
        if (infoTabRoot != null) infoTabRoot.SetActive(true);
        if (monsterReelTabRoot != null) monsterReelTabRoot.SetActive(false);
    }

    private void ShowReelTab()
    {
        // Only valid in monster mode + reel UI wired.
        if (_mode != Mode.Monster || _currentMonster == null || monsterReelTabRoot == null || monsterReelPanelUI == null)
        {
            ShowInfoTab();
            return;
        }

        if (infoTabRoot != null) infoTabRoot.SetActive(false);
        monsterReelTabRoot.SetActive(true);

        monsterReelPanelUI.ShowForMonster(_currentMonster);
    }

    private void OnDisable()
    {
        // Safety: if this GameObject gets disabled externally, make sure we don't leave the lock held.
        reelDisableManager?.EnableReels();
        if (partyHUD != null && partyHUD.GetCurrentOpenPanel() == PartyHUD.UIPanelType.InfoPanel)
            partyHUD.NotifyPanelClosed(PartyHUD.UIPanelType.InfoPanel);
    }

    private void Update()
    {
        if (!logFlow || !debugRaycastOnClick) return;
        if (!IsOpen) return;

        if (Input.GetMouseButtonDown(0))
        {
            DumpTopRaycastHits("MouseDown");
        }
    }

    private void DumpTopRaycastHits(string reason)
    {
        if (_raycaster == null)
        {
            Debug.LogWarning($"{TAG} No GraphicRaycaster found in parents, cannot dump raycast hits.", this);
            return;
        }

        if (EventSystem.current == null)
        {
            Debug.LogWarning($"{TAG} No EventSystem.current, cannot dump raycast hits.", this);
            return;
        }

        PointerEventData ped = new PointerEventData(EventSystem.current);
        ped.position = Input.mousePosition;

        var results = new List<RaycastResult>();
        _raycaster.Raycast(ped, results);

        int count = Mathf.Min(results.Count, 12);
        if (count == 0)
        {
            Debug.Log($"{TAG} Raycast dump ({reason}): NO HITS at {ped.position}", this);
            return;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append($"{TAG} Raycast dump ({reason}) at {ped.position}: hits={results.Count}\n");
        for (int i = 0; i < count; i++)
        {
            var r = results[i];
            var go = r.gameObject;
            var g = go != null ? go.GetComponent<Graphic>() : null;
            bool rt = g != null && g.raycastTarget;
            sb.Append($"  {i:00}: {GetPath(go)} depth={r.depth} sortingLayer={r.sortingLayer} sortingOrder={r.sortingOrder} raycastTarget={rt}\n");
        }

        Debug.Log(sb.ToString(), this);
    }

    private static string GetPath(GameObject go)
    {
        if (go == null) return "<null>";
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

/// <summary>
/// Attached to the BG button at runtime (by InfoPanelController).
/// Prevents the BG (close overlay) from intercepting clicks over the Panel area,
/// while still allowing outside-of-panel clicks to close the InfoPanel.
/// </summary>
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
            return true; // allow raycasts if we can't determine panel bounds

        // If pointer is over the panel rect, BG should NOT be a raycast hit.
        // That allows child buttons (tabs) to receive clicks normally.
        bool overPanel = RectTransformUtility.RectangleContainsScreenPoint(_panelRect, sp, eventCamera);
        return !overPanel;
    }
}


