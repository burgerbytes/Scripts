using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Post-battle panel shown (starting at hero level 2) after the Reel Upgrade Minigame.
/// Player chooses 1 of 2 abilities (based on AbilityDefinitionSO.unlockAtLevel) to permanently unlock.
/// </summary>
public class PostBattleAbilityUpgradePanel : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private GameObject root;
    [SerializeField] private Image portraitImage;

    [Header("Ability Buttons")]
    [SerializeField] private Button abilityButton1;
    [SerializeField] private Button abilityButton2;
    [SerializeField] private TextMeshProUGUI abilityText1;
    [SerializeField] private TextMeshProUGUI abilityText2;

    [Header("Description")]
    [SerializeField] private TextMeshProUGUI abilityDescriptionText;

    [Header("Next")]
    [SerializeField] private Button nextButton;

    [Header("Optional: Disable Battle Reels While Open")]
    [Tooltip("If assigned, disables the configured reel roots while this panel is open (e.g., battle reels).")]
    [SerializeField] private ReelDisableManager reelDisableManager;

    private HeroStats _hero;
    private Action _onDone;

    private AbilityDefinitionSO _opt1;
    private AbilityDefinitionSO _opt2;

    private void Awake()
    {
        if (nextButton != null)
            nextButton.onClick.AddListener(OnNextClicked);

        if (abilityButton1 != null)
            abilityButton1.onClick.AddListener(() => OnAbilityClicked(_opt1));

        if (abilityButton2 != null)
            abilityButton2.onClick.AddListener(() => OnAbilityClicked(_opt2));

        Hide();
    }

    public void Show(HeroStats hero, Action onDone)
    {
        _hero = hero;
        _onDone = onDone;

        if (root != null) root.SetActive(true);
        gameObject.SetActive(true);

        // Ability upgrade UI is not compatible with interacting with battle reels.
        reelDisableManager?.DisableReels();

        if (_hero != null && portraitImage != null)
        {
            // HeroStats already stores a portrait sprite reference.
            // If you don't have a getter, wire portrait directly in the panel or add a getter in HeroStats.
            portraitImage.sprite = _hero.Portrait;
        }

        int unlockLevel = (_hero != null) ? _hero.NextPendingAbilityChoiceLevel : -1;
        List<AbilityDefinitionSO> options = (_hero != null) ? _hero.GetAbilityChoiceOptionsForLevel(unlockLevel, 2) : null;

        // Defensive: never allow a misconfigured unlock level to soft-lock the run.
        if (_hero != null && (options == null || options.Count == 0))
        {
            Debug.LogWarning($"[UI][AbilityUpgradePanel] No options for hero='{_hero.name}' unlockLevel={unlockLevel}. Consuming pending choice and continuing.");
            _hero.TryConsumeNextPendingAbilityChoiceWithoutSelection();
            Hide();
            _onDone?.Invoke();
            return;
        }


        _opt1 = (options != null && options.Count > 0) ? options[0] : null;
        _opt2 = (options != null && options.Count > 1) ? options[1] : null;
        Debug.Log($"[UI][AbilityUpgradePanel] Show hero='{(_hero!=null?_hero.name:"<null>")}' unlockLevel={unlockLevel} optionsCount={(options!=null?options.Count:0)} opt1='{(_opt1!=null?_opt1.abilityName:"<none>")}' opt2='{(_opt2!=null?_opt2.abilityName:"<none>")}'");


        SetupButton(abilityButton1, abilityText1, _opt1);
        SetupButton(abilityButton2, abilityText2, _opt2);

        if (abilityDescriptionText != null)
            abilityDescriptionText.text = "Choose an ability to learn.";

        if (nextButton != null)
        {
            nextButton.interactable = false;
            nextButton.gameObject.SetActive(true);
        }
    }

    public void Hide()
    {
        _hero = null;
        _onDone = null;
        _opt1 = null;
        _opt2 = null;

        // Restore reels (safe even if already restored).
        reelDisableManager?.EnableReels();

        if (root != null) root.SetActive(false);
        gameObject.SetActive(false);
    }

    private void SetupButton(Button b, TextMeshProUGUI t, AbilityDefinitionSO a)
    {
        if (t != null)
            t.text = (a != null) ? a.abilityName : "—";

        if (b != null)
            b.interactable = (a != null);
    }

    private void OnAbilityClicked(AbilityDefinitionSO chosen)
    {
        if (_hero == null)
        {
            Debug.LogWarning("[UI][AbilityUpgradePanel] OnAbilityClicked but hero is null");
            return;
        }
        if (chosen == null)
        {
            Debug.LogWarning($"[UI][AbilityUpgradePanel] OnAbilityClicked with null option hero='{_hero.name}'");
            return;
        }

        Debug.Log($"[UI][AbilityUpgradePanel] Ability clicked hero='{_hero.name}' chosen='{chosen.abilityName}' unlockAt={chosen.unlockAtLevel}");
        bool applied = _hero.TryAcceptAbilityChoice(chosen);
        if (!applied)
        {
            Debug.LogWarning($"[PostBattleAbilityUpgradePanel] Choice rejected for hero='{_hero.name}' ability='{chosen.abilityName}'.");
            return;
        }

        // Lock in selection
        if (abilityButton1 != null) abilityButton1.interactable = false;
        if (abilityButton2 != null) abilityButton2.interactable = false;

        if (abilityDescriptionText != null)
            abilityDescriptionText.text = chosen.description;

        if (nextButton != null)
            nextButton.interactable = true;
    }

    private void OnDisable()
    {
        // Safety: if the panel is disabled externally, ensure reels are restored.
        reelDisableManager?.EnableReels();
    }

    private void OnNextClicked()
    {
        Debug.Log($"[UI][AbilityUpgradePanel] Next clicked hero='{(_hero!=null?_hero.name:"<null>")}'");
        if (nextButton != null)
            nextButton.interactable = false;

        _onDone?.Invoke();
    }
}


////////////////////////////////////////////////////////////
