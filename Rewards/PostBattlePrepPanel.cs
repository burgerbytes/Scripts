// PATH: Assets/Scripts/UI/PostBattle/PostBattlePrepPanel.cs
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

    [Header("Optional Sub-Panels")]
    [SerializeField] private GameObject inventoryPanel;
    [SerializeField] private GameObject deckPanel;

    private Action _onContinue;

    public bool IsOpen => root != null && root.activeSelf;

    private void Awake()
    {
        if (root != null)
            root.SetActive(false);

        WireButtons();
    }

    public void Show(int battlesCompleted, int battlesPerStretch, Action onContinue)
    {
        _onContinue = onContinue;

        if (root != null)
            root.SetActive(true);

        UpdateHeader(battlesCompleted, battlesPerStretch);

        if (inventoryPanel != null) inventoryPanel.SetActive(false);
        if (deckPanel != null) deckPanel.SetActive(false);
    }

    public void Hide()
    {
        _onContinue = null;

        if (root != null)
            root.SetActive(false);

        if (inventoryPanel != null) inventoryPanel.SetActive(false);
        if (deckPanel != null) deckPanel.SetActive(false);
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
                _onContinue?.Invoke();
            });
        }
    }
}
