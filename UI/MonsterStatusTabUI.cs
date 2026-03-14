using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MonsterStatusTabUI : MonoBehaviour
{
    [Header("Primary UI")]
    [SerializeField] private TMP_Text hpText;
    [SerializeField] private Transform statusListRoot;
    [SerializeField] private GameObject statusEntryButtonPrefab;
    [SerializeField] private TMP_Text emptyStateText;

    [Header("Popup")]
    [SerializeField] private GameObject popupRoot;
    [SerializeField] private TMP_Text popupTitleText;
    [SerializeField] private TMP_Text popupBodyText;
    [SerializeField] private Button popupCloseButton;

    [Header("Refresh")]
    [SerializeField] private bool refreshEveryFrameWhileBound = false;

    private readonly List<GameObject> _spawnedEntries = new List<GameObject>(8);
    private Monster _boundMonster;

    private struct StatusEntryData
    {
        public string title;
        public string summary;
        public string description;
    }

    private void Awake()
    {
        if (popupCloseButton != null)
        {
            popupCloseButton.onClick.RemoveAllListeners();
            popupCloseButton.onClick.AddListener(HidePopup);
        }

        HidePopup();
        Refresh(null);
    }

    private void OnDisable()
    {
        BindMonster(null);
    }

    private void LateUpdate()
    {
        if (refreshEveryFrameWhileBound && _boundMonster != null)
            Refresh(_boundMonster);
    }

    public void ShowForMonster(Monster monster)
    {
        BindMonster(monster);
        HidePopup();
        Refresh(monster);
    }

    private void BindMonster(Monster monster)
    {
        if (_boundMonster == monster)
            return;

        if (_boundMonster != null)
        {
            _boundMonster.OnHpChanged -= HandleHpChanged;
            _boundMonster.OnStatusChanged -= HandleStatusChanged;
        }

        _boundMonster = monster;

        if (_boundMonster != null)
        {
            _boundMonster.OnHpChanged += HandleHpChanged;
            _boundMonster.OnStatusChanged += HandleStatusChanged;
        }
    }

    private void HandleHpChanged(int currentHp, int maxHp)
    {
        Refresh(_boundMonster);
    }

    private void HandleStatusChanged()
    {
        Refresh(_boundMonster);
    }

    private void Refresh(Monster monster)
    {
        if (hpText != null)
        {
            hpText.text = monster != null
                ? $"HP: {monster.CurrentHp}/{monster.MaxHp}"
                : "HP: --";
        }

        ClearSpawnedEntries();

        if (monster == null)
        {
            SetEmptyState(false, string.Empty);
            HidePopup();
            return;
        }

        List<StatusEntryData> entries = BuildEntries(monster);
        bool hasEntries = entries.Count > 0;
        SetEmptyState(!hasEntries, hasEntries ? string.Empty : "No active status effects.");

        for (int i = 0; i < entries.Count; i++)
            CreateEntry(entries[i]);
    }

    private List<StatusEntryData> BuildEntries(Monster monster)
    {
        var entries = new List<StatusEntryData>(5);

        int bleedStacks = 0;
        try { bleedStacks = monster.BleedStacks; } catch { bleedStacks = 0; }
        if (bleedStacks > 0)
        {
            entries.Add(new StatusEntryData
            {
                title = "Bleeding",
                summary = $"{bleedStacks} stack{(bleedStacks == 1 ? string.Empty : "s")}",
                description = "Bleeding deals damage to this enemy at the end of the player's turn equal to its current bleed stacks, then the bleed stacks go down by 1."
            });
        }

        bool hasFocusRune = false;
        try { hasFocusRune = monster.HasFocusRune; } catch { hasFocusRune = false; }
        if (hasFocusRune)
        {
            entries.Add(new StatusEntryData
            {
                title = "Focus Rune",
                summary = "Focused",
                description = "Focused monsters are valid targets for your current sigil procs. When a magic-based sigil effect triggers, it can apply its effect to enemies with Focus Rune."
            });
        }

        int ignitionStacks = 0;
        int maxIgnitionStacks = 0;
        try { ignitionStacks = monster.IgnitionStacks; } catch { ignitionStacks = 0; }
        try { maxIgnitionStacks = monster.maxIgnitionStacks; } catch { maxIgnitionStacks = 0; }
        if (ignitionStacks > 0)
        {
            string summary = maxIgnitionStacks > 0
                ? $"{ignitionStacks}/{maxIgnitionStacks} stacks"
                : $"{ignitionStacks} stack{(ignitionStacks == 1 ? string.Empty : "s")}";

            string description = maxIgnitionStacks > 0
                ? $"Ignition is building toward an explosion threshold of {maxIgnitionStacks}. If more ignition is added and the cap is reached, the enemy explodes, takes damage, and ignition is cleared."
                : "Ignition is building toward an explosion. If enough ignition is added to reach its cap, the enemy explodes, takes damage, and ignition is cleared.";

            entries.Add(new StatusEntryData
            {
                title = "Ignition",
                summary = summary,
                description = description
            });
        }

        int stasisStacks = 0;
        int maxStasisStacks = 0;
        try { stasisStacks = monster.StasisStacks; } catch { stasisStacks = 0; }
        try { maxStasisStacks = monster.maxStasisStacks; } catch { maxStasisStacks = 0; }
        if (stasisStacks > 0)
        {
            string summary = maxStasisStacks > 0
                ? $"{stasisStacks}/{maxStasisStacks} stacks"
                : $"{stasisStacks} stack{(stasisStacks == 1 ? string.Empty : "s")}";

            string description = maxStasisStacks > 0
                ? $"Stasis is building toward a threshold of {maxStasisStacks}. When enough stasis is added to hit the cap, stasis is cleared."
                : "Stasis is building toward its cap. When enough stasis is added to hit the cap, stasis is cleared.";

            entries.Add(new StatusEntryData
            {
                title = "Stasis",
                summary = summary,
                description = description
            });
        }

        int sabotageStacks = 0;
        bool hasSabotage = false;
        int sabotagedAttackIndex = -1;
        try { sabotageStacks = monster.SabotageStacks; } catch { sabotageStacks = 0; }
        try { hasSabotage = monster.HasSabotage; } catch { hasSabotage = sabotageStacks > 0; }
        try { sabotagedAttackIndex = monster.SabotagedAttackIndex; } catch { sabotagedAttackIndex = -1; }
        if (hasSabotage && sabotageStacks > 0)
        {
            string summary = sabotagedAttackIndex >= 0
                ? $"{sabotageStacks} self-damage on attack {sabotagedAttackIndex + 1}"
                : $"{sabotageStacks} stack{(sabotageStacks == 1 ? string.Empty : "s")}";

            string description = sabotagedAttackIndex >= 0
                ? $"One of this enemy's attacks has been sabotaged. If it uses that sabotaged attack, it takes {sabotageStacks} self-damage. Current sabotaged attack slot: {sabotagedAttackIndex + 1}."
                : $"One of this enemy's attacks has been sabotaged. If it uses that sabotaged attack, it takes self-damage equal to its sabotage stacks. Current stacks: {sabotageStacks}.";

            entries.Add(new StatusEntryData
            {
                title = "Sabotage",
                summary = summary,
                description = description
            });
        }

        return entries;
    }

    private void CreateEntry(StatusEntryData data)
    {
        GameObject go = statusEntryButtonPrefab != null
            ? Instantiate(statusEntryButtonPrefab, statusListRoot)
            : CreateFallbackEntry(data.title, data.summary);

        if (go == null)
            return;

        _spawnedEntries.Add(go);

        TMP_Text label = go.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
            label.text = string.IsNullOrEmpty(data.summary) ? data.title : $"{data.title} - {data.summary}";

        Button button = go.GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => ShowPopup(data.title, data.description));
        }
    }

    private GameObject CreateFallbackEntry(string title, string summary)
    {
        if (statusListRoot == null)
            return null;

        GameObject go = new GameObject(title + "Entry", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(statusListRoot, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0f, 32f);

        Image bg = go.GetComponent<Image>();
        bg.raycastTarget = true;
        bg.color = new Color(1f, 1f, 1f, 0.08f);

        GameObject textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);

        RectTransform textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(10f, 4f);
        textRt.offsetMax = new Vector2(-10f, -4f);

        var text = textGo.AddComponent<TextMeshProUGUI>();
        text.text = string.IsNullOrEmpty(summary) ? title : $"{title} - {summary}";
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;

        return go;
    }

    private void ClearSpawnedEntries()
    {
        for (int i = 0; i < _spawnedEntries.Count; i++)
        {
            if (_spawnedEntries[i] != null)
                Destroy(_spawnedEntries[i]);
        }

        _spawnedEntries.Clear();
    }

    private void SetEmptyState(bool visible, string message)
    {
        if (emptyStateText != null)
        {
            emptyStateText.gameObject.SetActive(visible);
            emptyStateText.text = message;
        }
    }

    private void ShowPopup(string title, string body)
    {
        if (popupTitleText != null)
            popupTitleText.text = title;

        if (popupBodyText != null)
            popupBodyText.text = body;

        if (popupRoot != null)
            popupRoot.SetActive(true);
    }

    public void HidePopup()
    {
        if (popupRoot != null)
            popupRoot.SetActive(false);
    }
}

