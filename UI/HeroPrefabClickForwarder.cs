using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Allows clicking a hero's world prefab (via its Collider/Collider2D) to forward interactions to PartyHUD.
/// - Click: normal hero select/open behavior
/// - Click + hold: open hero info panel
///
/// Requirements:
/// - EventSystem in the scene
/// - Physics2DRaycaster (2D colliders) or PhysicsRaycaster (3D colliders) on the camera
/// </summary>
public class HeroPrefabClickForwarder : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Header("Optional Fallback")]
    [Tooltip("If HeroStats cannot be found on this prefab, we'll use this party index.")]
    [SerializeField] private int fallbackPartyIndex = -1;

    [Header("Info Panel Hold")]
    [SerializeField] private float infoPanelHoldSeconds = 0.35f;

    private bool _pointerDown;
    private bool _holdFired;
    private float _pointerDownTime;

    private PartyHUD _partyHud;
    private HeroStats _heroStats;

    private void Awake()
    {
        // Prefer HeroStats mapping so we don't need inspector wiring.
        _heroStats = GetComponentInParent<HeroStats>();
        _partyHud = FindFirstObjectByType<PartyHUD>(FindObjectsInactive.Include);
    }

    private void Update()
    {
        if (_pointerDown && !_holdFired)
        {
            if (Time.unscaledTime - _pointerDownTime >= infoPanelHoldSeconds)
            {
                _holdFired = true;
                TryOpenInfoPanel();
            }
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _pointerDown = true;
        _holdFired = false;
        _pointerDownTime = Time.unscaledTime;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!_pointerDown)
            return;

        _pointerDown = false;

        // If we already fired the hold action, do not also treat this as a click.
        if (_holdFired)
            return;

        TryHandleClick();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _pointerDown = false;
        _holdFired = false;
    }

    private void TryHandleClick()
    {
        if (_partyHud == null)
            _partyHud = FindFirstObjectByType<PartyHUD>(FindObjectsInactive.Include);

        if (_partyHud == null)
            return;

        if (_heroStats == null)
            _heroStats = GetComponentInParent<HeroStats>();

        if (_heroStats != null)
        {
            _partyHud.HandleHeroPrefabClicked(_heroStats);
            return;
        }

        if (fallbackPartyIndex >= 0)
            _partyHud.HandleHeroPrefabClicked(fallbackPartyIndex);
    }

    private void TryOpenInfoPanel()
    {
        if (_partyHud == null)
            _partyHud = FindFirstObjectByType<PartyHUD>(FindObjectsInactive.Include);

        if (_partyHud == null)
            return;

        if (_heroStats == null)
            _heroStats = GetComponentInParent<HeroStats>();

        if (_heroStats != null)
        {
            _partyHud.HandleHeroPrefabHeld(_heroStats);
            return;
        }

        if (fallbackPartyIndex >= 0)
            _partyHud.HandleHeroPrefabHeld(fallbackPartyIndex);
    }
}
