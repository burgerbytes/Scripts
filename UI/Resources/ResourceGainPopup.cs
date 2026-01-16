using UnityEngine;
using TMPro;

public class ResourceGainPopup : MonoBehaviour
{
    [Header("Animation")]
    [SerializeField] private float floatDistance = 30f;
    [SerializeField] private float lifetime = 0.75f;

    private TextMeshProUGUI text;
    private Vector3 startPos;
    private float timer;

    public void Initialize(int amount, Color color)
    {
        text = GetComponent<TextMeshProUGUI>();
        startPos = transform.localPosition;

        text.text = $"+{amount}";
        text.color = color;
    }

    private void Update()
    {
        timer += Time.deltaTime;
        float t = timer / lifetime;

        transform.localPosition = startPos + Vector3.up * floatDistance * t;

        Color c = text.color;
        c.a = Mathf.Lerp(1f, 0f, t);
        text.color = c;

        if (timer >= lifetime)
            Destroy(gameObject);
    }
}
