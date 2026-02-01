using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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

    // For "buttons open on click"
    private bool pendingClickOnButton;
    private InfoPanelData pendingClickData;
    private GameObject pendingClickSourceGO;

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

            // If we pressed down on a UI Button with InfoPanelContent, allow click-to-open on mouse up.
            pendingClickOnButton = false;
            pendingClickSourceGO = null;
            pendingClickData = default;

            if (TryGetInspectableUnderCursor(out var go, out var data, out var isUIButton) && isUIButton)
            {
                pendingClickOnButton = true;
                pendingClickSourceGO = go;
                pendingClickData = data;

                if (logFlow)
                    Debug.Log($"{LOG_TAG} CLICK CANDIDATE (UIButton) '{go.name}'", this);
            }

            if (logFlow)
                Debug.Log($"{LOG_TAG} HOLD START", this);
        }

        if (Input.GetMouseButtonUp(0))
        {
            // If this was a simple click (hold not triggered) and we pressed on a UI Button, open now.
            if (!triggered && pendingClickOnButton && pendingClickSourceGO != null)
            {
                if (logFlow)
                    Debug.Log($"{LOG_TAG} CLICK OPEN (UIButton) '{pendingClickSourceGO.name}'", this);

                ShowOrOpenFallback(pendingClickSourceGO, pendingClickData);
            }

            holding = false;
            holdTimer = 0f;
            triggered = false;

            pendingClickOnButton = false;
            pendingClickSourceGO = null;
            pendingClickData = default;

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
        // "Any object can be inspectable":
        // If anything under cursor has InfoPanelContent, show it.
        if (TryGetInspectableUnderCursor(out var go, out var data, out var isUIButton))
        {
            if (logFlow)
                Debug.Log($"{LOG_TAG} INSPECT '{go.name}' (isUIButton={isUIButton})", this);

            ShowOrOpenFallback(go, data);
            return;
        }

        if (logFlow)
            Debug.Log($"{LOG_TAG} Nothing inspectable under cursor", this);
    }

    /// <summary>
    /// Finds an inspectable under the cursor.
    /// Priority: UI (EventSystem raycast) -> World 2D collider (Physics2D raycast).
    /// Inspectable = anything with InfoPanelContent in parents.
    /// Also returns whether that inspectable is a UI Button (for click-to-open behavior).
    /// </summary>
    private bool TryGetInspectableUnderCursor(out GameObject sourceGO, out InfoPanelData data, out bool isUIButton)
    {
        sourceGO = null;
        data = default;
        isUIButton = false;

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
                if (result.gameObject == null)
                    continue;

                if (TryGetInfoFromGO(result.gameObject, out var uiData, out var contentGO))
                {
                    sourceGO = contentGO;
                    data = uiData;

                    // "Button" means a Unity UI Button is present on the object or its parents.
                    var btn = result.gameObject.GetComponentInParent<Button>();
                    isUIButton = (btn != null);

                    return true;
                }
            }
        }

        // 2) World raycast (2D)
        Camera cam = worldCamera != null ? worldCamera : Camera.main;
        if (cam == null)
        {
            Debug.LogWarning($"{LOG_TAG} No world camera assigned and Camera.main is null. Cannot raycast 2D world.", this);
            return false;
        }

        Vector3 screen = Input.mousePosition;
        Vector3 worldPoint = cam.ScreenToWorldPoint(screen);
        Vector2 worldPoint2D = new Vector2(worldPoint.x, worldPoint.y);

        RaycastHit2D hit2D = Physics2D.Raycast(worldPoint2D, Vector2.zero, 0f, world2DMask);

        if (hit2D.collider != null)
        {
            if (TryGetInfoFromGO(hit2D.collider.gameObject, out var worldData, out var contentGO))
            {
                sourceGO = contentGO;
                data = worldData;
                isUIButton = false;
                return true;
            }

            if (logFlow)
                Debug.Log($"{LOG_TAG} World(2D) hit '{hit2D.collider.name}' but no InfoPanelContent found in parents", this);
        }
        else
        {
            if (logFlow)
                Debug.Log($"{LOG_TAG} World(2D) hit nothing", this);
        }

        return false;
    }

    /// <summary>
    /// Looks for InfoPanelContent in parents and extracts InfoPanelData.
    /// Returns contentGO as the GameObject that actually owns the InfoPanelContent.
    /// </summary>
    private bool TryGetInfoFromGO(GameObject go, out InfoPanelData data, out GameObject contentGO)
    {
        data = default;
        contentGO = null;

        if (go == null)
            return false;

        var content = go.GetComponentInParent<InfoPanelContent>();
        if (content == null)
            return false;

        contentGO = content.gameObject;

        // Assumes your InfoPanelContent has TryGetData(out InfoPanelData)
        if (content.TryGetData(out data))
            return true;

        // If it exists but returns false, we still treat it as inspectable,
        // but will fall back to opening the panel.
        return true;
    }

    private void ShowOrOpenFallback(GameObject sourceGO, InfoPanelData data)
    {
        if (infoPanel == null)
        {
            Debug.LogWarning($"{LOG_TAG} InfoPanelController reference missing", this);
            return;
        }

        // If InfoPanelContent exists but has empty data, fall back to just opening.
        // (InfoPanelController.Show may also handle empty fine, but this keeps behavior predictable.)
        bool hasTitle = !string.IsNullOrWhiteSpace(data.title);
        bool hasBody = !string.IsNullOrWhiteSpace(data.body);
        bool hasImage = (data.image != null);

        if (hasTitle || hasBody || hasImage)
        {
            if (logFlow)
                Debug.Log($"{LOG_TAG} SHOW InfoPanel data from '{(sourceGO != null ? sourceGO.name : "null")}'", this);

            infoPanel.Show(data);
        }
        else
        {
            if (logFlow)
                Debug.Log($"{LOG_TAG} OPEN InfoPanel (no data) source='{(sourceGO != null ? sourceGO.name : "null")}'", this);

            infoPanel.Open();
        }
    }
}
