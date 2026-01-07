using UnityEngine;

public class TopStatusBar : MonoBehaviour
{
    [Header("Resource Slots (wire these in the Inspector)")]
    [SerializeField] private ResourceSlot attackSlot;
    [SerializeField] private ResourceSlot defenseSlot;
    [SerializeField] private ResourceSlot magicSlot;
    [SerializeField] private ResourceSlot wildSlot;

    [Header("Player Vitals Bars (HP + Stamina)")]
    [SerializeField] private PlayerVitalsBarUI vitalsBar;

    [Header("Test Mode")]
    [SerializeField] private bool useTestValues = false;
    [SerializeField] private float testRefreshInterval = 0.25f;

    [Header("Temp Test Values")]
    [SerializeField] private long testAttack = 12;
    [SerializeField] private long testDefense = 8;
    [SerializeField] private long testMagic = 5;
    [SerializeField] private long testWild = 2;

    [Header("Temp Vitals Test Values")]
    [Range(0f, 1f)] [SerializeField] private float testHP01 = 1.0f;
    [Range(0f, 1f)] [SerializeField] private float testStamina01 = 1.0f;

    private float _nextRefreshTime;

    private long _attack;
    private long _defense;
    private long _magic;
    private long _wild;

    private float _hp01 = 1f;
    private float _stamina01 = 1f;

    private void OnEnable()
    {
        RefreshUI();
        _nextRefreshTime = Time.unscaledTime + testRefreshInterval;
    }

    private void Update()
    {
        if (!useTestValues) return;

        if (Time.unscaledTime >= _nextRefreshTime)
        {
            testAttack = 10 + (long)(Mathf.PingPong(Time.unscaledTime * 2f, 20f));
            testDefense = 6 + (long)(Mathf.PingPong(Time.unscaledTime * 1.5f, 14f));
            testMagic = 3 + (long)(Mathf.PingPong(Time.unscaledTime * 1.1f, 10f));
            testWild = 1 + (long)(Mathf.PingPong(Time.unscaledTime * 0.9f, 6f));

            testHP01 = 0.35f + 0.65f * Mathf.PingPong(Time.unscaledTime * 0.15f, 1f);
            testStamina01 = Mathf.PingPong(Time.unscaledTime * 0.8f, 1f);

            RefreshUI();
            _nextRefreshTime = Time.unscaledTime + testRefreshInterval;
        }
    }

    public void SetValues(long attack, long defense, long magic, long wild, float hp01, float stamina01)
    {
        _attack = attack;
        _defense = defense;
        _magic = magic;
        _wild = wild;

        _hp01 = Mathf.Clamp01(hp01);
        _stamina01 = Mathf.Clamp01(stamina01);

        if (!useTestValues)
            RefreshUI();
    }

    public void AddResources(long addAttack, long addDefense, long addMagic, long addWild)
    {
        _attack += addAttack;
        _defense += addDefense;
        _magic += addMagic;
        _wild += addWild;

        if (!useTestValues)
            RefreshUI();
    }

    public long GetAttack() => _attack;
    public long GetDefense() => _defense;
    public long GetMagic() => _magic;
    public long GetWild() => _wild;

    public void RefreshUI()
    {
        if (useTestValues)
        {
            SetSlotValue(attackSlot, testAttack);
            SetSlotValue(defenseSlot, testDefense);
            SetSlotValue(magicSlot, testMagic);
            SetSlotValue(wildSlot, testWild);

            if (vitalsBar != null)
            {
                vitalsBar.SetHP01(testHP01);
                vitalsBar.SetStamina01(testStamina01);
            }

            return;
        }

        SetSlotValue(attackSlot, _attack);
        SetSlotValue(defenseSlot, _defense);
        SetSlotValue(magicSlot, _magic);
        SetSlotValue(wildSlot, _wild);

        if (vitalsBar != null)
        {
            vitalsBar.SetHP01(_hp01);
            vitalsBar.SetStamina01(_stamina01);
        }
    }

    private static void SetSlotValue(ResourceSlot slot, long value)
    {
        if (slot == null) return;
        slot.SetValue(value);
    }
}
