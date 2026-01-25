using UnityEngine;
using UnityEngine.EventSystems;

public class HoldToOpenInfoPanel : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Header("Refs")]
    [Tooltip("Drag the InfoPanelController component here.")]
    [SerializeField] private InfoPanelController infoPanel;

    [Header("Settings")]
    [SerializeField] private float holdSeconds = 0.5f;
    [SerializeField] private bool logFlow = true;

    private InfoPanelController _controller;
    private bool _holding;
    private float _t;

    private const string TAG = "[InfoPanel][Hold]";

    private void Awake()
    {
        ResolveController();
    }

    private void OnValidate()
    {
        if (holdSeconds < 0f) holdSeconds = 0f;
        ResolveController();
    }

    private void ResolveController()
    {
        _controller = null;

        if (infoPanel == null) return;

        // Direct cast
        _controller = infoPanel as InfoPanelController;
        if (_controller != null) return;

        // If they dragged some other component on the controller GO, try to fetch InfoPanelController from that GO
        _controller = infoPanel.GetComponent<InfoPanelController>();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        if (_controller == null)
            ResolveController();

        if (_controller == null)
        {
            if (logFlow) Debug.LogWarning($"{TAG} No InfoPanelController assigned on '{name}'.", this);
            return;
        }

        _holding = true;
        _t = 0f;

        if (logFlow) Debug.Log($"{TAG} START source='{gameObject.name}' holdSeconds={holdSeconds:0.00}", this);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!_holding) return;
        _holding = false;

        if (logFlow) Debug.Log($"{TAG} UP source='{gameObject.name}' elapsed={_t:0.00}", this);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!_holding) return;
        _holding = false;

        if (logFlow) Debug.Log($"{TAG} EXIT/CANCEL source='{gameObject.name}' elapsed={_t:0.00}", this);
    }

    private void Update()
    {
        if (!_holding) return;
        if (_controller == null) return;

        _t += Time.unscaledDeltaTime;

        if (_t >= holdSeconds)
        {
            _holding = false;

            if (logFlow) Debug.Log($"{TAG} COMPLETE -> Open InfoPanel source='{gameObject.name}' elapsed={_t:0.00}", this);
            _controller.Open();
        }
    }
}
