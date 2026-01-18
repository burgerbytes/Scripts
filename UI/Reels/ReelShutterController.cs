using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Simple helper that "closes" a pair of UI shutters over the reels after the player cashes out.
///
/// Goals:
/// - When Cashout is pressed, hide/disable the reel area (3D reels handled by ReelSpinSystem) and
///   disable Spin + Cashout buttons.
/// - Reveal a "post-spin" space below the reels where we can place Ability UI and a Stats panel.
///
/// This is intentionally animation-agnostic:
/// - If an Animator is provided, we trigger it.
/// - Otherwise, we just toggle the shutters GameObject on/off.
///
/// Wiring (recommended):
/// - Put this on a UI object near your reel panel.
/// - Create a "Shutters" GameObject (two images, left/right) and assign it to shuttersRoot.
/// - Optionally add an Animator to shuttersRoot and set the trigger names below.
/// - Assign spinButton (the main spin button) and cashoutButton (same as ReelSpinSystem.stopSpinningButton).
/// - Assign a contentRoot transform that is the container shown when shutters close.
///   Then assign abilityUIRoot + statsUIRoot to be reparented into that container.
/// </summary>
public class ReelShutterController : MonoBehaviour
{
    [Header("Shutter Visuals")]
    [SerializeField] private GameObject shuttersRoot;
    [SerializeField] private Animator shuttersAnimator;

    [Tooltip("Animator trigger fired when shutters close.")]
    [SerializeField] private string closeTrigger = "Close";

    [Tooltip("Animator trigger fired when shutters open.")]
    [SerializeField] private string openTrigger = "Open";

    [Tooltip("If no animator is provided, we simply SetActive(true/false) on shuttersRoot.")]
    [SerializeField] private bool fallbackToggleActiveIfNoAnimator = true;

    [Header("Buttons To Disable")]
    [SerializeField] private Button spinButton;
    [SerializeField] private Button cashoutButton;

    [Header("Post-Spin Space")]
    [Tooltip("Container that becomes relevant/visible when shutters close.")]
    [SerializeField] private GameObject postSpinSpaceRoot;

    [Tooltip("Optional: Ability menu root to move into the post-spin space.")]
    [SerializeField] private RectTransform abilityUIRoot;

    [Tooltip("Optional: Stats panel root to move into the post-spin space.")]
    [SerializeField] private RectTransform statsUIRoot;

    [Tooltip("Parent within postSpinSpaceRoot that ability UI should live under.")]
    [SerializeField] private RectTransform abilityDestination;

    [Tooltip("Parent within postSpinSpaceRoot that stats UI should live under.")]
    [SerializeField] private RectTransform statsDestination;

    [Header("Behavior")]
    [SerializeField] private bool startOpen = true;
    [SerializeField] private float animatorSafetyDelaySeconds = 0.05f;

    private Transform _abilityOriginalParent;
    private int _abilityOriginalSibling;
    private Transform _statsOriginalParent;
    private int _statsOriginalSibling;

    private bool _isClosed;

    private void Awake()
    {
        if (shuttersAnimator == null && shuttersRoot != null)
            shuttersAnimator = shuttersRoot.GetComponent<Animator>();

        CacheOriginalParents();

        if (startOpen)
            OpenShutters();
        else
            CloseShutters();
    }

    private void CacheOriginalParents()
    {
        if (abilityUIRoot != null)
        {
            _abilityOriginalParent = abilityUIRoot.parent;
            _abilityOriginalSibling = abilityUIRoot.GetSiblingIndex();
        }

        if (statsUIRoot != null)
        {
            _statsOriginalParent = statsUIRoot.parent;
            _statsOriginalSibling = statsUIRoot.GetSiblingIndex();
        }
    }

    public bool IsClosed => _isClosed;

    public void CloseShutters()
    {
        _isClosed = true;

        // Disable controls.
        if (spinButton != null) spinButton.interactable = false;
        if (cashoutButton != null) cashoutButton.interactable = false;

        // Ensure post-spin space is enabled.
        if (postSpinSpaceRoot != null)
            postSpinSpaceRoot.SetActive(true);

        // Move UI into the space.
        ReparentIntoPostSpinSpace();

        // Visual close.
        if (shuttersRoot != null)
        {
            if (shuttersAnimator != null)
            {
                shuttersAnimator.ResetTrigger(openTrigger);
                shuttersAnimator.SetTrigger(closeTrigger);
            }
            else if (fallbackToggleActiveIfNoAnimator)
            {
                shuttersRoot.SetActive(true);
            }
        }
    }

    public void OpenShutters()
    {
        _isClosed = false;

        // Re-enable controls.
        if (spinButton != null) spinButton.interactable = true;
        if (cashoutButton != null) cashoutButton.interactable = true;

        // Restore UI to original layout.
        RestoreOriginalParents();

        // Hide post-spin space.
        if (postSpinSpaceRoot != null)
            postSpinSpaceRoot.SetActive(false);

        // Visual open.
        if (shuttersRoot != null)
        {
            if (shuttersAnimator != null)
            {
                shuttersAnimator.ResetTrigger(closeTrigger);
                shuttersAnimator.SetTrigger(openTrigger);
                // Some animator graphs rely on 1 frame to update; keep the object enabled.
                StartCoroutine(AnimatorSafetyDelayThenOptionallyHide());
            }
            else if (fallbackToggleActiveIfNoAnimator)
            {
                shuttersRoot.SetActive(false);
            }
        }
    }

    private IEnumerator AnimatorSafetyDelayThenOptionallyHide()
    {
        yield return new WaitForSeconds(animatorSafetyDelaySeconds);

        // If you want shuttersRoot hidden after open animation, you can keep this true.
        if (!_isClosed && fallbackToggleActiveIfNoAnimator && shuttersAnimator != null && shuttersRoot != null)
        {
            // Leave active by default so the animation can play; comment this out if you prefer.
            // shuttersRoot.SetActive(false);
        }
    }

    private void ReparentIntoPostSpinSpace()
    {
        if (abilityUIRoot != null && abilityDestination != null)
            abilityUIRoot.SetParent(abilityDestination, worldPositionStays: false);

        if (statsUIRoot != null && statsDestination != null)
            statsUIRoot.SetParent(statsDestination, worldPositionStays: false);
    }

    private void RestoreOriginalParents()
    {
        if (abilityUIRoot != null && _abilityOriginalParent != null)
        {
            abilityUIRoot.SetParent(_abilityOriginalParent, worldPositionStays: false);
            abilityUIRoot.SetSiblingIndex(Mathf.Clamp(_abilityOriginalSibling, 0, _abilityOriginalParent.childCount - 1));
        }

        if (statsUIRoot != null && _statsOriginalParent != null)
        {
            statsUIRoot.SetParent(_statsOriginalParent, worldPositionStays: false);
            statsUIRoot.SetSiblingIndex(Mathf.Clamp(_statsOriginalSibling, 0, _statsOriginalParent.childCount - 1));
        }
    }
}
