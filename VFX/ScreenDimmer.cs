using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class ScreenDimmer : MonoBehaviour
{
    public Image dimmingImage; // Drag your UI Image here in the Inspector
    public float dimSpeed = 0.5f;

    // Example method to dim the screen to a specific alpha
    public void DimScreenTo(float targetAlpha)
    {
        StartCoroutine(FadeRoutine(targetAlpha));
    }

    private IEnumerator FadeRoutine(float targetAlpha)
    {
        Color startColor = dimmingImage.color;
        Color targetColor = new Color(startColor.r, startColor.g, startColor.b, targetAlpha);
        float timer = 0;

        while (timer < 1)
        {
            timer += Time.deltaTime * dimSpeed;
            dimmingImage.color = Color.Lerp(startColor, targetColor, timer);
            yield return null;
        }
        dimmingImage.color = targetColor;
    }
}
