using UnityEngine;

public class MetaNavBarUI : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private StatsPanelUI statsPanel;

    // Future panels (safe to leave null for now)
    [SerializeField] private GameObject inventoryPanel;
    [SerializeField] private GameObject mapPanel;
    [SerializeField] private GameObject skillsPanel;

    public void OnStatsPressed()
    {
        if (statsPanel == null) return;

        // Optional: close others when opening stats
        CloseAll();
        statsPanel.Show();
    }

    public void OnInventoryPressed()
    {
        ToggleExclusive(inventoryPanel);
    }

    public void OnMapPressed()
    {
        ToggleExclusive(mapPanel);
    }

    public void OnSkillsPressed()
    {
        ToggleExclusive(skillsPanel);
    }

    private void ToggleExclusive(GameObject panel)
    {
        if (panel == null) return;

        bool newState = !panel.activeSelf;
        CloseAll();
        panel.SetActive(newState);
    }

    private void CloseAll()
    {
        if (statsPanel != null) statsPanel.Hide();
        if (inventoryPanel != null) inventoryPanel.SetActive(false);
        if (mapPanel != null) mapPanel.SetActive(false);
        if (skillsPanel != null) skillsPanel.SetActive(false);
    }
}
