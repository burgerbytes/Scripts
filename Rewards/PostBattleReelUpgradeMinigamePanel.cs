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
///
/// NEW (Level 5 Evolution):
/// - If the hero is eligible to evolve (for now: Fighter at level 5, no advanced class yet),
///   pressing Spin triggers an accelerated/glowing spin, then a pop that swaps the reel visuals
///   to the advanced class reel. Simultaneously, a hero preview swaps from Fighter -> Templar.
/// - When Next is pressed, the party member prefab is swapped in BattleManager so the evolved
///   hero is used for battle going forward.
/// </summary>
public class PostBattleReelUpgradeMinigamePanel : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject root;
    [SerializeField] private Image portraitImage;

    [Header("Scene Refs")]
    [SerializeField] private BattleManager battleManager;

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

    [Header("Evolution (Level 5)")]
    [SerializeField] private bool enableEvolutionAtLevel5 = true;
    [SerializeField] private int evolutionLevel = 5;

    [Tooltip("Optional: assign your Fighter base class definition for strict matching.")]
    [SerializeField] private ClassDefinitionSO fighterBaseClassDef;

    [Tooltip("For testing: Fighter evolves into this Advanced class (Templar).")]
    [SerializeField] private ClassDefinitionSO templarAdvancedClassDef;

    [Tooltip("Prefab used for the evolved hero in battle (Templar).")]
    [SerializeField] private GameObject templarBattlePrefab;

    [Tooltip("Reel strip template for the evolved class (Templar). If null, the reel won't swap visually.")]
    [SerializeField] private ReelStripSO templarReelStripTemplate;

    [Tooltip("Optional: portrait override applied to the hero after evolution.")]
    [SerializeField] private Sprite templarPortraitOverride;

    [Tooltip("Optional: world sprite override applied to the evolved hero's SpriteRenderer(s). Useful if the prefab sprite is not set yet.")]
    [SerializeField] private Sprite templarWorldSpriteOverride;

    [Header("Evolution VFX (Reel)")]
    [SerializeField] private float evolveSpinRampSeconds = 1.2f;
    [SerializeField] private float evolveMaxSpinSpeedMultiplier = 6f;
    [SerializeField] private float evolvePopHoldSeconds = 0.10f;

    [Header("Hero Preview (Panel)")]
    [Tooltip("Where the hero preview prefab should be spawned.")]
    [SerializeField] private Transform heroPreviewAnchor;

    [Tooltip("If set, we spawn this prefab as the preview when the hero is a Fighter (instead of cloning the in-scene avatar).")]
    [SerializeField] private GameObject fighterPreviewPrefab;

    [Tooltip("Optional: preview prefab for the evolved class (Templar). If null, we reuse templarBattlePrefab.")]
    [SerializeField] private GameObject templarPreviewPrefab;

    [Tooltip("Local position offset applied to the preview under the anchor.")]
    [SerializeField] private Vector3 heroPreviewLocalOffset = Vector3.zero;

    [Tooltip("Local rotation applied to the preview under the anchor.")]
    [SerializeField] private Vector3 heroPreviewLocalEuler = Vector3.zero;

    [Tooltip("Local scale applied to the preview under the anchor.")]
    [SerializeField] private Vector3 heroPreviewLocalScale = Vector3.one;

    [Header("Hero Preview Glow")]
    [SerializeField] private float previewGlowRampSeconds = 0.6f;

    private HeroStats _hero;
    private Action _onDone;
    private bool _spun;
    private System.Random _rng;

    private Coroutine _spinRoutine;

    // We restore whatever we disable when leaving the minigame.
    private readonly List<GameObject> _disabledOtherReelObjects = new List<GameObject>();
    private bool _othersHidden;

    // Evolution runtime
    private int _partyIndex = -1;
    private bool _evolutionEligible;
    private bool _pendingEvolutionApply;

    // Preview runtime
    private GameObject _previewGO;
    private readonly List<Renderer> _previewRenderers = new List<Renderer>(32);

    private float _baseReelSpinSpeed;

    // Persistent trace logging (helps when the editor hard-freezes before Console can be read).
    // Writes to: Application.persistentDataPath/levelup_reel_trace.txt
    private string _tracePath;

    private void Trace(string msg)
    {
        try
        {
            if (string.IsNullOrEmpty(_tracePath))
                _tracePath = System.IO.Path.Combine(Application.persistentDataPath, "levelup_reel_trace.txt");

            // Include realtime timestamp so we can see where it stopped.
            string line = $"[{Time.realtimeSinceStartup:F3}] {msg}";
            System.IO.File.AppendAllText(_tracePath, line + "\n");
        }
        catch
        {
            // Never let tracing break gameplay.
        }
    }

    private void Awake()
    {
        if (battleManager == null)
            battleManager = FindFirstObjectByType<BattleManager>();
        if (root == null) root = gameObject;

        // Defensive: the Button may also have inspector-assigned listeners.
        // If any of those listeners contain an accidental infinite loop (or a heavy synchronous call),
        // the editor can appear to hard-freeze as soon as the button is pressed.
        // We explicitly own these buttons while this panel is active.
        if (spinButton != null)
        {
            spinButton.onClick.RemoveAllListeners();
            spinButton.onClick.AddListener(OnSpinPressed);
        }

        if (nextButton != null)
        {
            nextButton.onClick.RemoveAllListeners();
            nextButton.onClick.AddListener(OnNextPressed);
        }

        // Init trace path early.
        _tracePath = System.IO.Path.Combine(Application.persistentDataPath, "levelup_reel_trace.txt");
        Trace($"Awake panel='{name}' persistentDataPath='{Application.persistentDataPath}'");

        if (_rng == null)
            _rng = new System.Random(unchecked(Environment.TickCount * 31 + (int)(Time.realtimeSinceStartup * 1000f)));

        if (reel3d != null)
            _baseReelSpinSpeed = reel3d.SpinDegreesPerSecond;

        // Auto-wire heroPreviewAnchor if not set (safe fallback for older scenes).
        if (heroPreviewAnchor == null)
        {
            var t = transform.Find("HeroPreviewAnchor");
            if (t == null)
                t = transform.Find("HeroPreview");
            if (t == null)
            {
                // Search by name contains (case-insensitive)
                foreach (var tr in GetComponentsInChildren<Transform>(true))
                {
                    var n = tr.name.ToLowerInvariant();
                    if (n.Contains("heropreview") && (n.Contains("anchor") || n.EndsWith("heropreview")))
                    {
                        t = tr;
                        break;
                    }
                }
            }

            heroPreviewAnchor = t;
            Debug.Log($"[Evolution] AutoFind heroPreviewAnchor -> '{(heroPreviewAnchor != null ? heroPreviewAnchor.name : "NULL")}'", this);
        }

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
        _pendingEvolutionApply = false;

        if (_spinRoutine != null)
        {
            StopCoroutine(_spinRoutine);
            _spinRoutine = null;
        }

        if (root != null && !root.activeSelf) root.SetActive(true);
        gameObject.SetActive(true);

        // Determine party index + evolution eligibility
        _partyIndex = (battleManager != null) ? battleManager.GetPartyIndexForHeroStats(hero) : -1;
        _evolutionEligible = ComputeEvolutionEligible(hero);

        Debug.Log(
            $"[Evolution] PanelShow hero='{(hero != null ? hero.name : "NULL")}' level={(hero != null ? hero.Level : 0)} " +
            $"baseClass='{(hero != null && hero.BaseClassDef != null ? hero.BaseClassDef.className : "NULL")}' " +
            $"advancedClass='{(hero != null && hero.AdvancedClassDef != null ? hero.AdvancedClassDef.className : "NULL")}' " +
            $"eligible={_evolutionEligible} partyIndex={_partyIndex}",
            this
        );

        // Hide ally sprites during the reel upgrade minigame; only the relevant hero portrait/preview should be shown.
        if (battleManager != null)
            battleManager.SetPartyAvatarsActive(false);

        // Hard-hide any other 3D reels in the scene so they don't visually overlap or interfere with midrow detection.
        HideOtherReels(true);

        // Ensure reel objects are enabled (they can be disabled in inspector)
        if (reel3d != null && !reel3d.gameObject.activeSelf) reel3d.gameObject.SetActive(true);
        if (midrowPlane != null && !midrowPlane.activeSelf) midrowPlane.SetActive(true);

        // Portrait
        if (portraitImage != null)
        {
            portraitImage.sprite = hero != null ? hero.Portrait : null;
            portraitImage.enabled = portraitImage.sprite != null;
            portraitImage.preserveAspect = true;
        }

        // Reel
        ReelStripSO strip = hero != null ? hero.ReelStrip : null;
        if (reel3d != null && strip != null)
        {
            reel3d.SetStrip(strip, rebuildNow: true);
            reel3d.SetGlobalWhiteGlow(0f);
            _baseReelSpinSpeed = reel3d.SpinDegreesPerSecond;
        }

        // Preview
        BuildHeroPreview();

        if (spinButton != null)
            spinButton.interactable = (hero != null && hero.HasPendingReelUpgrades);

        if (nextButton != null)
            nextButton.gameObject.SetActive(false);

        Debug.Log($"[LevelUpReel] Show hero='{(hero != null ? hero.name : "NULL")}', pendingUpgrades={(hero != null ? hero.PendingReelUpgrades : 0)}, level={(hero != null ? hero.Level : 0)}, evolveEligible={_evolutionEligible}, partyIndex={_partyIndex}");
    }

    public void Hide()
    {
        if (_spinRoutine != null)
        {
            StopCoroutine(_spinRoutine);
            _spinRoutine = null;
        }

        DestroyHeroPreview();

        // Disable the reel objects when leaving the minigame
        if (reel3d != null) reel3d.gameObject.SetActive(false);

        // Restore party sprites
        if (battleManager != null)
            battleManager.SetPartyAvatarsActive(true);
        if (midrowPlane != null) midrowPlane.SetActive(false);

        // Restore other reels
        HideOtherReels(false);

        if (root != null) root.SetActive(false);
        gameObject.SetActive(false);
    }

    private void HideImmediate()
    {
        DestroyHeroPreview();

        if (battleManager != null)
            battleManager.SetPartyAvatarsActive(true);

        if (nextButton != null) nextButton.gameObject.SetActive(false);
        if (root != null) root.SetActive(false);
        gameObject.SetActive(false);
    }

    private bool ComputeEvolutionEligible(HeroStats hero)
    {
        if (!enableEvolutionAtLevel5) return false;
        if (hero == null) return false;

        int req = Mathf.Max(1, evolutionLevel);
        if (hero.Level < req)
        {
            Debug.Log($"[Evolution] Not eligible: hero='{hero.name}' level={hero.Level} < required={req}", this);
            return false;
        }

        if (hero.AdvancedClassDef != null)
        {
            Debug.Log($"[Evolution] Not eligible: hero='{hero.name}' already has advancedClass='{hero.AdvancedClassDef.className}'", this);
            return false;
        }

        // For now we only handle Fighter.
        if (fighterBaseClassDef != null)
        {
            bool ok = hero.BaseClassDef == fighterBaseClassDef;
            Debug.Log($"[Evolution] Eligibility strictBaseDef hero='{hero.name}' ok={ok} base='{(hero.BaseClassDef != null ? hero.BaseClassDef.className : "NULL")}'", this);
            return ok;
        }

        // Fallback: match by class name if a base def exists.
        if (hero.BaseClassDef != null && !string.IsNullOrEmpty(hero.BaseClassDef.className))
        {
            bool ok = string.Equals(hero.BaseClassDef.className, "Fighter", StringComparison.OrdinalIgnoreCase);
            Debug.Log($"[Evolution] Eligibility byName hero='{hero.name}' ok={ok} base='{hero.BaseClassDef.className}'", this);
            return ok;
        }

        return false;
    }

    private void OnSpinPressed()
    {
        if (_spun) return;
        if (_hero == null || !_hero.HasPendingReelUpgrades) return;
        if (reel3d == null) return;

        Trace($"SpinPressed hero='{_hero.name}' level={_hero.Level} eligible={_evolutionEligible} pending={_hero.PendingReelUpgrades}");

        Debug.Log(
            $"[Evolution] SpinPressed hero='{_hero.name}' level={_hero.Level} eligible={_evolutionEligible} pendingReelUpgrades={_hero.PendingReelUpgrades}",
            this
        );

        _spinRoutine = StartCoroutine(SpinAndResolveRoutine());
    }

    private IEnumerator SpinAndResolveRoutine()
    {
        _spun = true;
        if (spinButton != null) spinButton.interactable = false;

        Trace($"SpinRoutine begin hero='{(_hero != null ? _hero.name : "NULL")}' evoEligible={_evolutionEligible} minFullRot={minFullRotations3D}");

        Debug.Log($"[LevelUpReel] Spin start hero='{_hero.name}' minFullRotations3D={minFullRotations3D} evolveEligible={_evolutionEligible}");

        // Spin (normal or evolving)
        if (_evolutionEligible)
        {
            Debug.Log("[Evolution] Entering evolving spin routine (speed ramp + reel glow)", this);
            yield return EvolvingSpinRoutine();
        }
        else
        {
            Trace("SpinRoutine normal spin -> reel3d.SpinRandom");
            reel3d.SpinRandom(_rng, minFullRotations3D);

            // Safety: if something prevents the reel spin coroutine from completing (timescale issues,
            // disabled component, etc.), don't hang the minigame forever.
            float startRt = Time.realtimeSinceStartup;
            while (reel3d != null && reel3d.IsSpinning)
            {
                if (Time.realtimeSinceStartup - startRt > 10f)
                {
                    Trace("SpinRoutine TIMEOUT waiting for reel3d.IsSpinning=false (10s). Forcing continue.");
                    Debug.LogWarning("[LevelUpReel] TIMEOUT waiting for reel to stop spinning. Forcing continue.", this);
                    break;
                }
                yield return null;
            }

            Trace($"SpinRoutine normal spin done. IsSpinning={(reel3d != null ? reel3d.IsSpinning : false)}");
        }

        // Let any final snap/settle complete before sampling intersection.
        yield return null;

        int quadIndex;
        ReelSymbolSO landedSym = reel3d.GetMidrowSymbolByIntersection(midrowPlane, out quadIndex);
        if (quadIndex < 0) quadIndex = 0;

        Debug.Log(
            $"[LevelUpReel] Spin complete hero='{_hero.name}' " +
            $"quadIndex={quadIndex} landed='{(landedSym != null ? landedSym.name : "NULL")}'"
        );

        // Apply reel upgrade
        ReelSymbolSO fromSym, toSym;
        int appliedStripIndex;
        bool upgraded = _hero.TryApplyPendingReelUpgradeFromQuadIndex(quadIndex, out fromSym, out toSym, out appliedStripIndex);

        // If the landed symbol wasn't upgradeable, HeroStats may have upgraded a different symbol on the strip.
        int appliedQuadIndex = quadIndex;
        if (appliedStripIndex >= 0 && reel3d != null && reel3d.Strip != null && reel3d.Strip.symbols != null)
        {
            int n = reel3d.Strip.symbols.Count;
            if (n > 0)
            {
                int startStripIndex = ((quadIndex % n) + n) % n;
                int delta = (appliedStripIndex - startStripIndex + n) % n;
                appliedQuadIndex = quadIndex + delta;
            }
        }

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
                ReelSymbolSO visualBefore = reel3d.GetSymbolAtQuadIndex(appliedQuadIndex);
                Debug.Log($"[LevelUpReel] LiveReplace quadIndex={appliedQuadIndex} visualBefore='{(visualBefore != null ? visualBefore.name : "NULL")}' -> '{toSym.name}'");
                reel3d.ReplaceSymbolAtQuadIndex(appliedQuadIndex, toSym);
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

        // Evolution swap (visual + pending party swap)
        if (_evolutionEligible)
        {
            Debug.Log("[Evolution] Starting evolution swap sequence (reel pop + strip swap + preview swap)", this);
            yield return EvolutionSwapRoutine();
            _pendingEvolutionApply = true;

            Debug.Log(
                $"[Evolution] Evolution visuals complete. pendingEvolutionApply={_pendingEvolutionApply} " +
                $"nextWillSwapPartyPrefab={(templarBattlePrefab != null)}",
                this
            );
        }

        if (nextButton != null) nextButton.gameObject.SetActive(true);

        _spinRoutine = null;
    }

    private IEnumerator EvolvingSpinRoutine()
    {
        float baseSpeed = (reel3d != null) ? reel3d.SpinDegreesPerSecond : 720f;
        if (baseSpeed <= 0f) baseSpeed = 720f;

        float maxMult = Mathf.Max(1f, evolveMaxSpinSpeedMultiplier);
        float dur = Mathf.Max(0.05f, evolveSpinRampSeconds);

        // Start spinning with extra steps so we have time to ramp.
        reel3d.SpinRandom(_rng, Mathf.Max(1, minFullRotations3D + 2));

        Debug.Log(
            $"[Evolution] EvolvingSpin start baseSpeed={baseSpeed:F1} dur={dur:F2}s maxMult={maxMult:F2} " +
            $"targetSpeed={(baseSpeed * maxMult):F1}",
            this
        );

        float t = 0f;
        while (reel3d != null && reel3d.IsSpinning)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / dur);

            reel3d.SpinDegreesPerSecond = Mathf.Lerp(baseSpeed, baseSpeed * maxMult, a);
            reel3d.SetGlobalWhiteGlow(a);

            // Lightweight milestone logs (25/50/75/100%).
            if (Mathf.Abs(a - 0.25f) < 0.01f) Debug.Log("[Evolution] EvolvingSpin ramp ~25%", this);
            if (Mathf.Abs(a - 0.50f) < 0.01f) Debug.Log("[Evolution] EvolvingSpin ramp ~50%", this);
            if (Mathf.Abs(a - 0.75f) < 0.01f) Debug.Log("[Evolution] EvolvingSpin ramp ~75%", this);
            if (a >= 0.99f) Debug.Log("[Evolution] EvolvingSpin ramp ~100%", this);

            yield return null;
        }

        // Restore base speed for future spins.
        if (reel3d != null)
        {
            reel3d.SpinDegreesPerSecond = baseSpeed;
            reel3d.SetGlobalWhiteGlow(0f);
        }

        Debug.Log("[Evolution] EvolvingSpin finished. Restored base spin speed + cleared glow.", this);
    }

    private IEnumerator EvolutionSwapRoutine()
    {
        if (reel3d == null) yield break;

        Debug.Log(
            $"[Evolution] EvolutionSwap begin. templarStrip={(templarReelStripTemplate != null ? templarReelStripTemplate.name : "NULL")}",
            this
        );

        // Reel: flash + pop, then swap to advanced strip.
        reel3d.SetGlobalWhiteGlow(1f);

        Debug.Log("[Evolution] Reel glow forced to white (1.0). Holding before pop.", this);

        if (evolvePopHoldSeconds > 0f)
            yield return new WaitForSeconds(evolvePopHoldSeconds);

        if (templarReelStripTemplate != null)
        {
            // Visual swap: show the advanced strip on the minigame reel.
            Debug.Log($"[Evolution] Swapping minigame reel strip -> '{templarReelStripTemplate.name}' (rebuildNow=true)", this);
            reel3d.SetStrip(templarReelStripTemplate, rebuildNow: true);
        }
        else
        {
            Debug.LogWarning("[Evolution] templarReelStripTemplate is NULL - reel will not swap visually.", this);
        }

        yield return ShakeAndPopRoutine(reel3d.transform);

        reel3d.SetGlobalWhiteGlow(0f);

        Debug.Log("[Evolution] Reel pop complete. Cleared reel glow.", this);

        // Preview: glow + swap prefab
        Debug.Log("[Evolution] Preview glow + swap starting.", this);
        yield return PreviewGlowAndSwapRoutine();
        Debug.Log("[Evolution] Preview glow + swap complete.", this);

        // UI: update portrait for the remainder of the panel.
        Sprite newPortrait = templarPortraitOverride;
        if (newPortrait == null && templarAdvancedClassDef != null)
            newPortrait = templarAdvancedClassDef.portraitSprite;

        if (newPortrait != null && portraitImage != null)
        {
            portraitImage.sprite = newPortrait;
            portraitImage.enabled = true;
            Debug.Log($"[Evolution] Portrait updated to '{newPortrait.name}'", this);
        }
        else
        {
            Debug.Log("[Evolution] No portrait override provided; portrait unchanged.", this);
        }
    }

    private IEnumerator PreviewGlowAndSwapRoutine()
    {
        if (heroPreviewAnchor == null) yield break;

        Debug.Log(
            $"[Evolution] PreviewGlowAndSwap begin. currentPreview='{(_previewGO != null ? _previewGO.name : "NULL")}'", 
            this
        );

        // Ramp glow on current preview
        float dur = Mathf.Max(0.05f, previewGlowRampSeconds);
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / dur);
            ApplyPreviewGlow(a);
            yield return null;
        }

        // Swap preview prefab
        GameObject prefab = templarPreviewPrefab != null ? templarPreviewPrefab : templarBattlePrefab;
        if (prefab != null)
        {
            Debug.Log($"[Evolution] Spawning evolved preview prefab '{prefab.name}'", this);
            DestroyHeroPreview();
            SpawnPreviewPrefab(prefab);
            if (templarWorldSpriteOverride != null)
                ApplyWorldSpriteOverride(_previewGO, templarWorldSpriteOverride);
        }
        else
        {
            Debug.LogWarning("[Evolution] No evolved preview prefab available (templarPreviewPrefab and templarBattlePrefab are NULL).", this);
        }

        // Fade glow back down
        t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float a = 1f - Mathf.Clamp01(t / dur);
            ApplyPreviewGlow(a);
            yield return null;
        }

        ApplyPreviewGlow(0f);

        Debug.Log(
            $"[Evolution] PreviewGlowAndSwap end. newPreview='{(_previewGO != null ? _previewGO.name : "NULL")}'", 
            this
        );
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
        Debug.Log($"[LevelUpReel] Next pressed hero='{(_hero != null ? _hero.name : "NULL")}' pendingEvolutionApply={_pendingEvolutionApply} partyIndex={_partyIndex}");

        if (_pendingEvolutionApply)
        {
            Debug.Log(
                $"[Evolution] NextPressed -> will apply party prefab swap now. partyIndex={_partyIndex} " +
                $"advancedPrefab='{(templarBattlePrefab != null ? templarBattlePrefab.name : "NULL")}' " +
                $"advancedClass='{(templarAdvancedClassDef != null ? templarAdvancedClassDef.className : "NULL")}'", 
                this
            );
        }

        // Disable the reel objects when leaving the minigame (per requirement)
        if (reel3d != null) reel3d.gameObject.SetActive(false);
        if (midrowPlane != null) midrowPlane.SetActive(false);

        // Restore any other reels we hid while the minigame was showing.
        HideOtherReels(false);

        // If we did the evolution animation, apply the actual party prefab swap now.
        if (_pendingEvolutionApply && battleManager != null)
        {
            Debug.Log(
                $"[Evolution] Applying party evolution swap now. partyIndex={_partyIndex} advancedPrefab='{(templarBattlePrefab != null ? templarBattlePrefab.name : "NULL")}' " +
                $"advancedClassDef='{(templarAdvancedClassDef != null ? templarAdvancedClassDef.className : "NULL")}' advancedStrip='{(templarReelStripTemplate != null ? templarReelStripTemplate.name : "NULL")}'",
                this
            );

            Sprite p = templarPortraitOverride;
            if (p == null && templarAdvancedClassDef != null)
                p = templarAdvancedClassDef.portraitSprite;

            bool ok = battleManager.EvolvePartyMemberToAdvanced(
                _partyIndex,
                templarBattlePrefab,
                templarAdvancedClassDef,
                templarReelStripTemplate,
                p,
                templarWorldSpriteOverride
            );

            Debug.Log($"[LevelUpReel] Evolution apply result={ok}");

            if (ok)
                Debug.Log("[Evolution] Party prefab swap COMPLETE. Hero should now be the advanced prefab for battle.", this);
            else
                Debug.LogWarning("[Evolution] Party prefab swap FAILED. Check errors above (missing prefab/stats/partyIndex).", this);
        }

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

    // ---------------- Preview Helpers ----------------
    private void BuildHeroPreview()
    {
        if (heroPreviewAnchor == null)
        {
            Debug.LogWarning("[Evolution] heroPreviewAnchor is NULL - preview will not be shown on the upgrade panel.", this);
            return;
        }

        GameObject prefab = null;

        // Prefer explicit preview prefab for Fighter
        if (_evolutionEligible && fighterPreviewPrefab != null)
            prefab = fighterPreviewPrefab;

        // IMPORTANT: Never clone transform.root here.
        // In many scenes the hero's transform.root is the *entire battle scene* (e.g., a parent with
        // BattleManager, UI, reels, enemies, etc.). Instantiating that as a "preview" can hang/freeeze
        // Unity (massive hierarchy clone) and/or cause extreme stalls when we later Destroy() it.
        //
        // We only want the hero avatar itself for the preview.
        if (prefab == null && _hero != null)
            prefab = _hero.gameObject;

        if (prefab == null) return;

        SpawnPreviewPrefab(prefab);
    }

    private void SpawnPreviewPrefab(GameObject prefab)
    {
        if (heroPreviewAnchor == null || prefab == null) return;

        _previewGO = Instantiate(prefab, heroPreviewAnchor);
        _previewGO.transform.localPosition = heroPreviewLocalOffset;
        _previewGO.transform.localRotation = Quaternion.Euler(heroPreviewLocalEuler);
        _previewGO.transform.localScale = heroPreviewLocalScale;

        // Disable gameplay-affecting scripts on the preview copy.
        // (We keep renderers/animators intact, but prevent any Update/OnEnable loops from running.)
        var mbs = _previewGO.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < mbs.Length; i++)
        {
            var mb = mbs[i];
            if (mb == null) continue;
            if (mb is PostBattleReelUpgradeMinigamePanel) continue;

            // Allow Animators to run (not a MonoBehaviour), everything else gets disabled.
            mb.enabled = false;
        }

        // Collect renderers for glow tinting.
        _previewRenderers.Clear();
        _previewRenderers.AddRange(_previewGO.GetComponentsInChildren<Renderer>(true));
        ApplyPreviewGlow(0f);
    }


    private void ApplyWorldSpriteOverride(GameObject go, Sprite sprite)
    {
        if (go == null || sprite == null) return;

        int changed = 0;
        var srs = go.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < srs.Length; i++)
        {
            if (srs[i] == null) continue;
            srs[i].sprite = sprite;
            changed++;
        }

        Debug.Log($"[Evolution] Applied world sprite override '{sprite.name}' to preview GO='{go.name}'. renderersChanged={changed}", this);
    }


    private void DestroyHeroPreview()
    {
        if (_previewGO != null)
            Destroy(_previewGO);

        _previewGO = null;
        _previewRenderers.Clear();
    }

    private void ApplyPreviewGlow(float glow01)
    {
        float a = Mathf.Clamp01(glow01);

        for (int i = 0; i < _previewRenderers.Count; i++)
        {
            Renderer r = _previewRenderers[i];
            if (r == null) continue;

            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);

            // Tint toward white.
            Color baseCol = Color.white;
            if (r.sharedMaterial != null)
            {
                if (r.sharedMaterial.HasProperty("_BaseColor"))
                    baseCol = r.sharedMaterial.GetColor("_BaseColor");
                else if (r.sharedMaterial.HasProperty("_Color"))
                    baseCol = r.sharedMaterial.color;
            }

            Color outCol = Color.Lerp(baseCol, Color.white, a);
            mpb.SetColor("_Color", outCol);
            mpb.SetColor("_BaseColor", outCol);

            // Optional emission (if supported)
            Color emis = Color.white * a;
            mpb.SetColor("_EmissionColor", emis);

            r.SetPropertyBlock(mpb);
        }
    }
}


////////////////////////////////////////////////////////////
