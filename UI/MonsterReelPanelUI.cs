using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// InfoPanel "Abilities" tab UI.
///
/// Preserved behavior:
/// - InfoPanelController opens the panel from a clicked Monster or Hero instance.
/// - Monsters show their ReelDefinition strip as clickable icons.
/// - Heroes reuse the same icon-button presenter, but source icons/descriptions from unlocked abilities.
/// </summary>
public class MonsterReelPanelUI : MonoBehaviour
{
    private const string TAG = "[MonsterReelPanelUI]";

    private enum SubjectMode
    {
        None,
        Monster,
        Hero
    }

    [Header("Reel Root (optional)")]
    [Tooltip("Root containing the InfoPanel 3D reel (Reel3DColumn). If null, we try to find a child named 'MonsterAbilityReel'.")]
    [SerializeField, FormerlySerializedAs("slotsParent"), FormerlySerializedAs("slotRoot"), FormerlySerializedAs("monsterAbilityReelRoot")]
    private Transform slotsRoot;

    [Tooltip("Optional: explicitly wire the InfoPanel Reel3DColumn. If null, we'll look under slotsRoot.")]
    [SerializeField] private Reel3DColumn infoPanelReel;

    [Header("Icon List (recommended)")]
    [Tooltip("If true, we lay out the strip / abilities as clickable icons.")]
    [SerializeField] private bool useIconList = true;

    [Tooltip("Parent transform that will hold the instantiated icon buttons (e.g., a HorizontalLayoutGroup).")]
    [SerializeField] private Transform iconListRoot;

    [Tooltip("Optional prefab for an icon button. Should have Button + Image on the root (or an Image on a child).")]
    [SerializeField] private Button iconButtonPrefab;

    [Tooltip("If no prefab is provided, we create a simple Button+Image with this size.")]
    [SerializeField] private Vector2 fallbackIconSize = new Vector2(48f, 48f);

    [Tooltip("When using the icon list, hide the 3D reel root for clarity.")]
    [SerializeField] private bool hide3DReelWhenUsingIconList = true;

    [Tooltip("Visual nudge on the selected icon.")]
    [SerializeField] private float selectedScale = 1.15f;

    [Header("Legacy Slot Renderers (Optional)")]
    [Tooltip("Optional: manually wire 3 UI Images for top/mid/bottom icon previews.")]
    [SerializeField] private List<Image> slotImages = new List<Image>();

    [Tooltip("Optional: if using SpriteRenderers for the 3 preview icons.")]
    [SerializeField] private List<SpriteRenderer> slotSpriteRenderers = new List<SpriteRenderer>();

    [Header("Text")]
    [SerializeField] private TMP_Text attackNameText;
    [SerializeField] private TMP_Text attackDescText;

    [Header("Display")]
    [Tooltip("Default selected slot when showing a monster / hero (0-based index).")]
    [SerializeField] private int defaultSelectedSlotIndex = 0;

    [Tooltip("Rotate the 2D preview icons visually (does not affect lookup).")]
    [SerializeField] private float iconRotateDegreesCCW = 0f;

    [SerializeField] private bool preserveAspect = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private SubjectMode _mode = SubjectMode.None;
    private Monster _currentMonster;
    private HeroStats _currentHero;
    private ReelStripSO _currentStrip;
    private int[] _currentSlotToAttack;
    private List<AbilityDefinitionSO> _currentHeroAbilities;

    private readonly List<Button> _spawnedIconButtons = new List<Button>();
    private int _selectedSlotIndex = -1;
    private RectTransform _runtimeIconListRect;

    private bool ShouldUseIconList()
    {
        return useIconList || iconListRoot != null;
    }

    private bool CanBuildIconList()
    {
        if (iconListRoot == null)
            EnsureIconListRootCached("CanBuildIconList");

        return iconListRoot != null;
    }

    private void Log(string message)
    {
        if (debugLogs)
            Debug.Log($"{TAG} {message}", this);
    }

    private void EnsureVisibleForCurrentMode(bool usingIconList)
    {
        if (usingIconList && iconListRoot != null)
        {
            Transform t = iconListRoot;
            while (t != null && t != transform.parent)
            {
                t.gameObject.SetActive(true);
                t = t.parent;
            }
            iconListRoot.gameObject.SetActive(true);
        }

        if (iconListRoot != null && !usingIconList)
            iconListRoot.gameObject.SetActive(false);

        if (slotsRoot != null)
            slotsRoot.gameObject.SetActive(!usingIconList || !hide3DReelWhenUsingIconList);

        if (attackNameText != null)
            attackNameText.gameObject.SetActive(true);

        if (attackDescText != null)
            attackDescText.gameObject.SetActive(true);

        if (debugLogs)
        {
            Debug.Log($"{TAG} EnsureVisibleForCurrentMode usingIconList={usingIconList} iconListRoot={(iconListRoot != null ? iconListRoot.name : "<null>")} iconListActive={(iconListRoot != null && iconListRoot.gameObject.activeSelf)} slotsRoot={(slotsRoot != null ? slotsRoot.name : "<null>")} slotsActive={(slotsRoot != null && slotsRoot.gameObject.activeSelf)} attackNameActive={(attackNameText != null && attackNameText.gameObject.activeSelf)} attackDescActive={(attackDescText != null && attackDescText.gameObject.activeSelf)}", this);
        }
    }

    private void RebuildIconListLayout()
    {
        Canvas.ForceUpdateCanvases();

        if (iconListRoot is RectTransform rect)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);

            if (debugLogs)
                Debug.Log($"{TAG} RebuildIconListLayout root='{rect.name}' activeSelf={rect.gameObject.activeSelf} activeInHierarchy={rect.gameObject.activeInHierarchy} childCount={rect.childCount} size={rect.rect.size}", rect);
        }
    }

    private void Awake()
    {
        Debug.Log($"{TAG} Awake on '{name}'", this);
        EnsureSlotsRootCached("Awake");
        EnsureInfoPanelReelCached("Awake");
        EnsureLegacySlotsCached("Awake");
        EnsureIconListRootCached("Awake");
        EnsureTextRefsCached("Awake");
        ClearUI();
    }

    private void OnEnable()
    {
        Debug.Log($"{TAG} OnEnable -> activeSelf={gameObject.activeSelf} activeInHierarchy={gameObject.activeInHierarchy}", this);
        EnsureSlotsRootCached("OnEnable");
        EnsureInfoPanelReelCached("OnEnable");
        EnsureLegacySlotsCached("OnEnable");
        EnsureIconListRootCached("OnEnable");
        EnsureTextRefsCached("OnEnable");

        if (_mode == SubjectMode.Monster && _currentMonster != null)
            ShowForMonster(_currentMonster);
        else if (_mode == SubjectMode.Hero && _currentHero != null)
            ShowForHero(_currentHero);

        ForceRebuildVisibleState();
    }

    private void OnDisable()
    {
        Debug.Log($"{TAG} OnDisable", this);
        ClearSpawnedIconButtons();
    }

    public static bool HasDisplayableReelStrip(Monster monster)
    {
        return monster != null &&
               monster.ReelDefinition != null &&
               monster.ReelDefinition.Strip != null;
    }

    public static bool HasDisplayableHeroAbilities(HeroStats hero)
    {
        return GetHeroAbilities(hero).Count > 0;
    }

    public void ShowForMonster(Monster monster)
    {
        Debug.Log($"{TAG} ShowForMonster START monster={(monster != null ? monster.name : "<null>")}", this);

        if (monster == null)
        {
            Debug.LogWarning($"{TAG} ShowForMonster called with null.", this);
            ClearUI();
            return;
        }

        _mode = SubjectMode.Monster;
        _currentMonster = monster;
        _currentHero = null;
        _currentHeroAbilities = null;

        EnsureSlotsRootCached("ShowForMonster");
        EnsureInfoPanelReelCached("ShowForMonster");
        EnsureLegacySlotsCached("ShowForMonster");
        EnsureIconListRootCached("ShowForMonster");
        EnsureTextRefsCached("ShowForMonster");

        Debug.Log($"{TAG} ShowForMonster refs => iconListRoot={(iconListRoot != null ? iconListRoot.name : "<null>")} active={(iconListRoot != null && iconListRoot.gameObject.activeInHierarchy)} slotsRoot={(slotsRoot != null ? slotsRoot.name : "<null>")} active={(slotsRoot != null && slotsRoot.gameObject.activeInHierarchy)} reel={(infoPanelReel != null ? infoPanelReel.name : "<null>")} attackHeader={(attackNameText != null ? attackNameText.name : "<null>")} attackDesc={(attackDescText != null ? attackDescText.name : "<null>")}", this);

        ReelStripSO strip = null;
        int[] slotToAttack = null;

        Debug.Log($"{TAG} ReelDefinition={(monster.ReelDefinition != null ? "FOUND" : "NULL")}", this);

        if (monster.ReelDefinition != null)
        {
            strip = monster.ReelDefinition.Strip;
            slotToAttack = monster.ReelDefinition.SlotToAttackIndex;
        }

        Debug.Log($"{TAG} Reel strip length={(strip != null && strip.symbols != null ? strip.symbols.Count : -1)}", this);

        if (strip == null)
        {
            Debug.LogWarning($"{TAG} Monster '{monster.name}' has no ReelDefinition strip assigned. Skipping monster abilities / reel panel refresh.", monster);
            ClearUI();
            return;
        }

        _currentStrip = strip;
        _currentSlotToAttack = slotToAttack;

        bool wantIconList = ShouldUseIconList();
        bool canBuildIconList = CanBuildIconList();
        bool usingIconList = wantIconList && canBuildIconList;

        Log($"Init with monster='{monster.name}' strip='{strip.name}' symbols={strip.symbols?.Count ?? 0} requestedMode={(wantIconList ? "IconList" : "3DReel")} actualMode={(usingIconList ? "IconList" : "3DReel")} iconListRoot={(iconListRoot != null ? iconListRoot.name : "<null>")} slotsRoot={(slotsRoot != null ? slotsRoot.name : "<null>")} reel={(infoPanelReel != null ? infoPanelReel.name : "<null>")}");

        if (wantIconList && !canBuildIconList)
            Debug.LogWarning($"{TAG} Icon list mode was requested, but iconListRoot is missing. Falling back to the 3D reel / legacy preview instead of showing a blank panel.", this);

        if (infoPanelReel != null)
            infoPanelReel.SetStrip(strip, rebuildNow: true);

        EnsureVisibleForCurrentMode(usingIconList);
        UpdateLegacyPreviewIcons(strip);

        if (usingIconList)
        {
            BuildMonsterIconList(strip);
            RebuildIconListLayout();
        }

        SelectMonsterSlot(Mathf.Clamp(defaultSelectedSlotIndex, 0, (strip.symbols?.Count ?? 1) - 1));
        ForceRebuildVisibleState();
    }

    public void ShowForHero(HeroStats hero)
    {
        if (hero == null)
        {
            if (debugLogs) Debug.LogWarning($"{TAG} ShowForHero called with null.", this);
            ClearUI();
            return;
        }

        _mode = SubjectMode.Hero;
        _currentHero = hero;
        _currentMonster = null;
        _currentStrip = null;
        _currentSlotToAttack = null;

        EnsureSlotsRootCached("ShowForHero");
        EnsureInfoPanelReelCached("ShowForHero");
        EnsureLegacySlotsCached("ShowForHero");
        EnsureIconListRootCached("ShowForHero");
        EnsureTextRefsCached("ShowForHero");

        _currentHeroAbilities = GetHeroAbilities(hero);

        if (_currentHeroAbilities.Count == 0)
        {
            ClearUI();
            if (attackNameText != null) attackNameText.text = "Abilities";
            if (attackDescText != null) attackDescText.text = "No unlocked active abilities found for this hero.";
            return;
        }

        bool wantIconList = ShouldUseIconList();
        bool canBuildIconList = CanBuildIconList();
        bool usingIconList = wantIconList && canBuildIconList;

        Log($"Init with hero='{hero.name}' abilities={_currentHeroAbilities.Count} requestedMode={(wantIconList ? "IconList" : "3DReel")} actualMode={(usingIconList ? "IconList" : "TextOnly")} iconListRoot={(iconListRoot != null ? iconListRoot.name : "<null>")}");

        if (wantIconList && !canBuildIconList)
            Debug.LogWarning($"{TAG} Icon list mode was requested for hero abilities, but iconListRoot is missing. Falling back to text-only selection state.", this);

        EnsureVisibleForCurrentMode(usingIconList);

        if (usingIconList)
        {
            BuildHeroIconList(_currentHeroAbilities);
            RebuildIconListLayout();
        }

        SelectHeroAbility(Mathf.Clamp(defaultSelectedSlotIndex, 0, _currentHeroAbilities.Count - 1));
        ForceRebuildVisibleState();
    }

    public void ClearCurrentSelection()
    {
        ClearUI();
    }

    private void BuildMonsterIconList(ReelStripSO strip)
    {
        Debug.Log($"{TAG} BuildMonsterIconList START", this);
        ClearSpawnedIconButtons();

        if (iconListRoot == null)
        {
            Debug.LogError($"{TAG} BuildMonsterIconList failed: iconListRoot is not assigned.", this);
            return;
        }

        Debug.Log($"{TAG} IconListRoot found: {iconListRoot.name} active={iconListRoot.gameObject.activeInHierarchy}", iconListRoot);

        if (strip == null || strip.symbols == null)
        {
            Debug.LogWarning($"{TAG} BuildMonsterIconList strip or symbols are null.", this);
            return;
        }

        Debug.Log($"{TAG} Creating buttons for {strip.symbols.Count} symbols", this);

        for (int i = 0; i < strip.symbols.Count; i++)
        {
            ReelSymbolSO sym = strip.symbols[i];
            Sprite sprite = sym != null ? sym.icon : null;
            Button btn = CreateOrSpawnButton(i, sprite);
            if (btn == null) continue;

            int capturedIndex = i;
            btn.onClick.AddListener(() => SelectMonsterSlot(capturedIndex));
            _spawnedIconButtons.Add(btn);

            Log($"Populate monster symbol slot={i} symbol={(sym != null ? sym.name : "<null>")} sprite={(sprite != null ? sprite.name : "<null>")} button={(btn != null ? btn.name : "<null>")}");
        }

        Log($"Bound {_spawnedIconButtons.Count} monster reel interaction handlers.");
    }

    private void BuildHeroIconList(List<AbilityDefinitionSO> abilities)
    {
        ClearSpawnedIconButtons();

        if (iconListRoot == null)
        {
            Debug.LogError($"{TAG} BuildHeroIconList failed: iconListRoot is not assigned.", this);
            return;
        }

        if (abilities == null)
            return;

        for (int i = 0; i < abilities.Count; i++)
        {
            AbilityDefinitionSO ability = abilities[i];
            Sprite sprite = ability != null ? ability.icon : null;
            Button btn = CreateOrSpawnButton(i, sprite);
            if (btn == null) continue;

            int capturedIndex = i;
            btn.onClick.AddListener(() => SelectHeroAbility(capturedIndex));
            _spawnedIconButtons.Add(btn);

            Log($"Populate hero ability slot={i} ability={(ability != null ? ability.abilityName : "<null>")} sprite={(sprite != null ? sprite.name : "<null>")} button={(btn != null ? btn.name : "<null>")}");
        }

        Log($"Bound {_spawnedIconButtons.Count} hero ability interaction handlers.");
    }

    private Button CreateOrSpawnButton(int index, Sprite sprite)
    {
        Debug.Log($"{TAG} Creating reel button index={index} sprite={(sprite != null ? sprite.name : "<null>")}", this);

        Button btn = null;
        Image img = null;

        if (iconButtonPrefab != null)
        {
            btn = Instantiate(iconButtonPrefab, iconListRoot);
            img = btn.GetComponent<Image>();
            if (img == null)
                img = btn.GetComponentInChildren<Image>(true);
        }
        else
        {
            GameObject go = new GameObject($"ReelIcon_{index}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(iconListRoot, false);

            RectTransform rt = (RectTransform)go.transform;
            rt.sizeDelta = fallbackIconSize;

            img = go.GetComponent<Image>();
            btn = go.GetComponent<Button>();
        }

        if (btn == null)
            return null;

        btn.name = $"ReelIconButton_{index}";
        btn.interactable = true;
        btn.transform.localScale = Vector3.one;

        Image rootImage = btn.GetComponent<Image>();
        if (rootImage != null)
            rootImage.raycastTarget = true;

        if (img != null)
        {
            img.sprite = sprite;
            img.enabled = (sprite != null);
            img.preserveAspect = preserveAspect;
            img.raycastTarget = true;

            RectTransform rt = img.rectTransform;
            if (rt != null)
                rt.localEulerAngles = new Vector3(0f, 0f, iconRotateDegreesCCW);
        }

        if (btn != null)
            Debug.Log($"{TAG} Button created: {btn.name} parent={(btn.transform.parent != null ? btn.transform.parent.name : "<null>")}", btn);

        return btn;
    }

    private void ClearSpawnedIconButtons()
    {
        for (int i = 0; i < _spawnedIconButtons.Count; i++)
        {
            Button b = _spawnedIconButtons[i];
            if (b == null) continue;

            b.onClick.RemoveAllListeners();

            if (Application.isPlaying)
                Destroy(b.gameObject);
            else
                DestroyImmediate(b.gameObject);
        }
        _spawnedIconButtons.Clear();
        _selectedSlotIndex = -1;
    }

    private void SelectMonsterSlot(int slotIndex)
    {
        if (_currentStrip == null || _currentStrip.symbols == null || _currentStrip.symbols.Count == 0)
        {
            ClearUI();
            return;
        }

        slotIndex = Mathf.Clamp(slotIndex, 0, _currentStrip.symbols.Count - 1);
        _selectedSlotIndex = slotIndex;

        UpdateSelectionVisuals();
        UpdateMonsterAttackText(_currentMonster, _currentStrip, _currentSlotToAttack, slotIndex);

        if (debugLogs)
            Debug.Log($"{TAG} Selected monster slot {slotIndex} sym='{(_currentStrip.symbols[slotIndex] != null ? _currentStrip.symbols[slotIndex].id : "NULL")}'", this);
    }

    private void SelectHeroAbility(int abilityIndex)
    {
        if (_currentHeroAbilities == null || _currentHeroAbilities.Count == 0)
        {
            ClearUI();
            return;
        }

        abilityIndex = Mathf.Clamp(abilityIndex, 0, _currentHeroAbilities.Count - 1);
        _selectedSlotIndex = abilityIndex;

        UpdateSelectionVisuals();
        UpdateHeroAbilityText(_currentHeroAbilities[abilityIndex]);

        if (debugLogs)
            Debug.Log($"{TAG} Selected hero ability {abilityIndex} ability='{(_currentHeroAbilities[abilityIndex] != null ? _currentHeroAbilities[abilityIndex].abilityName : "NULL")}'", this);
    }

    private void UpdateSelectionVisuals()
    {
        for (int i = 0; i < _spawnedIconButtons.Count; i++)
        {
            Button btn = _spawnedIconButtons[i];
            if (btn == null) continue;

            float s = (i == _selectedSlotIndex) ? selectedScale : 1f;
            btn.transform.localScale = new Vector3(s, s, 1f);
        }
    }

    private void UpdateLegacyPreviewIcons(ReelStripSO strip)
    {
        if (strip == null || strip.symbols == null) return;

        int count = strip.symbols.Count;

        for (int i = 0; i < 3; i++)
        {
            ReelSymbolSO sym = (i >= 0 && i < count) ? strip.symbols[i] : null;
            Sprite sprite = sym != null ? sym.icon : null;

            if (slotImages != null && i < slotImages.Count && slotImages[i] != null)
            {
                slotImages[i].sprite = sprite;
                slotImages[i].enabled = (sprite != null);
                slotImages[i].preserveAspect = preserveAspect;

                RectTransform rt = slotImages[i].rectTransform;
                if (rt != null)
                    rt.localEulerAngles = new Vector3(0f, 0f, iconRotateDegreesCCW);
            }

            if (slotSpriteRenderers != null && i < slotSpriteRenderers.Count && slotSpriteRenderers[i] != null)
            {
                slotSpriteRenderers[i].sprite = sprite;
                slotSpriteRenderers[i].enabled = (sprite != null);
                slotSpriteRenderers[i].transform.localEulerAngles = new Vector3(0f, 0f, iconRotateDegreesCCW);
            }
        }
    }

    private void UpdateMonsterAttackText(Monster monster, ReelStripSO strip, int[] slotToAttack, int slotIndex)
    {
        if (attackNameText == null && attackDescText == null) return;

        string name = "";
        string desc = "";

        if (strip != null && strip.symbols != null && strip.symbols.Count > slotIndex)
        {
            ReelSymbolSO sym = strip.symbols[slotIndex];
            name = sym != null
                ? (!string.IsNullOrWhiteSpace(sym.id) ? sym.id : sym.name)
                : string.Empty;
            desc = name;

            if (debugLogs)
                Debug.Log($"{TAG} UpdateMonsterAttackText slot={slotIndex} name='{name}'", this);
        }

        if (attackNameText != null) attackNameText.text = name;
        if (attackDescText != null) attackDescText.text = desc;
    }

    private void UpdateHeroAbilityText(AbilityDefinitionSO ability)
    {
        if (attackNameText == null && attackDescText == null)
            return;

        string name = ability != null && !string.IsNullOrWhiteSpace(ability.abilityName)
            ? ability.abilityName
            : (ability != null ? ability.name : string.Empty);

        string desc = ability != null ? (ability.description ?? string.Empty) : string.Empty;
        string cost = ability != null ? BuildHeroAbilityCostLine(ability) : string.Empty;
        if (!string.IsNullOrWhiteSpace(cost))
            desc = string.IsNullOrWhiteSpace(desc) ? cost : desc + "\n\n" + cost;

        if (attackNameText != null) attackNameText.text = name;
        if (attackDescText != null) attackDescText.text = desc;
    }

    private static string BuildAttackDescription(Monster.MonsterAttack atk)
    {
        if (atk == null) return "";

        List<string> bits = new List<string>(8);

        if (atk.isConsume)
        {
            string only = atk.consumeOnlySummoned ? "summoned " : "";
            bits.Add($"Consumes a {only}ally.");
            bits.Add("Heals self for the ally's Max HP.");
        }
        else if (atk.isSummon)
        {
            string who = (atk.summonPrefab != null) ? atk.summonPrefab.name : "ally";
            bits.Add($"Summons {atk.summonCount}× {who}.");
        }
        else
        {
            bits.Add($"Deals {atk.damage} damage{(atk.isAoe ? " to all allies" : "")}." );
        }

        if (atk.stunsTarget)
            bits.Add($"Stuns for {Mathf.Max(1, atk.stunPlayerPhases)} phase(s).");

        if (atk.appliesBleed)
            bits.Add($"Applies Bleed {Mathf.Max(1, atk.bleedStacks)}.");

        if (atk.appliesCorrosion)
            bits.Add($"Corrodes {Mathf.Max(1, atk.corrosionIconCount)} reel icon(s).");

        bits.Add($"Speed {atk.speed}.");

        return string.Join("\n", bits);
    }

    private static string BuildHeroAbilityCostLine(AbilityDefinitionSO ability)
    {
        if (ability == null)
            return string.Empty;

        List<string> costs = new List<string>(4);
        if (ability.cost.attack > 0) costs.Add($"ATK {ability.cost.attack}");
        if (ability.cost.defense > 0) costs.Add($"DEF {ability.cost.defense}");
        if (ability.cost.magic > 0) costs.Add($"MAG {ability.cost.magic}");
        if (ability.cost.wild > 0) costs.Add($"WLD {ability.cost.wild}");

        if (ability.spendAllAttackResources)
            costs.Add("Uses all ATK");

        return costs.Count > 0 ? "Cost: " + string.Join("  ", costs) : string.Empty;
    }


    public void RefreshCurrentSubject()
    {
        Debug.Log($"{TAG} RefreshCurrentSubject mode={_mode} monster={(_currentMonster != null ? _currentMonster.name : "<null>")} hero={(_currentHero != null ? _currentHero.name : "<null>")}", this);

        if (_mode == SubjectMode.Monster && _currentMonster != null)
            ShowForMonster(_currentMonster);
        else if (_mode == SubjectMode.Hero && _currentHero != null)
            ShowForHero(_currentHero);

        ForceRebuildVisibleState();
    }

    public void ForceRebuildVisibleState()
    {
        EnsureSlotsRootCached("ForceRebuildVisibleState");
        EnsureInfoPanelReelCached("ForceRebuildVisibleState");
        EnsureLegacySlotsCached("ForceRebuildVisibleState");
        EnsureIconListRootCached("ForceRebuildVisibleState");
        EnsureTextRefsCached("ForceRebuildVisibleState");

        bool usingIconList = iconListRoot != null && iconListRoot.gameObject.activeInHierarchy;
        EnsureVisibleForCurrentMode(usingIconList);
        Canvas.ForceUpdateCanvases();
        RebuildIconListLayout();

        if (_mode == SubjectMode.Monster && _currentStrip != null && _currentStrip.symbols != null && _currentStrip.symbols.Count > 0)
        {
            if (_spawnedIconButtons.Count == 0 && usingIconList)
                BuildMonsterIconList(_currentStrip);

            if (_selectedSlotIndex < 0)
                SelectMonsterSlot(Mathf.Clamp(defaultSelectedSlotIndex, 0, _currentStrip.symbols.Count - 1));
        }
        else if (_mode == SubjectMode.Hero && _currentHeroAbilities != null && _currentHeroAbilities.Count > 0)
        {
            if (_spawnedIconButtons.Count == 0 && usingIconList)
                BuildHeroIconList(_currentHeroAbilities);

            if (_selectedSlotIndex < 0)
                SelectHeroAbility(Mathf.Clamp(defaultSelectedSlotIndex, 0, _currentHeroAbilities.Count - 1));
        }

        Debug.Log($"{TAG} ForceRebuildVisibleState usingIconList={usingIconList} iconRoot={(iconListRoot != null ? iconListRoot.name : "<null>")} childCount={(iconListRoot != null ? iconListRoot.childCount : -1)} selected={_selectedSlotIndex}", this);
    }

    private void ClearUI()
    {
        Debug.Log($"{TAG} ClearUI called. mode={_mode} currentMonster={(_currentMonster != null ? _currentMonster.name : "<null>")} currentHero={(_currentHero != null ? _currentHero.name : "<null>")}", this);

        _mode = SubjectMode.None;
        _currentMonster = null;
        _currentHero = null;
        _currentStrip = null;
        _currentSlotToAttack = null;
        _currentHeroAbilities = null;
        _selectedSlotIndex = -1;

        ClearSpawnedIconButtons();

        if (attackNameText != null) attackNameText.text = "";
        if (attackDescText != null) attackDescText.text = "";

        if (slotImages != null)
        {
            foreach (Image img in slotImages)
            {
                if (img == null) continue;
                img.sprite = null;
                img.enabled = false;
            }
        }

        if (slotSpriteRenderers != null)
        {
            foreach (SpriteRenderer sr in slotSpriteRenderers)
            {
                if (sr == null) continue;
                sr.sprite = null;
                sr.enabled = false;
            }
        }

        if (slotsRoot != null)
            slotsRoot.gameObject.SetActive(false);

        if (iconListRoot != null)
            iconListRoot.gameObject.SetActive(false);
    }

    private void EnsureSlotsRootCached(string reason)
    {
        if (slotsRoot != null) return;

        Transform direct = FindChildByTrimmedName(transform, "MonsterAbilityReel");
        if (direct != null)
        {
            slotsRoot = direct;
            if (debugLogs)
                Debug.Log($"{TAG} EnsureSlotsRootCached({reason}): found '{slotsRoot.name}' activeSelf={slotsRoot.gameObject.activeSelf} activeInHierarchy={slotsRoot.gameObject.activeInHierarchy}", slotsRoot);
            return;
        }

        Debug.LogWarning($"{TAG} EnsureSlotsRootCached({reason}): could not find child 'MonsterAbilityReel'.", this);
    }

    private void EnsureInfoPanelReelCached(string reason)
    {
        if (infoPanelReel != null) return;

        if (slotsRoot != null)
        {
            infoPanelReel = slotsRoot.GetComponent<Reel3DColumn>();
            if (infoPanelReel == null)
                infoPanelReel = slotsRoot.GetComponentInChildren<Reel3DColumn>(true);
        }

        if (infoPanelReel != null)
        {
            if (debugLogs)
                Debug.Log($"{TAG} EnsureInfoPanelReelCached({reason}): using '{infoPanelReel.name}' activeSelf={infoPanelReel.gameObject.activeSelf} activeInHierarchy={infoPanelReel.gameObject.activeInHierarchy}", infoPanelReel);
        }
        else
        {
            Debug.LogWarning($"{TAG} EnsureInfoPanelReelCached({reason}): no Reel3DColumn found under slotsRoot.", this);
        }
    }

    private void EnsureLegacySlotsCached(string reason)
    {
        if (slotImages != null && slotImages.Count > 0) return;
        if (slotSpriteRenderers != null && slotSpriteRenderers.Count > 0) return;

        if (slotsRoot == null) return;

        Image[] imgs = slotsRoot.GetComponentsInChildren<Image>(true);
        if (imgs != null && imgs.Length > 0)
        {
            slotImages = new List<Image>(imgs);
            List<Image> filtered = slotImages.FindAll(i => i != null && i.name.IndexOf("slot", StringComparison.OrdinalIgnoreCase) >= 0);
            if (filtered.Count >= 3) slotImages = filtered;
            if (slotImages.Count > 3) slotImages = slotImages.GetRange(0, 3);

            if (preserveAspect)
                foreach (Image img in slotImages)
                    if (img != null) img.preserveAspect = true;

            if (debugLogs)
                Debug.Log($"{TAG} EnsureLegacySlotsCached({reason}) -> UI Images={slotImages.Count}", this);

            return;
        }

        SpriteRenderer[] srs = slotsRoot.GetComponentsInChildren<SpriteRenderer>(true);
        if (srs != null && srs.Length > 0)
        {
            slotSpriteRenderers = new List<SpriteRenderer>(srs);
            List<SpriteRenderer> filtered = slotSpriteRenderers.FindAll(s => s != null && s.name.IndexOf("slot", StringComparison.OrdinalIgnoreCase) >= 0);
            if (filtered.Count >= 3) slotSpriteRenderers = filtered;
            if (slotSpriteRenderers.Count > 3) slotSpriteRenderers = slotSpriteRenderers.GetRange(0, 3);

            if (debugLogs)
                Debug.Log($"{TAG} EnsureLegacySlotsCached({reason}) -> SpriteRenderers={slotSpriteRenderers.Count}", this);
        }
    }

    private void EnsureIconListRootCached(string reason)
    {
        if (iconListRoot != null)
        {
            if (debugLogs)
                Debug.Log($"{TAG} EnsureIconListRootCached({reason}): using assigned '{iconListRoot.name}' activeSelf={iconListRoot.gameObject.activeSelf} activeInHierarchy={iconListRoot.gameObject.activeInHierarchy}", iconListRoot);
            return;
        }

        Transform direct = FindChildByTrimmedName(transform, "IconListRoot");
        if (direct != null)
        {
            iconListRoot = direct;
            if (debugLogs)
                Debug.Log($"{TAG} EnsureIconListRootCached({reason}): found '{iconListRoot.name}' activeSelf={iconListRoot.gameObject.activeSelf} activeInHierarchy={iconListRoot.gameObject.activeInHierarchy}", iconListRoot);
            return;
        }

        Transform parent = transform;
        if (parent != null)
        {
            GameObject go = new GameObject("IconListRoot", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(600f, fallbackIconSize.y + 16f);

            HorizontalLayoutGroup h = go.GetComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = false;
            h.childControlHeight = false;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            h.spacing = 8f;

            ContentSizeFitter fitter = go.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            iconListRoot = go.transform;
            _runtimeIconListRect = rt;

            Debug.LogWarning($"{TAG} EnsureIconListRootCached({reason}): could not find child 'IconListRoot'. Created runtime fallback root.", iconListRoot);
            return;
        }

        Debug.LogWarning($"{TAG} EnsureIconListRootCached({reason}): could not find child 'IconListRoot'.", this);
    }

    private void EnsureTextRefsCached(string reason)
    {
        if (attackNameText == null)
            attackNameText = FindTextByName("AttackHeaderText");
        if (attackDescText == null)
            attackDescText = FindTextByName("AttackDescriptionText");

        if (debugLogs && (attackNameText == null || attackDescText == null))
            Debug.LogWarning($"{TAG} EnsureTextRefsCached({reason}): one or more text refs are missing. header={(attackNameText != null)} desc={(attackDescText != null)}", this);
    }

    private TMP_Text FindTextByName(string childName)
    {
        foreach (TMP_Text txt in GetComponentsInChildren<TMP_Text>(true))
        {
            if (txt != null && string.Equals(txt.name.Trim(), childName.Trim(), StringComparison.Ordinal))
                return txt;
        }
        return null;
    }

    private static Transform FindChildByTrimmedName(Transform root, string childName)
    {
        if (root == null || string.IsNullOrWhiteSpace(childName))
            return null;

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t != null && string.Equals(t.name.Trim(), childName.Trim(), StringComparison.Ordinal))
                return t;
        }

        return null;
    }

    private static List<AbilityDefinitionSO> GetHeroAbilities(HeroStats hero)
    {
        List<AbilityDefinitionSO> results = new List<AbilityDefinitionSO>();
        if (hero == null)
            return results;

        ClassDefinitionSO classDef = hero.GetActiveClassDefinition();
        if (classDef == null)
            return results;

        List<AbilityDefinitionSO> unlocked = hero.GetUnlockedAbilitiesFromClassDef(classDef);
        if (unlocked == null)
            return results;

        for (int i = 0; i < unlocked.Count; i++)
        {
            AbilityDefinitionSO ability = unlocked[i];
            if (ability == null) continue;
            if (ability.kind != AbilityKind.Active) continue;
            if (!GameDebugSettings.IsAbilityAllowed(ability)) continue;
            results.Add(ability);
        }

        return results;
    }
}




////////////////////////////////////////////////////////////


////////////////////////////////////////////////////////////