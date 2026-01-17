using UnityEngine;

public class DeactivateOnEnable : MonoBehaviour
{
    [SerializeField] private GameObject[] objectsToDeactivate;

    private void OnEnable()
    {
        if (objectsToDeactivate == null) return;

        foreach (var obj in objectsToDeactivate)
        {
            if (obj != null && obj.activeSelf)
                obj.SetActive(false);
        }
    }
}
