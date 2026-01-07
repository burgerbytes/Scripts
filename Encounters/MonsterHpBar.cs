using UnityEngine;
using UnityEngine.UI;

public class MonsterHpBar : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Monster monster;
    [SerializeField] private Image fillImage;

    private void Reset()
    {
        monster = GetComponentInParent<Monster>();
    }

    private void Awake()
    {
        if (monster == null) monster = GetComponentInParent<Monster>();
    }

    private void OnEnable()
    {
        if (monster != null)
            monster.OnHpChanged += HandleHpChanged;

        // Initialize if we already have values
        if (monster != null)
            HandleHpChanged(monster.CurrentHp, monster.MaxHp);
    }

    private void OnDisable()
    {
        if (monster != null)
            monster.OnHpChanged -= HandleHpChanged;
    }

    private void HandleHpChanged(int current, int max)
    {
        if (fillImage == null) return;

        float t = (max <= 0) ? 0f : Mathf.Clamp01((float)current / max);
        fillImage.fillAmount = t;

        // Hide when dead (optional)
        gameObject.SetActive(current > 0);
    }
}
