// PATH: Assets/Scripts/UI/ReelcraftAbilityButtonForwarder.cs
// GUID: 0e46fb73f7566334798e1378396d5631
////////////////////////////////////////////////////////////
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Put this on PartyHUD/PickAllyX/ReelcraftAbilityButton.
/// - Sets the button Image sprite to the hero's Base/Advanced class reelcraftIcon.
/// - Opens ReelcraftPanelUI (ShowForHero(partyIndex)) when clicked.
/// 
/// This is written to be robust even if PartyHUDSlot is NOT present on PickAllyX.
/// It will fallback to parsing the hierarchy for "PickAlly1/2/3..." names.
/// </summary>
public class ReelcraftAbilityButtonForwarder : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    [Header("Icon")]
    [Tooltip("Optional: if your Image is on a child, assign it here. If left null, we use GetComponent<Image>().")]
    [SerializeField] private Image iconImageOverride;

    [Tooltip("Optional fallback icon if hero/class icon can't be resolved yet.")]
    [SerializeField] private Sprite fallbackIcon;

    [Tooltip("If true, we disable the Image component when no icon is available.")]
    [SerializeField] private bool hideIconWhenNull = true;

    [Header("Timing")]
    [Tooltip(
        "How often (seconds, unscaled) we retry resolving BattleManager/hero to sync the icon.\n" +
        "This runs while the object is enabled (no hard timeout) so the icon still appears even if\n" +
        "BattleManager/party spawns late, the HUD is enabled before party setup, or objects are toggled on/off.")]
    [SerializeField] private float retryIntervalSeconds = 0.25f;

    private Button _thisButton;
    private Image _thisImage;
    private PartyHUDSlot _slot;

    private int _partyIndex = -1;
    private Coroutine _initRoutine;

    private BattleManager _cachedBM;
    private bool _syncedWithRealHero;

    private void Awake()
    {
        _thisButton = GetComponent<Button>();
        _thisImage = iconImageOverride != null ? iconImageOverride : GetComponent<Image>();

        _slot = GetComponentInParent<PartyHUDSlot>(true);

        // IMPORTANT: PartyHUDSlot may not exist on PickAllyX in some prefabs.
        // So resolve party index in a more robust way.
        _partyIndex = ResolvePartyIndexRobust(_slot, transform);

        if (_thisButton != null)
        {
            _thisButton.onClick.RemoveListener(HandleClicked);
            _thisButton.onClick.AddListener(HandleClicked);
        }
        else
        {
            Debug.LogWarning($"[ReelcraftAbilityButtonForwarder] Missing Button on '{name}'.", this);
        }

        if (debugLogs)
        {
            Debug.Log(
                $"[ReelcraftAbilityButtonForwarder] Awake on '{name}' thisButton={(_thisButton != null)} thisImage={(_thisImage != null)} " +
                $"slot={(_slot != null ? _slot.name : "<null>")} partyIndex={_partyIndex}",
                this);
        }
    }

    private void OnEnable()
    {
        if (_initRoutine != null) StopCoroutine(_initRoutine);
        _initRoutine = StartCoroutine(InitAndSyncIcon());
    }

    private void OnDisable()
    {
        if (_initRoutine != null)
        {
            StopCoroutine(_initRoutine);
            _initRoutine = null;
        }
    }

    private IEnumerator InitAndSyncIcon()
    {
        // Re-resolve in case prefab/hierarchy changed or PartyHUDSlot wasn't present at Awake time.
        if (_partyIndex < 0)
            _partyIndex = ResolvePartyIndexRobust(_slot, transform);

        // If we have *no* party index yet, keep trying; UI hierarchies sometimes stabilize a frame later.
        // (Example: HUD instantiated, then PickAlly slots are renamed/duplicated.)
        while (isActiveAndEnabled)
        {
            if (_partyIndex < 0)
                _partyIndex = ResolvePartyIndexRobust(_slot, transform);

            if (_cachedBM == null)
                _cachedBM = FindFirstObjectByType<BattleManager>(FindObjectsInactive.Include);

            HeroStats hero = null;
            if (_cachedBM != null && _partyIndex >= 0)
                hero = SafeGetHeroAtPartyIndex(_cachedBM, _partyIndex);

            if (debugLogs)
                Debug.Log($"[ReelcraftAbilityButtonForwarder] Sync tick. bm={(_cachedBM != null)} hero={(hero != null)} partyIndex={_partyIndex} synced={_syncedWithRealHero}", this);

            // If we have a real hero, always prefer syncing from it (e.g., evolution changed AdvancedClassDef).
            if (hero != null)
            {
                SyncIconFromHero(hero);
                _syncedWithRealHero = true;
            }
            else
            {
                // Only apply fallback once; don't permanently hide if we simply haven't spawned the party yet.
                if (!_syncedWithRealHero)
                    SyncIconFromHero(null);
            }

            // If the icon is now valid (either hero or fallback), we can throttle retries.
            // We still keep looping so late party setup/evolution updates are picked up reliably.
            float wait = Mathf.Max(0.01f, retryIntervalSeconds);
            yield return new WaitForSecondsRealtime(wait);
        }
    }

    private void HandleClicked()
    {
        // Re-resolve on click in case this was instantiated before indices were correct.
        if (_partyIndex < 0)
            _partyIndex = ResolvePartyIndexRobust(_slot, transform);

        if (debugLogs)
            Debug.Log($"[ReelcraftAbilityButtonForwarder] Click '{name}' partyIndex={_partyIndex}", this);

        if (_partyIndex < 0)
        {
            Debug.LogWarning($"[ReelcraftAbilityButtonForwarder] Click ignored (unknown party index) on '{name}'.", this);
            return;
        }

        // Open Reelcraft panel directly.
        bool opened = TryOpenReelcraftPanelDirect(_partyIndex);

        if (!opened)
        {
            Debug.LogWarning($"[ReelcraftAbilityButtonForwarder] Failed to open Reelcraft panel for partyIndex={_partyIndex}.", this);

            // Optional fallback: invoke the PartyHUDSlot click if present (your old behavior).
            var slotButton = _slot != null ? _slot.GetComponent<Button>() : null;
            if (slotButton != null)
            {
                if (debugLogs)
                    Debug.Log($"[ReelcraftAbilityButtonForwarder] Fallback: invoking PartyHUDSlot button '{_slot.name}'", this);
                slotButton.onClick.Invoke();
            }
        }
    }

    private void SyncIconFromHero(HeroStats hero)
    {
        if (_thisImage == null)
            return;

        Sprite icon = null;

        if (hero != null)
        {
            // Use Advanced if present, else Base.
            var classDef = hero.AdvancedClassDef != null ? hero.AdvancedClassDef : hero.BaseClassDef;
            if (classDef != null)
                icon = classDef.reelcraftIcon;
        }

        if (icon == null)
            icon = fallbackIcon;

        _thisImage.sprite = icon;

        // Important: do NOT permanently hide the Image just because the hero isn't resolved yet.
        // We may be racing party spawning / scene enabling. We'll keep retrying while enabled.
        if (hideIconWhenNull)
            _thisImage.enabled = (icon != null);
        else
            _thisImage.enabled = true;

        if (debugLogs)
        {
            Debug.Log(
                $"[ReelcraftAbilityButtonForwarder] SyncIconFromHero partyIndex={_partyIndex} hero={(hero != null ? hero.name : "<null>")} " +
                $"icon={(icon != null ? icon.name : "<null>")} imgEnabled={_thisImage.enabled}",
                this);
        }
    }

    private static int ResolvePartyIndexRobust(PartyHUDSlot slot, Transform t)
    {
        // 1) Best case: PartyHUDSlot exposes PartyIndex/partyIndex.
        if (slot != null)
        {
            var val = GetMemberValue(slot, "PartyIndex") ?? GetMemberValue(slot, "partyIndex");
            if (val is int i)
                return i;
        }

        // 2) Walk up the hierarchy and parse from a parent named PickAlly1/2/3...
        // (Your screenshot shows ReelcraftAbilityButton is under PickAllyX.)
        Transform cur = t;
        while (cur != null)
        {
            int parsed = ParseTrailingInt(cur.name);
            if (parsed >= 0)
                return parsed;

            cur = cur.parent;
        }

        // 3) Last resort: if we can find ANY PartyHUDSlot in parents, use that.
        var slotInParents = t.GetComponentInParent<PartyHUDSlot>(true);
        if (slotInParents != null)
        {
            try { return slotInParents.PartyIndex; } catch { }
        }

        return -1;
    }

    private static int ParseTrailingInt(string s)
    {
        if (string.IsNullOrEmpty(s)) return -1;

        // Match something like "PickAlly1" or "PartySlot3" or "Hero2"
        var m = System.Text.RegularExpressions.Regex.Match(s, @"(\d+)$");
        if (!m.Success) return -1;

        if (int.TryParse(m.Groups[1].Value, out int oneBased))
        {
            // In your UI naming, PickAlly1 -> partyIndex 0.
            return Mathf.Max(0, oneBased - 1);
        }
        return -1;
    }

    private static HeroStats SafeGetHeroAtPartyIndex(BattleManager bm, int index)
    {
        if (bm == null) return null;

        try
        {
            return bm.GetHeroAtPartyIndex(index);
        }
        catch
        {
            var mi = bm.GetType().GetMethod("GetHeroAtPartyIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mi != null)
            {
                try { return mi.Invoke(bm, new object[] { index }) as HeroStats; }
                catch { }
            }
        }

        return null;
    }

    private bool TryOpenReelcraftPanelDirect(int partyIndex)
    {
        // Prefer specifically ReelcraftPanelUI if it exists.
        var panel = FindFirstObjectByType<ReelcraftPanelUI>(FindObjectsInactive.Include);
        if (panel != null)
        {
            panel.ShowForHero(partyIndex);
            if (debugLogs)
                Debug.Log($"[ReelcraftAbilityButtonForwarder] Opened Reelcraft via ReelcraftPanelUI.ShowForHero({partyIndex})", this);
            return true;
        }

        // Otherwise: reflection-safe scan for any component with ShowForHero(int)
        MonoBehaviour found = null;
        var all = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        for (int i = 0; i < all.Length; i++)
        {
            var mb = all[i];
            if (mb == null) continue;

            var t = mb.GetType();
            var mi = t.GetMethod("ShowForHero", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mi == null) continue;

            var ps = mi.GetParameters();
            if (ps == null || ps.Length != 1 || ps[0].ParameterType != typeof(int))
                continue;

            if (found == null || t.Name.IndexOf("Reelcraft", StringComparison.OrdinalIgnoreCase) >= 0)
                found = mb;
        }

        if (found == null)
        {
            if (debugLogs)
                Debug.LogWarning("[ReelcraftAbilityButtonForwarder] Could not find a component with ShowForHero(int).", this);
            return false;
        }

        try
        {
            var t = found.GetType();
            var show = t.GetMethod("ShowForHero", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            show.Invoke(found, new object[] { partyIndex });

            if (debugLogs)
                Debug.Log($"[ReelcraftAbilityButtonForwarder] Opened Reelcraft via {t.Name}.ShowForHero({partyIndex})", this);

            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ReelcraftAbilityButtonForwarder] Failed opening Reelcraft panel directly: {e.Message}", this);
            return false;
        }
    }

    private static object GetMemberValue(object obj, string name)
    {
        if (obj == null || string.IsNullOrEmpty(name)) return null;

        var t = obj.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        var p = t.GetProperty(name, flags);
        if (p != null && p.CanRead)
        {
            try { return p.GetValue(obj); } catch { }
        }

        var f = t.GetField(name, flags);
        if (f != null)
        {
            try { return f.GetValue(obj); } catch { }
        }

        return null;
    }

    public void ForceResync()
    {
        if (!isActiveAndEnabled) return;

        _syncedWithRealHero = false;
        _cachedBM = null;

        if (_initRoutine != null)
            StopCoroutine(_initRoutine);

        _initRoutine = StartCoroutine(InitAndSyncIcon());
    }

}


////////////////////////////////////////////////////////////
