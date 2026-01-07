using UnityEngine;

public class MenuButtonsUI : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private StatsPanelUI statsPanel;

    // Placeholders for later
    [SerializeField] private GameObject inventoryPanel;
    [SerializeField] private GameObject mapPanel;
    [SerializeField] private GameObject skillsPanel;

    public void OnStatsClicked()
    {
        if (statsPanel == null) return;
        statsPanel.Toggle();
    }

    public void OnInventoryClicked()
    {
        if (inventoryPanel != null)
            inventoryPanel.SetActive(!inventoryPanel.activeSelf);
    }

    public void OnMapClicked()
    {
        if (mapPanel != null)
            mapPanel.SetActive(!mapPanel.activeSelf);
    }

    public void OnSkillsClicked()
    {
        if (skillsPanel != null)
            skillsPanel.SetActive(!skillsPanel.activeSelf);
    }
}
