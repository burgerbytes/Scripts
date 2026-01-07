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

    [Header("Resource Source (authoritative)")]
    [SerializeField] private ResourcePool resourcePool;

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

    private float _hp01 = 1f;
    private float _stamina01 = 1f;

    private void Awake()
    {
        if (resourcePool == null)
            resourcePool = ResourcePool.Instance != null ? ResourcePool.Instance : FindFirstObjectByType<ResourcePool>();
    }

    private void OnEnable()
    {
        if (resourcePool != null)
            resourcePool.OnChanged += RefreshUI;

        RefreshUI();
        _nextRefreshTime = Time.unscaledTime + testRefreshInterval;
    }

    private void OnDisable()
    {
        if (resourcePool != null)
            resourcePool.OnChanged -= RefreshUI;
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

    // Kept for compatibility; now writes to the authoritative pool.
    public void SetValues(long attack, long defense, long magic, long wild, float hp01, float stamina01)
    {
        _hp01 = Mathf.Clamp01(hp01);
        _stamina01 = Mathf.Clamp01(stamina01);

        if (!useTestValues && resourcePool != null)
            resourcePool.ResetForNewRun(attack, defense, magic, wild);

        RefreshUI();
    }

    // Kept for compatibility; now adds to the authoritative pool.
    public void AddResources(long addAttack, long addDefense, long addMagic, long addWild)
    {
        if (!useTestValues && resourcePool != null)
            resourcePool.Add(addAttack, addDefense, addMagic, addWild);

        RefreshUI();
    }

    // These now reflect authoritative values
    public long GetAttack() => useTestValues ? testAttack : (resourcePool != null ? resourcePool.Attack : 0);
    public long GetDefense() => useTestValues ? testDefense : (resourcePool != null ? resourcePool.Defense : 0);
    public long GetMagic() => useTestValues ? testMagic : (resourcePool != null ? resourcePool.Magic : 0);
    public long GetWild() => useTestValues ? testWild : (resourcePool != null ? resourcePool.Wild : 0);

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

        long a = resourcePool != null ? resourcePool.Attack : 0;
        long d = resourcePool != null ? resourcePool.Defense : 0;
        long m = resourcePool != null ? resourcePool.Magic : 0;
        long w = resourcePool != null ? resourcePool.Wild : 0;

        SetSlotValue(attackSlot, a);
        SetSlotValue(defenseSlot, d);
        SetSlotValue(magicSlot, m);
        SetSlotValue(wildSlot, w);

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
