using UnityEngine;

public class RotateVFX : MonoBehaviour
{
    public float rotationSpeed = 180f;

    void Update()
    {
        transform.Rotate(Vector3.forward, rotationSpeed * Time.deltaTime);

        float scale = 1f + Mathf.Sin(Time.time * 6f) * 0.05f;
        transform.localScale = Vector3.one * scale;
    }
}
