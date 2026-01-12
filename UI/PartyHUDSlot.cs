////////////////////////////////////////////////////////////
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PartyHUDSlot : MonoBehaviour
{
    [Header("Index")]
    [SerializeField] private int partyIndex = 0;

    [Header("UI Root")]
    [SerializeField] private Button slotButton;

    [Header("Portrait")]
    [Tooltip("Image used to display the hero/class portrait on this slot button.")]
    [SerializeField] private Image portraitImage;

    [Tooltip("Optional fallback portrait if none is set.")]
    [SerializeField] private Sprite fallbackPortrait;

    [Tooltip("If true, portraitImage will be disabled when no portrait is available.")]
    [SerializeField] private bool hidePortraitWhenNull = true;

    [Header("Conceal / Hidden")]
    [SerializeField] private Color hiddenPortraitTint = new Color(0.65f, 0.65f, 0.65f, 1f);

    [Header("Texts")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text hpText;
    [SerializeField] private TMP_Text staminaText;
    [SerializeField] private TMP_Text statusText;

    [Header("Block / Shield")]
    [SerializeField] private GameObject blockIcon;
    [SerializeField] private TMP_Text blockValueText;

    [Header("Bars (HP)")]
    [Tooltip("The filled HP bar (red) that shows predicted HP (or current HP when no preview). Must be Image Type=Filled.")]
    [SerializeField] private Image hpFill;

    [Tooltip("A non-filled Image used as a RECT segment to show incoming damage (yellow).")]
    [SerializeField] private RectTransform hpDamagePreviewRect;

    [Tooltip("Optional: Image on the same object as hpDamagePreviewRect, used just to enable/disable.")]
    [SerializeField] private Image hpDamagePreviewImage;

    [Tooltip("The full width rect of the HP bar area (usually the parent of hpFill).")]
    [SerializeField] private RectTransform hpBarFullRect;

    [Header("Bars (Stamina)")]
    [SerializeField] private Image staminaFill;

    [Header("Selection / Panel")]
    [SerializeField] private GameObject selectedHighlight;
    [SerializeField] private GameObject actionPanelRoot;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    public int PartyIndex => partyIndex;
    public RectTransform RectTransform => (RectTransform)transform;


    // Cache to avoid spamming SetActive/logs every RefreshAllSlots.
    private bool _blockDesiredVisible;

    // Debug/state tracking to avoid log spam.
    private bool _lastShowActualShield = false;
    private bool _lastShowPreviewShield = false;
    private int _lastShieldValue = -1;
    /// <summary>
    /// Set the portrait sprite shown on this slot. Call this from PartyHUD / wherever you bind heroes to slots.
    /// </summary>
    public void SetPortrait(Sprite portrait)
    {
        if (portraitImage == null)
            return;

        Sprite s = portrait != null ? portrait : fallbackPortrait;

        if (s == null && hidePortraitWhenNull)
        {
            portraitImage.enabled = false;
            portraitImage.sprite = null;
            return;
        }

        portraitImage.enabled = true;
        portraitImage.sprite = s;

        // Optional: keep portrait proportions sane
        portraitImage.preserveAspect = true;
    }

    public void Initialize(System.Action<int> onSlotClicked)
    {
        if (slotButton == null)
            slotButton = GetComponent<Button>();

        if (slotButton != null)
        {
            slotButton.onClick.RemoveAllListeners();
            slotButton.onClick.AddListener(() => onSlotClicked?.Invoke(partyIndex));
        }

        SetSelected(false);
        SetActionPanelVisible(false);
        SetBlockVisualVisible(true);
        SetDamagePreviewVisible(false);

        // Ensure portrait starts in a reasonable state
        SetPortrait(null);
    }

    public void Render(
        BattleManager.PartyMemberSnapshot snapshot,
        bool isSelected,
        int incomingDamagePreview)
    {
        if (nameText != null) nameText.text = snapshot.Name ?? $"Ally {partyIndex + 1}";

        // Hidden (Conceal) visual: tint portrait gray.
        if (portraitImage != null)
            portraitImage.color = snapshot.IsHidden ? hiddenPortraitTint : Color.white;
        if (hpText != null) hpText.text = $"{snapshot.HP}/{snapshot.MaxHP}";

        // --- HP prediction & damage segment ---
        int currentHP = snapshot.HP;
        int maxHP = Mathf.Max(1, snapshot.MaxHP);

        int incoming = Mathf.Max(0, incomingDamagePreview);
        int predictedHP = Mathf.Max(0, currentHP - incoming);

        float current01 = Mathf.Clamp01((float)currentHP / maxHP);
        float predicted01 = Mathf.Clamp01((float)predictedHP / maxHP);

        // Red HP fill shows predicted when incoming > 0, else current.
        if (hpFill != null)
            hpFill.fillAmount = (incoming > 0) ? predicted01 : current01;

        // Yellow segment shows the "loss" region from predicted -> current.
        if (incoming > 0 && hpDamagePreviewRect != null && hpBarFullRect != null)
        {
            float barWidth = hpBarFullRect.rect.width;

            float left01 = predicted01;  // segment starts at predicted
            float right01 = current01;   // segment ends at current

            float leftX = left01 * barWidth;
            float rightX = right01 * barWidth;

            float width = Mathf.Max(0f, rightX - leftX);

            // Anchors: left/stretchY. We set anchoredPosition.x as left edge, and sizeDelta.x as width.
            hpDamagePreviewRect.anchorMin = new Vector2(0f, hpDamagePreviewRect.anchorMin.y);
            hpDamagePreviewRect.anchorMax = new Vector2(0f, hpDamagePreviewRect.anchorMax.y);
            hpDamagePreviewRect.pivot = new Vector2(0f, hpDamagePreviewRect.pivot.y);

            hpDamagePreviewRect.anchoredPosition = new Vector2(leftX, hpDamagePreviewRect.anchoredPosition.y);
            hpDamagePreviewRect.sizeDelta = new Vector2(width, hpDamagePreviewRect.sizeDelta.y);

            SetDamagePreviewVisible(width > 0.5f);
        }
        else
        {
            SetDamagePreviewVisible(false);
        }

        // --- Stamina ---
        if (staminaFill != null) staminaFill.fillAmount = snapshot.Stamina01;
        if (staminaText != null) staminaText.text = $"{snapshot.Stamina}/{snapshot.MaxStamina}";

        // --- Status ---
        if (statusText != null)
        {
            if (snapshot.IsDead) statusText.text = "Status: DEAD";
            else if (snapshot.IsHidden) statusText.text = "Status: HIDDEN";
            else if (snapshot.IsBlocking) statusText.text = "Status: BLOCKING";
            else statusText.text = "Status: READY";
        }

        // --- Block icon ---
        // Block UI should only appear AFTER the ability has been cast (no preview while targeting).
        bool showActualShield = snapshot.Shield > 0;
        int shieldValueForUI = showActualShield ? snapshot.Shield : 0;

        if (debugLogs)
        {
            if (showActualShield != _lastShowActualShield || shieldValueForUI != _lastShieldValue)
            {
                Debug.Log($"[PartyHUDSlot][BlockUI] slot={partyIndex} showActual={showActualShield} value={shieldValueForUI}", this);
                _lastShowActualShield = showActualShield;
                _lastShieldValue = shieldValueForUI;

                // Keep these in sync to avoid confusing future logs.
                _lastShowPreviewShield = false;
            }
        }

        bool desiredVisible = showActualShield;
        _blockDesiredVisible = desiredVisible;

        // Only toggle actual GameObjects when the desired visibility changes (avoids log spam).
        //SetBlockVisualVisible(desiredVisible);

        if (desiredVisible)
        {
            blockIcon.SetActive(true);
            if (blockValueText != null)
                blockValueText.text = shieldValueForUI.ToString();
        }
        else
        {
            blockIcon.SetActive(false);
            if (blockValueText != null)
                blockValueText.text = string.Empty;
        }

        SetSelected(isSelected);


        if (slotButton != null)
            slotButton.interactable = !snapshot.IsDead;
    }

    private void SetDamagePreviewVisible(bool visible)
    {
        if (hpDamagePreviewRect != null)
            hpDamagePreviewRect.gameObject.SetActive(visible);

        if (hpDamagePreviewImage != null)
            hpDamagePreviewImage.enabled = visible;
    }

    public void SetSelected(bool selected)
    {
        if (selectedHighlight != null)
            selectedHighlight.SetActive(selected);
    }

    public void SetActionPanelVisible(bool visible)
    {
        if (actionPanelRoot != null)
            actionPanelRoot.SetActive(visible);
    }

    public void SetActionButtonsInteractable(bool attackEnabled, bool blockEnabled, bool endTurnEnabled)
    {
        if (actionPanelRoot == null)
            return;

        Button[] buttons = actionPanelRoot.GetComponentsInChildren<Button>(true);
        foreach (Button b in buttons)
        {
            if (b == null) continue;

            string n = b.gameObject.name.ToLowerInvariant();

            if (n.Contains("attack")) b.interactable = attackEnabled;
            else if (n.Contains("block")) b.interactable = blockEnabled;
            else if (n.Contains("end")) b.interactable = endTurnEnabled;
        }
    }


    private void SetBlockVisualVisible(bool visible)
    {
        if (blockIcon != null)
            blockIcon.SetActive(visible);

        if (blockValueText != null)
            blockValueText.gameObject.SetActive(visible);
    }
}

