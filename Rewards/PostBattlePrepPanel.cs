using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PostBattlePrepPanel : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text headerText;

    [SerializeField] private Button inventoryButton;
    [SerializeField] private Button deckButton;
    [SerializeField] private Button continueButton;

    [Header("Optional Panels")]
    [SerializeField] private GameObject inventoryPanel;
    [SerializeField] private GameObject deckPanel;

    private Action _onContinue;

    private void Awake()
    {
        // If root isn't wired, default to this object so we can still show/hide reliably.
        if (root == null) root = gameObject;

        WireButtons();

        // Start hidden (but don't disable the GameObject itself — BattleManager may need to activate it).
        if (root != null) root.SetActive(false);
        if (inventoryPanel != null) inventoryPanel.SetActive(false);
        if (deckPanel != null) deckPanel.SetActive(false);
    }

    private void OnEnable()
    {
        Debug.Log($"[PostBattlePrepPanel] Enabled. activeInHierarchy={gameObject.activeInHierarchy} time={Time.time:0.00}", this);
    }

    private void OnDisable()
    {
        Debug.Log($"[PostBattlePrepPanel] Disabled. time={Time.time:0.00}", this);
    }

    public void Show(int battlesCompleted, int battlesPerStretch, Action onContinue)
    {
        _onContinue = onContinue;

        // Ensure object & root are active.
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        if (root != null && !root.activeSelf) root.SetActive(true);

        UpdateHeader(battlesCompleted, battlesPerStretch);

        // Close subpanels by default
        if (inventoryPanel != null) inventoryPanel.SetActive(false);
        if (deckPanel != null) deckPanel.SetActive(false);

        Debug.Log($"[PostBattlePrepPanel] Show() called. battlesCompleted={battlesCompleted} battlesPerStretch={battlesPerStretch} time={Time.time:0.00}", this);
    }

    public void Hide()
    {
        if (inventoryPanel != null) inventoryPanel.SetActive(false);
        if (deckPanel != null) deckPanel.SetActive(false);

        if (root != null) root.SetActive(false);
    }

    private void UpdateHeader(int battlesCompleted, int battlesPerStretch)
    {
        if (headerText == null) return;

        battlesPerStretch = Mathf.Max(1, battlesPerStretch);

        // Show "Fight X/Y" where X is the NEXT fight index.
        int nextFight = Mathf.Clamp(battlesCompleted + 1, 1, battlesPerStretch);

        headerText.text = $"Fight {nextFight}/{battlesPerStretch}";
    }

    private void WireButtons()
    {
        if (inventoryButton != null)
        {
            inventoryButton.onClick.RemoveAllListeners();
            inventoryButton.onClick.AddListener(() =>
            {
                if (inventoryPanel != null)
                    inventoryPanel.SetActive(!inventoryPanel.activeSelf);
                else
                    Debug.Log("[PostBattlePrepPanel] Inventory clicked (no panel wired).", this);

                if (deckPanel != null)
                    deckPanel.SetActive(false);
            });
        }

        if (deckButton != null)
        {
            deckButton.onClick.RemoveAllListeners();
            deckButton.onClick.AddListener(() =>
            {
                if (deckPanel != null)
                    deckPanel.SetActive(!deckPanel.activeSelf);
                else
                    Debug.Log("[PostBattlePrepPanel] Deck clicked (no panel wired).", this);

                if (inventoryPanel != null)
                    inventoryPanel.SetActive(false);
            });
        }

        if (continueButton != null)
        {
            continueButton.onClick.RemoveAllListeners();
            continueButton.onClick.AddListener(() =>
            {
                // Hide immediately so it feels responsive
                Hide();
                _onContinue?.Invoke();
            });
        }
    }
}
