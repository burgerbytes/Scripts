// PATH: Assets/Scripts/UI/RuntimeDamageNumber.cs
// Simple world-space floating damage number.
// Used as a fallback when BattleManager.damageNumberPrefab is not assigned.

using UnityEngine;
using TMPro;

public sealed class RuntimeDamageNumber : MonoBehaviour
{
    private Camera _cam;
    private TextMeshPro _tmp;

    private float _lifetime = 0.75f;
    private float _riseDistance = 0.8f;

    private float _startTime;
    private Vector3 _startPos;
    private Vector3 _endPos;

    public void Initialize(Camera cam, float lifetime, float riseDistance)
    {
        _cam = cam;
        _lifetime = Mathf.Max(0.05f, lifetime);
        _riseDistance = Mathf.Max(0f, riseDistance);

        _tmp = GetComponent<TextMeshPro>();

        _startTime = Time.time;
        _startPos = transform.position;
        _endPos = _startPos + Vector3.up * _riseDistance;
    }

    private void Awake()
    {
        _tmp = GetComponent<TextMeshPro>();
    }

    private void LateUpdate()
    {
        float t = (Time.time - _startTime) / _lifetime;
        if (t >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        // Ease-out rise
        float eased = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 2f);
        transform.position = Vector3.LerpUnclamped(_startPos, _endPos, eased);

        // Billboard towards camera (if available)
        if (_cam != null)
        {
            Vector3 toCam = transform.position - _cam.transform.position;
            if (toCam.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(toCam);
        }

        // Fade out
        if (_tmp != null)
        {
            Color c = _tmp.color;
            c.a = 1f - Mathf.Clamp01(t);
            _tmp.color = c;
        }
    }
}
