using UnityEngine;

public class ResourceBarPopupSpawner : MonoBehaviour
{
    [SerializeField] private ResourceGainPopup popupPrefab;

    [Header("Resource Anchors")]
    [SerializeField] private Transform attackAnchor;
    [SerializeField] private Transform defenseAnchor;
    [SerializeField] private Transform magicAnchor;
    [SerializeField] private Transform wildAnchor;

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

        Transform anchor = GetAnchor(type);
        if (anchor == null) return;

        ResourceGainPopup popup =
            Instantiate(popupPrefab, anchor);

        popup.transform.localPosition = Vector3.zero;
        popup.Initialize(amount, GetColor(type));
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
            ResourceType.Attack => Color.red,
            ResourceType.Defense => Color.cyan,
            ResourceType.Magic => Color.magenta,
            ResourceType.Wild => Color.yellow,
            _ => Color.white
        };
    }
}
