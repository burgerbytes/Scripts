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

    [Header("Tabs")]
    [Tooltip("Root that contains the existing 'Info' text fields (name/stats/description).")]
    [SerializeField] private GameObject infoTabRoot;

    [Tooltip("Root for the Monster Reel tab content.")]
    [SerializeField] private GameObject monsterReelTabRoot;

    [SerializeField] private Button infoTabButton;
    [SerializeField] private Button reelTabButton;

    [Tooltip("Optional: component that drives the Monster Reel tab UI.")]
    [SerializeField] private MonsterReelPanelUI monsterReelPanelUI;

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
        Reel = 1
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
        if (panelRect == null && monsterInfoPanel != null)
            panelRect = monsterInfoPanel.GetComponent<RectTransform>();

        if (rootCanvas == null)
            rootCanvas = GetComponentInParent<Canvas>();

        // Auto-find the reel panel UI under the reel tab root if not assigned.
        if (monsterReelPanelUI == null)
        {
            if (monsterReelTabRoot != null)
                monsterReelPanelUI = monsterReelTabRoot.GetComponentInChildren<MonsterReelPanelUI>(true);
            if (monsterReelPanelUI == null)
                monsterReelPanelUI = GetComponentInChildren<MonsterReelPanelUI>(true);
        }

        // Initially disabled
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
        // Hide immediately after the player confirms a target (before attack anims).
        Hide();
    }

    public void Show(Monster monster)
    {
        // Do NOT allow this panel to open while the player is in a cast/targeting state.
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

        // Default tab each time we open (keeps behavior predictable).
        ActiveTab openingTab = defaultTab;
        if (openingTab == ActiveTab.Reel && !CurrentMonsterHasReelStrip())
            openingTab = ActiveTab.Info;

        SetActiveTab(openingTab, force: true);

        UpdatePanelPosition();
    }

    private void LateUpdate()
    {
        // If the player enters cast/targeting state while this is open, force-hide it.
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

        // Convert screen -> local point in canvas space.
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

        // Clamp to canvas bounds so it doesn't go offscreen.
        Vector2 min = canvasRect.rect.min + new Vector2(panelHalfW, panelRect.rect.height * 0.5f);
        Vector2 max = canvasRect.rect.max - new Vector2(panelHalfW, panelRect.rect.height * 0.5f);

        desired.x = Mathf.Clamp(desired.x, min.x, max.x);
        desired.y = Mathf.Clamp(desired.y, min.y, max.y);

        panelRect.anchoredPosition = desired;
    }

    public void Hide()
    {
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

    // =======================
    // Tabs
    // =======================
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

        // When entering reel tab, refresh it for the current monster.
        if (tab == ActiveTab.Reel && monsterReelPanelUI != null)
            monsterReelPanelUI.ShowForMonster(_currentMonster);

        // Keep the panel positioned correctly after layout changes.
        if (monsterInfoPanel != null && monsterInfoPanel.activeSelf)
            UpdatePanelPosition();
    }

    // =======================
    // Stats formatting helpers
    // =======================
    // Expose the same stats formatting used by this panel so other UI can reuse it.
    public string BuildStatsForPanel(Monster monster)
    {
        if (monster == null) return string.Empty;
        return BuildStatsText(monster);
    }

    private string BuildStatsText(Monster monster)
    {
        var sb = new StringBuilder(256);

        // Core stats (always)
        sb.AppendLine($"HP: {monster.CurrentHp}/{monster.MaxHp}");
        sb.AppendLine($"Damage: {monster.GetDamage()}");

        // Tags (optional; pulled via reflection so this script won't break if Monster changes)
        string tagsLine = TryBuildTagsLine(monster);
        if (!string.IsNullOrWhiteSpace(tagsLine))
        {
            sb.AppendLine();
            sb.AppendLine(tagsLine);
        }

        // Resistances (optional; pulled via reflection)
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
        // Expected in Monster.cs:
        //   public IReadOnlyList<MonsterTag> Tags { get; }
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
        // Monster currently has physicalResistance/electricResistance fields.
        // We read them via reflection so this panel won't break if the model changes.
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

