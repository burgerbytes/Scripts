using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NewStartupClassSelectionPanel : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject root;

    [Header("Reels (size 3)")]
    [Tooltip("The three visible selection reels.")]
    [SerializeField] private Reel3DColumn[] reels = new Reel3DColumn[3];

    [Tooltip("Shared midrow plane used to query what symbol is currently in the midrow.")]
    [SerializeField] private GameObject midrowPlane;

    [Header("Buttons")]
    [SerializeField] private Button randomizeButton;
    [SerializeField] private Button startButton;

    [Header("Per-Reel Controls (size 3)")]
    [Tooltip("Scroll Up buttons for each reel index 0..2")]
    [SerializeField] private Button[] scrollUpButtons = new Button[3];

    [Tooltip("Scroll Down buttons for each reel index 0..2")]
    [SerializeField] private Button[] scrollDownButtons = new Button[3];

    [Tooltip("UI button placed over each reel's midrow portrait region.")]
    [SerializeField] private Button[] midrowClickButtons = new Button[3];

    [Header("Spin / Nudge Animation")]
    [Tooltip("Base duration used for 1-step nudges. Actual duration is scaled by Scroll Spin Speed.")]
    [SerializeField] private float nudgeDurationSeconds = 0.14f;

    [Tooltip("Higher = faster scroll animation. 1 = baseline.")]
    [SerializeField] private float scrollSpinSpeed = 1.0f;

    [SerializeField] private AnimationCurve nudgeEase;

    [Header("Randomize Spin Settings")]
    [Tooltip("Randomize nudges this many steps minimum per spin pass.")]
    [SerializeField] private int randomizeMinSteps = 6;

    [Tooltip("Randomize nudges this many steps maximum per spin pass.")]
    [SerializeField] private int randomizeMaxSteps = 18;

    [Tooltip("Max attempts per reel to avoid landing on a null character quad.")]
    [SerializeField] private int randomizeMaxAttemptsPerReel = 10;

    [Header("Null Character Detection")]
    [Tooltip("Material name that indicates a 'null character' quad. Example: Null_Char_Reel_Icon")]
    [SerializeField] private string nullCharacterMaterialName = "Null_Char_Reel_Icon";

    [Header("Hero Summary UI")]
    [SerializeField] private TMP_Text heroNameText;
    [SerializeField] private TMP_Text reelcraftNameText;
    [SerializeField] private TMP_Text reelcraftDescText;

    // You said you already added this variable:
    [SerializeField] private Image reelcraftIcon;

    [SerializeField] private TMP_Text startingAbilityHeaderText;
    [SerializeField] private TMP_Text startingAbilityDescText;

    [Header("Reel Symbols Preview")]
    [SerializeField] private Transform reelSymbolsContainer;
    [SerializeField] private Image reelSymbolIconPrefab;
    [SerializeField] private int maxReelSymbolsToShow = 12;
    [SerializeField] private bool showUniqueReelSymbolsOnly = false;

    [Header("Debug")]
    [SerializeField] private bool logFlow = false;

    private GameObject[] _available = Array.Empty<GameObject>();
    private int _partySize = 3;
    private Action<GameObject[]> _onConfirm;

    // symbolId -> prefab index
    private readonly Dictionary<string, int> _symbolIdToPrefabIndex = new Dictionary<string, int>(StringComparer.Ordinal);

    // created symbol icons
    private readonly List<Image> _reelIcons = new List<Image>();

    // drives summary
    private int _activeReelIndex = 0;

    private Coroutine _randomizeRoutine;
    private Coroutine _showRoutine;
    private bool _wired;

    private void Awake()
    {
        WireOnce();
        RefreshPerReelButtonInteractable();
    }

    private void OnDisable()
    {
        if (_randomizeRoutine != null)
        {
            StopCoroutine(_randomizeRoutine);
            _randomizeRoutine = null;
        }

        if (_showRoutine != null)
        {
            StopCoroutine(_showRoutine);
            _showRoutine = null;
        }
    }

    private void WireOnce()
    {
        if (_wired) return;
        _wired = true;

        if (randomizeButton != null)
        {
            randomizeButton.onClick.RemoveAllListeners();
            randomizeButton.onClick.AddListener(OnRandomizePressed);
        }

        if (startButton != null)
        {
            startButton.onClick.RemoveAllListeners();
            startButton.onClick.AddListener(OnStartPressed);
        }

        for (int i = 0; i < 3; i++)
        {
            int idx = i;

            if (scrollUpButtons != null && idx < scrollUpButtons.Length && scrollUpButtons[idx] != null)
            {
                scrollUpButtons[idx].onClick.RemoveAllListeners();
                scrollUpButtons[idx].onClick.AddListener(() => OnScrollPressed(idx, +1));
            }

            if (scrollDownButtons != null && idx < scrollDownButtons.Length && scrollDownButtons[idx] != null)
            {
                scrollDownButtons[idx].onClick.RemoveAllListeners();
                scrollDownButtons[idx].onClick.AddListener(() => OnScrollPressed(idx, -1));
            }

            if (midrowClickButtons != null && idx < midrowClickButtons.Length && midrowClickButtons[idx] != null)
            {
                midrowClickButtons[idx].onClick.RemoveAllListeners();
                midrowClickButtons[idx].onClick.AddListener(() => OnMidrowClicked(idx));
            }
        }
    }

    /// <summary>
    /// Call this from your bootstrapper.
    /// </summary>
    public void Show(GameObject[] availablePartyPrefabs, int partySize, Action<GameObject[]> onConfirm)
    {
        // In case bootstrapper calls Show before this object's Awake runs.
        WireOnce();

        _available = availablePartyPrefabs ?? Array.Empty<GameObject>();
        _partySize = Mathf.Clamp(partySize, 1, 3);
        _onConfirm = onConfirm;

        if (root != null) root.SetActive(true);
        gameObject.SetActive(true);

        BuildSymbolCache();

        _activeReelIndex = 0;

        if (_showRoutine != null) StopCoroutine(_showRoutine);
        _showRoutine = StartCoroutine(ShowDeferred());
    }

    private IEnumerator ShowDeferred()
    {
        // Let UI/reels settle so IsReelReady() becomes accurate.
        yield return null;

        RefreshPerReelButtonInteractable();

        // Initial summary comes from reel 0 midrow.
        string id0 = GetMidrowSymbolIdSafe(0, out var midSym0);
        if (logFlow) Debug.Log($"[NewStartupClassSelectionPanel] InitialPreview: id0='{id0}' sym='{(midSym0 != null ? midSym0.name : "<null>")}'", this);

        if (!string.IsNullOrEmpty(id0))
            PreviewHeroBySymbolId(id0);
        else
            PreviewHeroByPrefabIndex(0);

        _showRoutine = null;
    }

    public void Hide()
    {
        if (root != null) root.SetActive(false);
        gameObject.SetActive(false);
    }

    private float GetScrollDurationSeconds()
    {
        // Higher speed => shorter duration (faster spin).
        float speed = Mathf.Max(0.01f, scrollSpinSpeed);
        return Mathf.Max(0.01f, nudgeDurationSeconds / speed);
    }

    private void OnScrollPressed(int reelIndex, int deltaSteps)
    {
        _activeReelIndex = Mathf.Clamp(reelIndex, 0, 2);

        if (_randomizeRoutine != null) return;

        if (!IsReelReady(reelIndex))
        {
            if (logFlow) Debug.LogWarning($"[NewStartupClassSelectionPanel] Scroll ignored: reel {reelIndex} not ready.", this);
            return;
        }

        var r = reels[reelIndex];
        if (r == null) return;

        float dur = GetScrollDurationSeconds();

        // deltaSteps is still +/-1 (one quad). We only adjust animation speed.
        bool ok = r.TryNudgeStepsAnimated(deltaSteps, dur, nudgeEase);
        if (!ok)
        {
            if (logFlow) Debug.Log($"[NewStartupClassSelectionPanel] Scroll: nudge failed reel={reelIndex} delta={deltaSteps}", this);
            RefreshPerReelButtonInteractable();
            return;
        }

        RefreshPerReelButtonInteractable();
        StartCoroutine(PostNudgeUpdate(reelIndex));
    }

    private IEnumerator PostNudgeUpdate(int reelIndex)
    {
        var r = (reels != null && reelIndex >= 0 && reelIndex < reels.Length) ? reels[reelIndex] : null;
        if (r == null) yield break;

        while (r.IsNudging)
            yield return null;

        RefreshPerReelButtonInteractable();

        string symbolId = GetMidrowSymbolIdSafe(reelIndex, out var sym);
        if (logFlow) Debug.Log($"[NewStartupClassSelectionPanel] PostNudge: reel={reelIndex} midrow='{symbolId}' sym='{(sym != null ? sym.name : "<null>")}'", this);

        if (!string.IsNullOrEmpty(symbolId))
            PreviewHeroBySymbolId(symbolId);
    }

    private void OnMidrowClicked(int reelIndex)
    {
        _activeReelIndex = Mathf.Clamp(reelIndex, 0, 2);

        if (_randomizeRoutine != null) return;

        if (!IsReelReady(reelIndex))
        {
            if (logFlow) Debug.LogWarning($"[NewStartupClassSelectionPanel] MidrowClick ignored: reel {reelIndex} not ready.", this);
            return;
        }

        string symbolId = GetMidrowSymbolIdSafe(reelIndex, out var sym);
        if (logFlow) Debug.Log($"[NewStartupClassSelectionPanel] MidrowClick: reel={reelIndex} symbolId='{symbolId}' sym='{(sym != null ? sym.name : "<null>")}'", this);

        if (!string.IsNullOrEmpty(symbolId))
            PreviewHeroBySymbolId(symbolId);
    }

    private void OnRandomizePressed()
    {
        if (_randomizeRoutine != null) return;

        if (!HasValidReelsAndPlane())
        {
            Debug.LogWarning("[NewStartupClassSelectionPanel] Randomize ignored: reels/midrowPlane not wired.", this);
            return;
        }

        _randomizeRoutine = StartCoroutine(RandomizeRoutine());
    }

    private IEnumerator RandomizeRoutine()
    {
        if (logFlow) Debug.Log("[NewStartupClassSelectionPanel] Randomize: spinning all reels...", this);

        SetAllButtonsInteractable(false);

        for (int i = 0; i < 3; i++)
        {
            if (!IsReelReady(i))
                continue;

            yield return SpinOneReelAvoidNullCharacter(i);
        }

        _activeReelIndex = 0;
        string id0 = GetMidrowSymbolIdSafe(0, out var sym0);
        if (logFlow) Debug.Log($"[NewStartupClassSelectionPanel] Randomize done. Refresh summary reel0 id='{id0}' sym='{(sym0 != null ? sym0.name : "<null>")}'", this);

        if (!string.IsNullOrEmpty(id0))
            PreviewHeroBySymbolId(id0);

        SetAllButtonsInteractable(true);
        RefreshPerReelButtonInteractable();

        _randomizeRoutine = null;
    }

    private IEnumerator SpinOneReelAvoidNullCharacter(int reelIndex)
    {
        var r = reels[reelIndex];
        if (r == null) yield break;

        int attempts = 0;

        while (attempts < Mathf.Max(1, randomizeMaxAttemptsPerReel))
        {
            attempts++;

            int steps = UnityEngine.Random.Range(randomizeMinSteps, randomizeMaxSteps + 1);
            int dir = UnityEngine.Random.value < 0.5f ? -1 : +1;
            steps *= dir;

            if (logFlow) Debug.Log($"[NewStartupClassSelectionPanel] Randomize reel={reelIndex} attempt={attempts} steps={steps}", this);

            float dur = Mathf.Max(0.2f, GetScrollDurationSeconds() * 2f);

            if (!r.TryNudgeStepsAnimated(steps, dur, nudgeEase))
            {
                yield return null;
                continue;
            }

            while (r.IsNudging)
                yield return null;

            // Determine what landed.
            int qi, mult;
            ReelSymbolSO sym = r.GetMidrowSymbolAndMultiplier(midrowPlane, out qi, out mult);
            string id = (sym != null) ? sym.id : null;

            bool emptyId = string.IsNullOrEmpty(id);
            bool isNullChar = IsMidrowQuadNullCharacterByMaterial(reelIndex);

            if (logFlow)
            {
                Debug.Log(
                    $"[NewStartupClassSelectionPanel] Randomize reel={reelIndex} landed id='{id}' sym='{(sym != null ? sym.name : "<null>")}' emptyId={emptyId} isNullChar={isNullChar}",
                    this);
            }

            // Reject if: (a) no id, or (b) midrow quad is flagged as Null Character by material.
            if (!emptyId && !isNullChar)
                yield break;
        }

        if (logFlow) Debug.LogWarning($"[NewStartupClassSelectionPanel] Randomize reel={reelIndex}: max attempts reached; leaving current midrow.", this);
    }

    /// <summary>
    /// Returns true if the currently-midrow quad (for this reel) has a material whose name matches nullCharacterMaterialName.
    /// We find the midrow quad renderer by selecting the reel child renderer whose bounds intersects the midrowPlane bounds
    /// and is closest to the midrowPlane center.
    /// </summary>
    private bool IsMidrowQuadNullCharacterByMaterial(int reelIndex)
    {
        if (string.IsNullOrWhiteSpace(nullCharacterMaterialName)) return false;
        if (midrowPlane == null) return false;
        if (reels == null || reelIndex < 0 || reelIndex >= reels.Length) return false;

        var reel = reels[reelIndex];
        if (reel == null) return false;

        // Midrow bounds from plane (prefer renderer, fallback to collider).
        Bounds planeBounds;
        var planeRenderer = midrowPlane.GetComponent<Renderer>();
        if (planeRenderer != null)
        {
            planeBounds = planeRenderer.bounds;
        }
        else
        {
            var planeCollider = midrowPlane.GetComponent<Collider>();
            if (planeCollider != null) planeBounds = planeCollider.bounds;
            else
            {
                // last resort: tiny bounds at position
                planeBounds = new Bounds(midrowPlane.transform.position, Vector3.one * 0.01f);
            }
        }

        Vector3 planeCenter = planeBounds.center;

        Renderer best = null;
        float bestDist = float.MaxValue;

        // Search all renderers under the reel.
        var renderers = reel.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var rr = renderers[i];
            if (rr == null) continue;

            // Ignore the reel itself if it has a renderer, we want quads.
            if (rr.gameObject == reel.gameObject) continue;

            // Must intersect the midrow plane bounds (i.e., the current midrow quad).
            if (!rr.bounds.Intersects(planeBounds)) continue;

            float d = (rr.bounds.center - planeCenter).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = rr;
            }
        }

        if (best == null) return false;

        // Check all materials (Unity may use "Name (Instance)").
        var mats = best.sharedMaterials;
        if (mats == null || mats.Length == 0) return false;

        for (int i = 0; i < mats.Length; i++)
        {
            var m = mats[i];
            if (m == null) continue;

            string matName = m.name ?? "";
            if (matName.Equals(nullCharacterMaterialName, StringComparison.Ordinal) ||
                matName.StartsWith(nullCharacterMaterialName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void OnStartPressed()
    {
        var chosen = new GameObject[_partySize];

        for (int i = 0; i < _partySize; i++)
        {
            string symbolId = GetMidrowSymbolIdSafe(i, out _);
            int prefabIndex = ResolvePrefabIndex(symbolId);

            if (prefabIndex >= 0 && prefabIndex < _available.Length)
                chosen[i] = _available[prefabIndex];

            if (logFlow) Debug.Log($"[NewStartupClassSelectionPanel] Start: reel={i} symbolId='{symbolId}' prefabIndex={prefabIndex} prefab='{(chosen[i] != null ? chosen[i].name : "<null>")}'", this);
        }

        Hide();
        _onConfirm?.Invoke(chosen);
    }

    private bool HasValidReelsAndPlane()
    {
        if (midrowPlane == null) return false;
        if (reels == null || reels.Length < 3) return false;
        for (int i = 0; i < 3; i++)
            if (reels[i] == null) return false;
        return true;
    }

    private bool IsReelReady(int reelIndex)
    {
        if (reels == null || reelIndex < 0 || reelIndex >= reels.Length) return false;
        var r = reels[reelIndex];
        if (r == null) return false;
        if (!r.isActiveAndEnabled) return false;
        if (!r.gameObject.activeInHierarchy) return false;
        if (r.IsNudging) return false;
        return true;
    }

    private string GetMidrowSymbolIdSafe(int reelIndex, out ReelSymbolSO sym)
    {
        sym = null;

        if (reels == null || reelIndex < 0 || reelIndex >= reels.Length) return null;
        if (midrowPlane == null) return null;

        var r = reels[reelIndex];
        if (r == null) return null;

        int qi, mult;
        sym = r.GetMidrowSymbolAndMultiplier(midrowPlane, out qi, out mult);
        return (sym != null) ? sym.id : null;
    }

    private void RefreshPerReelButtonInteractable()
    {
        for (int i = 0; i < 3; i++)
        {
            bool can = IsReelReady(i);

            if (scrollUpButtons != null && i < scrollUpButtons.Length && scrollUpButtons[i] != null)
                scrollUpButtons[i].interactable = can;

            if (scrollDownButtons != null && i < scrollDownButtons.Length && scrollDownButtons[i] != null)
                scrollDownButtons[i].interactable = can;

            if (midrowClickButtons != null && i < midrowClickButtons.Length && midrowClickButtons[i] != null)
                midrowClickButtons[i].interactable = can;
        }

        if (randomizeButton != null)
            randomizeButton.interactable = (_randomizeRoutine == null);

        if (startButton != null)
            startButton.interactable = true;
    }

    private void SetAllButtonsInteractable(bool on)
    {
        for (int i = 0; i < 3; i++)
        {
            if (scrollUpButtons != null && i < scrollUpButtons.Length && scrollUpButtons[i] != null)
                scrollUpButtons[i].interactable = on;

            if (scrollDownButtons != null && i < scrollDownButtons.Length && scrollDownButtons[i] != null)
                scrollDownButtons[i].interactable = on;

            if (midrowClickButtons != null && i < midrowClickButtons.Length && midrowClickButtons[i] != null)
                midrowClickButtons[i].interactable = on;
        }

        if (randomizeButton != null) randomizeButton.interactable = on;
        if (startButton != null) startButton.interactable = on;
    }

    private void BuildSymbolCache()
    {
        _symbolIdToPrefabIndex.Clear();
        if (_available == null) return;

        for (int i = 0; i < _available.Length; i++)
        {
            var go = _available[i];
            if (go == null) continue;

            string key1 = null;
            var hs = go.GetComponentInChildren<HeroStats>(true);
            if (hs != null && hs.BaseClassDef != null && !string.IsNullOrEmpty(hs.BaseClassDef.className))
                key1 = hs.BaseClassDef.className;

            if (!string.IsNullOrEmpty(key1) && !_symbolIdToPrefabIndex.ContainsKey(key1))
                _symbolIdToPrefabIndex.Add(key1, i);

            if (!_symbolIdToPrefabIndex.ContainsKey(go.name))
                _symbolIdToPrefabIndex.Add(go.name, i);
        }
    }

    private int ResolvePrefabIndex(string symbolId)
    {
        if (string.IsNullOrEmpty(symbolId)) return -1;

        if (_symbolIdToPrefabIndex.TryGetValue(symbolId, out int idx))
            return idx;

        for (int i = 0; i < _available.Length; i++)
        {
            var go = _available[i];
            if (go == null) continue;

            if (go.name == symbolId) return i;

            var hs = go.GetComponentInChildren<HeroStats>(true);
            if (hs != null && hs.BaseClassDef != null && hs.BaseClassDef.className == symbolId)
                return i;
        }

        return -1;
    }

    private void PreviewHeroBySymbolId(string symbolId)
    {
        int idx = ResolvePrefabIndex(symbolId);
        if (idx < 0)
        {
            if (logFlow) Debug.LogWarning($"[NewStartupClassSelectionPanel] Preview MISS symbolId='{symbolId}'", this);
            return;
        }

        PreviewHeroByPrefabIndex(idx);
    }

    private void PreviewHeroByPrefabIndex(int idx)
    {
        if (_available == null || _available.Length == 0) return;
        if (idx < 0 || idx >= _available.Length) return;

        var heroPrefab = _available[idx];
        if (heroPrefab == null) return;

        var hs = heroPrefab.GetComponentInChildren<HeroStats>(true);
        var classDef = (hs != null) ? hs.BaseClassDef : null;

        if (heroNameText != null)
            heroNameText.text = (classDef != null && !string.IsNullOrEmpty(classDef.className)) ? classDef.className : heroPrefab.name;

        if (reelcraftNameText != null)
            reelcraftNameText.text = (classDef != null) ? classDef.reelcraftName : "";

        if (reelcraftDescText != null)
            reelcraftDescText.text = (classDef != null) ? classDef.reelcraftDescription : "";

        // ✅ NEW: reelcraft icon
        ApplyReelcraftIcon(classDef);

        if (startingAbilityHeaderText != null)
            startingAbilityHeaderText.text = "Starting Ability";

        AbilityDefinitionSO starter = null;
        if (classDef != null)
        {
            if (classDef.abilities != null && classDef.abilities.Count > 0)
            {
                for (int i = 0; i < classDef.abilities.Count; i++)
                {
                    var a = classDef.abilities[i];
                    if (a != null && a.starterChoice) { starter = a; break; }
                    if (starter == null && a != null) starter = a;
                }
            }
            else
            {
                if (classDef.ability1 != null) starter = classDef.ability1;
                else if (classDef.ability2 != null) starter = classDef.ability2;
            }
        }

        if (startingAbilityDescText != null)
            startingAbilityDescText.text = (starter != null) ? starter.description : "";

        RefreshReelSymbols(hs);
    }

    /// <summary>
    /// Pulls reelcraft icon sprite from the classDef via common field/property names, and updates UI image.
    /// This avoids hard-coding a specific member name on your BaseClassDef type.
    /// </summary>
    private void ApplyReelcraftIcon(object classDef)
    {
        if (reelcraftIcon == null) return;

        Sprite spr = TryGetSpriteFromClassDef(classDef);

        if (spr != null)
        {
            reelcraftIcon.sprite = spr;
            reelcraftIcon.enabled = true;
            reelcraftIcon.preserveAspect = true;
        }
        else
        {
            reelcraftIcon.sprite = null;
            reelcraftIcon.enabled = false;
        }
    }

    private static Sprite TryGetSpriteFromClassDef(object classDef)
    {
        if (classDef == null) return null;

        // Try common names first (fields or properties).
        // Add/remove names here to match your actual BaseClassDef.
        string[] names =
        {
            "reelcraftIcon",
            "reelcraftSprite",
            "reelcraftIconSprite",
            "reelcraftIconOverride",
            "reelcraftIconArt",
            "reelcraftIconImage"
        };

        Type t = classDef.GetType();

        for (int i = 0; i < names.Length; i++)
        {
            string n = names[i];

            // Field?
            FieldInfo f = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null && typeof(Sprite).IsAssignableFrom(f.FieldType))
                return f.GetValue(classDef) as Sprite;

            // Property?
            PropertyInfo p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && typeof(Sprite).IsAssignableFrom(p.PropertyType) && p.CanRead)
            {
                try { return p.GetValue(classDef, null) as Sprite; }
                catch { /* ignore */ }
            }
        }

        return null;
    }

    private void RefreshReelSymbols(HeroStats hs)
    {
        if (reelSymbolsContainer == null || reelSymbolIconPrefab == null) return;

        for (int i = _reelIcons.Count - 1; i >= 0; i--)
        {
            if (_reelIcons[i] != null)
                Destroy(_reelIcons[i].gameObject);
        }
        _reelIcons.Clear();

        if (hs == null || hs.ReelStrip == null || hs.ReelStrip.symbols == null) return;

        int limit = (maxReelSymbolsToShow <= 0) ? int.MaxValue : maxReelSymbolsToShow;
        HashSet<ReelSymbolSO> seen = showUniqueReelSymbolsOnly ? new HashSet<ReelSymbolSO>() : null;

        int shown = 0;
        for (int i = 0; i < hs.ReelStrip.symbols.Count; i++)
        {
            if (shown >= limit) break;

            var sym = hs.ReelStrip.symbols[i];
            if (sym == null || sym.icon == null) continue;

            if (seen != null)
            {
                if (seen.Contains(sym)) continue;
                seen.Add(sym);
            }

            var img = Instantiate(reelSymbolIconPrefab, reelSymbolsContainer);
            img.sprite = sym.icon;
            img.enabled = true;
            img.preserveAspect = true;
            _reelIcons.Add(img);
            shown++;
        }
    }
}
