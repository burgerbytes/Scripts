using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UI.Reels
{
    public class ReelColumnUI : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [Header("References")]
        [SerializeField] private RectTransform viewport;
        [SerializeField] private RectTransform stripContent;

        [Tooltip("Visible row images (odd length: 3/5/7). If left empty, will auto-pull Image components from StripContent children in sibling order.")]
        [SerializeField] private Image[] rowImages;

        [Header("Initial Setup (Fallback)")]
        [SerializeField] private ReelStripSO initialStrip;
        [SerializeField] private int initialStartIndex = 0;

        [Header("Appearance")]
        [SerializeField] private Sprite missingIconFallback;

        [Header("Spin Tuning")]
        [SerializeField] private float rowHeight = 64f;
        [SerializeField] private float spinDuration = 0.35f;
        [SerializeField] private int extraLoops = 2;

        [Header("Stop Bounce")]
        [SerializeField] private bool stopBounceEnabled = true;
        [SerializeField] private float stopBouncePixels = 10f;
        [SerializeField] private float stopBounceSeconds = 0.12f;
        [SerializeField] private bool useUnscaledTime = false;

        [Header("Reel Lock")]
        [Tooltip("Hold the mouse/finger on this reel for this many seconds to toggle lock/unlock.")]
        [SerializeField] private float holdToToggleSeconds = 0.25f;

        [Tooltip("Optional root object (e.g., LockIcon + Checkbox) shown when locked.")]
        [SerializeField] private GameObject lockVisualRoot;

        [Tooltip("Optional checkbox toggle shown under the reel. Will be set to ON when locked.")]
        [SerializeField] private Toggle lockCheckbox;

        [Header("Debug")]
        [SerializeField] private bool debugLog = false;

        private ReelStripSO strip;
        private int currentCenterIndex = 0;
        private float scrollOffset = 0f;
        private bool spinning = false;
        private bool hasInitialized = false;

        private bool _locked = false;
        private Coroutine _holdRoutine;
        private bool _toggledThisPress;

        /* ============================================================
         * Unity
         * ============================================================ */

        private void Awake()
        {
            AutoWireRowImagesIfNeeded();
            TryInitializeFromInspector();
            ApplyLockedVisual();
        }

        private void OnEnable()
        {
            AutoWireRowImagesIfNeeded();
            TryInitializeFromInspector();
            ApplyLockedVisual();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying)
                AutoWireRowImagesIfNeeded();
        }
#endif

        /* ============================================================
         * Public API (used by ReelSpinSystem)
         * ============================================================ */

        public float ConfiguredSpinDuration => spinDuration;

        public bool HasValidStrip => strip != null && strip.symbols != null && strip.symbols.Count > 0;

        public bool IsSpinning() => spinning;

        public bool IsLocked => _locked;

        public void SetLocked(bool locked)
        {
            _locked = locked;
            ApplyLockedVisual();
        }

        public void ToggleLocked()
        {
            SetLocked(!_locked);
        }

        public void SetStrip(ReelStripSO newStrip, int startIndex = 0)
        {
            strip = newStrip;
            AutoWireRowImagesIfNeeded();

            // If strip is null, allow wiring/layout to still run, but don’t hard fail.
            ApplyRectTransformConventions();
            PositionRows();

            if (!HasValidStrip)
            {
                if (debugLog)
                    Debug.LogWarning($"{name}: SetStrip called but strip has no symbols yet.", this);
                return;
            }

            int n = strip.symbols.Count;
            currentCenterIndex = Mathf.Clamp(startIndex, 0, n - 1);

            scrollOffset = 0f;
            stripContent.anchoredPosition = Vector2.zero;

            RefreshRows(currentCenterIndex);
            hasInitialized = true;
        }

        public IEnumerator SpinToRandom(System.Random rng)
        {
            if (!EnsureReadyForSpin(logErrors: true))
                yield break;

            if (_locked)
                yield break;

            int n = strip.symbols.Count;
            int target = rng.Next(0, n);
            yield return SpinToIndex(target, -1f);
        }

        public IEnumerator SpinToRandom(System.Random rng, float durationOverrideSeconds)
        {
            if (!EnsureReadyForSpin(logErrors: true))
                yield break;

            if (_locked)
                yield break;

            int n = strip.symbols.Count;
            int target = rng.Next(0, n);
            yield return SpinToIndex(target, durationOverrideSeconds);
        }

        public IEnumerator SpinToIndex(int targetIndex)
        {
            yield return SpinToIndex(targetIndex, -1f);
        }

        public IEnumerator SpinToIndex(int targetIndex, float durationOverrideSeconds)
        {
            if (!EnsureReadyForSpin(logErrors: true))
                yield break;

            if (_locked)
                yield break;

            if (spinning)
                yield break;

            spinning = true;

            int n = strip.symbols.Count;
            targetIndex = Mathf.Clamp(targetIndex, 0, n - 1);

            // How many steps forward (each step = advance one symbol)
            int distance = (targetIndex - currentCenterIndex + n) % n;
            int totalSteps = extraLoops * n + distance;

            float totalDistance = totalSteps * rowHeight;

            float elapsed = 0f;
            float remaining = totalDistance;

            scrollOffset = Mathf.Clamp(scrollOffset, 0f, rowHeight);
            stripContent.anchoredPosition = new Vector2(0f, -scrollOffset);

            float duration = (durationOverrideSeconds > 0f) ? durationOverrideSeconds : spinDuration;

            while (elapsed < duration && remaining > 0.0001f)
            {
                float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                elapsed += dt;

                float t = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, duration));

                // fast -> slow
                float speedFactor = Mathf.Lerp(2.0f, 0.35f, SmoothStep01(t));
                float baseSpeed = totalDistance / Mathf.Max(0.0001f, duration);
                float speed = baseSpeed * speedFactor;

                float move = Mathf.Min(speed * dt, remaining);
                remaining -= move;

                AdvanceVisualScroll(move, n);

                yield return null;
            }

            // Snap cleanly to final
            currentCenterIndex = targetIndex;
            scrollOffset = 0f;
            stripContent.anchoredPosition = Vector2.zero;
            RefreshRows(currentCenterIndex);

            if (stopBounceEnabled)
                yield return PlayStopBounce();

            if (debugLog)
                Debug.Log($"{name}: Spin complete. Landed on {currentCenterIndex}", this);

            spinning = false;
        }

        /// <summary>
        /// Uses what is VISUALLY centered in the viewport (closest row image to viewport center).
        /// </summary>
        public ReelSymbolSO GetMiddleRowSymbol()
        {
            if (!HasValidStrip)
                return null;

            // Fallback: logical center if we can't evaluate visuals
            if (viewport == null || rowImages == null || rowImages.Length == 0)
                return strip.symbols[currentCenterIndex];

            Vector3 viewportCenterWorld = viewport.TransformPoint(viewport.rect.center);

            int bestRow = -1;
            float bestSqr = float.PositiveInfinity;

            for (int i = 0; i < rowImages.Length; i++)
            {
                var img = rowImages[i];
                if (img == null) continue;

                RectTransform imgRt = img.rectTransform;
                Vector3 imgCenterWorld = imgRt.TransformPoint(imgRt.rect.center);

                float sqr = (imgCenterWorld - viewportCenterWorld).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    bestRow = i;
                }
            }

            if (bestRow < 0)
                return strip.symbols[currentCenterIndex];

            int n = strip.symbols.Count;
            int mid = rowImages.Length / 2;

            int offsetFromLogicalCenter = bestRow - mid;
            int idx = (currentCenterIndex + offsetFromLogicalCenter) % n;
            if (idx < 0) idx += n;

            return strip.symbols[idx];
        }

        /* ============================================================
         * Pointer hold-to-lock
         * ============================================================ */

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!isActiveAndEnabled) return;
            if (spinning) return; // don't toggle mid-spin (keeps UX predictable)

            _toggledThisPress = false;

            if (_holdRoutine != null)
                StopCoroutine(_holdRoutine);

            _holdRoutine = StartCoroutine(HoldToToggleRoutine());
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            CancelHoldRoutine();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            CancelHoldRoutine();
        }

        private void CancelHoldRoutine()
        {
            if (_holdRoutine != null)
            {
                StopCoroutine(_holdRoutine);
                _holdRoutine = null;
            }
        }

        private IEnumerator HoldToToggleRoutine()
        {
            float t = 0f;
            while (t < holdToToggleSeconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            _toggledThisPress = true;
            ToggleLocked();

            if (debugLog)
                Debug.Log($"{name}: Reel lock toggled -> {(_locked ? "LOCKED" : "UNLOCKED")}", this);

            _holdRoutine = null;
        }

        private void ApplyLockedVisual()
        {
            // Root should always stay active so layout doesn't collapse
            if (lockVisualRoot != null && !lockVisualRoot.activeSelf)
                lockVisualRoot.SetActive(true);

            if (lockCheckbox != null)
            {
                lockCheckbox.isOn = _locked;
                lockCheckbox.interactable = false; // visual-only indicator
            }
        }

        /* ============================================================
         * Layout / Visual (top-aligned fix)
         * ============================================================ */

        private void ApplyRectTransformConventions()
        {
            if (stripContent == null) return;

            stripContent.anchorMin = new Vector2(0f, 1f);
            stripContent.anchorMax = new Vector2(1f, 1f);
            stripContent.pivot = new Vector2(0.5f, 1f);
            stripContent.localScale = Vector3.one;
            stripContent.anchoredPosition = Vector2.zero;
        }

        private void PositionRows()
        {
            if (rowImages == null) return;

            for (int i = 0; i < rowImages.Length; i++)
            {
                var img = rowImages[i];
                if (img == null) continue;

                RectTransform rt = img.rectTransform;

                // Ensure each row is top-anchored/pivoted so y=0 starts at top of viewport.
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);

                rt.localScale = Vector3.one;
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, rowHeight);

                rt.anchoredPosition = new Vector2(0f, -i * rowHeight);
            }

            if (stripContent != null)
            {
                stripContent.sizeDelta = new Vector2(
                    stripContent.sizeDelta.x,
                    rowImages.Length * rowHeight
                );
            }
        }

        private void RefreshRows(int centerIndex)
        {
            if (!HasValidStrip) return;

            int n = strip.symbols.Count;
            int mid = rowImages.Length / 2;

            for (int i = 0; i < rowImages.Length; i++)
            {
                Image img = rowImages[i];
                if (img == null) continue;

                int idx = (centerIndex + (i - mid)) % n;
                if (idx < 0) idx += n;

                ReelSymbolSO sym = strip.symbols[idx];
                Sprite sprite = sym != null && sym.icon != null ? sym.icon : missingIconFallback;

                img.enabled = sprite != null;
                img.sprite = sprite;
                img.preserveAspect = true;
                img.color = Color.white;
            }
        }

        /* ============================================================
         * Core looping scroll
         * ============================================================ */

        private void AdvanceVisualScroll(float delta, int stripCount)
        {
            scrollOffset += delta;

            while (scrollOffset >= rowHeight)
            {
                scrollOffset -= rowHeight;
                currentCenterIndex = (currentCenterIndex + 1) % stripCount;
                RefreshRows(currentCenterIndex);
            }

            stripContent.anchoredPosition = new Vector2(0f, -scrollOffset);
        }

        /* ============================================================
         * Helpers / Validation
         * ============================================================ */

        private void AutoWireRowImagesIfNeeded()
        {
            if (rowImages != null && rowImages.Length > 0)
            {
                foreach (var img in rowImages)
                    if (img == null) goto rebuild;
                return;
            }

        rebuild:
            if (!stripContent) return;

            List<Image> found = new List<Image>();
            foreach (Transform t in stripContent)
                if (t.TryGetComponent(out Image img))
                    found.Add(img);

            rowImages = found.ToArray();
        }

        private void TryInitializeFromInspector()
        {
            if (hasInitialized || strip != null) return;
            if (initialStrip != null && initialStrip.symbols != null && initialStrip.symbols.Count > 0)
                SetStrip(initialStrip, initialStartIndex);
        }

        private bool EnsureReadyForSpin(bool logErrors)
        {
            AutoWireRowImagesIfNeeded();
            ApplyRectTransformConventions();
            PositionRows();

            if (viewport == null || stripContent == null || rowImages == null || rowImages.Length < 3)
            {
                if (logErrors)
                    Debug.LogError($"{name}: ReelColumnUI not wired correctly (viewport/stripContent/rows).", this);
                return false;
            }

            if (!HasValidStrip)
            {
                if (logErrors)
                    Debug.LogError($"{name}: ReelColumnUI has no strip assigned (or strip has 0 symbols).", this);
                return false;
            }

            return true;
        }

        private IEnumerator PlayStopBounce()
        {
            if (!stopBounceEnabled || stripContent == null)
                yield break;

            float dur = Mathf.Max(0.01f, stopBounceSeconds);
            float amp = Mathf.Max(0f, stopBouncePixels);

            Vector2 basePos = stripContent.anchoredPosition;
            float t = 0f;

            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / dur);

                float y = Mathf.Sin(u * Mathf.PI) * amp;
                stripContent.anchoredPosition = new Vector2(basePos.x, basePos.y - y);

                yield return null;
            }

            stripContent.anchoredPosition = basePos;
        }

        private static float SmoothStep01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
