using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Interactive reel-upgrade minigame shown after the Results panel.
/// Shows a single hero portrait + their reel.
/// Player presses Spin -> reel spins -> landed symbol is upgraded.
/// Then Spin is disabled and Next appears.
/// </summary>
public class PostBattleReelUpgradeMinigamePanel : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject root;
    [SerializeField] private Image portraitImage;

    [Header("Reel")]
    [Tooltip("3D reel column used for the minigame.")]
    [SerializeField] private Reel3DColumn reel3d;

    [Tooltip("Midrow plane/collider used to detect which quad is in the window.")]
    [SerializeField] private GameObject midrowPlane;

    [Header("Buttons")]
    [SerializeField] private Button spinButton;
    [SerializeField] private Button nextButton;

    [Header("Spin")]
    [SerializeField] private int minFullRotations3D = 1;

    [Header("Upgrade Feedback")]
    [SerializeField] private float shakeDuration = 0.12f;
    [SerializeField] private float shakeMagnitude = 6f;
    [SerializeField] private float popScale = 1.12f;
    [SerializeField] private float popDuration = 0.18f;

    private HeroStats _hero;
    private Action _onDone;
    private bool _spun;
    private System.Random _rng;

    private Coroutine _spinRoutine;

    // We restore whatever we disable when leaving the minigame.
    private readonly List<GameObject> _disabledOtherReelObjects = new List<GameObject>();
    private bool _othersHidden;

    private void Awake()
    {
        if (root == null) root = gameObject;
        if (spinButton != null) spinButton.onClick.AddListener(OnSpinPressed);
        if (nextButton != null) nextButton.onClick.AddListener(OnNextPressed);

        if (_rng == null)
            _rng = new System.Random(unchecked(Environment.TickCount * 31 + (int)(Time.realtimeSinceStartup * 1000f)));

        HideImmediate();
    }

    private void OnDestroy()
    {
        if (spinButton != null) spinButton.onClick.RemoveListener(OnSpinPressed);
        if (nextButton != null) nextButton.onClick.RemoveListener(OnNextPressed);
    }

    public void Show(HeroStats hero, Action onDone)
    {
        _hero = hero;
        _onDone = onDone;
        _spun = false;

        if (_spinRoutine != null)
        {
            StopCoroutine(_spinRoutine);
            _spinRoutine = null;
        }

        if (root != null && !root.activeSelf) root.SetActive(true);
        gameObject.SetActive(true);

        // Hard-hide any other 3D reels in the scene so they don't visually overlap or
        // interfere with midrow detection.
        HideOtherReels(true);

        // Ensure reel objects are enabled (you said they can be disabled in inspector)
        if (reel3d != null && !reel3d.gameObject.activeSelf) reel3d.gameObject.SetActive(true);
        if (midrowPlane != null && !midrowPlane.activeSelf) midrowPlane.SetActive(true);

        if (portraitImage != null)
        {
            portraitImage.sprite = hero != null ? hero.Portrait : null;
            portraitImage.enabled = portraitImage.sprite != null;
            portraitImage.preserveAspect = true;
        }

        ReelStripSO strip = hero != null ? hero.ReelStrip : null;
        if (reel3d != null && strip != null)
            reel3d.SetStrip(strip, rebuildNow: true);

        if (spinButton != null)
            spinButton.interactable = (hero != null && hero.HasPendingReelUpgrades);

        if (nextButton != null)
            nextButton.gameObject.SetActive(false);

        Debug.Log($"[LevelUpReel] Show hero='{(hero != null ? hero.name : "NULL")}', pendingUpgrades={(hero != null ? hero.PendingReelUpgrades : 0)}");
    }

    public void Hide()
    {
        if (_spinRoutine != null)
        {
            StopCoroutine(_spinRoutine);
            _spinRoutine = null;
        }

        // Disable the reel objects when leaving the minigame
        if (reel3d != null) reel3d.gameObject.SetActive(false);
        if (midrowPlane != null) midrowPlane.SetActive(false);

        // Restore other reels
        HideOtherReels(false);

        if (root != null) root.SetActive(false);
        gameObject.SetActive(false);
    }

    private void HideImmediate()
    {
        if (nextButton != null) nextButton.gameObject.SetActive(false);
        if (root != null) root.SetActive(false);
        gameObject.SetActive(false);
    }

    private void OnSpinPressed()
    {
        if (_spun) return;
        if (_hero == null || !_hero.HasPendingReelUpgrades) return;
        if (reel3d == null) return;

        _spinRoutine = StartCoroutine(SpinAndUpgradeRoutine());
    }

    private IEnumerator SpinAndUpgradeRoutine()
    {
        _spun = true;
        if (spinButton != null) spinButton.interactable = false;

        Debug.Log($"[LevelUpReel] Spin start hero='{_hero.name}' minFullRotations3D={minFullRotations3D}");

        reel3d.SpinRandom(_rng, minFullRotations3D);
        yield return new WaitUntil(() => !reel3d.IsSpinning);

        // Let any final snap/settle complete before sampling intersection.
        yield return null;

        int quadIndex;
        ReelSymbolSO landedSym = reel3d.GetMidrowSymbolByIntersection(midrowPlane, out quadIndex);
        if (quadIndex < 0) quadIndex = 0;

        Debug.Log(
            $"[LevelUpReel] Spin complete hero='{_hero.name}' " +
            $"quadIndex={quadIndex} landed='{(landedSym != null ? landedSym.name : "NULL")}'"
        );

        ReelSymbolSO fromSym, toSym;
        bool upgraded = _hero.TryApplyPendingReelUpgradeFromQuadIndex(quadIndex, out fromSym, out toSym);

        if (upgraded)
        {
            Debug.Log(
                $"[LevelUpReel] Upgrade applied hero='{_hero.name}' " +
                $"from='{(fromSym != null ? fromSym.name : "NULL")}' to='{(toSym != null ? toSym.name : "NULL")}' " +
                $"quadIndex={quadIndex}"
            );

            // Live in-place replacement of the landed quad (no rebuild, no reel jump)
            if (toSym != null)
            {
                ReelSymbolSO visualBefore = reel3d.GetSymbolAtQuadIndex(quadIndex);
                Debug.Log($"[LevelUpReel] LiveReplace quadIndex={quadIndex} visualBefore='{(visualBefore != null ? visualBefore.name : "NULL")}' -> '{toSym.name}'");
                reel3d.ReplaceSymbolAtQuadIndex(quadIndex, toSym);
            }
            yield return ShakeAndPopRoutine(reel3d.transform);
        }
        else
        {
            Debug.Log(
                $"[LevelUpReel] No upgrade applied hero='{_hero.name}' " +
                $"landed='{(landedSym != null ? landedSym.name : "NULL")}' quadIndex={quadIndex} " +
                $"pendingUpgradesNow={_hero.PendingReelUpgrades}"
            );
        }

        if (nextButton != null) nextButton.gameObject.SetActive(true);

        _spinRoutine = null;
    }

    private IEnumerator ShakeAndPopRoutine(Transform t)
    {
        if (t == null) yield break;

        Vector3 basePos = t.localPosition;
        Vector3 baseScale = t.localScale;

        float st = 0f;
        while (st < shakeDuration)
        {
            st += Time.deltaTime;
            float mag = shakeMagnitude;
            float x = UnityEngine.Random.Range(-mag, mag);
            float y = UnityEngine.Random.Range(-mag, mag);
            t.localPosition = basePos + new Vector3(x, y, 0f) * 0.01f;
            yield return null;
        }
        t.localPosition = basePos;

        float half = Mathf.Max(0.0001f, popDuration * 0.5f);
        float tt = 0f;
        Vector3 up = baseScale * popScale;

        while (tt < half)
        {
            tt += Time.deltaTime;
            float a = Mathf.Clamp01(tt / half);
            t.localScale = Vector3.Lerp(baseScale, up, a);
            yield return null;
        }

        tt = 0f;
        while (tt < half)
        {
            tt += Time.deltaTime;
            float a = Mathf.Clamp01(tt / half);
            t.localScale = Vector3.Lerp(up, baseScale, a);
            yield return null;
        }

        t.localScale = baseScale;
    }

    private void OnNextPressed()
    {
        Debug.Log($"[LevelUpReel] Next pressed hero='{(_hero != null ? _hero.name : "NULL")}' -> disabling reel objects");

        // Disable the reel objects when leaving the minigame (per your requirement)
        if (reel3d != null) reel3d.gameObject.SetActive(false);
        if (midrowPlane != null) midrowPlane.SetActive(false);

        // Restore any other reels we hid while the minigame was showing.
        HideOtherReels(false);

        _onDone?.Invoke();
    }

    private void HideOtherReels(bool hide)
    {
        if (hide)
        {
            if (_othersHidden) return;
            _othersHidden = true;

            _disabledOtherReelObjects.Clear();

            // Disable ALL other Reel3DColumns in the scene (combat reels), keep only the minigame reel enabled.
            Reel3DColumn[] allReels = FindObjectsOfType<Reel3DColumn>(true);
            for (int i = 0; i < allReels.Length; i++)
            {
                Reel3DColumn r = allReels[i];
                if (r == null) continue;
                if (reel3d != null && r == reel3d) continue;

                if (r.gameObject.activeSelf)
                {
                    r.gameObject.SetActive(false);
                    _disabledOtherReelObjects.Add(r.gameObject);
                }
            }

            Debug.Log($"[LevelUpReel] Hid other reels: {_disabledOtherReelObjects.Count} reel objects.");
        }
        else
        {
            if (!_othersHidden) return;
            _othersHidden = false;

            for (int i = 0; i < _disabledOtherReelObjects.Count; i++)
            {
                if (_disabledOtherReelObjects[i] != null)
                    _disabledOtherReelObjects[i].SetActive(true);
            }

            _disabledOtherReelObjects.Clear();
            Debug.Log("[LevelUpReel] Restored other reels.");
        }
    }
}
