using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Quick-toggle button for Enemy Intent lines that does NOT require EnemyIntentVisualizer
/// to expose ToggleIntentLines/IsShowingIntentLines at compile-time.
/// Works with:
///  - public void SetShowIntentLines(bool)
///  - or a private/serialized bool field named "showIntentLines"
///  - or falls back to enabling/disabling child LineRenderers.
/// </summary>
public class EnemyIntentLinesToggleButton : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Drag your EnemyIntentVisualizer here. If left null, this script will find one in the scene at runtime.")]
    [SerializeField] private MonoBehaviour visualizerBehaviour;

    [SerializeField] private Button button;

    [Header("Optional Label")]
    [SerializeField] private TMP_Text label;
    [SerializeField] private string onText = "Intent Lines: ON";
    [SerializeField] private string offText = "Intent Lines: OFF";

    private MethodInfo _setShowMethod;
    private FieldInfo _showField;

    private void Awake()
    {
        if (!button) button = GetComponent<Button>();
        if (button) button.onClick.AddListener(OnClick);
    }

    private void Start()
    {
        if (!visualizerBehaviour)
        {
            // Avoid hard-typing EnemyIntentVisualizer so this still compiles even if its API changes.
            var found = FindFirstObjectByType<EnemyIntentVisualizer>();
            if (found) visualizerBehaviour = found;
        }

        CacheReflection();
        RefreshLabel();
    }

    private void OnDestroy()
    {
        if (button) button.onClick.RemoveListener(OnClick);
    }

    private void CacheReflection()
    {
        _setShowMethod = null;
        _showField = null;

        if (!visualizerBehaviour) return;

        var t = visualizerBehaviour.GetType();

        // Prefer a public (or non-public) instance method: SetShowIntentLines(bool)
        _setShowMethod = t.GetMethod(
            "SetShowIntentLines",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(bool) },
            modifiers: null
        );

        // Also try to read a backing field named "showIntentLines"
        _showField = t.GetField("showIntentLines", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    }

    private void OnClick()
    {
        if (!visualizerBehaviour) return;

        bool current = GetCurrentShowState();
        bool next = !current;
        ApplyShowState(next);
        RefreshLabel();
    }

    private bool GetCurrentShowState()
    {
        if (!visualizerBehaviour) return true;

        if (_showField != null && _showField.FieldType == typeof(bool))
        {
            try { return (bool)_showField.GetValue(visualizerBehaviour); }
            catch { /* ignore */ }
        }

        // If we can't read a field, infer from child LineRenderers
        var lrs = visualizerBehaviour.GetComponentsInChildren<LineRenderer>(true);
        if (lrs != null && lrs.Length > 0)
        {
            // Consider "on" if any are enabled
            foreach (var lr in lrs)
                if (lr && lr.enabled) return true;
            return false;
        }

        // Default
        return true;
    }

    private void ApplyShowState(bool show)
    {
        if (!visualizerBehaviour) return;

        // Best: call SetShowIntentLines(bool) if present
        if (_setShowMethod != null)
        {
            try
            {
                _setShowMethod.Invoke(visualizerBehaviour, new object[] { show });
                return;
            }
            catch { /* fall through */ }
        }

        // Next: set field directly if present
        if (_showField != null && _showField.FieldType == typeof(bool))
        {
            try
            {
                _showField.SetValue(visualizerBehaviour, show);
            }
            catch { /* ignore */ }
        }

        // Fallback: just enable/disable child LineRenderers
        foreach (var lr in visualizerBehaviour.GetComponentsInChildren<LineRenderer>(true))
        {
            if (lr) lr.enabled = show;
        }
    }

    private void RefreshLabel()
    {
        if (!label) return;

        bool isOn = GetCurrentShowState();
        label.text = isOn ? onText : offText;
    }
}
