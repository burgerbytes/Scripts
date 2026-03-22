using UnityEngine;

public class FieldObjectClickReceiver : MonoBehaviour
{
    [SerializeField] private FieldObjectInstance fieldObject;

    private void Awake()
    {
        if (fieldObject == null)
            fieldObject = GetComponentInParent<FieldObjectInstance>();
    }

    public FieldObjectInstance FieldObject => fieldObject;
}
