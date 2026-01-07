using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class EnemyIntentVisualizer : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private BattleManager battleManager;
    [SerializeField] private PartyHUD partyHUD;
    [SerializeField] private Camera worldCamera;

    [Header("UI (order numbers)")]
    [SerializeField] private Canvas uiCanvas;
    [SerializeField] private RectTransform uiRoot;

    [Header("Line Sorting")]
    [SerializeField] private string lineSortingLayerName = "UI";
    [SerializeField] private int lineSortingOrder = 500;

    [Header("Line Look")]
    [SerializeField] private Material lineMaterial;
    [SerializeField] private float lineWidth = 0.05f;

    [Header("World Depth")]
    [SerializeField] private float worldZ = 0f;

    [Header("Offsets")]
    [SerializeField] private Vector3 monsterLineAnchorOffset = new Vector3(0f, 1.0f, 0f);
    [SerializeField] private Vector3 orderNumberWorldOffset = new Vector3(0f, 1.35f, 0f);
    [SerializeField] private Vector2 slotScreenOffset = Vector2.zero;

    [Header("Order Text")]
    [SerializeField] private TMP_FontAsset orderFont;
    [SerializeField] private int orderFontSize = 40;

    private readonly List<IntentVisual> _visuals = new();

    private void Awake()
    {
        if (battleManager == null) battleManager = FindFirstObjectByType<BattleManager>();
        if (partyHUD == null) partyHUD = FindFirstObjectByType<PartyHUD>();
        if (worldCamera == null) worldCamera = Camera.main;

        if (uiCanvas == null) uiCanvas = FindFirstObjectByType<Canvas>();
        if (uiRoot == null && uiCanvas != null) uiRoot = uiCanvas.transform as RectTransform;
    }

    private void OnEnable()
    {
        if (battleManager != null)
        {
            battleManager.OnEnemyIntentsPlanned += HandleIntentsPlanned;
            battleManager.OnBattleStateChanged += HandleBattleStateChanged;
        }
    }

    private void OnDisable()
    {
        if (battleManager != null)
        {
            battleManager.OnEnemyIntentsPlanned -= HandleIntentsPlanned;
            battleManager.OnBattleStateChanged -= HandleBattleStateChanged;
        }
    }

    private void LateUpdate()
    {
        // Update and prune if monster got destroyed
        for (int i = _visuals.Count - 1; i >= 0; i--)
        {
            if (_visuals[i] == null || !_visuals[i].IsValid)
            {
                _visuals[i]?.Destroy();
                _visuals.RemoveAt(i);
                continue;
            }

            _visuals[i].Update(
                worldCamera,
                partyHUD,
                uiCanvas,
                uiRoot,
                worldZ,
                monsterLineAnchorOffset,
                orderNumberWorldOffset,
                slotScreenOffset
            );
        }
    }

    private void HandleBattleStateChanged(BattleManager.BattleState state)
    {
        if (state != BattleManager.BattleState.PlayerPhase)
            ClearAll();
    }

    private void HandleIntentsPlanned(List<BattleManager.EnemyIntent> intents)
    {
        ClearAll();

        if (intents == null || intents.Count == 0)
            return;

        for (int i = 0; i < intents.Count; i++)
        {
            var intent = intents[i];
            if (intent.enemy == null) continue;

            CreateVisual(intent.enemy.transform, intent.targetPartyIndex, i + 1);
        }
    }

    private void CreateVisual(Transform monsterTransform, int targetPartyIndex, int order)
    {
        GameObject root = new GameObject($"IntentVisual_{order}");
        root.transform.SetParent(transform, false);

        LineRenderer lr = root.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.useWorldSpace = true;
        lr.startWidth = lineWidth;
        lr.endWidth = lineWidth;
        lr.sortingLayerName = lineSortingLayerName;
        lr.sortingOrder = lineSortingOrder;
        if (lineMaterial != null) lr.material = lineMaterial;

        TextMeshProUGUI tmpUI = null;

        if (uiRoot != null)
        {
            GameObject txtGo = new GameObject($"OrderUI_{order}");
            txtGo.transform.SetParent(uiRoot, false);

            tmpUI = txtGo.AddComponent<TextMeshProUGUI>();
            tmpUI.text = order.ToString();
            tmpUI.fontSize = orderFontSize;
            tmpUI.alignment = TextAlignmentOptions.Center;
            tmpUI.raycastTarget = false;
            if (orderFont != null) tmpUI.font = orderFont;

            RectTransform rt = tmpUI.rectTransform;
            rt.sizeDelta = new Vector2(80, 80);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        _visuals.Add(new IntentVisual(monsterTransform, targetPartyIndex, lr, tmpUI, root));
    }

    private void ClearAll()
    {
        for (int i = 0; i < _visuals.Count; i++)
            _visuals[i]?.Destroy();

        _visuals.Clear();
    }

    private class IntentVisual
    {
        private readonly Transform _monster;
        private readonly int _targetPartyIndex;
        private readonly LineRenderer _lr;
        private readonly TextMeshProUGUI _tmpUI;
        private readonly GameObject _root;

        public bool IsValid => _monster != null && _lr != null && _root != null;

        public IntentVisual(Transform monster, int targetPartyIndex, LineRenderer lr, TextMeshProUGUI tmpUI, GameObject root)
        {
            _monster = monster;
            _targetPartyIndex = targetPartyIndex;
            _lr = lr;
            _tmpUI = tmpUI;
            _root = root;
        }

        public void Update(
            Camera cam,
            PartyHUD hud,
            Canvas canvas,
            RectTransform uiRoot,
            float worldZ,
            Vector3 monsterLineOffset,
            Vector3 monsterNumberOffset,
            Vector2 slotScreenOffset)
        {
            if (_monster == null || cam == null || hud == null)
                return;

            RectTransform targetSlot = hud.GetSlotRectTransform(_targetPartyIndex);
            if (targetSlot == null)
                return;

            // Monster start (world)
            Vector3 startWorld = _monster.position + monsterLineOffset;
            startWorld.z = worldZ;

            // Slot right-edge (screen -> world)
            Vector2 slotLocalEdge = new Vector2(targetSlot.rect.xMax, targetSlot.rect.center.y);
            Vector3 slotWorld = targetSlot.TransformPoint(slotLocalEdge);
            Vector3 slotScreen = RectTransformUtility.WorldToScreenPoint(null, slotWorld);
            slotScreen += new Vector3(slotScreenOffset.x, slotScreenOffset.y, 0f);

            float zDist = Mathf.Abs(cam.transform.position.z - worldZ);
            Vector3 endWorld = cam.ScreenToWorldPoint(new Vector3(slotScreen.x, slotScreen.y, zDist));
            endWorld.z = worldZ;

            _lr.SetPosition(0, startWorld);
            _lr.SetPosition(1, endWorld);

            // Number directly above monster (UI)
            if (_tmpUI != null && canvas != null)
            {
                Vector3 numWorld = _monster.position + monsterNumberOffset;
                Vector3 numScreen = cam.WorldToScreenPoint(numWorld);

                RectTransform canvasRect = canvas.transform as RectTransform;
                if (canvasRect != null)
                {
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        canvasRect,
                        numScreen,
                        canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : cam,
                        out Vector2 local);

                    _tmpUI.rectTransform.anchoredPosition = local;
                }
            }
        }

        public void Destroy()
        {
            if (_tmpUI != null)
                Object.Destroy(_tmpUI.gameObject);

            if (_root != null)
                Object.Destroy(_root);
        }
    }
}
