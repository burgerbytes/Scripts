using UnityEngine;

public class ResourceBarPopupSpawner : MonoBehaviour
{
    [SerializeField] private ResourceGainPopup popupPrefab;

    [Header("Resource Anchors")]
    [SerializeField] private Transform attackAnchor;
    [SerializeField] private Transform defenseAnchor;
    [SerializeField] private Transform magicAnchor;
    [SerializeField] private Transform wildAnchor;

    [Header("Popup Colors")]
    [SerializeField] private Color attackColor = new Color(1f, 0.35f, 0.35f, 1f);
    [SerializeField] private Color defenseColor = new Color(0.35f, 0.75f, 1f, 1f);
    [SerializeField] private Color magicColor = new Color(0.75f, 0.45f, 1f, 1f);
    [SerializeField] private Color wildColor = new Color(1f, 0.9f, 0.35f, 1f);

    [Header("Resource Icons (optional)")]
    [Tooltip("Sprite shown beside +amount for Attack.")]
    [SerializeField] private Sprite attackIcon;

    [Tooltip("Sprite shown beside +amount for Defense.")]
    [SerializeField] private Sprite defenseIcon;

    [Tooltip("Sprite shown beside +amount for Magic.")]
    [SerializeField] private Sprite magicIcon;

    [Tooltip("Sprite shown beside +amount for Wild.")]
    [SerializeField] private Sprite wildIcon;

    private void OnEnable()
    {
        ResourcePool.OnResourceAdded += HandleResourceAdded;
    }

    private void OnDisable()
    {
        ResourcePool.OnResourceAdded -= HandleResourceAdded;
    }

    private void HandleResourceAdded(ResourceType type, long amount)
    {
        if (amount <= 0) return;
        if (popupPrefab == null) return;

        Transform anchor = GetAnchor(type);
        if (anchor == null) return;

        ResourceGainPopup popup = Instantiate(popupPrefab, anchor);
        popup.transform.localPosition = Vector3.zero;

        popup.Initialize(amount, GetColor(type), GetIcon(type));
    }

    private Transform GetAnchor(ResourceType type)
    {
        return type switch
        {
            ResourceType.Attack => attackAnchor,
            ResourceType.Defense => defenseAnchor,
            ResourceType.Magic => magicAnchor,
            ResourceType.Wild => wildAnchor,
            _ => null
        };
    }

    private Color GetColor(ResourceType type)
    {
        return type switch
        {
            ResourceType.Attack => attackColor,
            ResourceType.Defense => defenseColor,
            ResourceType.Magic => magicColor,
            ResourceType.Wild => wildColor,
            _ => Color.white
        };
    }

    private Sprite GetIcon(ResourceType type)
    {
        return type switch
        {
            ResourceType.Attack => attackIcon,
            ResourceType.Defense => defenseIcon,
            ResourceType.Magic => magicIcon,
            ResourceType.Wild => wildIcon,
            _ => null
        };
    }
}
