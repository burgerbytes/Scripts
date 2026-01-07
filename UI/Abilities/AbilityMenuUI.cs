using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class AbilityMenuUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject root;          // AbilityMenuPanel
    [SerializeField] private TMP_Text headerText;      // HeaderText TMP
    [SerializeField] private Transform listParent;     // AbilityList transform
    [SerializeField] private AbilityButtonUI buttonPrefab;

    [Header("Refs")]
    [SerializeField] private TopStatusBar topStatusBar;
    [SerializeField] private BattleManager battleManager;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    private readonly List<AbilityButtonUI> buttons = new();
    private AbilityButtonUI selectedButton;

    private HeroStats currentHero;

    private void Awake()
    {
        if (topStatusBar == null)
            topStatusBar = FindFirstObjectByType<TopStatusBar>();

        if (battleManager == null)
            battleManager = FindFirstObjectByType<BattleManager>();

        AutoWireIfNeeded();

        if (debugLogs)
        {
            Debug.Log(
                $"[AbilityMenuUI] Awake on '{name}'. " +
                $"root={(root ? root.name : "NULL")}, " +
                $"headerText={(headerText ? headerText.name : "NULL")}, " +
                $"listParent={(listParent ? listParent.name : "NULL")}, " +
                $"buttonPrefab={(buttonPrefab ? buttonPrefab.name : "NULL")}",
                this);
        }

        if (root != null)
            root.SetActive(false);
    }

    private void AutoWireIfNeeded()
    {
        if (root == null)
        {
            Transform t = transform.Find("AbilityMenuPanel");
            if (t != null) root = t.gameObject;
        }

        if (headerText == null && root != null)
        {
            Transform t = root.transform.Find("HeaderText");
            if (t != null) headerText = t.GetComponent<TMP_Text>();
        }

        if (listParent == null && root != null)
        {
            Transform t = root.transform.Find("AbilityList");
            if (t != null) listParent = t;
        }
    }

    public void OpenForHero(HeroStats hero, List<AbilityDefinitionSO> abilities)
    {
        AutoWireIfNeeded();

        if (debugLogs)
        {
            Debug.Log(
                $"[AbilityMenuUI] OpenForHero called. hero={(hero ? hero.name : "NULL")}, " +
                $"abilitiesCount={(abilities != null ? abilities.Count : -1)}, " +
                $"root={(root ? root.name : "NULL")} (activeSelf={(root ? root.activeSelf.ToString() : "n/a")})",
                this);
        }

        if (hero == null) return;
        currentHero = hero;

        if (root == null)
        {
            Debug.LogError("[AbilityMenuUI] OpenForHero: root is NULL. Drag AbilityMenuPanel into AbilityMenuUI.root OR ensure child is named 'AbilityMenuPanel'.", this);
            return;
        }

        root.SetActive(true);

        if (headerText != null)
            headerText.text = $"{hero.name} Abilities";

        if (listParent == null || buttonPrefab == null)
        {
            Debug.LogWarning("[AbilityMenuUI] Missing listParent or buttonPrefab; cannot populate buttons.", this);
            return;
        }

        ClearButtons();
        selectedButton = null;

        if (abilities != null)
        {
            foreach (var ability in abilities)
            {
                if (ability == null) continue;

                var btn = Instantiate(buttonPrefab, listParent);

                // ✅ Matches your AbilityButtonUI API
                btn.Bind(
                    ability,
                    topStatusBar,
                    OnAbilityButtonSelected,
                    OnAbilityConfirmed
                );

                buttons.Add(btn);
            }
        }

        RefreshAffordability();
    }

    private void OnAbilityButtonSelected(AbilityButtonUI btn)
    {
        if (btn == null) return;

        if (selectedButton != null && selectedButton != btn)
            selectedButton.SetSelected(false);

        selectedButton = btn;
        selectedButton.SetSelected(true);

        if (debugLogs)
            Debug.Log($"[AbilityMenuUI] Selected ability button: {(btn.Ability ? btn.Ability.name : "NULL")}", this);
    }

    private void OnAbilityConfirmed(AbilityDefinitionSO ability)
    {
        if (ability == null) return;

        if (debugLogs)
            Debug.Log($"[AbilityMenuUI] Confirmed/casting ability: {ability.name}", this);

        if (AbilityCastState.Instance != null)
        {
            // ✅ FIX: use currentHero (not _currentHero)
            AbilityCastState.Instance.BeginCast(currentHero, ability);
        }
        else
        {
            Debug.LogWarning("[AbilityMenuUI] No AbilityCastState in scene. Add it to a GameObject.");
        }

        if (battleManager == null)
            battleManager = FindFirstObjectByType<BattleManager>();

        if (battleManager == null || currentHero == null)
            return;

        // BattleManager will now wait for enemy click if targetType==Enemy.
        battleManager.BeginAbilityUseFromMenu(currentHero, ability);

        Close();
    }

    public void Close()
    {
        if (root != null) root.SetActive(false);
        selectedButton = null;
    }

    public void RefreshAffordability()
    {
        foreach (var b in buttons)
            if (b != null)
                b.RefreshInteractable(); // ✅ Matches your AbilityButtonUI API
    }

    private void ClearButtons()
    {
        if (listParent == null)
        {
            buttons.Clear();
            return;
        }

        for (int i = listParent.childCount - 1; i >= 0; i--)
            Destroy(listParent.GetChild(i).gameObject);

        buttons.Clear();
    }
}
