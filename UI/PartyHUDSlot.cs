using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class PartyHUDSlot : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Header("Index")]
    [SerializeField] private int partyIndex = 0;

    [Header("UI Root")]
    [SerializeField] private Button slotButton;

    [Header("Info Panel Hold")]
    [Tooltip("Seconds the pointer must be held down on this slot to trigger the InfoPanel (instead of a normal click).")]
    [SerializeField] private float infoPanelHoldSeconds = 0.35f;

    [Header("Portrait")]
    [Tooltip("Image used to display the hero/class portrait on this slot button.")]
    [SerializeField] private Image portraitImage;

    [Tooltip("Optional fallback portrait if none is set.")]
    [SerializeField] private Sprite fallbackPortrait;

    [Tooltip("If true, portraitImage will be disabled when no portrait is available.")]
    [SerializeField] private bool hidePortraitWhenNull = true;

    [Header("Casting Aura (optional)")]
    [Tooltip("Optional aura GameObject (usually an Image) shown when this hero enters cast state.")]
    [SerializeField] private GameObject castingAuraRoot;

    [Tooltip("If set, we will fade the aura via CanvasGroup (auto-added if missing).")]
    [SerializeField] private bool useCanvasGroupForAura = true;

    [Tooltip("Seconds to fade aura in/out.")]
    [SerializeField] private float auraFadeSeconds = 0.12f;

    [Tooltip("If true, aura pulses (scale) while visible.")]
    [SerializeField] private bool pulseAura = true;

    [Tooltip("Pulse speed (higher = faster).")]
    [SerializeField] private float auraPulseSpeed = 6f;

    [Tooltip("Max scale multiplier at pulse peak.")]
    [SerializeField] private float auraPulseScale = 1.08f;

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

    [Header("Bars (HP Blocks)")]
    [Tooltip("Optional. If assigned, we render HP as discrete blocks (green/missing/orange preview + blue shields).")]
    [SerializeField] private HpBlocksBarUI hpBlocksBar;

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


// Click/hold runtime
private Action<int> _onSlotClicked;
private Action<int> _onSlotHeld;
private bool _pointerDown = false;
private bool _holdFired = false;
private float _pointerDownTime = 0f;

    // Aura runtime
    private Coroutine _auraRoutine;
    private CanvasGroup _auraCg;
    private Vector3 _auraBaseScale = Vector3.one;

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

    public void Initialize(Action<int> onSlotClicked)
    {
        Initialize(onSlotClicked, null);
    }

    public void Initialize(Action<int> onSlotClicked, Action<int> onSlotHeld)
    {
        if (slotButton == null)
            slotButton = GetComponent<Button>();

        _onSlotClicked = onSlotClicked;
        _onSlotHeld = onSlotHeld;

        if (slotButton != null)
        {
            // We handle click vs hold ourselves via pointer events.
            slotButton.onClick.RemoveAllListeners();
        }

        SetSelected(false);
        SetActionPanelVisible(false);
        SetBlockVisualVisible(true);
        SetDamagePreviewVisible(false);
        if (hpBlocksBar != null) hpBlocksBar.Clear();

        SetCastingAuraVisible(false);



    }

private void Update()
{
    // Hold detection uses unscaled time so it still works if Time.timeScale changes during UI.
    if (_pointerDown && !_holdFired)
    {
        if (Time.unscaledTime - _pointerDownTime >= infoPanelHoldSeconds)
        {
            _holdFired = true;
            _onSlotHeld?.Invoke(partyIndex);
        }
    }

    // Aura pulse is handled by the aura coroutine (if enabled).

}

public void OnPointerDown(PointerEventData eventData)
{
    if (slotButton != null && !slotButton.interactable)
        return;

    _pointerDown = true;
    _holdFired = false;
    _pointerDownTime = Time.unscaledTime;
}

public void OnPointerUp(PointerEventData eventData)
{
    if (!_pointerDown) return;

    _pointerDown = false;

    if (!_holdFired)
        _onSlotClicked?.Invoke(partyIndex);
}

public void OnPointerExit(PointerEventData eventData)
{
    // If the user drags off the slot, cancel the pending click/hold.
    _pointerDown = false;
    _holdFired = false;

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
        if (hpBlocksBar != null && hpBlocksBar.IsConfigured)
        {
            // Use block-based HP bar (includes shield blocks appended after max HP).
            // IMPORTANT: preview damage is reduced by shield first (shield absorbs first),
            // then remaining HP damage is shown as orange within the HP region.
            hpBlocksBar.Render(snapshot.MaxHP, snapshot.HP, snapshot.Shield, incomingDamagePreview);

            // Hide legacy bar pieces (if they're still in the prefab).
            if (hpFill != null) hpFill.enabled = false;
            SetDamagePreviewVisible(false);
        }
        else
        {
            // Legacy continuous bar behavior (resize fill + yellow damage segment).
            int currentHP = snapshot.HP;
            int maxHP = Mathf.Max(1, snapshot.MaxHP);

            int incoming = Mathf.Max(0, incomingDamagePreview);
            int predictedHP = Mathf.Max(0, currentHP - incoming);

            float current01 = Mathf.Clamp01((float)currentHP / maxHP);
            float predicted01 = Mathf.Clamp01((float)predictedHP / maxHP);

            float barWidth = GetHpBarWidth();

            if (hpFill != null) hpFill.enabled = true;

            ApplyBarSegment(
                rect: hpFill != null ? hpFill.rectTransform : null,
                barWidth: barWidth,
                left01: 0f,
                right01: current01,
                stretchFullHeight: true
            );

            if (incoming > 0 && hpDamagePreviewRect != null && hpBarFullRect != null && predictedHP < currentHP)
            {
                ApplyBarSegment(
                    rect: hpDamagePreviewRect,
                    barWidth: barWidth,
                    left01: predicted01,
                    right01: current01,
                    stretchFullHeight: true
                );

                hpDamagePreviewRect.SetAsLastSibling();

                float widthPx = Mathf.Max(0f, (current01 - predicted01) * barWidth);
                SetDamagePreviewVisible(widthPx > 0.5f);
            }
            else
            {
                SetDamagePreviewVisible(false);
            }
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

    // -------------------- Casting Aura --------------------

    public void PlayCastingAura(float seconds)
    {
        if (castingAuraRoot == null) return;

        if (_auraRoutine != null)
            StopCoroutine(_auraRoutine);

        _auraRoutine = StartCoroutine(CastingAuraRoutine(Mathf.Max(0.05f, seconds)));
    }

    public void SetCastingAuraVisible(bool visible)
    {
        if (castingAuraRoot == null) return;

        if (_auraRoutine != null)
        {
            StopCoroutine(_auraRoutine);
            _auraRoutine = null;
        }

        castingAuraRoot.SetActive(visible);

        if (visible)
            EnsureAuraSetup();

        if (_auraCg != null)
            _auraCg.alpha = visible ? 1f : 0f;

        if (castingAuraRoot != null)
            castingAuraRoot.transform.localScale = _auraBaseScale;
    }

    private void EnsureAuraSetup()
    {
        if (castingAuraRoot == null) return;

        if (_auraBaseScale == Vector3.one && castingAuraRoot.transform != null)
            _auraBaseScale = castingAuraRoot.transform.localScale;

        if (useCanvasGroupForAura)
        {
            _auraCg = castingAuraRoot.GetComponent<CanvasGroup>();
            if (_auraCg == null)
                _auraCg = castingAuraRoot.AddComponent<CanvasGroup>();
        }
    }

    private IEnumerator CastingAuraRoutine(float seconds)
    {
        EnsureAuraSetup();

        castingAuraRoot.SetActive(true);

        // Fade in
        float t = 0f;
        while (t < auraFadeSeconds)
        {
            t += Time.unscaledDeltaTime;
            float a = (auraFadeSeconds <= 0f) ? 1f : Mathf.Clamp01(t / auraFadeSeconds);
            if (_auraCg != null) _auraCg.alpha = a;
            yield return null;
        }
        if (_auraCg != null) _auraCg.alpha = 1f;

        // Hold + pulse
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;

            if (pulseAura && castingAuraRoot != null)
            {
                float s = 1f + (Mathf.Sin(Time.unscaledTime * auraPulseSpeed) * 0.5f + 0.5f) * (auraPulseScale - 1f);
                castingAuraRoot.transform.localScale = _auraBaseScale * s;
            }

            yield return null;
        }

        // Fade out
        t = 0f;
        while (t < auraFadeSeconds)
        {
            t += Time.unscaledDeltaTime;
            float a = 1f - ((auraFadeSeconds <= 0f) ? 1f : Mathf.Clamp01(t / auraFadeSeconds));
            if (_auraCg != null) _auraCg.alpha = a;
            yield return null;
        }

        if (_auraCg != null) _auraCg.alpha = 0f;
        if (castingAuraRoot != null)
        {
            castingAuraRoot.transform.localScale = _auraBaseScale;
            castingAuraRoot.SetActive(false);
        }

        _auraRoutine = null;
    }

    // -------------------- Existing helpers --------------------

    private float GetHpBarWidth()
    {
        if (hpBarFullRect == null)
            return 0f;

        LayoutRebuilder.ForceRebuildLayoutImmediate(hpBarFullRect);
        return hpBarFullRect.rect.width;
    }

    private void ApplyBarSegment(RectTransform rect, float barWidth, float left01, float right01, bool stretchFullHeight)
    {
        if (rect == null) return;

        left01 = Mathf.Clamp01(left01);
        right01 = Mathf.Clamp01(right01);

        float leftX = left01 * barWidth;
        float rightX = right01 * barWidth;
        float width = Mathf.Max(0f, rightX - leftX);

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

    private void SetBlockVisualVisible(bool visible)
    {
        if (blockIcon != null)
            blockIcon.SetActive(visible);

        if (blockValueText != null && !visible)
            blockValueText.text = string.Empty;
    }
}


