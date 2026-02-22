using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Allows clicking a hero's world prefab (via its BoxCollider2D/Collider) to open the same
/// hero ability panel you normally open by clicking the PartyHUD portrait.
/// 
/// Requirements:
/// - EventSystem in the scene
/// - Physics2DRaycaster (2D colliders) or PhysicsRaycaster (3D colliders) on the camera
/// </summary>
public class HeroPrefabClickForwarder : MonoBehaviour, IPointerClickHandler
{
    [Header("Optional Fallback")]
    [Tooltip("If HeroStats cannot be found on this prefab, we'll use this party index.")]
    [SerializeField] private int fallbackPartyIndex = -1;

    private PartyHUD _partyHud;
    private HeroStats _heroStats;

    private void Awake()
    {
        // Prefer HeroStats mapping so we don't need inspector wiring.
        _heroStats = GetComponentInParent<HeroStats>();
        _partyHud = FindFirstObjectByType<PartyHUD>(FindObjectsInactive.Include);
    }

    public void OnPointerClick(PointerEventData eventData)
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
}
