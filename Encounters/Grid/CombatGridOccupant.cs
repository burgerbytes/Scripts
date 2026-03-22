using UnityEngine;

public class CombatGridOccupant : MonoBehaviour
{
    [Header("Grid Anchor")]
    [SerializeField] private Transform centerPoint;

    public Vector2Int GridPosition { get; private set; }

    public Transform CenterPoint
    {
        get
        {
            if (centerPoint != null)
                return centerPoint;

            Transform found = transform.Find("CenterPoint");
            if (found != null)
            {
                centerPoint = found;
                return centerPoint;
            }

            return transform;
        }
    }

    public void SetGridPosition(Vector2Int gridPos)
    {
        GridPosition = gridPos;
    }

    public void SnapToWorldPositionUsingCenterPoint(Vector3 targetWorldPos)
    {
        Transform anchor = CenterPoint;
        Vector3 delta = targetWorldPos - anchor.position;
        transform.position += delta;
    }
}