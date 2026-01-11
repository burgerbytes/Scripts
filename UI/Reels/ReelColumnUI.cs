using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UI.Reels
{
    public class ReelColumnUI : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        [Header("Strip Data")]
        [SerializeField] private ReelStripSO strip;
        [SerializeField] private int initialStartIndex = 0;

        [Header("Spin Settings")]
        [SerializeField] private float spinSeconds = 0.9f;
        [SerializeField] private int extraLoops = 2;
        [SerializeField] private float stopBounceSeconds = 0.12f;

        [Header("UI References")]
        [SerializeField] private RectTransform viewport;
        [Tooltip("Visible row images (odd length: 3/5/7...). Middle one is the \"payout\" row.")]
        [SerializeField] private Image[] rowImages;

        [Header("Pointer hold-to-lock (optional)")]
        [SerializeField] private bool allowHoldToLock = false;
        [SerializeField] private float holdSecondsToLock = 0.35f;

        private bool isPointerDown;
        private float pointerDownTime;
        private bool isLocked;

        private bool isSpinning;
        private int currentCenterIndex = 0;

        public bool IsIdle => !isSpinning;
        public float ConfiguredSpinDuration => spinSeconds;
        public ReelStripSO Strip => strip;

        private bool HasValidStrip => strip != null && strip.symbols != null && strip.symbols.Count > 0;

        private void Awake()
        {
            if (HasValidStrip)
                currentCenterIndex = Mod(initialStartIndex, strip.symbols.Count);

            RefreshVisibleRows();
        }

        /// <summary>
        /// ✅ NEW: Allow BattleManager/ReelSpinSystem to assign strips based on party.
        /// </summary>
        public void SetStrip(ReelStripSO newStrip, int startIndex = 0, bool refreshNow = true)
        {
            strip = newStrip;
            initialStartIndex = startIndex;

            if (HasValidStrip)
                currentCenterIndex = Mod(initialStartIndex, strip.symbols.Count);
            else
                currentCenterIndex = 0;

            if (refreshNow)
                RefreshVisibleRows();
        }

        private void Update()
        {
            if (!allowHoldToLock)
                return;

            if (isPointerDown && !isLocked && !isSpinning)
            {
                if (Time.unscaledTime - pointerDownTime >= holdSecondsToLock)
                    isLocked = true;
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            isPointerDown = true;
            pointerDownTime = Time.unscaledTime;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            isPointerDown = false;

            // Tap unlocks if already locked
            if (allowHoldToLock && isLocked && !isSpinning)
                isLocked = false;
        }

        public IEnumerator SpinToRandom(System.Random rng, float durationOverride = -1f)
        {
            if (!HasValidStrip)
                yield break;

            if (isSpinning)
                yield break;

            if (allowHoldToLock && isLocked)
                yield break;

            isSpinning = true;

            float dur = (durationOverride > 0f) ? durationOverride : spinSeconds;
            int n = strip.symbols.Count;

            int target = rng.Next(0, n);

            // Ensure we spin extra loops + land on target
            int totalSteps = (extraLoops * n) + StepsForward(currentCenterIndex, target, n);
            float stepSeconds = dur / Mathf.Max(1, totalSteps);

            for (int s = 0; s < totalSteps; s++)
            {
                currentCenterIndex = Mod(currentCenterIndex + 1, n);
                RefreshVisibleRows();
                yield return new WaitForSeconds(stepSeconds);
            }

            // Optional little bounce delay
            if (stopBounceSeconds > 0f)
                yield return new WaitForSeconds(stopBounceSeconds);

            isSpinning = false;
        }

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

        public int GetMiddleRowIndex()
        {
            if (!HasValidStrip)
                return 0;

            int n = strip.symbols.Count;
            int idx = currentCenterIndex % n;
            if (idx < 0) idx += n;
            return idx;
        }

        private void RefreshVisibleRows()
        {
            if (!HasValidStrip || rowImages == null || rowImages.Length == 0)
                return;

            int n = strip.symbols.Count;
            int mid = rowImages.Length / 2;

            for (int i = 0; i < rowImages.Length; i++)
            {
                int delta = i - mid;
                int idx = Mod(currentCenterIndex + delta, n);

                ReelSymbolSO sym = strip.symbols[idx];
                if (rowImages[i] != null)
                    rowImages[i].sprite = sym != null ? sym.icon : null;
            }
        }

        private static int StepsForward(int from, int to, int n)
        {
            if (n <= 0) return 0;
            int d = (to - from) % n;
            if (d < 0) d += n;
            return d;
        }

        private static int Mod(int x, int m)
        {
            if (m <= 0) return 0;
            int r = x % m;
            return r < 0 ? r + m : r;
        }
    }
}
