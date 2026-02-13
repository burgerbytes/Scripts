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

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    private readonly List<QuickAbilityIconButtonUI> _spawned = new();

    private bool _inSelectionMode;
    private QuickAbilityIconButtonUI _selectedButton;
    private bool _closeAfterThisPendingClears;

    private void Awake()
    {
        if (battleManager == null)
            battleManager = FindFirstObjectByType<BattleManager>();

        if (resourcePool == null)
            resourcePool = ResourcePool.Instance != null ? ResourcePool.Instance : FindFirstObjectByType<ResourcePool>();

        AutoWireIfMissing();
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
        root.SetActive(next);

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

            for (int a = 0; a < abilities.Count; a++)
            {
                var ability = abilities[a];
                if (ability == null) continue;

                if (!CanUseAbilityNow(hero, ability))
                    continue;

                if (!CanAffordNow(ability))
                    continue;

                var btn = Instantiate(buttonPrefab, listParent);
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
                count++;
            }
        }

        if (debugLogs)
            Debug.Log($"[QuickAbilityMenuUI] Rebuilt ALL heroes entries={count}", this);

        if (count == 0)
            HideDetails();
    }

    private void ClearList()
    {
        for (int i = 0; i < _spawned.Count; i++)
        {
            if (_spawned[i] != null)
                Destroy(_spawned[i].gameObject);
        }
        _spawned.Clear();
    }

    private bool CanAffordNow(AbilityDefinitionSO ability)
    {
        if (ability == null || resourcePool == null) return false;

        ResourceCost effectiveCost = ability.cost;

        if (ability.spendAllAttackResources)
        {
            long atk = resourcePool.Attack;
            if (atk <= 0) return false;
            effectiveCost.attack = atk;
        }

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

        // 1) Disable menu and only keep clicked ability visible.
        EnterSelectionMode(button);

        // 2) Open detail panel.
        ShowDetails(hero, ability);

        // 3) Begin ability targeting flow.
        battleManager.BeginAbilityUseFromMenu(hero, ability);

        _closeAfterThisPendingClears = closeWhenPendingCastClears;
    }

    private void OnHoldDetails(QuickAbilityIconButtonUI button, HeroStats hero, AbilityDefinitionSO ability)
    {
        // Placeholder for later: open a deeper/expanded details panel.
        // For now, keep the existing details panel behavior.
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

        // Rich details block.
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
            detailsCostText.text = QuickAbilityIconButtonUI.BuildCostStringStatic(ability, resourcePool);
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

        for (int i = 0; i < _spawned.Count; i++)
        {
            var b = _spawned[i];
            if (b == null) continue;
            b.gameObject.SetActive(b == selected);
        }
    }

    private void ExitSelectionMode()
    {
        if (!_inSelectionMode) return;
        _inSelectionMode = false;
        _selectedButton = null;

        for (int i = 0; i < _spawned.Count; i++)
        {
            var b = _spawned[i];
            if (b == null) continue;
            b.gameObject.SetActive(true);
        }
    }

    private void HandlePendingAbilityCleared()
    {
        // Called whenever the pending ability is canceled OR resolved.
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

    private void ForceBringToFrontAndEnableCanvasGroup()
    {
        if (root == null) return;
        root.transform.SetAsLastSibling();

        var cg = root.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.alpha = 1f;
            cg.interactable = true;
            cg.blocksRaycasts = true;
        }
    }
}
