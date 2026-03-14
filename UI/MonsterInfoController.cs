using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class MonsterInfoController : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Root panel GameObject (the one you want enabled/disabled).")]
    [SerializeField] private GameObject monsterInfoPanel;

    [Header("Shared Info Panel")]
    [Tooltip("Optional. If assigned, this legacy controller will forward monster inspection to the shared InfoPanelController instead of using the old standalone MonsterInfoPanel.")]
    [SerializeField] private InfoPanelController sharedInfoPanelController;

    [Tooltip("If true and sharedInfoPanelController is assigned, Show/Hide will use the shared generic info panel shell.")]
    [SerializeField] private bool preferSharedInfoPanel = true;

    [Header("Tabs")]
    [Tooltip("Root that contains the existing 'Info' text fields (name/stats/description).")]
    [SerializeField] private GameObject infoTabRoot;

    [Tooltip("Root for the Monster Reel tab content.")]
    [SerializeField] private GameObject monsterReelTabRoot;

    [Tooltip("Root for the monster status tab content.")]
    [SerializeField] private GameObject statusTabRoot;

    [SerializeField] private Button infoTabButton;
    [SerializeField] private Button reelTabButton;
    [SerializeField] private Button statusTabButton;

    [Tooltip("Optional: component that drives the Monster Reel tab UI.")]
    [SerializeField] private MonsterReelPanelUI monsterReelPanelUI;

    [Tooltip("Optional: component that drives the Monster Status tab UI.")]
    [SerializeField] private MonsterStatusTabUI monsterStatusTabUI;

    [Header("Positioning")]
    [Tooltip("Optional. If null, we use monsterInfoPanel's RectTransform.")]
    [SerializeField] private RectTransform panelRect;

    [Tooltip("Canvas containing the panel (used for ScreenPoint->UI conversion). If null, will search parents.")]
    [SerializeField] private Canvas rootCanvas;

    [Tooltip("If true, the panel will follow the monster each frame while open.")]
    [SerializeField] private bool followMonster = true;

    [Tooltip("Padding in pixels between the monster and the panel.")]
    [SerializeField] private float screenPadding = 16f;

    [Tooltip("If true, always place the panel to the LEFT of the monster.")]
    [SerializeField] private bool forceLeftOfMonster = true;

    [Header("Info Tab Text")]
    [SerializeField] private TMP_Text monsterNameText;
    [SerializeField] private TMP_Text monsterStatsText;
    [SerializeField] private TMP_Text monsterDescriptionText;

    [Header("Optional Sorting")]
    [Tooltip("If assigned, we will force this canvas to a low sorting order so other UI (like Ability panel) can draw above it.")]
    [SerializeField] private Canvas monsterCanvas;
    [SerializeField] private int sortingOrder = 0;

    private Monster _currentMonster;

    private enum ActiveTab
    {
        Info = 0,
        Reel = 1,
        Status = 2
    }

    [SerializeField] private ActiveTab defaultTab = ActiveTab.Info;
    private ActiveTab _activeTab;

    private bool IsPlayerCasting()
    {
        return AbilityCastState.Instance != null && AbilityCastState.Instance.HasPendingCast;
    }

    private bool CurrentMonsterHasReelStrip()
    {
        return MonsterReelPanelUI.HasDisplayableReelStrip(_currentMonster);
    }

    private void RefreshReelTabAvailability()
    {
        bool hasReelStrip = CurrentMonsterHasReelStrip();

        if (reelTabButton != null)
            reelTabButton.interactable = hasReelStrip;

        if (!hasReelStrip && _activeTab == ActiveTab.Reel)
            SetActiveTab(ActiveTab.Info, force: true);
    }

    private void Awake()
    {
        if (sharedInfoPanelController == null)
            sharedInfoPanelController = FindFirstObjectByType<InfoPanelController>(FindObjectsInactive.Include);

        if (panelRect == null && monsterInfoPanel != null)
            panelRect = monsterInfoPanel.GetComponent<RectTransform>();

        if (rootCanvas == null)
            rootCanvas = GetComponentInParent<Canvas>();

        if (monsterReelPanelUI == null)
        {
            if (monsterReelTabRoot != null)
                monsterReelPanelUI = monsterReelTabRoot.GetComponentInChildren<MonsterReelPanelUI>(true);
            if (monsterReelPanelUI == null)
                monsterReelPanelUI = GetComponentInChildren<MonsterReelPanelUI>(true);
        }

        if (monsterStatusTabUI == null)
        {
            if (statusTabRoot != null)
                monsterStatusTabUI = statusTabRoot.GetComponentInChildren<MonsterStatusTabUI>(true);
            if (monsterStatusTabUI == null)
                monsterStatusTabUI = GetComponentInChildren<MonsterStatusTabUI>(true);
        }

        if (monsterInfoPanel != null)
            monsterInfoPanel.SetActive(false);

        if (monsterCanvas != null)
        {
            monsterCanvas.overrideSorting = true;
            monsterCanvas.sortingOrder = sortingOrder;
        }

        WireTabButtons();
        SetActiveTab(defaultTab, force: true);
    }

    private void WireTabButtons()
    {
        if (infoTabButton != null)
        {
            infoTabButton.onClick.RemoveAllListeners();
            infoTabButton.onClick.AddListener(OnInfoTabPressed);
        }

        if (reelTabButton != null)
        {
            reelTabButton.onClick.RemoveAllListeners();
            reelTabButton.onClick.AddListener(OnReelTabPressed);
        }

        if (statusTabButton != null)
        {
            statusTabButton.onClick.RemoveAllListeners();
            statusTabButton.onClick.AddListener(OnStatusTabPressed);
        }
    }

    private void OnEnable()
    {
        AbilityCastState.OnTargetConfirmed += HandleTargetConfirmed;
    }

    private void OnDisable()
    {
        AbilityCastState.OnTargetConfirmed -= HandleTargetConfirmed;
    }

    private void HandleTargetConfirmed()
    {
        Hide();
    }

    public void Show(Monster monster)
    {
        if (IsPlayerCasting())
        {
            Hide();
            return;
        }

        if (monster == null || monster.IsDead)
        {
            Hide();
            return;
        }

        if (preferSharedInfoPanel && sharedInfoPanelController != null)
        {
            sharedInfoPanelController.ShowMonster(monster, new InfoPanelData
            {
                title = monster.DisplayName,
                body = BuildStatsForPanel(monster) + " " + (monster.Description ?? string.Empty),
                image = null
            });
            _currentMonster = monster;
            return;
        }

        _currentMonster = monster;
        RefreshReelTabAvailability();

        if (monsterInfoPanel != null)
            monsterInfoPanel.SetActive(true);

        if (monsterNameText != null)
            monsterNameText.text = monster.DisplayName;

        if (monsterStatsText != null)
            monsterStatsText.text = BuildStatsText(monster);

        if (monsterDescriptionText != null)
            monsterDescriptionText.text = monster.Description;

        ActiveTab openingTab = defaultTab;
        if (openingTab == ActiveTab.Reel && !CurrentMonsterHasReelStrip())
            openingTab = ActiveTab.Info;

        SetActiveTab(openingTab, force: true);
        UpdatePanelPosition();
    }

    private void LateUpdate()
    {
        if (monsterInfoPanel != null && monsterInfoPanel.activeSelf && IsPlayerCasting())
        {
            Hide();
            return;
        }

        if (!followMonster) return;
        if (_currentMonster == null) return;
        if (monsterInfoPanel == null || !monsterInfoPanel.activeSelf) return;

        UpdatePanelPosition();
    }

    private void UpdatePanelPosition()
    {
        if (_currentMonster == null) return;
        if (panelRect == null) return;

        Camera cam = Camera.main;
        if (cam == null) cam = FindFirstObjectByType<Camera>();
        if (cam == null) return;

        Vector3 world = _currentMonster.transform.position;
        Vector3 screen = cam.WorldToScreenPoint(world);

        Canvas canvas = rootCanvas != null ? rootCanvas : GetComponentInParent<Canvas>();
        if (canvas == null) return;

        RectTransform canvasRect = canvas.transform as RectTransform;
        if (canvasRect == null) return;

        Camera uiCam = null;
        if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            uiCam = canvas.worldCamera;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, uiCam, out Vector2 localPoint))
            return;

        float panelHalfW = panelRect.rect.width * 0.5f;
        Vector2 offset = Vector2.zero;

        if (forceLeftOfMonster)
            offset.x = -(panelHalfW + screenPadding);
        else
            offset.x = (panelHalfW + screenPadding);

        Vector2 desired = localPoint + offset;

        Vector2 min = canvasRect.rect.min + new Vector2(panelHalfW, panelRect.rect.height * 0.5f);
        Vector2 max = canvasRect.rect.max - new Vector2(panelHalfW, panelRect.rect.height * 0.5f);

        desired.x = Mathf.Clamp(desired.x, min.x, max.x);
        desired.y = Mathf.Clamp(desired.y, min.y, max.y);

        panelRect.anchoredPosition = desired;
    }

    public void Hide()
    {
        if (preferSharedInfoPanel && sharedInfoPanelController != null && sharedInfoPanelController.IsOpen)
            sharedInfoPanelController.Close();

        if (monsterStatusTabUI != null)
            monsterStatusTabUI.ShowForMonster(null);

        _currentMonster = null;

        if (reelTabButton != null)
            reelTabButton.interactable = true;

        if (monsterInfoPanel != null)
            monsterInfoPanel.SetActive(false);
    }

    public bool IsShowing(Monster monster)
    {
        return monster != null && _currentMonster == monster && monsterInfoPanel != null && monsterInfoPanel.activeSelf;
    }

    public void HideIfShowing(Monster monster)
    {
        if (IsShowing(monster))
            Hide();
    }

    public void SetSortingOrder(int order)
    {
        sortingOrder = order;
        if (monsterCanvas != null)
        {
            monsterCanvas.overrideSorting = true;
            monsterCanvas.sortingOrder = sortingOrder;
        }
    }

    public void OnInfoTabPressed()
    {
        SetActiveTab(ActiveTab.Info);
    }

    public void OnReelTabPressed()
    {
        if (!CurrentMonsterHasReelStrip())
        {
            Debug.LogWarning($"[MonsterInfoController] Monster '{(_currentMonster != null ? _currentMonster.name : "NULL")}' has no reel strip assigned. Keeping monster info panel on the Info tab.", this);
            SetActiveTab(ActiveTab.Info);
            return;
        }

        SetActiveTab(ActiveTab.Reel);
    }

    public void OnStatusTabPressed()
    {
        SetActiveTab(ActiveTab.Status);
    }

    private void SetActiveTab(ActiveTab tab, bool force = false)
    {
        if (tab == ActiveTab.Reel && !CurrentMonsterHasReelStrip())
            tab = ActiveTab.Info;

        if (!force && _activeTab == tab) return;
        _activeTab = tab;

        if (infoTabRoot != null)
            infoTabRoot.SetActive(tab == ActiveTab.Info);

        if (monsterReelTabRoot != null)
            monsterReelTabRoot.SetActive(tab == ActiveTab.Reel);

        if (statusTabRoot != null)
            statusTabRoot.SetActive(tab == ActiveTab.Status);

        if (tab == ActiveTab.Reel && monsterReelPanelUI != null)
            monsterReelPanelUI.ShowForMonster(_currentMonster);

        if (monsterStatusTabUI != null)
        {
            if (tab == ActiveTab.Status)
                monsterStatusTabUI.ShowForMonster(_currentMonster);
            else
                monsterStatusTabUI.ShowForMonster(null);
        }

        if (monsterInfoPanel != null && monsterInfoPanel.activeSelf)
            UpdatePanelPosition();
    }

    public string BuildStatsForPanel(Monster monster)
    {
        if (monster == null) return string.Empty;
        return BuildStatsText(monster);
    }

    private string BuildStatsText(Monster monster)
    {
        var sb = new StringBuilder(256);

        sb.AppendLine($"HP: {monster.CurrentHp}/{monster.MaxHp}");
        sb.AppendLine($"Damage: {monster.GetDamage()}");

        string tagsLine = TryBuildTagsLine(monster);
        if (!string.IsNullOrWhiteSpace(tagsLine))
        {
            sb.AppendLine();
            sb.AppendLine(tagsLine);
        }

        string resBlock = TryBuildResistanceBlock(monster);
        if (!string.IsNullOrWhiteSpace(resBlock))
        {
            sb.AppendLine();
            sb.Append(resBlock);
        }

        return sb.ToString().TrimEnd();
    }

    private static string TryBuildTagsLine(Monster monster)
    {
        try
        {
            PropertyInfo pi = monster.GetType().GetProperty("Tags", BindingFlags.Public | BindingFlags.Instance);
            if (pi == null) return string.Empty;

            object tagsObj = pi.GetValue(monster);
            if (tagsObj is System.Collections.IEnumerable enumerable)
            {
                List<string> tags = new List<string>();
                foreach (var t in enumerable)
                {
                    if (t == null) continue;
                    tags.Add(t.ToString().Replace("_", " "));
                }

                if (tags.Count > 0)
                    return "Tags: " + string.Join(", ", tags);
            }
        }
        catch { }

        return string.Empty;
    }

    private static string TryBuildResistanceBlock(Monster monster)
    {
        try
        {
            var t = monster.GetType();
            FieldInfo phys = t.GetField("physicalResistance", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo elec = t.GetField("electricResistance", BindingFlags.NonPublic | BindingFlags.Instance);

            bool hasAny = false;
            float physVal = 1f, elecVal = 1f;

            if (phys != null)
            {
                physVal = (float)phys.GetValue(monster);
                hasAny = true;
            }

            if (elec != null)
            {
                elecVal = (float)elec.GetValue(monster);
                hasAny = true;
            }

            if (!hasAny) return string.Empty;

            var sb = new StringBuilder(128);
            sb.AppendLine("Resistances:");
            if (phys != null) sb.AppendLine($"- Physical: {physVal:0.##}x");
            if (elec != null) sb.AppendLine($"- Electric: {elecVal:0.##}x");
            return sb.ToString().TrimEnd();
        }
        catch { }

        return string.Empty;
    }
}

