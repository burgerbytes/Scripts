// PATH: Assets/Scripts/Campfire/CampfireRewardPanel.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CampfireRewardPanel : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private StretchController stretch;
    [SerializeField] private HeroStats hero;

    [Header("UI")]
    [SerializeField] private GameObject root; // panel root
    [SerializeField] private Transform optionsContainer;
    [SerializeField] private RewardOptionCard optionCardPrefab;

    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text subtitleText;

    [Header("Options Pool")]
    [Tooltip("Pool of options to pick from at campfire.")]
    [SerializeField] private List<CampfireOptionSO> optionPool = new List<CampfireOptionSO>();

    [Tooltip("How many options to show each pick.")]
    [SerializeField] private int optionsPerPick = 3;

    [Header("Layout Fix")]
    [Tooltip("If true, this script will ensure OptionsContainer has a VerticalLayoutGroup + ContentSizeFitter at runtime.")]
    [SerializeField] private bool enforceRuntimeLayout = true;

    [Tooltip("Spacing between option cards.")]
    [SerializeField] private float optionSpacing = 10f;

    [Tooltip("Padding (left/right/top/bottom) inside the options container.")]
    [SerializeField] private int optionPadding = 12;

    [Tooltip("Minimum height for each option card (LayoutElement). Helps prevent overlap when prefabs are missing layout components).")]
    [SerializeField] private float optionCardMinHeight = 160f;

    [Header("Next Stretch Loop")]
    [Tooltip("If true, when the campfire completes (no pending upgrades), we will attempt to start the next stretch.")]
    [SerializeField] private bool startNextStretchOnExit = true;

    [Tooltip("Multiplier applied to a discovered 'current/target distance' if StretchController exposes one. If not, this is ignored.")]
    [SerializeField] private float nextStretchDistanceMultiplier = 1.25f;

    [Tooltip("Additive distance applied after multiplier if StretchController exposes a distance/target. If not, this is ignored.")]
    [SerializeField] private float nextStretchDistanceAdd = 25f;

    private readonly List<RewardOptionCard> _spawned = new List<RewardOptionCard>();

    private void Awake()
    {
        if (stretch == null)
            stretch = FindFirstObjectByType<StretchController>();

        if (hero == null)
            hero = FindFirstObjectByType<HeroStats>();
    }

    private void OnEnable()
    {
        if (stretch != null)
            stretch.OnStateChanged += OnStretchStateChanged;

        Hide();
    }

    private void OnDisable()
    {
        if (stretch != null)
            stretch.OnStateChanged -= OnStretchStateChanged;
    }

    private void OnStretchStateChanged(StretchController.StretchState state)
    {
        if (state == StretchController.StretchState.RestArea)
            OpenAtCampfire();
        else
            Hide();
    }

    private void OpenAtCampfire()
    {
        if (hero == null)
            return;

        hero.SetAllowLevelUps(true);

        Show();

        if (enforceRuntimeLayout)
            EnsureOptionsContainerLayout();

        RenderPick();
    }

    private void RenderPick()
    {
        ClearCards();

        if (titleText != null)
            titleText.text = "Campfire";

        if (subtitleText != null)
        {
            int pending = hero != null ? hero.PendingLevelUps : 0;
            subtitleText.text = pending > 0
                ? $"Choose an upgrade.\n<alpha=#AA>Pending upgrades: {pending}</alpha>"
                : "<alpha=#AA>Rest and prepare for the road ahead</alpha>";
        }

        if (optionPool == null || optionPool.Count == 0 || optionsContainer == null || optionCardPrefab == null)
        {
            ForceRebuildOptionsLayout();
            return;
        }

        // NEW: Filter pool by eligibility (class tier gating, etc.)
        List<CampfireOptionSO> eligible = new List<CampfireOptionSO>(optionPool.Count);
        for (int i = 0; i < optionPool.Count; i++)
        {
            CampfireOptionSO opt = optionPool[i];
            if (opt == null) continue;

            // If hero is missing, be permissive; otherwise enforce gating.
            if (hero == null || opt.IsEligible(hero))
                eligible.Add(opt);
        }

        if (eligible.Count == 0)
        {
            ForceRebuildOptionsLayout();
            return;
        }

        int count = Mathf.Clamp(optionsPerPick, 1, eligible.Count);
        HashSet<int> used = new HashSet<int>();

        for (int i = 0; i < count; i++)
        {
            int idx = GetUniqueRandomIndex(eligible.Count, used);
            CampfireOptionSO opt = eligible[idx];

            RewardOptionCard card = Instantiate(optionCardPrefab, optionsContainer);
            _spawned.Add(card);

            EnsureCardLayoutElement(card);

            card.Bind(opt, OnOptionChosen);
        }

        ForceRebuildOptionsLayout();
    }

    private void EnsureOptionsContainerLayout()
    {
        if (optionsContainer == null)
            return;

        GameObject go = optionsContainer.gameObject;

        // Vertical layout to stack cards cleanly.
        VerticalLayoutGroup vlg = go.GetComponent<VerticalLayoutGroup>();
        if (vlg == null) vlg = go.AddComponent<VerticalLayoutGroup>();

        vlg.spacing = optionSpacing;
        vlg.childAlignment = TextAnchor.UpperCenter;

        vlg.padding = new RectOffset(optionPadding, optionPadding, optionPadding, optionPadding);

        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        // Don't force expand height; we want cards to keep their own preferred/min heights.
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;

        // Fit content vertically so it works inside a ScrollRect (if present) and prevents overlap.
        ContentSizeFitter csf = go.GetComponent<ContentSizeFitter>();
        if (csf == null) csf = go.AddComponent<ContentSizeFitter>();

        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    private void EnsureCardLayoutElement(RewardOptionCard card)
    {
        if (card == null)
            return;

        LayoutElement le = card.GetComponent<LayoutElement>();
        if (le == null) le = card.gameObject.AddComponent<LayoutElement>();

        // A minimum height prevents cards from collapsing to 0 when layout is misconfigured.
        le.minHeight = Mathf.Max(20f, optionCardMinHeight);
        // Preferred height can help if your card uses ContentSizeFitter internally,
        // but we keep it unset by default to avoid fights with your prefab.
    }

    private void ForceRebuildOptionsLayout()
    {
        if (optionsContainer == null)
            return;

        RectTransform rt = optionsContainer as RectTransform;
        if (rt != null)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }
    }

    private int GetUniqueRandomIndex(int maxExclusive, HashSet<int> used)
    {
        int tries = 0;
        while (tries++ < 50)
        {
            int r = UnityEngine.Random.Range(0, maxExclusive);
            if (used.Add(r))
                return r;
        }

        for (int i = 0; i < maxExclusive; i++)
            if (used.Add(i))
                return i;

        return 0;
    }

    private void OnOptionChosen(CampfireOptionSO option)
    {
        if (hero == null || option == null)
            return;

        option.Apply(hero);

        bool spent = hero.SpendOnePendingLevelUp();

        if (spent && hero.PendingLevelUps > 0)
        {
            RenderPick();
            return;
        }

        hero.SetAllowLevelUps(false);
        Hide();

        if (startNextStretchOnExit)
            TryStartNextStretch();
    }

    /// <summary>
    /// Attempts to start the next stretch without assuming a specific StretchController API.
    /// This uses reflection so we don't break compilation if your StretchController differs.
    /// </summary>
    private void TryStartNextStretch()
    {
        if (stretch == null)
            return;

        // Try to compute a "reasonable" next distance if we can discover one.
        object nextDistanceArg = null;
        Type t = stretch.GetType();

        // Look for a float property/field that smells like a target distance.
        float? currentTarget = TryReadFloatMember(t, stretch,
            "TargetDistance", "targetDistance",
            "CurrentTargetDistance", "currentTargetDistance",
            "StretchTargetDistance", "stretchTargetDistance");

        if (currentTarget.HasValue)
        {
            float next = currentTarget.Value * nextStretchDistanceMultiplier + nextStretchDistanceAdd;
            nextDistanceArg = next;
        }

        // Method candidates in priority order.
        // - BeginNextStretch() / StartNextStretch() (no args)
        // - StartNewStretch(float) / StartNextStretch(float) / BeginNextStretch(float)
        // - StartNewStretch() (no args)
        string[] noArgNames = { "BeginNextStretch", "StartNextStretch", "StartNewStretch", "BeginStretch", "StartStretch" };
        string[] floatArgNames = { "StartNewStretch", "StartNextStretch", "BeginNextStretch", "BeginStretch", "StartStretch" };

        // Prefer float-arg if we have it.
        if (nextDistanceArg is float f)
        {
            if (TryInvokeFloatMethod(t, stretch, floatArgNames, f))
                return;
        }

        // Fall back to no-arg.
        TryInvokeNoArgMethod(t, stretch, noArgNames);
    }

    private static float? TryReadFloatMember(Type t, object instance, params string[] names)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (string n in names)
        {
            PropertyInfo p = t.GetProperty(n, flags);
            if (p != null && p.PropertyType == typeof(float) && p.CanRead)
            {
                try { return (float)p.GetValue(instance); } catch { }
            }

            FieldInfo f = t.GetField(n, flags);
            if (f != null && f.FieldType == typeof(float))
            {
                try { return (float)f.GetValue(instance); } catch { }
            }
        }

        return null;
    }

    private static bool TryInvokeFloatMethod(Type t, object instance, string[] methodNames, float arg)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (string name in methodNames)
        {
            MethodInfo m = t.GetMethod(name, flags, null, new[] { typeof(float) }, null);
            if (m == null) continue;

            try
            {
                m.Invoke(instance, new object[] { arg });
                return true;
            }
            catch { /* ignore */ }
        }

        return false;
    }

    private static bool TryInvokeNoArgMethod(Type t, object instance, string[] methodNames)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (string name in methodNames)
        {
            MethodInfo m = t.GetMethod(name, flags, null, Type.EmptyTypes, null);
            if (m == null) continue;

            try
            {
                m.Invoke(instance, null);
                return true;
            }
            catch { /* ignore */ }
        }

        return false;
    }

    private void ClearCards()
    {
        for (int i = 0; i < _spawned.Count; i++)
        {
            if (_spawned[i] != null)
                Destroy(_spawned[i].gameObject);
        }
        _spawned.Clear();
    }

    private void Show()
    {
        if (root != null)
            root.SetActive(true);
        else
            gameObject.SetActive(true);
    }

    private void Hide()
    {
        ClearCards();

        if (root != null)
            root.SetActive(false);
        else
            gameObject.SetActive(false);
    }
}
