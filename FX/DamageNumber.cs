using TMPro;
using UnityEngine;

public class DamageNumber : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private TMP_Text text;

    [Header("Motion")]
    [SerializeField] private Vector3 worldMovePerSecond = new Vector3(0f, 1.0f, 0f);

    [Header("Timing")]
    [SerializeField] private float lifetime = 0.6f;

    [Header("Fade")]
    [SerializeField] private bool fadeOut = true;

    private float _t;
    private Color _startColor;

    private void Awake()
    {
        if (text == null)
            text = GetComponent<TMP_Text>();

        if (text == null)
            text = GetComponentInChildren<TMP_Text>(true);

        if (text != null)
            _startColor = text.color;
    }

    public void Init(int amount)
    {
        if (text == null) return;

        text.text = amount.ToString();

        _startColor = text.color;

        var c = _startColor;
        c.a = 1f;
        text.color = c;

        _t = 0f;
    }

    private void Update()
    {
        _t += Time.deltaTime;

        transform.position += worldMovePerSecond * Time.deltaTime;

        if (fadeOut && text != null)
        {
            float a = Mathf.Clamp01(1f - (_t / lifetime));
            var c = _startColor;
            c.a = a;
            text.color = c;
        }

        if (_t >= lifetime)
            Destroy(gameObject);
    }
}
