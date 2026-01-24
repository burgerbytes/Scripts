using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MonsterHpBar : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Monster monster;
    [SerializeField] private Image fillImage;

    [Header("Damage Preview (Yellow Segment)")]
    [Tooltip("Optional. If not assigned, this is created at runtime.")]
    [SerializeField] private RectTransform hpBarFullRect;
    [SerializeField] private RectTransform hpDamagePreviewRect;
    [SerializeField] private Image hpDamagePreviewImage;

    [Range(0f, 1f)]
    [SerializeField] private float previewAlpha = 0.85f;

    [Tooltip("Color used for the damage preview segment.")]
    [SerializeField] private Color previewColor = new Color(1f, 0.92f, 0.1f, 1f);

    
    [Header("Status Icons (Under HP Bar)")]
    [Tooltip("Optional container. If null, one is created under the HP bar.")]
    [SerializeField] private RectTransform statusIconsContainer;

    [Tooltip("Sprite shown when the monster has Focus Rune.")]
    [SerializeField] private Sprite focusRuneStatusSprite;

    [Tooltip("Sprite shown when the monster has Ignition.")]
    [SerializeField] private Sprite ignitionStatusSprite;

    [Tooltip("Sprite shown when the monster has Stasis.")]
    [SerializeField] private Sprite stasisStatusSprite;
    
    [Tooltip("Sprite shown when the monster is Bleeding (stacks > 0).")]
    [SerializeField] private Sprite bleedingStatusSprite;

    [Tooltip("Pixel size of each status icon (UI units).")]
    [SerializeField] private float statusIconSize = 18f;

    [Tooltip("Spacing between status icons.")]
    [SerializeField] private float statusIconSpacing = 2f;

    [Tooltip("How far below the HP bar the status row sits (UI units).")]
    [SerializeField] private float statusRowYOffset = 2f;

    [Header("Debug")]
    [SerializeField] private bool logDebug = false;

    private float _baseFillWidth = -1f;
    private Vector3 _baseFillScale = Vector3.one;
    private Vector2 _baseFillAnchoredPos = Vector2.zero;
    private bool _baseFillCached = false;
    private int _lastHp = -1;
    private int _lastMaxHp = -1;

    // Some prefabs use a Filled Image (fillAmount), others use a Simple/Sliced image with a mask.
    // Support both by falling back to resizing/scaling when fillAmount has no effect.
    private bool _useFillAmount = true;
    private bool _useScaleFallback = false;
    private float _fullWidth = 0f;
    private Vector3 _baseScale = Vector3.one;

    // IMPORTANT: we update ALL plausible "fill" candidates (in case we auto-bound the wrong Image).
    private Image[] _fillCandidates;

    // Status icon instances
    private Image _statusIconFocus;
    private Image _statusIconBleed;
    private Image _statusIconIgnition;
    private Image _statusIconStasis;
    private TMP_Text _statusBleedStacksText;
    private TMP_Text _statusIgnitionStacksText;
    private TMP_Text _statusStasisStacksText;

    private void Reset()
    {
        monster = GetComponentInParent<Monster>();
        AutoFindFillImages();
    }

    private void Awake()
    {
        if (monster == null) monster = GetComponentInParent<Monster>();
        AutoFindFillImages();

        CacheFillMode();

        DisableLegacyGhostFill();
        EnsurePreviewObjects();
        EnsureStatusIcons();

        ClearPreview();
        RefreshStatusIcons();
        RefreshStatusIcons();

        if (monster != null)
            HandleHpChanged(monster.CurrentHp, monster.MaxHp);
    }

    private void OnEnable()
    {
        TryAutoBind();

        if (monster != null)
        {
            monster.OnHpChanged += HandleHpChanged;
            monster.OnStatusChanged += HandleStatusChanged;
        }

        if (monster != null)
            HandleHpChanged(monster.CurrentHp, monster.MaxHp);

        ClearPreview();
        RefreshStatusIcons();
    }

    private void OnDisable()
    {
        if (monster != null)
        {
            monster.OnHpChanged -= HandleHpChanged;
            monster.OnStatusChanged -= HandleStatusChanged;
        }
    }

    private void TryAutoBind()
    {
        if (monster == null)
            monster = GetComponentInParent<Monster>();

        if (fillImage == null || _fillCandidates == null || _fillCandidates.Length == 0)
            AutoFindFillImages();
    }

    private void AutoFindFillImages()
    {
        // Collect candidates first.
        var images = GetComponentsInChildren<Image>(true);
        _fillCandidates = images;

        if (fillImage != null)
            return;

        // Prefer an Image explicitly named "Fill".
        for (int i = 0; i < images.Length; i++)
        {
            var img = images[i];
            if (img == null) continue;
            if (img.name.Equals("Fill", System.StringComparison.OrdinalIgnoreCase))
            {
                fillImage = img;
                return;
            }
        }

        // Next: first Filled Image.
        for (int i = 0; i < images.Length; i++)
        {
            var img = images[i];
            if (img == null) continue;
            if (img.type == Image.Type.Filled)
            {
                fillImage = img;
                return;
            }
        }

        // Last resort: any Image whose name contains "fill".
        for (int i = 0; i < images.Length; i++)
        {
            var img = images[i];
            if (img == null) continue;
            if (img.name.ToLowerInvariant().Contains("fill"))
            {
                fillImage = img;
                return;
            }
        }
    }

    private void DisableLegacyGhostFill()
    {
        var imgs = GetComponentsInChildren<Image>(true);
        for (int i = 0; i < imgs.Length; i++)
        {
            if (imgs[i] != null && imgs[i].name == "GhostFill")
                imgs[i].enabled = false;
        }
    }

    private void EnsurePreviewObjects()
    {
        if (fillImage == null) return;

        if (hpBarFullRect == null)
            hpBarFullRect = fillImage.rectTransform;

        if (hpDamagePreviewRect != null && hpDamagePreviewImage != null)
            return;

        // Create a preview segment as a sibling on top of the fill.
        GameObject segGO = new GameObject("HPDamagePreview", typeof(RectTransform), typeof(Image));
        segGO.transform.SetParent(fillImage.transform.parent, false);
        segGO.transform.SetSiblingIndex(fillImage.transform.GetSiblingIndex() + 1);

        hpDamagePreviewRect = segGO.GetComponent<RectTransform>();
        hpDamagePreviewImage = segGO.GetComponent<Image>();

        // Copy sprite/material so it matches your bar shape (but tinted).
        hpDamagePreviewImage.sprite = fillImage.sprite;
        hpDamagePreviewImage.type = Image.Type.Sliced; // segment is sized by width, not filled amount
        hpDamagePreviewImage.material = fillImage.material;
        hpDamagePreviewImage.raycastTarget = false;

        Color c = previewColor;
        c.a = previewAlpha;
        hpDamagePreviewImage.color = c;

        // Match vertical sizing/anchors to the fill rect
        RectTransform src = fillImage.rectTransform;
        hpDamagePreviewRect.anchorMin = new Vector2(0f, src.anchorMin.y);
        hpDamagePreviewRect.anchorMax = new Vector2(0f, src.anchorMax.y);
        hpDamagePreviewRect.pivot = new Vector2(0f, src.pivot.y);
        hpDamagePreviewRect.anchoredPosition = new Vector2(0f, src.anchoredPosition.y);
        hpDamagePreviewRect.sizeDelta = new Vector2(0f, src.sizeDelta.y);
        hpDamagePreviewRect.localRotation = src.localRotation;
        // IMPORTANT: do NOT copy the fill's runtime scale. The fill may be scaled as HP changes.
        // The preview segment should stay in "full bar" space and be sized explicitly.
        hpDamagePreviewRect.localScale = Vector3.one;
    }

    private void CacheFillCandidates()
    {
        // Find likely fill images: name contains "fill" OR Image.Type.Filled.
        // Exclude background/frame names so we don't accidentally change them.
        var images = GetComponentsInChildren<Image>(true);
        System.Collections.Generic.List<Image> list = new System.Collections.Generic.List<Image>(8);
        for (int i = 0; i < images.Length; i++)
        {
            var img = images[i];
            if (img == null) continue;
            if (hpDamagePreviewImage != null && img == hpDamagePreviewImage) continue;

            string n = img.name != null ? img.name.ToLowerInvariant() : "";
            bool looksLikeBg = n.Contains("bg") || n.Contains("back") || n.Contains("frame") || n.Contains("border");
            bool looksLikeFill = n.Contains("fill") || img.type == Image.Type.Filled;

            if (looksLikeFill && !looksLikeBg)
                list.Add(img);
        }

        _fillCandidates = list.ToArray();

        if (logDebug)
        {
            string names = "";
            for (int i = 0; i < _fillCandidates.Length; i++)
                names += (i == 0 ? "" : ", ") + _fillCandidates[i].name + ":" + _fillCandidates[i].type;
            Debug.Log($"[MonsterHpBar] CacheFillCandidates count={_fillCandidates.Length} [{names}] barObj={name}", this);
        }
    }

    private void CacheFillMode()
    {
        if (fillImage == null) return;

        // If it's a Filled image, fillAmount works.
        _useFillAmount = (fillImage.type == Image.Type.Filled);

        // NOTE: We do NOT set any fillAmount here because this method is called during caching.
        // Actual fill updates happen in ApplyFillAmountToCandidates(value01).

        RectTransform rt = fillImage.rectTransform;
        if (rt == null)
            return;

        _baseScale = rt.localScale;

        if (_useFillAmount)
        {
            _useScaleFallback = false;
            _fullWidth = 0f;
            return;
        }

        // Non-filled images: either resize (if fixed anchors) or scale (if stretch anchors).
        _useScaleFallback = !Mathf.Approximately(rt.anchorMin.x, rt.anchorMax.x);

        // Try to capture the full width at "100% HP".
        _fullWidth = rt.rect.width;
        if (_fullWidth <= 0.01f)
            _fullWidth = rt.sizeDelta.x;
    }

    private void ApplyFillAmountToCandidates(float value01)
    {
        if (_fillCandidates == null) return;

        for (int i = 0; i < _fillCandidates.Length; i++)
        {
            var img = _fillCandidates[i];
            if (img == null) continue;
            if (!img.enabled) continue;
            if (img.gameObject == null) continue;
            if (img.gameObject.name == "HPDamagePreview") continue;

            // Only touch plausible fill visuals.
            string n = img.name.ToLowerInvariant();
            bool nameLooksLikeFill = (n == "fill" || n.Contains("fill"));
            bool typeIsFilled = (img.type == Image.Type.Filled);

            // Avoid modifying obvious non-fills.
            bool looksLikeBackground = n.Contains("bg") || n.Contains("back") || n.Contains("frame") || n.Contains("border");
            if (looksLikeBackground && !typeIsFilled) continue;

            if (typeIsFilled || nameLooksLikeFill)
            {
                // If it's actually filled, fillAmount is authoritative.
                if (img.type == Image.Type.Filled)
                    img.fillAmount = value01;
            }
        }
    }
    private void CacheBaseFillGeometry()
    {
        if (fillImage == null) return;
        var rt = fillImage.rectTransform;
        if (rt == null) return;

        if (_baseFillWidth <= 0.01f)
        {
            // Prefer sizeDelta.x if meaningful, otherwise rect.width.
            float w = (rt.sizeDelta.x > 0.01f) ? rt.sizeDelta.x : rt.rect.width;
            if (w <= 0.01f) w = 100f;
            _baseFillWidth = w;
        }

        // Cache the starting scale (used for stretch-anchor cases)
        if (_baseFillScale == Vector3.one && rt.localScale != Vector3.one)
            _baseFillScale = rt.localScale;
        else if (_baseFillScale == Vector3.one)
            _baseFillScale = rt.localScale;
    }

    private void SetFill01(float value01)
    {
        if (fillImage == null)
        {
            AutoFindFillImages();
            if (fillImage == null) return;
        }

        value01 = Mathf.Clamp01(value01);

        // Make sure candidates exist (your script already has this)
        if (_fillCandidates == null || _fillCandidates.Length == 0)
            CacheFillCandidates();

        CacheBaseFillGeometry();

        // 1) Filled-image path: update all Filled candidates (covers “wrong Image bound” issues)
        ApplyFillAmountToCandidates(value01);

        // 2) Width/scale path: ALSO update the RectTransform for the primary fill
        //    (covers prefabs where visible bar uses mask/width rather than fillAmount)
        RectTransform rt = fillImage.rectTransform;
        if (rt != null)
        {
            bool fixedAnchors = Mathf.Abs(rt.anchorMin.x - rt.anchorMax.x) < 0.0001f;

            if (fixedAnchors)
            {
                // Fixed anchors => width is driven by sizeDelta / SetSizeWithCurrentAnchors
                float fullW = (_baseFillWidth > 0.01f) ? _baseFillWidth : rt.rect.width;
                if (fullW <= 0.01f) fullW = 100f;
                SetWidthKeepLeft(rt, fullW * value01);
            }
            else
            {
                // Stretch anchors => safest is scale
                Vector3 s = _baseFillScale;
                s.x = _baseFillScale.x * value01;
                SetScaleKeepLeft(rt, s.x);
            }

            fillImage.SetVerticesDirty();
        }

        // 3) If there are non-Filled images named exactly “Fill”, resize/scale them too.
        //    (Some prefabs have a Simple Image called Fill under a Mask.)
        if (_fillCandidates != null)
        {
            for (int i = 0; i < _fillCandidates.Length; i++)
            {
                var img = _fillCandidates[i];
                if (img == null) continue;
                if (img == hpDamagePreviewImage) continue;

                string n = (img.name ?? "").ToLowerInvariant();
                if (n != "fill") continue;
                if (img.type == Image.Type.Filled) continue;

                var rti = img.rectTransform;
                if (rti == null) continue;

                bool fixedA = Mathf.Abs(rti.anchorMin.x - rti.anchorMax.x) < 0.0001f;
                if (fixedA)
                {
                    float fullW = (rti.sizeDelta.x > 0.01f) ? rti.sizeDelta.x : rti.rect.width;
                    if (fullW <= 0.01f) fullW = (_baseFillWidth > 0.01f) ? _baseFillWidth : 100f;
                    SetWidthKeepLeft(rti, fullW * value01);
                }
                else
                {
                    // Keep left edge fixed while scaling.
                    SetScaleKeepLeft(rti, value01);
                }

                img.SetVerticesDirty();
            }
        }

        if (logDebug && monster != null)
            Debug.Log($"[MonsterHpBar] SetFill01 applied value01={value01:0.###} monster={monster.name} hp={monster.CurrentHp}/{monster.MaxHp}", this);
    }

    private void DebugDumpVisualInternal(string tag, bool force)
    {
        if (!force && !logDebug) return;

        TryAutoBind();

        if (fillImage == null & logDebug)
        {
            Debug.LogWarning($"[MonsterHpBar] DebugDumpVisual tag={tag} no fillImage. barObj={name}", this);
            return;
        }

        var rt = fillImage.rectTransform;
        string m = (monster != null)
            ? $"monster={monster.name} hp={monster.CurrentHp}/{monster.MaxHp} monsterInstance={monster.GetInstanceID()}"
            : "monster=NULL";
        
        if (logDebug)
        {
            Debug.Log(
                $"[MonsterHpBar] DebugDumpVisual tag={tag} {m} barObj={name} barInstance={GetInstanceID()} " +
                $"imgType={fillImage.type} fillAmt={fillImage.fillAmount:0.###} rectW={rt.rect.width:0.###} rectH={rt.rect.height:0.###} " +
                $"anchors=({rt.anchorMin.x:0.###},{rt.anchorMin.y:0.###})-({rt.anchorMax.x:0.###},{rt.anchorMax.y:0.###}) " +
                $"sizeDelta=({rt.sizeDelta.x:0.###},{rt.sizeDelta.y:0.###}) localScale=({rt.localScale.x:0.###},{rt.localScale.y:0.###},{rt.localScale.z:0.###})",
                this);
        }

        // Also dump all filled candidates (helps diagnose "stomped"/wrong-image cases).
        if (_fillCandidates != null)
        {
            for (int i = 0; i < _fillCandidates.Length; i++)
            {
                var img = _fillCandidates[i];
                if (img == null) continue;
                if (img.gameObject != null && img.gameObject.name == "HPDamagePreview") continue;
                if (img.type != Image.Type.Filled) continue;
                if (logDebug)
                {
                    Debug.Log(
                    $"[MonsterHpBar] CandidateFilled tag={tag} name={img.name} enabled={img.enabled} fillAmt={img.fillAmount:0.###} colorA={img.color.a:0.###} goActive={img.gameObject.activeInHierarchy}",
                    this);
                }
            }
        }
    }

    public void DebugDumpVisual(string tag) => DebugDumpVisualInternal(tag, force: false);
    public void ForceDebugDumpVisual(string tag) => DebugDumpVisualInternal(tag, force: true);

    private void HandleHpChanged(int current, int max)
    {
        _lastHp = current;
        _lastMaxHp = max;

        if (logDebug && monster != null)
            Debug.Log($"[MonsterHpBar] HandleHpChanged monster={monster.name} hp={current}/{max} barObj={name} monsterInstance={monster.GetInstanceID()} barInstance={GetInstanceID()}", this);

        int safeMax = Mathf.Max(1, max);
        float current01 = Mathf.Clamp01((float)current / safeMax);
        SetFill01(current01);
    }

    /// <summary>
    /// Show a yellow segment representing HP that will be removed.
    /// IMPORTANT: This function expects the predicted HP AFTER damage (not "damage amount").
    /// </summary>
    public void SetDamagePreview(int predictedHpAfterDamage)
    {
        if (monster == null) return;
        if (fillImage == null) AutoFindFillImages();
        if (fillImage == null) return;

        EnsurePreviewObjects();
        if (hpBarFullRect == null || hpDamagePreviewRect == null || hpDamagePreviewImage == null) return;

        // IMPORTANT: preview math must be based on the FULL bar geometry (100% HP),
        // not on the current/shrunk fill rect. Otherwise the preview segment will
        // appear offset, too small/large, or leave "black gaps".
        CacheBaseFillGeometry();

        int currentHP = monster.CurrentHp;
        if (logDebug)
            Debug.Log($"[MonsterHpBar] SetDamagePreview monster={monster.name} hpNow={currentHP}/{monster.MaxHp} predictedAfter={predictedHpAfterDamage} barObj={name} monsterInstance={monster.GetInstanceID()} barInstance={GetInstanceID()}", this);
        int maxHP = Mathf.Max(1, monster.MaxHp);

        // Clamp predicted HP into a valid range
        int predictedHP = Mathf.Clamp(predictedHpAfterDamage, 0, currentHP);

        // Derive damage from predicted HP
        int dmg = Mathf.Max(0, currentHP - predictedHP);

        float current01 = Mathf.Clamp01((float)currentHP / maxHP);
        float predicted01 = Mathf.Clamp01((float)predictedHP / maxHP);

        // Create a segment from predicted -> current using FULL width (cached).
        // _baseFillWidth is the local-width at 100% HP (unscaled).
        float fullLocalWidth = (_baseFillWidth > 0.01f) ? _baseFillWidth : hpBarFullRect.rect.width;
        if (fullLocalWidth <= 0.01f) fullLocalWidth = 100f;

        float leftX = predicted01 * fullLocalWidth;
        float width = Mathf.Max(0f, (current01 - predicted01) * fullLocalWidth);

        // Position segment (our preview rect is anchored/pivoted to the left in EnsurePreviewObjects).
        bool visible = dmg > 0 && width > 0.5f; // avoid tiny slivers
        hpDamagePreviewImage.enabled = visible;

        if (visible)
        {
            hpDamagePreviewRect.anchoredPosition = new Vector2(leftX, hpDamagePreviewRect.anchoredPosition.y);
            hpDamagePreviewRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        }
        else
        {
            // Prevent stale segments from previous previews.
            hpDamagePreviewRect.anchoredPosition = new Vector2(0f, hpDamagePreviewRect.anchoredPosition.y);
            hpDamagePreviewRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 0f);
        }

        // Shrink main fill to predicted (after preview is computed, so we don't stomp our measurements)
        SetFill01(predicted01);
    }

    public void RefreshNow(string reason = "")
    {
        TryAutoBind();
        if (monster == null)
        {
            if (logDebug) Debug.LogWarning($"[MonsterHpBar] RefreshNow: no monster bound. reason={reason} this={name}", this);
            return;
        }

        if (logDebug)
            Debug.Log($"[MonsterHpBar] RefreshNow reason={reason} monster={monster.name} hp={monster.CurrentHp}/{monster.MaxHp} barObj={name} monsterInstance={monster.GetInstanceID()} barInstance={GetInstanceID()}", this);

        HandleHpChanged(monster.CurrentHp, monster.MaxHp);
    }

    public void ClearPreview()
    {
        if (hpDamagePreviewImage != null)
            hpDamagePreviewImage.enabled = false;

        if (hpDamagePreviewRect != null)
            hpDamagePreviewRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 0f);

        // Restore fill to current HP
        if (logDebug && monster != null)
            Debug.Log($"[MonsterHpBar] ClearPreview monster={monster.name} hp={monster.CurrentHp}/{monster.MaxHp} barObj={name} monsterInstance={monster.GetInstanceID()} barInstance={GetInstanceID()}", this);

        if (monster != null)
        {
            int maxHP = Mathf.Max(1, monster.MaxHp);
            float current01 = Mathf.Clamp01((float)monster.CurrentHp / maxHP);
            SetFill01(current01);
        }
    }

    private void CacheFillBase(RectTransform rt)
    {
        if (rt == null || _baseFillCached) return;

        // Full-width geometry at "100% HP"
        _baseFillWidth = (rt.sizeDelta.x > 0.01f) ? rt.sizeDelta.x : rt.rect.width;
        if (_baseFillWidth <= 0.01f) _baseFillWidth = 100f;

        _baseFillScale = rt.localScale;
        _baseFillAnchoredPos = rt.anchoredPosition;
        _baseFillCached = true;
    }

    private void SetWidthKeepLeft(RectTransform rt, float newWidth)
    {
        // Keep the left edge constant in anchored space
        float left = rt.anchoredPosition.x - rt.rect.width * rt.pivot.x;

        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, newWidth);

        // After width change, restore anchoredPosition so left edge stays fixed
        rt.anchoredPosition = new Vector2(left + newWidth * rt.pivot.x, rt.anchoredPosition.y);
    }

    private void SetScaleKeepLeft(RectTransform rt, float newScaleX)
    {
        // Keep the left edge constant in anchored space, without relying on cached base geometry.
        // This is robust even if the monster spawns already damaged.
        float currentRenderedW = rt.rect.width * rt.localScale.x;
        float left = rt.anchoredPosition.x - currentRenderedW * rt.pivot.x;

        var s = rt.localScale;
        s.x = newScaleX;
        rt.localScale = s;

        float newRenderedW = rt.rect.width * newScaleX;
        rt.anchoredPosition = new Vector2(left + newRenderedW * rt.pivot.x, rt.anchoredPosition.y);
    }


    // =======================
    // Status Icons
    // =======================

    /// <summary>
    /// Allows BattleManager (or prefab wiring) to provide sprites at runtime.
    /// </summary>
    public void ConfigureStatusSprites(Sprite bleedingSprite, 
                                       Sprite focusRuneSprite,
                                       Sprite ignitionSprite,
                                       Sprite stasisSprite)
    {
        bleedingStatusSprite = bleedingSprite;
        focusRuneStatusSprite = focusRuneSprite;
        ignitionStatusSprite = ignitionSprite;
        stasisStatusSprite = stasisSprite;
        RefreshStatusIcons();
    }

    private void HandleStatusChanged()
    {
        RefreshStatusIcons();
    }

    private void EnsureStatusIcons()
    {
        if (hpBarFullRect == null && fillImage != null)
            hpBarFullRect = fillImage.rectTransform;

        if (statusIconsContainer == null && hpBarFullRect != null)
        {
            // Create a child container under the HP bar parent so it sits "under" the bar.
            GameObject go = new GameObject("StatusIcons", typeof(RectTransform));
            go.transform.SetParent(hpBarFullRect.parent, false);

            statusIconsContainer = go.GetComponent<RectTransform>();
            statusIconsContainer.anchorMin = new Vector2(0f, 0f);
            statusIconsContainer.anchorMax = new Vector2(0f, 0f);
            statusIconsContainer.pivot = new Vector2(0f, 1f);

            // Position: bottom-left of the full bar, slightly below.
            statusIconsContainer.anchoredPosition = new Vector2(hpBarFullRect.anchoredPosition.x, hpBarFullRect.anchoredPosition.y - statusRowYOffset);
            statusIconsContainer.sizeDelta = new Vector2(200f, statusIconSize);

            // Add layout group so icons flow left->right.
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = false;
            layout.childControlWidth = false;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            layout.spacing = statusIconSpacing;

            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        }

        if (statusIconsContainer == null)
            return;

        // Ensure Focus icon
        if (_statusIconFocus == null)
            _statusIconFocus = CreateStatusImage("FocusRuneIcon", statusIconsContainer);

        // Ensure Ignition icon
        if (_statusIconIgnition == null)
        {
            _statusIconIgnition = CreateStatusImage("IgnitionIcon", statusIconsContainer);
            // Add stacks text overlay (child)
            var tgo = new GameObject("Stacks", typeof(RectTransform));
            tgo.transform.SetParent(_statusIconIgnition.transform, false);

            var tr = tgo.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = Vector2.zero;
            tr.offsetMax = Vector2.zero;

            _statusIgnitionStacksText = tgo.AddComponent<TextMeshProUGUI>();
            _statusIgnitionStacksText.alignment = TextAlignmentOptions.BottomRight;
            _statusIgnitionStacksText.fontSize = 14;
            _statusIgnitionStacksText.raycastTarget = false;
            _statusIgnitionStacksText.text = "";
        }
        
        // Ensure Bleed icon
        if (_statusIconBleed == null)
        {
            _statusIconBleed = CreateStatusImage("BleedIcon", statusIconsContainer);
            // Add stacks text overlay (child)
            var tgo = new GameObject("Stacks", typeof(RectTransform));
            tgo.transform.SetParent(_statusIconBleed.transform, false);

            var tr = tgo.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = Vector2.zero;
            tr.offsetMax = Vector2.zero;

            _statusBleedStacksText = tgo.AddComponent<TextMeshProUGUI>();
            _statusBleedStacksText.alignment = TextAlignmentOptions.BottomRight;
            _statusBleedStacksText.fontSize = 14;
            _statusBleedStacksText.raycastTarget = false;
            _statusBleedStacksText.text = "";
        }
    }

    private Image CreateStatusImage(string name, RectTransform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(statusIconSize, statusIconSize);

        var img = go.GetComponent<Image>();
        img.raycastTarget = false;
        img.preserveAspect = true;

        return img;
    }

    private void RefreshStatusIcons()
    {
        EnsureStatusIcons();

        if (monster == null)
            monster = GetComponentInParent<Monster>();

        if (monster == null)
        {
            if (_statusIconFocus != null) _statusIconFocus.enabled = false;
            if (_statusIconIgnition != null) _statusIconIgnition.enabled = false;
            if (_statusIgnitionStacksText != null) _statusIgnitionStacksText.text = "";
            if (_statusIconStasis != null) _statusIconStasis.enabled = false;
            if (_statusStasisStacksText != null) _statusStasisStacksText.text = "";
            if (_statusIconBleed != null) _statusIconBleed.enabled = false;
            if (_statusBleedStacksText != null) _statusBleedStacksText.text = "";
            return;
        }

        // Focus Rune
        bool hasFocus = false;
        try { hasFocus = monster.HasFocusRune; } catch { hasFocus = false; }

        if (_statusIconFocus != null)
        {
            _statusIconFocus.sprite = focusRuneStatusSprite;
            _statusIconFocus.enabled = hasFocus && focusRuneStatusSprite != null;
        }
        // Ignition
        int ignitionStacks = 0;
        try { ignitionStacks = monster.IgnitionStacks; } catch { ignitionStacks = 0; }

        bool hasIgnition = ignitionStacks > 0 && ignitionStatusSprite != null;

        if (_statusIconIgnition != null)
        {
            _statusIconIgnition.sprite = ignitionStatusSprite;
            _statusIconIgnition.enabled = hasIgnition;
        }

        if (_statusIgnitionStacksText != null)
        {
            _statusIgnitionStacksText.text = hasIgnition ? ignitionStacks.ToString() : "";
            _statusIgnitionStacksText.enabled = hasIgnition;
        }
        // Stasis
        int stasisStacks = 0;
        try { stasisStacks = monster.StasisStacks; } catch { stasisStacks = 0; }

        bool hasStasis = stasisStacks > 0 && stasisStatusSprite != null;

        if (_statusIconStasis != null)
        {
            _statusIconStasis.sprite = stasisStatusSprite;
            _statusIconStasis.enabled = hasStasis;
        }

        if (_statusStasisStacksText != null)
        {
            _statusStasisStacksText.text = hasStasis ? stasisStacks.ToString() : "";
            _statusStasisStacksText.enabled = hasStasis;
        }
        // Bleeding
        int bleedStacks = 0;
        try { bleedStacks = monster.BleedStacks; } catch { bleedStacks = 0; }

        bool hasBleed = bleedStacks > 0 && bleedingStatusSprite != null;

        if (_statusIconBleed != null)
        {
            _statusIconBleed.sprite = bleedingStatusSprite;
            _statusIconBleed.enabled = hasBleed;
        }

        if (_statusBleedStacksText != null)
        {
            _statusBleedStacksText.text = hasBleed ? bleedStacks.ToString() : "";
            _statusBleedStacksText.enabled = hasBleed;
        }
    }

}