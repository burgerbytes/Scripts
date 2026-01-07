using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ResourceSlot : MonoBehaviour
{
    [Header("Wired in Inspector")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text valueText;
    [SerializeField] private Image backgroundImage; // optional

    public void SetIcon(Sprite icon)
    {
        if (iconImage != null) iconImage.sprite = icon;
    }

    public void SetValue(long value)
    {
        if (valueText != null) valueText.text = FormatCompact(value);
    }

    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }

    // Simple compact formatter: 950, 1.2k, 3.4M, 5.6B
    private static string FormatCompact(long v)
    {
        double value = v;
        if (value < 1000) return v.ToString();

        string[] suffix = { "k", "M", "B", "T" };
        int i = -1;
        while (value >= 1000 && i < suffix.Length - 1)
        {
            value /= 1000;
            i++;
        }

        // Keep one decimal only when it adds information
        return value >= 10 ? $"{value:0}{suffix[i]}" : $"{value:0.0}{suffix[i]}";
    }
}
