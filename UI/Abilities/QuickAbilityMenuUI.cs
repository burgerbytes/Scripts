// GUID: 8c5b3a76a52c4f8aa7e7d3b9d6a2a1c1
////////////////////////////////////////////////////////////
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

    [Header("Details (shown on hold)")]
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
    [SerializeField] private bool closeAfterCastClick = true;

    [Tooltip("If true, menu auto-rebuilds whenever resources change.")]
    [SerializeField] private bool rebuildOnResourceChange = true;

    [Tooltip("If true, the menu can only be used during Player Phase.")]
    [SerializeField] private bool onlyUsableDuringPlayerPhase = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    private readonly List<QuickAbilityIconButtonUI> _spawned = new();

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
        ClearList();
    }

    private void HandleActivePartyChanged(int _)
    {
        if (IsOpen)
            RebuildForAllHeroes();
    }

    private void HandleResourcesChanged()
    {
        if (IsOpen)
            RebuildForAllHeroes();
    }

    private void HandleBattleStateChanged(BattleManager.BattleState _)
    {
        if (!onlyUsableDuringPlayerPhase) return;
        if (battleManager == null) return;

        if (!battleManager.IsPlayerPhase)
            Close();
        else if (IsOpen)
            RebuildForAllHeroes();
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

        // Show ALL affordable abilities across ALL party heroes.
        // Assumes party indices are contiguous 0..(PartySize-1).
        // We’ll attempt indices until we hit a stretch of nulls.
        int nullStreak = 0;
        for (int i = 0; i < 8; i++) // your party is likely <= 4; 8 is a safe cap
        {
            HeroStats hero = battleManager.GetHeroAtPartyIndex(i);
            if (hero == null)
            {
                nullStreak++;
                if (nullStreak >= 3) break; // stop once we’re clearly past the party
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
                    OnClickCast,
                    OnHoldDetails,
                    (h, ab) => CanUseAbilityNow(h, ab)
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

    private void OnClickCast(HeroStats hero, AbilityDefinitionSO ability)
    {
        if (battleManager == null || hero == null || ability == null) return;

        if (onlyUsableDuringPlayerPhase && !battleManager.IsPlayerPhase)
        {
            Close();
            return;
        }

        if (debugLogs)
            Debug.Log($"[QuickAbilityMenuUI] Click cast hero={hero.name} ability={ability.abilityName}", this);

        battleManager.BeginAbilityUseFromMenu(hero, ability);

        if (closeAfterCastClick)
            Close();
    }

    private void OnHoldDetails(HeroStats hero, AbilityDefinitionSO ability)
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
        if (detailsDescText != null) detailsDescText.text = ability.description;

        if (detailsHeroText != null)
            detailsHeroText.text = hero != null ? hero.name : "";

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

    private void AutoWireIfMissing()
    {
        // Root auto-find
        if (root == null)
        {
            var t = transform.Find("Root");
            if (t != null) root = t.gameObject;
        }

        // List parent auto-find
        if (listParent == null)
        {
            var t = transform.Find("Root/AbilityGrid");
            if (t != null) listParent = t;
        }

        // Details panel auto-find
        if (detailsPanel == null)
        {
            var t = transform.Find("Root/DetailsPanel");
            if (t != null) detailsPanel = t.gameObject;
        }
    }

    private void ForceBringToFrontAndEnableCanvasGroup()
    {
        if (root == null) return;

        // Bring to front so it isn't hidden behind other UI
        root.transform.SetAsLastSibling();

        // If a CanvasGroup is hiding it, fix it
        var cg = root.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.alpha = 1f;
            cg.interactable = true;
            cg.blocksRaycasts = true;
        }
    }
}
