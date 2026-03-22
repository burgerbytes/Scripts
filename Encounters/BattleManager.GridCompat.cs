using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public partial class BattleManager
{
    [Header("Grid / Battlefield")]
    [SerializeField] private BattleGridSystem battleGridSystem;

    private FieldObjectInstance _selectedFieldObjectTarget;

    private void EnsureBattleGridSystemResolved()
    {
        if (battleGridSystem != null)
            return;

        battleGridSystem = FindFirstObjectByType<BattleGridSystem>(FindObjectsInactive.Include);
        if (battleGridSystem != null)
        {
            Debug.Log($"[Battle][Grid] Resolve battleGridSystem => found existing '{battleGridSystem.name}'", this);
            return;
        }

        battleGridSystem = CreateRuntimeBattleGridSystem();
        Debug.Log($"[Battle][Grid] Resolve battleGridSystem => {(battleGridSystem != null ? "AUTO-CREATED" : "NULL")}", this);
    }

    private BattleGridSystem CreateRuntimeBattleGridSystem()
    {
        try
        {
            const int defaultWidth = 5;
            const int defaultHeight = 5;
            float cellSize = ComputeRuntimeGridCellSize();

            GameObject root = new GameObject("RuntimeBattleGridSystem");
            root.transform.SetParent(transform, false);

            GameObject originGo = new GameObject("GridOrigin");
            originGo.transform.SetParent(root.transform, false);
            originGo.transform.position = ComputeRuntimeGridOrigin(defaultWidth, defaultHeight, cellSize);

            BattleGridSystem grid = root.AddComponent<BattleGridSystem>();
            grid.ConfigureRuntimeGrid(originGo.transform, defaultWidth, defaultHeight, cellSize);
            grid.SetGridVisualActive(true);
            return grid;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Battle][Grid] Failed to auto-create BattleGridSystem: {ex}", this);
            return null;
        }
    }

    private float ComputeRuntimeGridCellSize()
    {
        List<Vector3> pts = GatherCombatantPositions();
        if (pts.Count < 2)
            return 1.25f;

        float minX = pts[0].x, maxX = pts[0].x;
        for (int i = 1; i < pts.Count; i++)
        {
            minX = Mathf.Min(minX, pts[i].x);
            maxX = Mathf.Max(maxX, pts[i].x);
        }

        float span = Mathf.Max(1f, maxX - minX);
        return Mathf.Clamp(span / 3f, 0.9f, 1.8f);
    }

    private Vector3 ComputeRuntimeGridOrigin(int width, int height, float cellSize)
    {
        List<Vector3> heroPts = GatherPartyPositions();
        List<Vector3> enemyPts = GatherMonsterPositions();

        float heroAvgX = AverageX(heroPts, -2f);
        float enemyAvgX = AverageX(enemyPts, heroAvgX + (3f * cellSize));
        float overallAvgY = AverageY(heroPts, 0f);
        if (enemyPts.Count > 0)
            overallAvgY = (overallAvgY + AverageY(enemyPts, overallAvgY)) * 0.5f;

        int centerRow = Mathf.Clamp((height - 1) / 2, 0, height - 1);
        float originX = heroAvgX - (1.5f * cellSize);
        float originY = overallAvgY + ((centerRow + 0.5f) * cellSize);

        if (enemyPts.Count > 0)
        {
            float desiredMonsterX = originX + ((width - 2) + 0.5f) * cellSize;
            float delta = enemyAvgX - desiredMonsterX;
            originX += delta * 0.5f;
        }

        return new Vector3(originX, originY, 0f);
    }

    private List<Vector3> GatherCombatantPositions()
    {
        List<Vector3> pts = GatherPartyPositions();
        pts.AddRange(GatherMonsterPositions());
        return pts;
    }

    private List<Vector3> GatherPartyPositions()
    {
        List<Vector3> pts = new();
        if (_party == null)
            return pts;

        for (int i = 0; i < _party.Count; i++)
        {
            var pm = _party[i];
            if (pm == null)
                continue;
            GameObject heroObject = pm.stats != null ? pm.stats.gameObject : pm.avatarGO;
            if (heroObject != null)
                pts.Add(heroObject.transform.position);
        }
        return pts;
    }

    private List<Vector3> GatherMonsterPositions()
    {
        List<Vector3> pts = new();
        if (_activeMonsters == null)
            return pts;

        for (int i = 0; i < _activeMonsters.Count; i++)
        {
            Monster monster = _activeMonsters[i];
            if (monster != null)
                pts.Add(monster.transform.position);
        }
        return pts;
    }

    private static float AverageX(List<Vector3> pts, float fallback)
    {
        if (pts == null || pts.Count == 0) return fallback;
        float sum = 0f;
        for (int i = 0; i < pts.Count; i++) sum += pts[i].x;
        return sum / pts.Count;
    }

    private static float AverageY(List<Vector3> pts, float fallback)
    {
        if (pts == null || pts.Count == 0) return fallback;
        float sum = 0f;
        for (int i = 0; i < pts.Count; i++) sum += pts[i].y;
        return sum / pts.Count;
    }

    private void InitializeBattleGridForParty()
    {
        EnsureBattleGridSystemResolved();
        if (battleGridSystem == null)
            return;

        int eligibleCount = 0;
        if (_party != null)
        {
            for (int i = 0; i < _party.Count; i++)
            {
                var pm = _party[i];
                if (pm == null) continue;
                GameObject heroObject = pm.stats != null ? pm.stats.gameObject : pm.avatarGO;
                if (heroObject != null) eligibleCount++;
            }
        }

        int aliveIndex = 0;
        for (int i = 0; i < PartyCount; i++)
        {
            if (_party == null || i >= _party.Count)
                break;

            var pm = _party[i];
            if (pm == null)
                continue;

            GameObject heroObject = pm.stats != null ? pm.stats.gameObject : pm.avatarGO;
            if (heroObject == null)
                continue;

            int row = GetCenteredRowForIndex(aliveIndex, Mathf.Max(1, eligibleCount));
            bool ok = battleGridSystem.RegisterHero(heroObject, row);
            Debug.Log($"[Battle][Grid] RegisterHero hero='{heroObject.name}' row={row} ok={ok}", heroObject);
            aliveIndex++;
        }

        battleGridSystem.SetGridVisualActive(true);
    }

    private void InitializeBattleGridForMonstersAndObjects()
    {
        EnsureBattleGridSystemResolved();
        Debug.Log($"[Battle][Grid] InitializeBattleGridForMonstersAndObjects | battleGridSystem={(battleGridSystem != null ? battleGridSystem.name : "NULL")} | monsters={(_activeMonsters != null ? _activeMonsters.Count : -1)} | party={(_party != null ? _party.Count : -1)}", this);
        if (battleGridSystem == null)
            return;

        battleGridSystem.ClearGrid();
        InitializeBattleGridForParty();

        if (_activeMonsters != null)
        {
            for (int i = 0; i < _activeMonsters.Count; i++)
            {
                Monster monster = _activeMonsters[i];
                if (monster == null)
                    continue;

                int row = GetCenteredRowForIndex(i, Mathf.Max(1, _activeMonsters.Count));
                bool ok = battleGridSystem.RegisterMonster(monster.gameObject, row);
                Debug.Log($"[Battle][Grid] RegisterMonster monster='{monster.name}' row={row} ok={ok}", monster);
            }
        }

        battleGridSystem.SpawnConfiguredFieldObjects();
        battleGridSystem.SetGridVisualActive(true);
    }

    private int GetCenteredRowForIndex(int index, int count)
    {
        int h = battleGridSystem != null ? Mathf.Max(1, battleGridSystem.Height) : 5;
        count = Mathf.Clamp(count, 1, h);
        int start = Mathf.Max(0, (h - count) / 2);
        return Mathf.Clamp(start + index, 0, h - 1);
    }

    private FieldObjectInstance TryGetClickedFieldObject()
    {
        if (_mainCam == null) _mainCam = Camera.main;
        if (_mainCam == null) return null;

        Physics.queriesHitTriggers = true;
        Vector3 world = _mainCam.ScreenToWorldPoint(Input.mousePosition);
        Vector2 p2 = new Vector2(world.x, world.y);

        Collider2D[] hits2D = Physics2D.OverlapPointAll(p2);
        if (hits2D != null && hits2D.Length > 0)
        {
            for (int i = 0; i < hits2D.Length; i++)
            {
                Collider2D c = hits2D[i];
                if (c == null) continue;
                FieldObjectInstance fo = c.GetComponentInParent<FieldObjectInstance>();
                if (fo != null) return fo;
            }
        }

        Ray ray = _mainCam.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits3D = Physics.RaycastAll(ray, 500f, ~0, QueryTriggerInteraction.Collide);
        if (hits3D != null && hits3D.Length > 0)
        {
            float best = float.MaxValue;
            FieldObjectInstance bestFieldObject = null;
            for (int i = 0; i < hits3D.Length; i++)
            {
                Collider c = hits3D[i].collider;
                if (c == null) continue;
                FieldObjectInstance fo = c.GetComponentInParent<FieldObjectInstance>();
                if (fo == null) continue;
                if (hits3D[i].distance < best)
                {
                    best = hits3D[i].distance;
                    bestFieldObject = fo;
                }
            }
            return bestFieldObject;
        }
        return null;
    }

    private void SelectFieldObjectTarget(FieldObjectInstance fieldObject)
    {
        if (fieldObject == null)
            return;

        _selectedFieldObjectTarget = fieldObject;
        _selectedEnemyTarget = null;

        if (!IsPendingAbilityTargetInRange())
        {
            if (logFlow) Debug.Log($"[Battle][Grid] Field object '{fieldObject.name}' is out of range for pending ability.", this);
            HideConfirmText();
            return;
        }

        ShowConfirmText();
    }

    private bool IsPendingAbilityTargetInRange()
    {
        if (_pendingActorIndex < 0 || _party == null || _pendingActorIndex >= _party.Count)
            return true;

        var pendingMember = _party[_pendingActorIndex];
        return IsPendingAbilityTargetInRange(
            pendingMember != null ? pendingMember.stats : null,
            _selectedEnemyTarget,
            _selectedFieldObjectTarget,
            _pendingAbility);
    }

    private bool IsPendingAbilityTargetInRange(HeroStats actorStats, Monster enemyTarget, FieldObjectInstance fieldTarget, AbilityDefinitionSO ability)
    {
        if (actorStats == null || ability == null || battleGridSystem == null)
            return true;

        GameObject actorObject = actorStats.gameObject;
        if (actorObject == null)
            return true;

        bool isMelee = ReadBoolMember(ability, "isMelee", false)
                       || string.Equals(ReadStringMember(ability, "rangeType", string.Empty), "Melee", StringComparison.OrdinalIgnoreCase);

        if (!isMelee)
            return true;

        if (enemyTarget != null)
            return battleGridSystem.IsMeleeRange(actorObject, enemyTarget.gameObject);

        if (fieldTarget != null)
            return battleGridSystem.IsMeleeRange(actorObject, fieldTarget.gameObject);

        return true;
    }

    private bool TryApplyAbilityKnockback(HeroStats attacker, Monster target, AbilityDefinitionSO ability)
    {
        if (battleGridSystem == null || attacker == null || target == null || ability == null)
            return false;

        if (!ReadBoolMember(ability, "hasKnockback", false) && !ReadBoolMember(ability, "knockback", false))
            return false;

        return battleGridSystem.TryKnockback(attacker.gameObject, target.gameObject);
    }

    private void ApplyGridSplashDamage(HeroStats attacker, Monster primaryTarget, AbilityDefinitionSO ability, int damage)
    {
        if (battleGridSystem == null || primaryTarget == null || ability == null)
            return;

        bool useSplash = ReadBoolMember(ability, "useGridAoe", false) || ReadBoolMember(ability, "isGridSplash", false);
        if (!useSplash)
            return;

        int radius = Mathf.Max(0, ReadIntMember(ability, "aoeRadiusTiles", 0));
        if (radius <= 0)
            return;

        List<Monster> splashTargets = battleGridSystem.GetMonstersInRadius(primaryTarget, radius);
        if (splashTargets == null || splashTargets.Count == 0)
            return;

        for (int i = 0; i < splashTargets.Count; i++)
        {
            Monster m = splashTargets[i];
            if (m == null || m == primaryTarget || m.IsDead)
                continue;

            int dealt = 0;
            try { dealt = m.TakeDamage(Mathf.Max(0, damage)); }
            catch { continue; }

            if (dealt > 0)
                SpawnDamageNumber(m.transform.position, dealt);

            if (m.IsDead)
                HandleMonsterKilled(m);
        }
    }

    private static bool ReadBoolMember(object obj, string memberName, bool fallback)
    {
        if (obj == null || string.IsNullOrWhiteSpace(memberName)) return fallback;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type t = obj.GetType();
        FieldInfo fi = t.GetField(memberName, flags);
        if (fi != null && fi.FieldType == typeof(bool)) return (bool)fi.GetValue(obj);
        PropertyInfo pi = t.GetProperty(memberName, flags);
        if (pi != null && pi.PropertyType == typeof(bool)) return (bool)pi.GetValue(obj, null);
        return fallback;
    }

    private static int ReadIntMember(object obj, string memberName, int fallback)
    {
        if (obj == null || string.IsNullOrWhiteSpace(memberName)) return fallback;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type t = obj.GetType();
        FieldInfo fi = t.GetField(memberName, flags);
        if (fi != null && fi.FieldType == typeof(int)) return (int)fi.GetValue(obj);
        PropertyInfo pi = t.GetProperty(memberName, flags);
        if (pi != null && pi.PropertyType == typeof(int)) return (int)pi.GetValue(obj, null);
        return fallback;
    }

    private static string ReadStringMember(object obj, string memberName, string fallback)
    {
        if (obj == null || string.IsNullOrWhiteSpace(memberName)) return fallback;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type t = obj.GetType();
        FieldInfo fi = t.GetField(memberName, flags);
        if (fi != null && fi.FieldType == typeof(string)) return (string)fi.GetValue(obj);
        PropertyInfo pi = t.GetProperty(memberName, flags);
        if (pi != null && pi.PropertyType == typeof(string)) return (string)pi.GetValue(obj, null);
        object enumVal = null;
        fi = t.GetField(memberName, flags);
        if (fi != null && fi.FieldType.IsEnum) enumVal = fi.GetValue(obj);
        if (enumVal == null)
        {
            pi = t.GetProperty(memberName, flags);
            if (pi != null && pi.PropertyType.IsEnum) enumVal = pi.GetValue(obj, null);
        }
        return enumVal != null ? enumVal.ToString() : fallback;
    }
}
