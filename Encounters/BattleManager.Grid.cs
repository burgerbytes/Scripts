using System.Collections.Generic;
using UnityEngine;

public class BattleGridSystem : MonoBehaviour
{
    [System.Serializable]
    public class StartingFieldObjectEntry
    {
        public FieldObjectDefinitionSO definition;
        public Vector2Int gridPosition = new Vector2Int(2, 2);
    }

    [Header("Grid")]
    [SerializeField] private Transform gridOrigin;
    [SerializeField] private int width = 5;
    [SerializeField] private int height = 5;
    [SerializeField] private float cellSize = 1f;

    [Header("Starting Field Objects")]
    [SerializeField] private List<StartingFieldObjectEntry> startingFieldObjects = new();
    [SerializeField] private Transform fieldObjectRoot;
    [SerializeField] private bool logFieldObjectSpawns = true;

    [Header("Scene Debug Visuals")]
    [SerializeField] private bool drawGridGizmos = true;
    [SerializeField] private bool drawCellCenters = true;

    [Header("Runtime Grid Visuals")]
    [SerializeField] private bool showRuntimeGrid = true;
    [SerializeField] private Material runtimeGridMaterial;
    [SerializeField] private float runtimeLineWidth = 0.03f;
    [SerializeField] private float runtimeZOffset = -0.25f;
    [SerializeField] private Color runtimeLineColor = new Color(0.15f, 1f, 1f, 0.95f);
    [SerializeField] private Color runtimeCellFillColor = new Color(0.1f, 0.7f, 1f, 0.18f);
    [SerializeField] private string runtimeSortingLayerName = "Default";
    [SerializeField] private int runtimeSortingOrder = 500;

    private readonly Dictionary<Vector2Int, CombatGridOccupant> occupants = new();
    private readonly List<FieldObjectInstance> spawnedFieldObjects = new();
    private readonly List<LineRenderer> runtimeLineRenderers = new();
    private readonly List<SpriteRenderer> runtimeCellFillRenderers = new();

    private Transform runtimeGridRoot;
    private Sprite runtimeCellSprite;

    public int Width => width;
    public int Height => height;

    public void ConfigureRuntimeGrid(Transform origin, int gridWidth, int gridHeight, float newCellSize)
    {
        gridOrigin = origin;
        width = Mathf.Max(3, gridWidth);
        height = Mathf.Max(3, gridHeight);
        cellSize = Mathf.Max(0.25f, newCellSize);
    }

    public Vector3 GetWorldPosition(Vector2Int gridPos)
    {
        Transform origin = gridOrigin != null ? gridOrigin : transform;

        return origin.position + new Vector3(
            (gridPos.x + 0.5f) * cellSize,
            -(gridPos.y + 0.5f) * cellSize,
            0f
        );
    }

    public bool IsWithinBounds(Vector2Int gridPos)
    {
        return gridPos.x >= 0 && gridPos.x < width &&
               gridPos.y >= 0 && gridPos.y < height;
    }

    public bool IsOccupied(Vector2Int gridPos)
    {
        return occupants.ContainsKey(gridPos) && occupants[gridPos] != null;
    }

    public CombatGridOccupant GetOccupant(Vector2Int gridPos)
    {
        occupants.TryGetValue(gridPos, out CombatGridOccupant occ);
        return occ;
    }

    public void ClearGrid(bool destroySpawnedFieldObjects = true)
    {
        occupants.Clear();

        if (!destroySpawnedFieldObjects)
            return;

        for (int i = spawnedFieldObjects.Count - 1; i >= 0; i--)
        {
            if (spawnedFieldObjects[i] != null)
                Destroy(spawnedFieldObjects[i].gameObject);
        }

        spawnedFieldObjects.Clear();
    }

    public bool TryPlaceOccupant(CombatGridOccupant occupant, Vector2Int gridPos)
    {
        if (occupant == null)
            return false;

        if (!IsWithinBounds(gridPos))
            return false;

        if (IsOccupied(gridPos))
            return false;

        RemoveOccupant(occupant);

        occupants[gridPos] = occupant;
        occupant.SetGridPosition(gridPos);
        occupant.SnapToWorldPositionUsingCenterPoint(GetWorldPosition(gridPos));
        return true;
    }

    public bool TryMoveOccupant(CombatGridOccupant occupant, Vector2Int newGridPos)
    {
        if (occupant == null)
            return false;

        if (!IsWithinBounds(newGridPos))
            return false;

        if (IsOccupied(newGridPos))
            return false;

        RemoveOccupant(occupant);

        occupants[newGridPos] = occupant;
        occupant.SetGridPosition(newGridPos);
        occupant.SnapToWorldPositionUsingCenterPoint(GetWorldPosition(newGridPos));
        return true;
    }

    public void RemoveOccupant(CombatGridOccupant occupant)
    {
        if (occupant == null)
            return;

        List<Vector2Int> keysToRemove = null;

        foreach (var kvp in occupants)
        {
            if (kvp.Value == occupant)
            {
                keysToRemove ??= new List<Vector2Int>();
                keysToRemove.Add(kvp.Key);
            }
        }

        if (keysToRemove != null)
        {
            for (int i = 0; i < keysToRemove.Count; i++)
                occupants.Remove(keysToRemove[i]);
        }
    }

    public bool RegisterHero(GameObject heroObject, int row)
    {
        if (heroObject == null)
            return false;

        CombatGridOccupant occupant = heroObject.GetComponent<CombatGridOccupant>();
        if (occupant == null)
            occupant = heroObject.AddComponent<CombatGridOccupant>();

        return TryPlaceOccupant(occupant, new Vector2Int(1, row));
    }

    public bool RegisterMonster(GameObject monsterObject, int row)
    {
        if (monsterObject == null)
            return false;

        CombatGridOccupant occupant = monsterObject.GetComponent<CombatGridOccupant>();
        if (occupant == null)
            occupant = monsterObject.AddComponent<CombatGridOccupant>();

        return TryPlaceOccupant(occupant, new Vector2Int(width - 2, row));
    }

    public bool RegisterHero(HeroStats hero, Vector2Int gridPos)
    {
        GameObject go = ExtractGameObject(hero);
        if (go == null)
            return false;

        CombatGridOccupant occupant = go.GetComponent<CombatGridOccupant>();
        if (occupant == null)
            occupant = go.AddComponent<CombatGridOccupant>();

        return TryPlaceOccupant(occupant, gridPos);
    }

    public bool RegisterMonster(Monster monster, Vector2Int gridPos)
    {
        GameObject go = ExtractGameObject(monster);
        if (go == null)
            return false;

        CombatGridOccupant occupant = go.GetComponent<CombatGridOccupant>();
        if (occupant == null)
            occupant = go.AddComponent<CombatGridOccupant>();

        return TryPlaceOccupant(occupant, gridPos);
    }

    public void SpawnConfiguredFieldObjects()
    {
        for (int i = spawnedFieldObjects.Count - 1; i >= 0; i--)
        {
            if (spawnedFieldObjects[i] == null)
                spawnedFieldObjects.RemoveAt(i);
        }

        for (int i = 0; i < startingFieldObjects.Count; i++)
        {
            StartingFieldObjectEntry entry = startingFieldObjects[i];
            if (entry == null)
            {
                if (logFieldObjectSpawns)
                    Debug.LogWarning($"[BattleGridSystem] Starting field object entry {i} is null.", this);
                continue;
            }

            if (entry.definition == null)
            {
                if (logFieldObjectSpawns)
                    Debug.LogWarning($"[BattleGridSystem] Starting field object entry {i} has no definition assigned.", this);
                continue;
            }

            if (entry.definition.prefab == null)
            {
                if (logFieldObjectSpawns)
                    Debug.LogWarning($"[BattleGridSystem] Field object '{entry.definition.name}' has no prefab assigned, so it cannot spawn.", entry.definition);
                continue;
            }

            if (!IsWithinBounds(entry.gridPosition))
            {
                if (logFieldObjectSpawns)
                    Debug.LogWarning($"[BattleGridSystem] Field object '{entry.definition.name}' wants to spawn out of bounds at {entry.gridPosition}.", this);
                continue;
            }

            if (IsOccupied(entry.gridPosition))
            {
                if (logFieldObjectSpawns)
                    Debug.LogWarning($"[BattleGridSystem] Field object '{entry.definition.name}' could not spawn at {entry.gridPosition} because that tile is already occupied by '{GetOccupant(entry.gridPosition).name}'.", this);
                continue;
            }

            GameObject spawned = Instantiate(entry.definition.prefab);
            spawned.name = entry.definition.prefab.name;

            if (fieldObjectRoot != null)
                spawned.transform.SetParent(fieldObjectRoot, true);

            if (!spawned.activeSelf)
                spawned.SetActive(true);

            FieldObjectInstance instance = spawned.GetComponent<FieldObjectInstance>();
            if (instance == null)
                instance = spawned.GetComponentInChildren<FieldObjectInstance>(true);
            if (instance == null)
                instance = spawned.AddComponent<FieldObjectInstance>();

            instance.Initialize(entry.definition);

            CombatGridOccupant occupant = spawned.GetComponent<CombatGridOccupant>();
            if (occupant == null)
                occupant = spawned.GetComponentInChildren<CombatGridOccupant>(true);
            if (occupant == null)
                occupant = spawned.AddComponent<CombatGridOccupant>();

            bool placed = TryPlaceOccupant(occupant, entry.gridPosition);
            if (placed)
            {
                Vector3 targetWorldPos = GetWorldPosition(entry.gridPosition);
                if (occupant != null)
                    occupant.SnapToWorldPositionUsingCenterPoint(targetWorldPos);
                else
                    spawned.transform.position = targetWorldPos;

                spawnedFieldObjects.Add(instance);

                if (logFieldObjectSpawns)
                    Debug.Log($"[BattleGridSystem] Spawned field object '{entry.definition.name}' at {entry.gridPosition} | world={targetWorldPos} | parent={(spawned.transform.parent != null ? spawned.transform.parent.name : "<none>")} | instanceOn={instance.gameObject.name} | occupantOn={occupant.gameObject.name}", spawned);
            }
            else
            {
                if (logFieldObjectSpawns)
                {
                    string occName = IsOccupied(entry.gridPosition) && GetOccupant(entry.gridPosition) != null ? GetOccupant(entry.gridPosition).name : "<none>";
                    Debug.LogWarning($"[BattleGridSystem] Failed to place spawned field object '{entry.definition.name}' at {entry.gridPosition} | occupiedBy={occName}", spawned);
                }
                Destroy(spawned);
            }
        }
    }

    public bool IsMeleeRange(GameObject attacker, GameObject target)
    {
        if (attacker == null || target == null)
            return false;

        CombatGridOccupant a = attacker.GetComponent<CombatGridOccupant>();
        CombatGridOccupant b = target.GetComponent<CombatGridOccupant>();

        if (a == null || b == null)
            return false;

        int dx = Mathf.Abs(a.GridPosition.x - b.GridPosition.x);
        int dy = Mathf.Abs(a.GridPosition.y - b.GridPosition.y);

        return (dx + dy) == 1;
    }

    public bool IsMeleeRange(HeroStats attacker, Monster target)
    {
        return IsMeleeRange(ExtractGameObject(attacker), ExtractGameObject(target));
    }

    public bool IsMeleeRange(HeroStats attacker, FieldObjectInstance target)
    {
        return IsMeleeRange(ExtractGameObject(attacker), ExtractGameObject(target));
    }

    public bool TryKnockback(GameObject attacker, GameObject target)
    {
        if (attacker == null || target == null)
            return false;

        CombatGridOccupant attackerOcc = attacker.GetComponent<CombatGridOccupant>();
        CombatGridOccupant targetOcc = target.GetComponent<CombatGridOccupant>();

        if (attackerOcc == null || targetOcc == null)
            return false;

        Vector2Int delta = targetOcc.GridPosition - attackerOcc.GridPosition;
        Vector2Int direction = Vector2Int.zero;

        if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
            direction = new Vector2Int(delta.x >= 0 ? 1 : -1, 0);
        else
            direction = new Vector2Int(0, delta.y >= 0 ? 1 : -1);

        if (direction == Vector2Int.zero)
            return false;

        Vector2Int destination = targetOcc.GridPosition + direction;

        if (!IsWithinBounds(destination))
            return false;

        CombatGridOccupant blockingOcc = GetOccupant(destination);
        if (blockingOcc != null)
        {
            FieldObjectInstance fieldObj = blockingOcc.GetComponent<FieldObjectInstance>();
            if (fieldObj != null)
            {
                if (fieldObj.Definition != null && fieldObj.Definition.electrified)
                    fieldObj.TriggerElectrifiedEffect(target);

                if (fieldObj.Definition != null && fieldObj.Definition.explosive)
                    fieldObj.TriggerExplosion();
            }

            return false;
        }

        return TryMoveOccupant(targetOcc, destination);
    }

    public bool TryKnockback(HeroStats attacker, Monster target, object context)
    {
        return TryKnockback(ExtractGameObject(attacker), ExtractGameObject(target));
    }

    public List<Monster> GetMonstersInRadius(Vector2Int center, int radius)
    {
        List<Monster> results = new();

        foreach (var kvp in occupants)
        {
            CombatGridOccupant occ = kvp.Value;
            if (occ == null)
                continue;

            Monster monster = occ.GetComponent<Monster>();
            if (monster == null)
                continue;

            int dist = Mathf.Abs(kvp.Key.x - center.x) + Mathf.Abs(kvp.Key.y - center.y);
            if (dist <= radius)
                results.Add(monster);
        }

        return results;
    }

    public List<Monster> GetMonstersInRadius(Monster centerMonster, int radius)
    {
        if (centerMonster == null)
            return new List<Monster>();

        CombatGridOccupant occ = centerMonster.GetComponent<CombatGridOccupant>();
        if (occ == null)
            return new List<Monster>();

        return GetMonstersInRadius(occ.GridPosition, radius);
    }

    public void SetGridVisualActive(bool active)
    {
        if (active)
        {
            RebuildRuntimeGridVisuals();
            if (runtimeGridRoot != null)
                runtimeGridRoot.gameObject.SetActive(true);
        }
        else
        {
            ClearRuntimeGridVisuals();
        }
    }

    public void RebuildRuntimeGridVisuals()
    {
        ClearRuntimeGridVisuals();

        if (!showRuntimeGrid)
            return;

        EnsureRuntimeGridVisualResources();

        runtimeGridRoot = new GameObject("RuntimeGridVisuals").transform;
        runtimeGridRoot.SetParent(transform, false);
        runtimeGridRoot.localPosition = Vector3.zero;
        runtimeGridRoot.localRotation = Quaternion.identity;
        runtimeGridRoot.localScale = Vector3.one;

        Transform origin = gridOrigin != null ? gridOrigin : transform;
        Vector3 topLeft = origin.position + new Vector3(0f, 0f, runtimeZOffset);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                CreateRuntimeCellFill($"GridFill_{x}_{y}", topLeft + new Vector3((x + 0.5f) * cellSize, -(y + 0.5f) * cellSize, 0.01f));
            }
        }

        for (int x = 0; x <= width; x++)
        {
            Vector3 start = topLeft + new Vector3(x * cellSize, 0f, 0f);
            Vector3 end = start + new Vector3(0f, -height * cellSize, 0f);
            CreateRuntimeLine($"GridV_{x}", start, end);
        }

        for (int y = 0; y <= height; y++)
        {
            Vector3 start = topLeft + new Vector3(0f, -y * cellSize, 0f);
            Vector3 end = start + new Vector3(width * cellSize, 0f, 0f);
            CreateRuntimeLine($"GridH_{y}", start, end);
        }

        Debug.Log($"[BattleGridSystem] Rebuilt runtime grid visuals | width={width} height={height} cellSize={cellSize} origin={origin.position} material={(runtimeGridMaterial != null ? runtimeGridMaterial.name : "<null>")} lineCount={runtimeLineRenderers.Count} fillCount={runtimeCellFillRenderers.Count}", this);
    }

    public void ClearRuntimeGridVisuals()
    {
        for (int i = 0; i < runtimeLineRenderers.Count; i++)
        {
            if (runtimeLineRenderers[i] != null)
                Destroy(runtimeLineRenderers[i].gameObject);
        }

        for (int i = 0; i < runtimeCellFillRenderers.Count; i++)
        {
            if (runtimeCellFillRenderers[i] != null)
                Destroy(runtimeCellFillRenderers[i].gameObject);
        }

        runtimeLineRenderers.Clear();
        runtimeCellFillRenderers.Clear();

        if (runtimeGridRoot != null)
        {
            Destroy(runtimeGridRoot.gameObject);
            runtimeGridRoot = null;
        }
    }

    private void EnsureRuntimeGridVisualResources()
    {
        if (runtimeGridMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            if (shader != null)
            {
                runtimeGridMaterial = new Material(shader);
                runtimeGridMaterial.name = "RuntimeBattleGridMaterial_Auto";
            }
        }

        if (runtimeGridMaterial != null && runtimeGridMaterial.HasProperty("_Color"))
            runtimeGridMaterial.color = Color.white;

        if (runtimeCellSprite == null)
            runtimeCellSprite = CreateRuntimeCellSprite();
    }

    private Sprite CreateRuntimeCellSprite()
    {
        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.name = "RuntimeGridCellTex_Auto";
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        return Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
    }

    private void CreateRuntimeCellFill(string name, Vector3 worldPos)
    {
        GameObject fillObj = new GameObject(name);
        fillObj.transform.SetParent(runtimeGridRoot, false);
        fillObj.transform.position = worldPos;

        SpriteRenderer sr = fillObj.AddComponent<SpriteRenderer>();
        sr.sprite = runtimeCellSprite;
        sr.color = runtimeCellFillColor;
        sr.sortingLayerName = runtimeSortingLayerName;
        sr.sortingOrder = Mathf.Max(0, runtimeSortingOrder - 1);
        fillObj.transform.localScale = new Vector3(cellSize * 0.94f, cellSize * 0.94f, 1f);

        runtimeCellFillRenderers.Add(sr);
    }

    private void CreateRuntimeLine(string lineName, Vector3 start, Vector3 end)
    {
        GameObject lineObj = new GameObject(lineName);
        lineObj.transform.SetParent(runtimeGridRoot, false);
        lineObj.transform.position = Vector3.zero;

        LineRenderer lr = lineObj.AddComponent<LineRenderer>();
        lr.material = runtimeGridMaterial;
        lr.positionCount = 2;
        lr.useWorldSpace = true;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        lr.startWidth = runtimeLineWidth;
        lr.endWidth = runtimeLineWidth;
        lr.startColor = runtimeLineColor;
        lr.endColor = runtimeLineColor;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.alignment = LineAlignment.TransformZ;
        lr.textureMode = LineTextureMode.Stretch;
        lr.numCapVertices = 0;
        lr.numCornerVertices = 0;
        lr.sortingLayerName = runtimeSortingLayerName;
        lr.sortingOrder = runtimeSortingOrder;
        lr.enabled = true;

        runtimeLineRenderers.Add(lr);
    }

    private GameObject ExtractGameObject(object obj)
    {
        if (obj == null)
            return null;

        if (obj is GameObject go)
            return go;

        if (obj is Component comp)
            return comp.gameObject;

        return null;
    }

    private void OnDisable()
    {
        ClearRuntimeGridVisuals();
    }

    private void OnDestroy()
    {
        ClearRuntimeGridVisuals();
    }

    private void OnDrawGizmos()
    {
        if (!drawGridGizmos)
            return;

        Transform origin = gridOrigin != null ? gridOrigin : transform;
        Vector3 topLeft = origin.position;

        Gizmos.color = Color.cyan;

        for (int x = 0; x <= width; x++)
        {
            Vector3 start = topLeft + new Vector3(x * cellSize, 0f, 0f);
            Vector3 end = start + new Vector3(0f, -height * cellSize, 0f);
            Gizmos.DrawLine(start, end);
        }

        for (int y = 0; y <= height; y++)
        {
            Vector3 start = topLeft + new Vector3(0f, -y * cellSize, 0f);
            Vector3 end = start + new Vector3(width * cellSize, 0f, 0f);
            Gizmos.DrawLine(start, end);
        }

        if (drawCellCenters)
        {
            Gizmos.color = Color.yellow;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vector3 cellCenter = GetWorldPosition(new Vector2Int(x, y));
                    Gizmos.DrawSphere(cellCenter, cellSize * 0.08f);
                }
            }
        }
    }
}


////////////////////////////////////////////////////////////
