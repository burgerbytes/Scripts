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

    [Header("Stun")]
    [SerializeField] private Color stunnedPortraitTint = new Color(0.55f, 0.55f, 1.0f, 1.0f);

    [Header("Triple Blade (Empowered)")]
    [SerializeField] private Color tripleBladeEmpoweredTint = new Color(1.0f, 0.9f, 0.55f, 1.0f);

    [Header("Texts")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text hpText;
    [SerializeField] private TMP_Text staminaText;
    [SerializeField] private TMP_Text statusText;

    [Header("Block / Shield")]
    [SerializeField] private GameObject blockIcon;
    [SerializeField] private TMP_Text blockValueText;

    [Header("Bars (HP)")]
    [Tooltip("The HP bar foreground (red). We resize its RectTransform width (NOT fillAmount).")]
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

    // Debug/state tracking to avoid log spam.
    private bool _lastShowActualShield = false;
    private int _lastShieldValue = -1;

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

        SetPortrait(null);
    }

    public void Render(
        BattleManager.PartyMemberSnapshot snapshot,
        bool isSelected,
        int incomingDamagePreview)
    {
        if (nameText != null) nameText.text = snapshot.Name ?? $"Ally {partyIndex + 1}";

        // Status-based portrait tint
        if (portraitImage != null)
        {
            if (snapshot.IsStunned)
                portraitImage.color = stunnedPortraitTint;
            else if (snapshot.IsHidden)
                portraitImage.color = hiddenPortraitTint;
            else if (snapshot.IsTripleBladeEmpowered)
                portraitImage.color = tripleBladeEmpoweredTint;
            else
                portraitImage.color = Color.white;
        }

        if (hpText != null) hpText.text = $"{snapshot.HP}/{snapshot.MaxHP}";

        // --- HP current + incoming preview ---
        int currentHP = snapshot.HP;
        int maxHP = Mathf.Max(1, snapshot.MaxHP);

        int incoming = Mathf.Max(0, incomingDamagePreview);
        int predictedHP = Mathf.Max(0, currentHP - incoming);

        float current01 = Mathf.Clamp01((float)currentHP / maxHP);
        float predicted01 = Mathf.Clamp01((float)predictedHP / maxHP);

        // Ensure we have a valid bar width BEFORE resizing any rects
        float barWidth = GetHpBarWidth();

        // ✅ Red HP fill uses the same rect-width logic as the yellow preview
        ApplyBarSegment(
            rect: hpFill != null ? hpFill.rectTransform : null,
            barWidth: barWidth,
            left01: 0f,
            right01: current01,
            stretchFullHeight: true
        );

        // Yellow preview segment shows loss region predicted -> current
        if (incoming > 0 && hpDamagePreviewRect != null && hpBarFullRect != null && predictedHP < currentHP)
        {
            ApplyBarSegment(
                rect: hpDamagePreviewRect,
                barWidth: barWidth,
                left01: predicted01,
                right01: current01,
                stretchFullHeight: true
            );

            // Put yellow on top so it can't be hidden
            hpDamagePreviewRect.SetAsLastSibling();

            float widthPx = Mathf.Max(0f, (current01 - predicted01) * barWidth);
            SetDamagePreviewVisible(widthPx > 0.5f);
        }
        else
        {
            SetDamagePreviewVisible(false);
        }

        // --- Stamina ---
        if (staminaFill != null) staminaFill.fillAmount = snapshot.Stamina01;
        if (staminaText != null) staminaText.text = $"{snapshot.Stamina}/{snapshot.MaxStamina}";

        // --- Status text ---
        if (statusText != null)
        {
            if (snapshot.IsDead) statusText.text = "Status: DEAD";
            else if (snapshot.IsHidden) statusText.text = "Status: HIDDEN";
            else if (snapshot.IsBlocking) statusText.text = "Status: BLOCKING";
            else statusText.text = "Status: READY";
        }

        // --- Block icon ---
        bool showActualShield = snapshot.Shield > 0;
        int shieldValueForUI = showActualShield ? snapshot.Shield : 0;

        if (debugLogs)
        {
            if (showActualShield != _lastShowActualShield || shieldValueForUI != _lastShieldValue)
            {
                Debug.Log($"[PartyHUDSlot][BlockUI] slot={partyIndex} showActual={showActualShield} value={shieldValueForUI}", this);
                _lastShowActualShield = showActualShield;
                _lastShieldValue = shieldValueForUI;
            }
        }

        if (showActualShield)
        {
            if (blockIcon != null) blockIcon.SetActive(true);
            if (blockValueText != null) blockValueText.text = shieldValueForUI.ToString();
        }
        else
        {
            if (blockIcon != null) blockIcon.SetActive(false);
            if (blockValueText != null) blockValueText.text = string.Empty;
        }

        SetSelected(isSelected);

        if (slotButton != null)
            slotButton.interactable = !snapshot.IsDead && !snapshot.IsStunned;
    }

    private float GetHpBarWidth()
    {
        if (hpBarFullRect == null)
            return 0f;

        // Layout groups can report width as 0 unless rebuilt
        LayoutRebuilder.ForceRebuildLayoutImmediate(hpBarFullRect);
        return hpBarFullRect.rect.width;
    }

    /// <summary>
    /// Resizes a rect to fill from left01 to right01 of the bar width.
    /// This is the exact approach used by the yellow damage preview.
    /// </summary>
    private void ApplyBarSegment(RectTransform rect, float barWidth, float left01, float right01, bool stretchFullHeight)
    {
        if (rect == null) return;

        left01 = Mathf.Clamp01(left01);
        right01 = Mathf.Clamp01(right01);

        float leftX = left01 * barWidth;
        float rightX = right01 * barWidth;
        float width = Mathf.Max(0f, rightX - leftX);

        // Anchor to left edge, stretch vertical if desired
        if (stretchFullHeight)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);

            rect.anchoredPosition = new Vector2(leftX, 0f);
            rect.sizeDelta = new Vector2(width, 0f);
        }
        else
        {
            rect.anchorMin = new Vector2(0f, rect.anchorMin.y);
            rect.anchorMax = new Vector2(0f, rect.anchorMax.y);
            rect.pivot = new Vector2(0f, rect.pivot.y);

            rect.anchoredPosition = new Vector2(leftX, rect.anchoredPosition.y);
            rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
        }
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
