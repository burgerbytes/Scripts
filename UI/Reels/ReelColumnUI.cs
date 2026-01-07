using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Reels
{
    public class ReelColumnUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private RectTransform viewport;
        [SerializeField] private RectTransform stripContent;

        [Tooltip("Visible row images (odd length: 3/5/7). If left empty, will auto-pull Image components from StripContent children in sibling order.")]
        [SerializeField] private Image[] rowImages; // MUST be odd length (5 or 7)

        [Header("Initial Setup (Fallback)")]
        [Tooltip("If your ReelSpinSystem no longer calls SetStrip(), assign this so the column can initialize itself.")]
        [SerializeField] private ReelStripSO initialStrip;

        [Tooltip("Starting center index when using Initial Strip.")]
        [SerializeField] private int initialStartIndex = 0;

        [Header("Appearance")]
        [Tooltip("If a ReelSymbolSO is missing an icon, this sprite is used instead.")]
        [SerializeField] private Sprite missingIconFallback;

        [Header("Spin Tuning")]
        [Tooltip("Height of a row in UI units. Must match Symbol image size/spacing.")]
        [SerializeField] private float rowHeight = 64f;

        [Tooltip("Total time of the spin animation.")]
        [SerializeField] private float spinDuration = 0.35f;

        [Tooltip("How many full strip loops before landing on the target.")]
        [SerializeField] private int extraLoops = 2;

        [Tooltip("If true, uses unscaled time (ignores Time.timeScale).")]
        [SerializeField] private bool useUnscaledTime = false;

        [Header("Debug")]
        [SerializeField] private bool debugLog = false;

        private ReelStripSO strip;

        // This is the "resolved" center symbol index in the strip (0..n-1)
        private int currentCenterIndex = 0;

        // Visual scroll offset within a single row [0, rowHeight)
        // We keep it bounded so the strip never runs out of children.
        private float scrollOffset = 0f;

        // Used to prevent overlapping spins
        private bool spinning = false;

        private bool hasInitialized = false;

        private void Awake()
        {
            // Auto-wire rowImages if user didn’t set them (or prefab changes broke them).
            AutoWireRowImagesIfNeeded();

            // Self-init if ReelSpinSystem doesn't call SetStrip anymore.
            TryInitializeFromInspector();
        }

        private void OnEnable()
        {
            // In case this object gets enabled after Awake (e.g., UI panels toggled)
            AutoWireRowImagesIfNeeded();
            TryInitializeFromInspector();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Helps keep inspector wiring resilient during edits.
            if (!Application.isPlaying)
            {
                AutoWireRowImagesIfNeeded();
            }
        }
#endif

        /* ============================================================
         * Public API
         * ============================================================ */

        public void SetStrip(ReelStripSO newStrip, int startIndex = 0)
        {
            strip = newStrip;

            AutoWireRowImagesIfNeeded();

            if (!ValidateSetup(logErrors: true))
                return;

            ApplyRectTransformConventions();
            PositionRows();

            int n = strip.symbols.Count;
            currentCenterIndex = Mathf.Clamp(startIndex, 0, n - 1);

            scrollOffset = 0f;
            stripContent.anchoredPosition = new Vector2(stripContent.anchoredPosition.x, 0f);

            RefreshRows(currentCenterIndex);

            hasInitialized = true;
        }

        /// <summary>
        /// Spins the reel and lands on a random symbol.
        /// MUST be started via StartCoroutine().
        /// </summary>
        public IEnumerator SpinToRandom(System.Random rng)
        {
            AutoWireRowImagesIfNeeded();

            if (!ValidateSetup(logErrors: true))
                yield break;

            int n = strip.symbols.Count;
            int target = rng.Next(0, n);
            yield return SpinToIndex(target);
        }

        /// <summary>
        /// Spins the reel and lands on a specific symbol index.
        /// MUST be started via StartCoroutine().
        /// </summary>
        public IEnumerator SpinToIndex(int targetIndex)
        {
            AutoWireRowImagesIfNeeded();

            if (!ValidateSetup(logErrors: true))
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

            // We'll move totalDistance over spinDuration, but with ease-out (fast then slow).
            float elapsed = 0f;
            float remaining = totalDistance;

            // Start fully "snapped"
            scrollOffset = Mathf.Clamp(scrollOffset, 0f, rowHeight);
            stripContent.anchoredPosition = new Vector2(stripContent.anchoredPosition.x, -scrollOffset);

            while (elapsed < spinDuration && remaining > 0.0001f)
            {
                float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                elapsed += dt;

                float t = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, spinDuration));

                // Speed curve: fast at start, slows near the end (no bounce)
                float speedFactor = Mathf.Lerp(2.0f, 0.35f, SmoothStep01(t));

                float baseSpeed = totalDistance / Mathf.Max(0.0001f, spinDuration);
                float speed = baseSpeed * speedFactor;

                float move = Mathf.Min(speed * dt, remaining);
                remaining -= move;

                AdvanceVisualScroll(move, n);

                yield return null;
            }

            // Hard snap to final target (no blank space, always looped)
            currentCenterIndex = targetIndex;
            scrollOffset = 0f;
            stripContent.anchoredPosition = new Vector2(stripContent.anchoredPosition.x, 0f);
            RefreshRows(currentCenterIndex);

            if (debugLog)
                Debug.Log($"{name}: Spin complete. Landed on {currentCenterIndex}", this);

            spinning = false;
        }

        /// <summary>
        /// Returns the symbol currently in the CENTER of the visible window.
        /// In the default setup (odd rowImages length, centered), this is your "middle row" symbol.
        /// </summary>
        public ReelSymbolSO GetMiddleRowSymbol()
        {
            if (strip == null || strip.symbols == null || strip.symbols.Count == 0)
                return null;

            return strip.symbols[currentCenterIndex];
        }

        /// <summary>
        /// Legacy name kept so older code continues to compile.
        /// </summary>
        public ReelSymbolSO GetCurrentSymbol()
        {
            return GetMiddleRowSymbol();
        }

        /// <summary>
        /// Returns true while this reel is currently spinning.
        /// </summary>
        public bool IsSpinning()
        {
            return spinning;
        }

        /* ============================================================
         * Core looping scroll mechanic
         * ============================================================ */

        private void AdvanceVisualScroll(float deltaDistance, int stripCount)
        {
            // We scroll "down" visually by increasing scrollOffset.
            scrollOffset += deltaDistance;

            // Every time we pass a full row, we:
            // - wrap scrollOffset back
            // - advance the center symbol index
            // - refresh sprites so it looks like an infinite loop
            while (scrollOffset >= rowHeight)
            {
                scrollOffset -= rowHeight;
                currentCenterIndex = (currentCenterIndex + 1) % stripCount;

                RefreshRows(currentCenterIndex);
            }

            // Apply offset (negative y because our rows are laid out downward)
            stripContent.anchoredPosition = new Vector2(stripContent.anchoredPosition.x, -scrollOffset);
        }

        /* ============================================================
         * Layout / Visual
         * ============================================================ */

        private void ApplyRectTransformConventions()
        {
            // Keep StripContent anchored at the top so our row positions are stable.
            stripContent.anchorMin = new Vector2(0f, 1f);
            stripContent.anchorMax = new Vector2(1f, 1f);
            stripContent.pivot = new Vector2(0.5f, 1f);
        }

        private void PositionRows()
        {
            // Rows are stacked downward: y = -i * rowHeight
            for (int i = 0; i < rowImages.Length; i++)
            {
                Image img = rowImages[i];
                if (img == null) continue;

                RectTransform rt = img.rectTransform;

                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);

                rt.sizeDelta = new Vector2(rowHeight, rowHeight);
                rt.anchoredPosition = new Vector2(0f, -i * rowHeight);

                img.enabled = true;
                img.color = Color.white;
                img.preserveAspect = true;
            }

            // This strip only needs to be tall enough for the visible rows.
            // The mask provides the "window"; we never scroll beyond one rowHeight visually.
            stripContent.sizeDelta = new Vector2(stripContent.sizeDelta.x, rowImages.Length * rowHeight);
        }

        private void RefreshRows(int centerIndex)
        {
            if (strip == null || strip.symbols == null || strip.symbols.Count == 0)
                return;

            int n = strip.symbols.Count;
            int mid = rowImages.Length / 2;

            for (int i = 0; i < rowImages.Length; i++)
            {
                Image img = rowImages[i];
                if (img == null) continue;

                int offset = i - mid;
                int symbolIndex = (centerIndex + offset) % n;
                if (symbolIndex < 0) symbolIndex += n;

                ReelSymbolSO sym = strip.symbols[symbolIndex];

                Sprite s = null;
                if (sym != null && sym.icon != null) s = sym.icon;
                else s = missingIconFallback;

                img.sprite = s;
                img.enabled = (img.sprite != null);
                img.color = Color.white;
                img.preserveAspect = true;

                if (debugLog && sym != null && sym.icon == null)
                    Debug.LogWarning($"{name}: Symbol '{sym.name}' has no icon. Using fallback.", this);
            }
        }

        /* ============================================================
         * Auto-init / Auto-wire
         * ============================================================ */

        private void TryInitializeFromInspector()
        {
            // If another system already called SetStrip, don't stomp it.
            if (hasInitialized)
                return;

            // Only attempt if we have something assigned in inspector.
            if (strip == null && initialStrip != null)
            {
                if (debugLog) Debug.Log($"{name}: Initializing from inspector initialStrip.", this);
                SetStrip(initialStrip, initialStartIndex);
            }
        }

        private void AutoWireRowImagesIfNeeded()
        {
            if (stripContent == null)
                return;

            // If rowImages is already correctly assigned, do nothing.
            if (rowImages != null && rowImages.Length >= 3 && (rowImages.Length % 2) == 1)
            {
                bool anyNull = false;
                for (int i = 0; i < rowImages.Length; i++)
                {
                    if (rowImages[i] == null) { anyNull = true; break; }
                }

                if (!anyNull)
                    return;
            }

            // Pull direct child Images under StripContent in sibling order.
            // This matches your hierarchy: StripContent -> Symbol_0, Symbol_0 (1), ...
            List<Image> found = new List<Image>();
            for (int i = 0; i < stripContent.childCount; i++)
            {
                Transform child = stripContent.GetChild(i);
                Image img = child.GetComponent<Image>();
                if (img != null)
                    found.Add(img);
            }

            if (found.Count >= 3)
            {
                // Ensure odd length.
                if (found.Count % 2 == 0)
                    found.RemoveAt(found.Count - 1);

                rowImages = found.ToArray();

                if (debugLog)
                    Debug.Log($"{name}: Auto-wired rowImages from StripContent children. Count={rowImages.Length}", this);
            }
        }

        /* ============================================================
         * Validation / Helpers
         * ============================================================ */

        private bool ValidateSetup(bool logErrors)
        {
            bool ok = true;

            if (viewport == null)
            {
                ok = false;
                if (logErrors) Debug.LogError($"{name}: viewport not assigned.", this);
            }

            if (stripContent == null)
            {
                ok = false;
                if (logErrors) Debug.LogError($"{name}: stripContent not assigned.", this);
            }

            if (rowImages == null || rowImages.Length < 3 || rowImages.Length % 2 == 0)
            {
                ok = false;
                if (logErrors) Debug.LogError($"{name}: rowImages must be odd length (3/5/7) and >= 3.", this);
            }
            else
            {
                for (int i = 0; i < rowImages.Length; i++)
                {
                    if (rowImages[i] == null)
                    {
                        ok = false;
                        if (logErrors) Debug.LogError($"{name}: rowImages[{i}] is NULL. Wire Symbol_{i}.", this);
                    }
                }
            }

            if (strip == null || strip.symbols == null || strip.symbols.Count == 0)
            {
                ok = false;
                if (logErrors) Debug.LogError($"{name}: strip is null or has 0 symbols.", this);
            }

            return ok;
        }

        private static float SmoothStep01(float t)
        {
            // smoothstep 0..1
            return t * t * (3f - 2f * t);
        }
    }
}
