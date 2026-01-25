using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class InfoPanelHoldManager : MonoBehaviour
{
    private const string LOG_TAG = "[InfoPanel][HoldMgr]";

    [Header("References")]
    [SerializeField] private InfoPanelController infoPanel;

    [Tooltip("Camera used for world (2D) raycasts. If null, uses Camera.main.")]
    [SerializeField] private Camera worldCamera;

    [Header("Hold Settings")]
    [SerializeField] private float holdSeconds = 0.5f;
    [SerializeField] private bool logFlow = true;

    [Header("World (2D) Raycast")]
    [Tooltip("Optional: limit raycast to certain layers (e.g., Monsters/Interactables). Leave Everything if unsure.")]
    [SerializeField] private LayerMask world2DMask = ~0;

    private bool holding;
    private float holdTimer;
    private bool triggered;

    private void Update()
    {
        HandleHoldInput();
    }

    private void HandleHoldInput()
    {
        if (Input.GetMouseButtonDown(0))
        {
            holding = true;
            triggered = false;
            holdTimer = 0f;

            if (logFlow)
                Debug.Log($"{LOG_TAG} HOLD START", this);
        }

        if (Input.GetMouseButtonUp(0))
        {
            holding = false;
            holdTimer = 0f;
            triggered = false;
            return;
        }

        if (!holding || triggered)
            return;

        holdTimer += Time.deltaTime;

        if (holdTimer >= holdSeconds)
        {
            triggered = true;

            if (logFlow)
                Debug.Log($"{LOG_TAG} HOLD COMPLETE (threshold reached)", this);

            TryOpenInfoPanel();
        }
    }

    private void TryOpenInfoPanel()
    {
        // 1) UI raycast first
        if (EventSystem.current != null)
        {
            var eventData = new PointerEventData(EventSystem.current)
            {
                position = Input.mousePosition
            };

            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);

            foreach (var result in results)
            {
                // Ability
                var abilityBtn = result.gameObject.GetComponentInParent<AbilityButtonUI>();
                if (abilityBtn != null)
                {
                    if (logFlow)
                        Debug.Log($"{LOG_TAG} UI hit AbilityButtonUI '{abilityBtn.name}'", this);

                    OpenInfoPanel();
                    return;
                }

                // Hero slot
                var heroSlot = result.gameObject.GetComponentInParent<PartyHUDSlot>();
                if (heroSlot != null)
                {
                    if (logFlow)
                        Debug.Log($"{LOG_TAG} UI hit PartyHUDSlot index={heroSlot.PartyIndex}", this);

                    OpenInfoPanel();
                    return;
                }
            }
        }

        // 2) World raycast (2D)
        Camera cam = worldCamera != null ? worldCamera : Camera.main;
        if (cam == null)
        {
            Debug.LogWarning($"{LOG_TAG} No world camera assigned and Camera.main is null. Cannot raycast 2D world.", this);
            return;
        }

        Vector3 screen = Input.mousePosition;
        Vector3 worldPoint = cam.ScreenToWorldPoint(screen);
        Vector2 worldPoint2D = new Vector2(worldPoint.x, worldPoint.y);

        RaycastHit2D hit2D = Physics2D.Raycast(worldPoint2D, Vector2.zero, 0f, world2DMask);

        if (hit2D.collider != null)
        {
            var monster = hit2D.collider.GetComponentInParent<Monster>();
            if (monster != null)
            {
                if (logFlow)
                    Debug.Log($"{LOG_TAG} World(2D) hit Monster '{monster.name}' via collider='{hit2D.collider.name}'", this);

                OpenInfoPanel();
                return;
            }

            // Hero (world)
            var heroStats = hit2D.collider.GetComponentInParent<HeroStats>();
            if (heroStats != null)
            {
                if (logFlow)
                    Debug.Log($"{LOG_TAG} World(2D) hit HeroStats heroGO='{heroStats.gameObject.name}' via collider='{hit2D.collider.name}'", this);

                OpenInfoPanel();
                return;
            }

            if (logFlow)
                Debug.Log($"{LOG_TAG} World(2D) hit '{hit2D.collider.name}' but no Monster/Hero found in parents", this);

        }
        else
        {
            if (logFlow)
                Debug.Log($"{LOG_TAG} World(2D) hit nothing", this);
        }

        if (logFlow)
            Debug.Log($"{LOG_TAG} Nothing inspectable under cursor", this);
    }

    private void OpenInfoPanel()
    {
        if (infoPanel == null)
        {
            Debug.LogWarning($"{LOG_TAG} InfoPanelController reference missing", this);
            return;
        }

        if (logFlow)
            Debug.Log($"{LOG_TAG} OPEN InfoPanel", this);

        infoPanel.Open();
    }
}
