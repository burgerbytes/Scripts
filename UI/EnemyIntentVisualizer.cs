using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class EnemyIntentVisualizer : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private BattleManager battleManager;
    [SerializeField] private PartyHUD partyHUD;
    [SerializeField] private Camera worldCamera;

    [Header("Canvas Selection")]
    [SerializeField] private bool preferCanvasByName = true;
    [SerializeField] private string preferredCanvasName = "Main Canvas";

    [Header("UI")]
    [SerializeField] private Canvas uiCanvas;
    [SerializeField] private RectTransform uiRoot;

    [Header("Toggles")]
    [SerializeField] private bool showIntentLines = true;
    [SerializeField] private bool showIntentIcons = true;
    [SerializeField] private bool enableHotkeyToggle = true;
    [SerializeField] private KeyCode toggleLinesKey = KeyCode.F3;

    [Header("Intent Lines")]
    [SerializeField] private Material lineMaterial;
    [SerializeField] private float lineWidth = 0.05f;
    [SerializeField] private string lineSortingLayerName = "Default";
    [SerializeField] private int lineSortingOrder = 5000;
    [SerializeField] private float lineWorldZ = 0f;
    [SerializeField] private Vector2 slotScreenOffset = Vector2.zero;

    [Header("Intent Icons")]
    [SerializeField] private Sprite attackIcon;
    [SerializeField] private Sprite aoeAttackIcon;
    [SerializeField] private Vector2 intentIconSize = new Vector2(56f, 56f);
    [SerializeField] private Vector2 intentIconScreenOffset = new Vector2(0f, 90f);
    [SerializeField] private Sprite targetDotSprite;
    [SerializeField] private float targetDotSize = 10f;
    [SerializeField] private float targetDotYOffset = -28f;

    [Header("Intent Damage Number")]
    [SerializeField] private Vector2 intentDamageTextOffset = new Vector2(34f, 6f);
    [SerializeField] private float intentDamageFontSize = 22f;

    [Header("Target Color Coding (by Base Class)")]
    [SerializeField] private Color fighterColor = Color.red;
    [SerializeField] private Color mageColor = Color.blue;
    [SerializeField] private Color ninjaColor = Color.green;
    [SerializeField] private Color fallbackColor = Color.magenta;

    private readonly List<IntentVisual> _visuals = new();
    private bool _refreshQueued;
    private int _framesSinceEnable;

    private void Awake()
    {
        if (battleManager == null) battleManager = FindFirstObjectByType<BattleManager>();
        if (partyHUD == null) partyHUD = FindFirstObjectByType<PartyHUD>();
        if (worldCamera == null) worldCamera = Camera.main;

        if (uiCanvas == null)
        {
            if (preferCanvasByName && !string.IsNullOrWhiteSpace(preferredCanvasName))
                uiCanvas = FindCanvasByName(preferredCanvasName);

            if (uiCanvas == null)
                uiCanvas = FindFirstObjectByType<Canvas>();
        }

        if (uiRoot == null && uiCanvas != null)
            uiRoot = uiCanvas.transform as RectTransform;
    }

    private void OnEnable()
    {
        if (battleManager != null)
            battleManager.OnEnemyIntentsPlanned += HandleIntentsPlanned;

        QueueRefresh();
    }

    private void OnDisable()
    {
        if (battleManager != null)
            battleManager.OnEnemyIntentsPlanned -= HandleIntentsPlanned;

        ClearAll();
    }

    private void Update()
    {
        if (enableHotkeyToggle && Input.GetKeyDown(toggleLinesKey))
            SetShowIntentLines(!showIntentLines);
    }

    private void LateUpdate()
    {
        _framesSinceEnable++;

        if (_refreshQueued)
        {
            _refreshQueued = false;
            TryForcePlanIntentsIfNone();
        }

        if (_visuals.Count == 0)
            return;

        Camera uiCam = GetUICamera();

        for (int i = 0; i < _visuals.Count; i++)
        {
            _visuals[i].Update(
                worldCamera,
                uiCanvas,
                uiRoot,
                uiCam,
                partyHUD,
                battleManager,
                showIntentLines,
                showIntentIcons,
                slotScreenOffset,
                lineWorldZ,
                intentIconScreenOffset
            );
        }
    }

    public void SetShowIntentLines(bool value)
    {
        showIntentLines = value;
        foreach (var v in _visuals)
            v.SetLineEnabled(value);
    }

    public void SetShowIntentIcons(bool value)
    {
        showIntentIcons = value;
        foreach (var v in _visuals)
            v.SetIconEnabled(value);
    }

    private void QueueRefresh()
    {
        _refreshQueued = true;
        _framesSinceEnable = 0;
    }

    private void HandleIntentsPlanned(List<BattleManager.EnemyIntent> intents)
    {
        Build(intents);
    }

    private void Build(List<BattleManager.EnemyIntent> intents)
    {
        ClearAll();
        if (intents == null || intents.Count == 0) return;
        EnsureUIRefs();

        foreach (var intent in intents)
        {
            if (intent.enemy == null) continue;
            CreateVisual(intent);
        }
    }

    private void CreateVisual(BattleManager.EnemyIntent intent)
    {
        GameObject root = new GameObject($"IntentVisual_{intent.enemy.name}");
        root.transform.SetParent(transform, false);

        LineRenderer lr = root.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.useWorldSpace = true;
        lr.startWidth = lineWidth;
        lr.endWidth = lineWidth;
        lr.sortingLayerName = lineSortingLayerName;
        lr.sortingOrder = lineSortingOrder;
        if (lineMaterial != null) lr.material = lineMaterial;
        lr.enabled = showIntentLines;

        Image iconImg = null;
        Image dotImg = null;
        TextMeshProUGUI dmgTmp = null;

        if (uiRoot != null && showIntentIcons)
        {
            GameObject iconGo = new GameObject($"IntentIcon_{intent.enemy.name}");
            iconGo.transform.SetParent(uiRoot, false);

            iconImg = iconGo.AddComponent<Image>();
            iconImg.sprite = GetIconForIntent(intent.type);
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;

            RectTransform rt = iconImg.rectTransform;
            rt.sizeDelta = intentIconSize;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            GameObject dotGo = new GameObject("TargetDot");
            dotGo.transform.SetParent(iconGo.transform, false);

            dotImg = dotGo.AddComponent<Image>();
            dotImg.sprite = targetDotSprite;
            dotImg.preserveAspect = true;
            dotImg.raycastTarget = false;

            RectTransform drt = dotImg.rectTransform;
            drt.sizeDelta = Vector2.one * targetDotSize;
            drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 0.5f);
            drt.pivot = new Vector2(0.5f, 0.5f);
            drt.anchoredPosition = new Vector2(0f, targetDotYOffset);

            GameObject dmgGo = new GameObject("IntentDamage");
            dmgGo.transform.SetParent(iconGo.transform, false);

            dmgTmp = dmgGo.AddComponent<TextMeshProUGUI>();
            dmgTmp.raycastTarget = false;
            dmgTmp.enableWordWrapping = false;
            dmgTmp.alignment = TextAlignmentOptions.Left;
            dmgTmp.fontSize = intentDamageFontSize;
            dmgTmp.text = "";

            RectTransform dmgRt = dmgTmp.rectTransform;
            dmgRt.anchorMin = dmgRt.anchorMax = new Vector2(0.5f, 0.5f);
            dmgRt.pivot = new Vector2(0f, 0.5f);
            dmgRt.anchoredPosition = intentDamageTextOffset;
        }

        _visuals.Add(new IntentVisual(
            intent.enemy,
            intent.enemy.transform,
            intent.targetPartyIndex,
            intent.type,
            lr,
            iconImg,
            dotImg,
            dmgTmp,
            root,
            fighterColor,
            mageColor,
            ninjaColor,
            fallbackColor
        ));
    }

    private void ClearAll()
    {
        foreach (var v in _visuals)
            v.Destroy();
        _visuals.Clear();
    }

    private void EnsureUIRefs()
    {
        if (uiCanvas == null)
            uiCanvas = FindCanvasByName(preferredCanvasName);

        if (uiRoot == null && uiCanvas != null)
            uiRoot = uiCanvas.transform as RectTransform;
    }

    private Camera GetUICamera()
    {
        if (uiCanvas == null) return null;
        return uiCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : uiCanvas.worldCamera;
    }

    private Canvas FindCanvasByName(string canvasName)
    {
        foreach (var c in Resources.FindObjectsOfTypeAll<Canvas>())
            if (c.name == canvasName) return c;
        return null;
    }

    private Sprite GetIconForIntent(BattleManager.IntentType type)
    {
        return type == BattleManager.IntentType.AoEAttack ? aoeAttackIcon : attackIcon;
    }

    private void TryForcePlanIntentsIfNone()
    {
        if (battleManager == null || _visuals.Count > 0 || _framesSinceEnable < 1)
            return;

        FieldInfo f = battleManager.GetType().GetField("_plannedIntents", BindingFlags.Instance | BindingFlags.NonPublic);
        if (f?.GetValue(battleManager) is List<BattleManager.EnemyIntent> cached && cached.Count > 0)
            Build(cached);
    }

    private class IntentVisual
    {
        private readonly Monster enemyRef;
        private readonly Transform enemyTf;
        private readonly int targetIndex;
        private readonly BattleManager.IntentType type;
        private readonly LineRenderer lr;
        private readonly Image icon;
        private readonly Image dot;
        private readonly TextMeshProUGUI dmg;
        private readonly GameObject root;

        private readonly Color fighterColor;
        private readonly Color mageColor;
        private readonly Color ninjaColor;
        private readonly Color fallbackColor;

        public IntentVisual(
            Monster enemyRef,
            Transform enemyTf,
            int targetIndex,
            BattleManager.IntentType type,
            LineRenderer lr,
            Image icon,
            Image dot,
            TextMeshProUGUI dmg,
            GameObject root,
            Color fighterColor,
            Color mageColor,
            Color ninjaColor,
            Color fallbackColor)
        {
            this.enemyRef = enemyRef;
            this.enemyTf = enemyTf;
            this.targetIndex = targetIndex;
            this.type = type;
            this.lr = lr;
            this.icon = icon;
            this.dot = dot;
            this.dmg = dmg;
            this.root = root;

            this.fighterColor = fighterColor;
            this.mageColor = mageColor;
            this.ninjaColor = ninjaColor;
            this.fallbackColor = fallbackColor;
        }

        public void SetLineEnabled(bool v)
        {
            if (lr != null) lr.enabled = v;
        }

        public void SetIconEnabled(bool v)
        {
            if (icon != null) icon.enabled = v;
            if (dot != null) dot.enabled = v;
            if (dmg != null) dmg.enabled = v;
        }

        public void Update(
            Camera worldCam,
            Canvas canvas,
            RectTransform uiRoot,
            Camera uiCam,
            PartyHUD hud,
            BattleManager bm,
            bool showLine,
            bool showIcon,
            Vector2 slotOffset,
            float worldZ,
            Vector2 iconOffset)
        {
            if (enemyTf == null) return;

            if (showLine && lr != null)
            {
                RectTransform slot = hud?.GetSlotRectTransform(targetIndex);
                if (slot != null && worldCam != null)
                {
                    Vector3 startWorld = enemyTf.position;
                    startWorld.z = worldZ;

                    Vector3 slotWorld = slot.TransformPoint(slot.rect.center);

                    Vector3 slotScreen = RectTransformUtility.WorldToScreenPoint(uiCam, slotWorld);
                    slotScreen += new Vector3(slotOffset.x, slotOffset.y, 0f);

                    float startScreenZ = worldCam.WorldToScreenPoint(startWorld).z;

                    Vector3 endWorld = worldCam.ScreenToWorldPoint(new Vector3(slotScreen.x, slotScreen.y, startScreenZ));
                    endWorld.z = worldZ;

                    lr.SetPosition(0, startWorld);
                    lr.SetPosition(1, endWorld);
                    lr.enabled = true;

                    Color targetColor = GetTargetColor(bm, targetIndex, fighterColor, mageColor, ninjaColor, fallbackColor);
                    lr.startColor = targetColor;
                    lr.endColor = targetColor;
                }
                else
                {
                    lr.enabled = false;
                }
            }
            else
            {
                if (lr != null) lr.enabled = false;
            }

            if (showIcon && icon != null && worldCam != null)
            {
                Vector3 screen = worldCam.WorldToScreenPoint(enemyTf.position);
                screen += new Vector3(iconOffset.x, iconOffset.y, 0f);

                if (uiRoot != null)
                {
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(uiRoot, screen, uiCam, out Vector2 local);
                    icon.rectTransform.anchoredPosition = local;
                }

                Color targetColor = GetTargetColor(bm, targetIndex, fighterColor, mageColor, ninjaColor, fallbackColor);

                if (dot != null)
                    dot.color = targetColor;

                if (dmg != null)
                {
                    int raw = (enemyRef != null) ? enemyRef.GetDamage() : 0;
                    dmg.text = raw.ToString();
                    dmg.color = Color.white;
                }

                icon.enabled = true;
                if (dot != null) dot.enabled = true;
                if (dmg != null) dmg.enabled = true;
            }
            else
            {
                if (icon != null) icon.enabled = false;
                if (dot != null) dot.enabled = false;
                if (dmg != null) dmg.enabled = false;
            }
        }

        private static Color GetTargetColor(
            BattleManager bm,
            int targetIndex,
            Color fighterColor,
            Color mageColor,
            Color ninjaColor,
            Color fallbackColor)
        {
            if (bm == null) return fallbackColor;

            HeroStats hero = bm.GetHeroAtPartyIndex(targetIndex);
            string cn = hero?.BaseClassDef?.className;

            if (string.IsNullOrWhiteSpace(cn))
                return fallbackColor;

            cn = cn.ToLowerInvariant();

            if (cn.Contains("fighter")) return fighterColor;
            if (cn.Contains("mage")) return mageColor;
            if (cn.Contains("ninja")) return ninjaColor;

            return fallbackColor;
        }

        public void Destroy()
        {
            if (icon != null) UnityEngine.Object.Destroy(icon.gameObject);
            if (root != null) UnityEngine.Object.Destroy(root);
        }
    }
}
