// GUID: NEW_ENDTURNBUTTONUI
////////////////////////////////////////////////////////////
// Assets/Scripts/UI/EndTurnButtonUI.cs
// Simple bridge between a Unity UI Button and BattleManager.EndTurn()
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class EndTurnButtonUI : MonoBehaviour
{
    [SerializeField] private BattleManager battleManager;
    [SerializeField] private Button button;

    private void Awake()
    {
        if (button == null) button = GetComponent<Button>();
        if (battleManager == null) battleManager = FindObjectOfType<BattleManager>();

        if (button != null)
            button.onClick.AddListener(OnClicked);
    }

    private void OnDestroy()
    {
        if (button != null)
            button.onClick.RemoveListener(OnClicked);
    }

    private void Update()
    {
        if (button == null) return;

        if (battleManager == null)
        {
            button.interactable = false;
            return;
        }

        // Only clickable during player phase and when not resolving.
        button.interactable = battleManager.IsPlayerPhase && !battleManager.IsResolving;
    }

    private void OnClicked()
    {
        if (battleManager == null) return;
        battleManager.EndTurn();
    }
}
