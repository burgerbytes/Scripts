using UnityEngine;
using UnityEngine.UI;

public class PlayerVitalsBarUI : MonoBehaviour
{
    [SerializeField] private Image hpFill;
    [SerializeField] private Image staminaFill;

    public void SetHP01(float hp01)
    {
        if (hpFill == null) return;
        hpFill.fillAmount = Mathf.Clamp01(hp01);
    }

    public void SetStamina01(float stamina01)
    {
        if (staminaFill == null) return;
        staminaFill.fillAmount = Mathf.Clamp01(stamina01);
    }
}
