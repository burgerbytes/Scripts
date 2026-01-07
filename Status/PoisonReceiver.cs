// PATH: Assets/Scripts/Status/PoisonReceiver.cs
using System.Collections;
using UnityEngine;

public class PoisonReceiver : MonoBehaviour
{
    [Header("Runtime (debug)")]
    [SerializeField] private int stacks;
    [SerializeField] private int dpsPerStack;
    [SerializeField] private float secondsRemaining;

    private Coroutine _tick;

    public void ApplyPoison(int addStacks, int dps, float durationSeconds)
    {
        addStacks = Mathf.Max(0, addStacks);
        dps = Mathf.Max(0, dps);
        durationSeconds = Mathf.Max(0f, durationSeconds);

        if (addStacks <= 0 || dps <= 0 || durationSeconds <= 0f)
            return;

        stacks += addStacks;
        dpsPerStack = Mathf.Max(dpsPerStack, dps);
        secondsRemaining = Mathf.Max(secondsRemaining, durationSeconds);

        if (_tick == null)
            _tick = StartCoroutine(Tick());
    }

    private IEnumerator Tick()
    {
        while (secondsRemaining > 0f && stacks > 0)
        {
            yield return new WaitForSeconds(1f);

            secondsRemaining -= 1f;

            // If your Monster has a TakeDamage method, use it.
            Monster m = GetComponentInParent<Monster>();
            if (m != null && !m.IsDead)
            {
                int dmg = Mathf.Max(1, stacks * dpsPerStack);
                m.TakeDamage(dmg);
            }
        }

        stacks = 0;
        dpsPerStack = 0;
        secondsRemaining = 0f;
        _tick = null;
    }
}
