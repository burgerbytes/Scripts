using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class QuickAbilityMenuUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject root;
    [SerializeField] private Transform listParent;
    [SerializeField] private QuickAbilityIconButtonUI buttonPrefab;

    [Header("Details (shown on click; hold will later open a deeper panel)")]
    [SerializeField] private GameObject detailsPanel;
    [SerializeField] private Image detailsIcon;
    [SerializeField] private TMP_Text detailsNameText;
    [SerializeField] private TMP_Text detailsDescText;
    [SerializeField] private TMP_Text detailsCostText;
    [SerializeField] private TMP_Text detailsHeroText;

    [Header("Refs")]
    [SerializeField] private BattleManager battleManager;
    private PartyHUD _partyHUD;
    [SerializeField] private ResourcePool resourcePool;

    [Header("Behavior")]
    [Tooltip("If true, the menu closes automatically once the pending cast is cleared (resolved OR canceled).")]
    [SerializeField] private bool closeWhenPendingCastClears = true;

    [Header("Grid Display")]
    [Tooltip("If false, quick grid shows only the ability icons (no cost text).")]
    [SerializeField] private bool showCostTextInGrid = false;

    [Tooltip("If true, menu auto-rebuilds whenever resources change.")]
    [SerializeField] private bool rebuildOnResourceChange = true;

    [Tooltip("If true, the menu can only be used during Player Phase.")]
    [SerializeField] private bool onlyUsableDuringPlayerPhase = true;

    [Header("Layout")]
    [Tooltip("If true, the quick ability menu root is forced to horizontal center and the icon grid aligns from the top-center instead of top-left.")]
    [SerializeField] private bool centerMenuHorizontally = true;

    [Tooltip("If true, each hero's ability section is positioned beside that hero instead of being stacked in one shared menu grid.")]
    [SerializeField] private bool positionSectionsBesideHeroes = true;

    [Tooltip("Screen-space offset applied from the hero position to the ability section anchor.")]
    [SerializeField] private Vector2 heroSectionScreenOffset = new Vector2(84f, 0f);

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    private readonly List<QuickAbilityIconButtonUI> _spawned = new();
    private readonly List<GameObject> _spawnedSections = new();

    private Vector2 _sectionCellSize = new(64f, 64f);
    private Vector2 _sectionSpacing = new(8f, 8f);
    private GridLayoutGroup.Corner _sectionStartCorner = GridLayoutGroup.Corner.UpperLeft;
    private GridLayoutGroup.Axis _sectionStartAxis = GridLayoutGroup.Axis.Horizontal;
    private GridLayoutGroup.Constraint _sectionConstraint = GridLayoutGroup.Constraint.FixedColumnCount;
    private int _sectionConstraintCount = 4;
    private RectOffset _sectionPadding;

    private bool _inSelectionMode;
    private QuickAbilityIconButtonUI _selectedButton;
    private bool _closeAfterThisPendingClears;

    private void Awake()
    {
        if (_sectionPadding == null)
            _sectionPadding = new RectOffset(0, 0, 0, 0);

        if (_partyHUD == null)
            _partyHUD = FindFirstObjectByType<PartyHUD>(FindObjectsInactive.Include);
        if (battleManager == null)
            battleManager = FindFirstObjectByType<BattleManager>();

        if (resourcePool == null)
            resourcePool = ResourcePool.Instance != null ? ResourcePool.Instance : FindFirstObjectByType<ResourcePool>();

        AutoWireIfMissing();
        ApplyLayoutSettings();
        HideDetails();

        if (root != null)
            root.SetActive(false);
    }

    private void OnEnable()
    {
        if (battleManager != null)
        {
            battleManager.OnActivePartyMemberChanged += HandleActivePartyChanged;
            battleManager.OnBattleStateChanged += HandleBattleStateChanged;
            battleManager.OnPendingAbilityCleared += HandlePendingAbilityCleared;
        }

        if (resourcePool != null && rebuildOnResourceChange)
            resourcePool.OnChanged += HandleResourcesChanged;
    }

    private void OnDisable()
    {
        if (battleManager != null)
        {
            battleManager.OnActivePartyMemberChanged -= HandleActivePartyChanged;
            battleManager.OnBattleStateChanged -= HandleBattleStateChanged;
            battleManager.OnPendingAbilityCleared -= HandlePendingAbilityCleared;
        }

        if (resourcePool != null)
            resourcePool.OnChanged -= HandleResourcesChanged;
    }

    public bool IsOpen => root != null && root.activeSelf;

    public void Toggle()
    {
        AutoWireIfMissing();
        ApplyLayoutSettings();

        if (onlyUsableDuringPlayerPhase && battleManager != null && !battleManager.IsPlayerPhase)
        {
            if (debugLogs) Debug.Log("[QuickAbilityMenuUI] Toggle blocked: not player phase.", this);
            Close();
            return;
        }

        if (root == null)
        {
            Debug.LogError("[QuickAbilityMenuUI] Toggle failed: root is NULL. Assign Root in inspector or ensure a child named 'Root' exists.", this);
            return;
        }

        bool next = !root.activeSelf;

        if (next && _partyHUD != null)
        {
            if (!_partyHUD.NotifyPanelOpened(PartyHUD.UIPanelType.QuickAbilityMenu))
                return;
        }

        root.SetActive(next);

        if (!next && _partyHUD != null)
            _partyHUD.NotifyPanelClosed(PartyHUD.UIPanelType.QuickAbilityMenu);

        if (next)
        {
            ForceBringToFrontAndEnableCanvasGroup();
            ExitSelectionMode();
            RebuildForAllHeroes();
        }
        else
        {
            HideDetails();
        }
    }

    public void Close()
    {
        if (root != null) root.SetActive(false);
        HideDetails();
        ExitSelectionMode();
        ClearList();
        _closeAfterThisPendingClears = false;

        if (_partyHUD != null)
            _partyHUD.NotifyPanelClosed(PartyHUD.UIPanelType.QuickAbilityMenu);
    }

    private void HandleActivePartyChanged(int _)
    {
        if (!IsOpen) return;
        ExitSelectionMode();
        RebuildForAllHeroes();
    }

    private void HandleResourcesChanged()
    {
        if (!IsOpen) return;
        ExitSelectionMode();
        RebuildForAllHeroes();
    }

    private void HandleBattleStateChanged(BattleManager.BattleState _)
    {
        if (!onlyUsableDuringPlayerPhase) return;
        if (battleManager == null) return;

        if (!battleManager.IsPlayerPhase)
        {
            Close();
            return;
        }

        if (IsOpen)
        {
            ExitSelectionMode();
            RebuildForAllHeroes();
        }
    }

    private void RebuildForAllHeroes()
    {
        AutoWireIfMissing();
        ApplyLayoutSettings();

        if (battleManager == null || resourcePool == null)
        {
            if (debugLogs) Debug.LogWarning("[QuickAbilityMenuUI] Rebuild aborted: missing BattleManager or ResourcePool.", this);
            return;
        }

        if (onlyUsableDuringPlayerPhase && !battleManager.IsPlayerPhase)
        {
            ClearList();
            HideDetails();
            return;
        }

        if (buttonPrefab == null || listParent == null)
        {
            Debug.LogError("[QuickAbilityMenuUI] Rebuild failed: buttonPrefab or listParent is NULL. Wire them in inspector or ensure children exist.", this);
            return;
        }

        ClearList();

        int count = 0;
        int sectionCount = 0;
        int nullStreak = 0;
        for (int i = 0; i < 8; i++)
        {
            HeroStats hero = battleManager.GetHeroAtPartyIndex(i);
            if (hero == null)
            {
                nullStreak++;
                if (nullStreak >= 3) break;
                continue;
            }
            nullStreak = 0;

            ClassDefinitionSO classDef = hero.AdvancedClassDef != null ? hero.AdvancedClassDef : hero.BaseClassDef;
            List<AbilityDefinitionSO> abilities = hero.GetUnlockedAbilitiesFromClassDef(classDef);
            if (abilities == null) continue;

            Transform heroSectionParent = null;
            int heroAbilityCount = 0;

            for (int a = 0; a < abilities.Count; a++)
            {
                var ability = abilities[a];
                if (ability == null) continue;

                if (!GameDebugSettings.IsAbilityAllowed(ability))
                    continue;

                if (!CanUseAbilityNow(hero, ability))
                    continue;

                if (!CanAffordNow(hero, ability))
                    continue;

                if (heroSectionParent == null)
                {
                    heroSectionParent = CreateHeroSectionGrid(hero);
                    if (heroSectionParent == null)
                        break;
                    sectionCount++;
                }

                var btn = Instantiate(buttonPrefab, heroSectionParent);
                btn.BindForHero(
                    hero,
                    ability,
                    resourcePool,
                    OnClickAbilityIcon,
                    OnHoldDetails,
                    (h, ab) => CanUseAbilityNow(h, ab),
                    showCostTextInGrid
                );

                if (!btn.IsUsableNow())
                {
                    Destroy(btn.gameObject);
                    continue;
                }

                _spawned.Add(btn);
                heroAbilityCount++;
                count++;
            }

            if (heroSectionParent != null && heroAbilityCount > 0)
                FinalizeHeroSectionLayout(hero, heroSectionParent as RectTransform, heroAbilityCount);
        }

        if (debugLogs)
            Debug.Log($"[QuickAbilityMenuUI] Rebuilt ALL heroes entries={count}, sections={sectionCount}", this);

        if (count == 0)
            HideDetails();

        LayoutRebuilder.ForceRebuildLayoutImmediate(listParent as RectTransform);
        Canvas.ForceUpdateCanvases();
    }

    private void ClearList()
    {
        for (int i = 0; i < _spawned.Count; i++)
        {
            if (_spawned[i] != null)
                Destroy(_spawned[i].gameObject);
        }
        _spawned.Clear();

        for (int i = 0; i < _spawnedSections.Count; i++)
        {
            if (_spawnedSections[i] != null)
                Destroy(_spawnedSections[i]);
        }
        _spawnedSections.Clear();
    }

    private bool CanAffordNow(HeroStats hero, AbilityDefinitionSO ability)
    {
        if (ability == null || resourcePool == null) return false;

        ResourceCost effectiveCost = ability.cost;

        if (ability.spendAllAttackResources)
        {
            long atk = resourcePool.Attack;
            if (atk <= 0) return false;
            effectiveCost.attack = atk;
        }

        if (hero != null && HeroStats.IsRampingBasicAttackAbility(ability))
            effectiveCost.attack = System.Math.Max(0L, effectiveCost.attack) + System.Math.Max(0L, hero.GetRampingBasicAttackAdditionalAtkCost(ability));

        return resourcePool.CanAfford(effectiveCost);
    }

    private bool CanUseAbilityNow(HeroStats hero, AbilityDefinitionSO ability)
    {
        if (hero == null || ability == null) return false;

        if (onlyUsableDuringPlayerPhase && battleManager != null && !battleManager.IsPlayerPhase)
            return false;

        if (!hero.CanUseAbilityThisTurn(ability))
            return false;

        if (ability.baseDamage > 0 && !hero.CanCommitDamageAttackThisTurn())
            return false;

        return true;
    }

    private void OnClickAbilityIcon(QuickAbilityIconButtonUI button, HeroStats hero, AbilityDefinitionSO ability)
    {
        if (battleManager == null || hero == null || ability == null) return;

        if (onlyUsableDuringPlayerPhase && !battleManager.IsPlayerPhase)
        {
            Close();
            return;
        }

        if (debugLogs)
            Debug.Log($"[QuickAbilityMenuUI] Click ability icon hero={hero.name} ability={ability.abilityName}", this);

        EnterSelectionMode(button);
        ShowDetails(hero, ability);

        if (!GameDebugSettings.IsAbilityAllowed(ability))
        {
            if (debugLogs)
                Debug.Log($"[QuickAbilityMenuUI] Blocked debug-only ability while debug abilities are disabled: {ability.abilityName}", this);
            return;
        }

        battleManager.BeginAbilityUseFromMenu(hero, ability);

        _closeAfterThisPendingClears = closeWhenPendingCastClears;
    }

    private void OnHoldDetails(QuickAbilityIconButtonUI button, HeroStats hero, AbilityDefinitionSO ability)
    {
        ShowDetails(hero, ability);
    }

    private void ShowDetails(HeroStats hero, AbilityDefinitionSO ability)
    {
        if (detailsPanel == null) return;

        if (ability == null)
        {
            HideDetails();
            return;
        }

        detailsPanel.SetActive(true);

        if (detailsIcon != null) detailsIcon.sprite = ability.icon;
        if (detailsNameText != null) detailsNameText.text = ability.abilityName;
        if (detailsHeroText != null) detailsHeroText.text = hero != null ? hero.name : "";

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(ability.description))
            sb.AppendLine(ability.description.Trim());

        sb.AppendLine(" ");
        sb.AppendLine($"<b>Target</b>: {ability.targetType}");
        sb.AppendLine($"<b>Element</b>: {ability.element}");
        sb.AppendLine($"<b>Kind</b>: {ability.kind}");

        if (ability.baseDamage > 0 || ability.isDamaging)
            sb.AppendLine($"<b>Damage</b>: {ability.baseDamage}  (isDamaging={ability.isDamaging})");
        if (ability.healAmount > 0)
            sb.AppendLine($"<b>Heal</b>: {ability.healAmount}");
        if (ability.shieldAmount > 0)
            sb.AppendLine($"<b>Block</b>: {ability.shieldAmount}");

        if (ability.spendAllAttackResources)
            sb.AppendLine("<b>Cost Rule</b>: Spends ALL ATK in pool");

        if (detailsDescText != null)
            detailsDescText.text = sb.ToString();

        if (detailsCostText != null)
        {
            detailsCostText.richText = true;
            detailsCostText.text = QuickAbilityIconButtonUI.BuildCostStringStatic(hero, ability, resourcePool);
        }
    }

    private void HideDetails()
    {
        if (detailsPanel != null)
            detailsPanel.SetActive(false);
    }

    private void EnterSelectionMode(QuickAbilityIconButtonUI selected)
    {
        _inSelectionMode = true;
        _selectedButton = selected;

        if (listParent != null)
            listParent.gameObject.SetActive(false);
    }

    private void ExitSelectionMode()
    {
        if (!_inSelectionMode) return;
        _inSelectionMode = false;
        _selectedButton = null;

        if (listParent != null)
            listParent.gameObject.SetActive(true);

        for (int i = 0; i < _spawned.Count; i++)
        {
            var b = _spawned[i];
            if (b == null) continue;
            b.gameObject.SetActive(true);
        }
    }

    private void HandlePendingAbilityCleared()
    {
        if (!IsOpen) return;

        ExitSelectionMode();
        HideDetails();
        RebuildForAllHeroes();

        if (_closeAfterThisPendingClears)
            Close();
    }

    private void AutoWireIfMissing()
    {
        if (root == null)
        {
            var t = transform.Find("Root");
            if (t != null) root = t.gameObject;
        }

        if (listParent == null)
        {
            var t = transform.Find("Root/AbilityGrid");
            if (t != null) listParent = t;
        }

        if (detailsPanel == null)
        {
            var t = transform.Find("Root/DetailsPanel");
            if (t != null) detailsPanel = t.gameObject;
        }
    }

    private void CaptureSectionTemplateSettings()
    {
        if (listParent == null)
            return;

        GridLayoutGroup templateGrid = listParent.GetComponent<GridLayoutGroup>();
        if (templateGrid == null)
            return;

        if (_sectionPadding == null)
            _sectionPadding = new RectOffset(0, 0, 0, 0);

        _sectionCellSize = templateGrid.cellSize;
        _sectionSpacing = templateGrid.spacing;
        _sectionStartCorner = templateGrid.startCorner;
        _sectionStartAxis = templateGrid.startAxis;
        _sectionConstraint = templateGrid.constraint;
        _sectionConstraintCount = templateGrid.constraintCount;
        _sectionPadding = new RectOffset(templateGrid.padding.left, templateGrid.padding.right, templateGrid.padding.top, templateGrid.padding.bottom);
    }

    private void ApplyLayoutSettings()
    {
        if (!centerMenuHorizontally)
            return;

        CenterRootHorizontally();
        CaptureSectionTemplateSettings();
        ConfigureSectionHostLayout();
        CenterGridContentFromTop();
    }

    private void ConfigureSectionHostLayout()
    {
        if (listParent == null)
            return;

        RectTransform listRect = listParent as RectTransform;
        if (listRect != null)
        {
            listRect.anchorMin = Vector2.zero;
            listRect.anchorMax = Vector2.one;
            listRect.pivot = new Vector2(0.5f, 0.5f);
            listRect.offsetMin = Vector2.zero;
            listRect.offsetMax = Vector2.zero;
            listRect.anchoredPosition = Vector2.zero;
            listRect.localScale = Vector3.one;
        }

        GridLayoutGroup existingGrid = listParent.GetComponent<GridLayoutGroup>();
        if (existingGrid != null)
            existingGrid.enabled = false;

        HorizontalLayoutGroup existingH = listParent.GetComponent<HorizontalLayoutGroup>();
        if (existingH != null)
            existingH.enabled = false;

        VerticalLayoutGroup existingV = listParent.GetComponent<VerticalLayoutGroup>();
        if (existingV != null)
            existingV.enabled = false;

        ContentSizeFitter fitter = listParent.GetComponent<ContentSizeFitter>();
        if (fitter != null)
            fitter.enabled = false;
    }

    private Transform CreateHeroSectionGrid(HeroStats hero)
    {
        if (listParent == null)
            return null;

        GameObject section = new GameObject($"HeroSection_{(hero != null ? hero.name : "Unknown")}", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter), typeof(LayoutElement));
        section.transform.SetParent(listParent, false);
        _spawnedSections.Add(section);

        RectTransform sectionRect = section.GetComponent<RectTransform>();
        sectionRect.anchorMin = new Vector2(0.5f, 0.5f);
        sectionRect.anchorMax = new Vector2(0.5f, 0.5f);
        sectionRect.pivot = new Vector2(0f, 0.5f);
        sectionRect.anchoredPosition = Vector2.zero;
        sectionRect.localScale = Vector3.one;

        LayoutElement layoutElement = section.GetComponent<LayoutElement>();
        layoutElement.ignoreLayout = true;

        GridLayoutGroup sectionGrid = section.GetComponent<GridLayoutGroup>();
        if (_sectionConstraintCount > 0)
        {
            sectionGrid.cellSize = _sectionCellSize;
            sectionGrid.spacing = _sectionSpacing;
            sectionGrid.startCorner = _sectionStartCorner;
            sectionGrid.startAxis = _sectionStartAxis;
            sectionGrid.constraint = _sectionConstraint;
            sectionGrid.constraintCount = _sectionConstraintCount;
            RectOffset sourcePadding = _sectionPadding ?? new RectOffset(0, 0, 0, 0);
            sectionGrid.padding = new RectOffset(sourcePadding.left, sourcePadding.right, sourcePadding.top, sourcePadding.bottom);
        }
        else
        {
            sectionGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            sectionGrid.constraintCount = 4;
            sectionGrid.cellSize = new Vector2(96f, 96f);
            sectionGrid.spacing = new Vector2(8f, 8f);
            sectionGrid.padding = new RectOffset(0, 0, 0, 0);
        }

        sectionGrid.childAlignment = TextAnchor.UpperLeft;

        ContentSizeFitter sectionFitter = section.GetComponent<ContentSizeFitter>();
        sectionFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        sectionFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        return section.transform;
    }

    private void FinalizeHeroSectionLayout(HeroStats hero, RectTransform sectionRect, int buttonCount)
    {
        if (hero == null || sectionRect == null || listParent == null || buttonCount <= 0)
            return;

        GridLayoutGroup grid = sectionRect.GetComponent<GridLayoutGroup>();
        if (grid == null)
            return;

        int columns = Mathf.Max(1, grid.constraint == GridLayoutGroup.Constraint.FixedColumnCount ? grid.constraintCount : buttonCount);
        columns = Mathf.Min(columns, buttonCount);
        int rows = Mathf.CeilToInt(buttonCount / (float)columns);

        float width = grid.padding.left + grid.padding.right + (columns * grid.cellSize.x) + (Mathf.Max(0, columns - 1) * grid.spacing.x);
        float height = grid.padding.top + grid.padding.bottom + (rows * grid.cellSize.y) + (Mathf.Max(0, rows - 1) * grid.spacing.y);
        sectionRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        sectionRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);

        PositionSectionBesideHero(hero, sectionRect);
        LayoutRebuilder.ForceRebuildLayoutImmediate(sectionRect);
    }

    private void PositionSectionBesideHero(HeroStats hero, RectTransform sectionRect)
    {
        if (hero == null || sectionRect == null || listParent == null)
            return;

        RectTransform parentRect = listParent as RectTransform;
        if (parentRect == null)
            return;

        Camera worldCamera = Camera.main;
        Canvas canvas = listParent.GetComponentInParent<Canvas>(true);
        Camera uiCamera = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            uiCamera = canvas.worldCamera;

        Transform anchor = ResolveHeroAnchor(hero);
        if (anchor == null)
            return;

        Vector3 screenPoint = RectTransformUtility.WorldToScreenPoint(worldCamera, anchor.position);
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPoint, uiCamera, out Vector2 localPoint))
            sectionRect.anchoredPosition = localPoint + heroSectionScreenOffset;
    }

    private Transform ResolveHeroAnchor(HeroStats hero)
    {
        if (hero == null)
            return null;

        Transform centerPoint = hero.transform.Find("CenterPoint");
        if (centerPoint != null)
            return centerPoint;

        return hero.transform;
    }

    private void CenterRootHorizontally()
    {
        if (root == null)
            return;

        RectTransform rootRect = root.GetComponent<RectTransform>();
        if (rootRect == null)
            return;

        if (positionSectionsBesideHeroes)
        {
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            rootRect.anchoredPosition = Vector2.zero;
            return;
        }

        rootRect.anchorMin = new Vector2(0.5f, rootRect.anchorMin.y);
        rootRect.anchorMax = new Vector2(0.5f, rootRect.anchorMax.y);
        rootRect.pivot = new Vector2(0.5f, rootRect.pivot.y);
        rootRect.anchoredPosition = new Vector2(0f, rootRect.anchoredPosition.y);
    }

    private void CenterGridContentFromTop()
    {
        if (listParent == null)
            return;

        if (!positionSectionsBesideHeroes)
        {
            if (listParent.TryGetComponent(out GridLayoutGroup grid))
                grid.childAlignment = TextAnchor.UpperCenter;

            if (listParent.TryGetComponent(out HorizontalOrVerticalLayoutGroup layoutGroup))
                layoutGroup.childAlignment = TextAnchor.UpperCenter;
        }

        RectTransform listRect = listParent as RectTransform;
        if (listRect != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(listRect);
            Canvas.ForceUpdateCanvases();
        }
    }

    private void ForceBringToFrontAndEnableCanvasGroup()
    {
        if (root == null) return;
        root.transform.SetAsLastSibling();
        ApplyLayoutSettings();

        var cg = root.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.alpha = 1f;
            cg.interactable = true;
            cg.blocksRaycasts = true;
        }
    }
}
