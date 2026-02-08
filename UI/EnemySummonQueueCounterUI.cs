using TMPro;
using UnityEngine;

/// <summary>
/// Simple UI readout for the number of enemies waiting in the summon queue.
/// Attach this to a UI GameObject (e.g., under your Battle HUD canvas) and assign a TMP_Text.
/// </summary>
public class EnemySummonQueueCounterUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private BattleManager battleManager;
    [SerializeField] private TMP_Text queueCountText;

    [Header("Display")]
    [SerializeField] private string format = "Queue: {0}";
    [SerializeField] private bool hideWhenZero = false;

    private void Awake()
    {
        if (battleManager == null) battleManager = FindFirstObjectByType<BattleManager>();
        Refresh();
    }

    private void OnEnable()
    {
        if (battleManager != null)
            battleManager.OnEnemySummonQueueChanged += HandleQueueChanged;

        Refresh();
    }

    private void OnDisable()
    {
        if (battleManager != null)
            battleManager.OnEnemySummonQueueChanged -= HandleQueueChanged;
    }

    private void HandleQueueChanged(int count)
    {
        SetCount(count);
    }

    private void Refresh()
    {
        if (battleManager == null) return;
        SetCount(battleManager.EnemySummonQueueCount);
    }

    private void SetCount(int count)
    {
        if (queueCountText == null) return;

        bool shouldShow = !hideWhenZero || count > 0;
        if (queueCountText.gameObject.activeSelf != shouldShow)
            queueCountText.gameObject.SetActive(shouldShow);

        if (shouldShow)
            queueCountText.text = string.Format(format, Mathf.Max(0, count));
    }
}
