using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Populates the MonsterInfoPanel UI from a Monster component.
/// Attach this to the MonsterInfoController GameObject in the scene.
/// </summary>
public class MonsterInfoController : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject monsterInfoPanel;

    [Header("Text Fields")]
    [SerializeField] private TMP_Text monsterNameText;
    [SerializeField] private TMP_Text monsterStatsText;
    [SerializeField] private TMP_Text monsterDescriptionText;

    private Monster _current;

    private void Awake()
    {
        // If the panel root isn't set, assume the first child is the panel.
        if (monsterInfoPanel == null && transform.childCount > 0)
            monsterInfoPanel = transform.GetChild(0).gameObject;

        if (monsterInfoPanel != null && monsterInfoPanel.activeSelf)
            monsterInfoPanel.SetActive(false);
    }

    public void Show(Monster monster)
    {
        _current = monster;

        if (monsterInfoPanel != null && !monsterInfoPanel.activeSelf)
            monsterInfoPanel.SetActive(true);

        if (monster == null)
        {
            ClearText();
            return;
        }

        if (monsterNameText != null)
            monsterNameText.text = CleanName(monster.name);

        if (monsterStatsText != null)
            monsterStatsText.text = BuildStats(monster);

        if (monsterDescriptionText != null)
        {
            string desc = monster.Description;
            monsterDescriptionText.text = string.IsNullOrWhiteSpace(desc) ? "" : desc;
        }
    }

    public void Hide()
    {
        _current = null;
        if (monsterInfoPanel != null)
            monsterInfoPanel.SetActive(false);
    }

    private void ClearText()
    {
        if (monsterNameText != null) monsterNameText.text = "";
        if (monsterStatsText != null) monsterStatsText.text = "";
        if (monsterDescriptionText != null) monsterDescriptionText.text = "";
    }

    private static string CleanName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        return raw.Replace("(Clone)", "").Trim();
    }

    private static string BuildStats(Monster m)
    {
        var sb = new StringBuilder();
        sb.Append("HP: ").Append(m.CurrentHp).Append("/").Append(m.MaxHp);
        sb.Append("\nDEF: ").Append(m.Defense);

        // Tags
        if (m.Tags != null && m.Tags.Count > 0)
        {
            sb.Append("\nTags: ");
            for (int i = 0; i < m.Tags.Count; i++)
            {
                sb.Append(m.Tags[i].ToString());
                if (i < m.Tags.Count - 1) sb.Append(", ");
            }
        }

        return sb.ToString();
    }
}
