#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ProjectLayoutExporter
{
    // Toggle this if you want to include simple serialized field snapshots per component.
    // This can make JSON bigger and can include a lot of noise, but sometimes helps.
    private const bool INCLUDE_SERIALIZED_FIELDS = false;

    // Single source of truth for exports directory.
    // All other export scripts should write into this directory (or subfolders under it).
    public const string ExportRoot = "ProjectExports";

    public static string GetExportDirectory()
    {
        Directory.CreateDirectory(ExportRoot);
        return ExportRoot;
    }

    [MenuItem("Tools/Export/Export Project Layout (Scenes + Prefabs)")]
    public static void ExportProjectLayout()
    {
        // Avoid losing work.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.LogWarning("Export cancelled (user declined to save modified scenes).");
            return;
        }

        var exportRoot = GetExportDirectory();

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var fullDumpPath = Path.Combine(exportRoot, $"ProjectLayout_export_{timestamp}.json");
        var indexJsonPath = Path.Combine(exportRoot, $"ScriptToObjects_index_{timestamp}.json");
        var indexMdPath   = Path.Combine(exportRoot, $"ScriptToObjects_index_{timestamp}.md");

        // Capture currently open scenes so we can restore them later.
        var openScenes = GetOpenScenePaths();

        var fullDump = new ProjectDump
        {
            unityVersion = Application.unityVersion,
            exportedAtLocal = DateTime.Now.ToString("O"),
            scenes = new List<SceneDump>(),
            prefabs = new List<PrefabDump>()
        };

        // This is your primary use-case: script -> attachments
        var scriptIndex = new Dictionary<string, List<ScriptAttachment>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            ExportAllScenes(fullDump, scriptIndex);
            ExportAllPrefabs(fullDump, scriptIndex);

            // Write outputs
            File.WriteAllText(fullDumpPath, EditorJsonUtility.ToJson(fullDump, true));
            File.WriteAllText(indexJsonPath, JsonUtility.ToJson(new ScriptIndexWrapper(scriptIndex), true));
            File.WriteAllText(indexMdPath, BuildMarkdownIndex(scriptIndex));

            Debug.Log($"✅ Export complete.\n- {fullDumpPath}\n- {indexJsonPath}\n- {indexMdPath}");
        }
        finally
        {
            // Restore original scene setup (best effort).
            RestoreScenes(openScenes);
        }
    }

    private static void ExportAllScenes(ProjectDump fullDump, Dictionary<string, List<ScriptAttachment>> scriptIndex)
    {
        // Scan all scene assets under Assets/
        var sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
        var scenePaths = sceneGuids.Select(AssetDatabase.GUIDToAssetPath).Distinct().ToList();

        foreach (var scenePath in scenePaths)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var sceneDump = new SceneDump
            {
                scenePath = scenePath,
                rootObjects = new List<GameObjectDump>()
            };

            foreach (var root in scene.GetRootGameObjects())
            {
                sceneDump.rootObjects.Add(SerializeGameObjectTree(root, locationType: "scene", locationPath: scenePath, scriptIndex: scriptIndex));
            }

            fullDump.scenes.Add(sceneDump);
        }
    }

    private static void ExportAllPrefabs(ProjectDump fullDump, Dictionary<string, List<ScriptAttachment>> scriptIndex)
    {
        var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
        var prefabPaths = prefabGuids.Select(AssetDatabase.GUIDToAssetPath).Distinct().ToList();

        foreach (var prefabPath in prefabPaths)
        {
            GameObject prefabRoot = null;

            try
            {
                prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);

                var prefabDump = new PrefabDump
                {
                    prefabPath = prefabPath,
                    root = SerializeGameObjectTree(prefabRoot, locationType: "prefab", locationPath: prefabPath, scriptIndex: scriptIndex)
                };

                fullDump.prefabs.Add(prefabDump);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Prefab export failed for {prefabPath}: {ex.Message}");
            }
            finally
            {
                if (prefabRoot != null)
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }
    }

    private static GameObjectDump SerializeGameObjectTree(
        GameObject go,
        string locationType,
        string locationPath,
        Dictionary<string, List<ScriptAttachment>> scriptIndex)
    {
        var dump = new GameObjectDump
        {
            name = go.name,
            path = GetHierarchyPath(go),
            activeSelf = go.activeSelf,
            tag = SafeGetTag(go),
            layer = LayerMask.LayerToName(go.layer),
            components = new List<ComponentDump>(),
            children = new List<GameObjectDump>()
        };

        // Capture components
        var comps = go.GetComponents<Component>();
        foreach (var c in comps)
        {
            if (c == null) continue;

            var type = c.GetType();
            var compDump = new ComponentDump
            {
                typeName = type.FullName,
                enabled = GetEnabledStateIfPossible(c),
                scriptAssetPath = null,
                fields = null
            };

            // If it's a MonoBehaviour, record the script asset path + index it.
            if (c is MonoBehaviour mb)
            {
                var monoScript = MonoScript.FromMonoBehaviour(mb);
                var scriptPath = monoScript != null ? AssetDatabase.GetAssetPath(monoScript) : null;
                compDump.scriptAssetPath = scriptPath;

                // Add to script index
                var key = !string.IsNullOrEmpty(scriptPath) ? scriptPath : type.FullName;
                if (!scriptIndex.TryGetValue(key, out var list))
                {
                    list = new List<ScriptAttachment>();
                    scriptIndex[key] = list;
                }

                list.Add(new ScriptAttachment
                {
                    locationType = locationType,
                    locationPath = locationPath,
                    gameObjectPath = dump.path,
                    componentType = type.FullName,
                    enabled = compDump.enabled
                });

                if (INCLUDE_SERIALIZED_FIELDS)
                {
                    compDump.fields = TrySerializeFields(mb);
                }
            }

            dump.components.Add(compDump);
        }

        // Recurse children
        foreach (Transform child in go.transform)
        {
            dump.children.Add(SerializeGameObjectTree(child.gameObject, locationType, locationPath, scriptIndex));
        }

        return dump;
    }

    private static string GetHierarchyPath(GameObject go)
    {
        var names = new List<string>();
        Transform t = go.transform;
        while (t != null)
        {
            names.Add(t.name);
            t = t.parent;
        }
        names.Reverse();
        return string.Join("/", names);
    }

    private static string SafeGetTag(GameObject go)
    {
        try { return go.tag; }
        catch { return "Untagged"; }
    }

    private static bool? GetEnabledStateIfPossible(Component c)
    {
        // Only some components have an "enabled" property.
        // MonoBehaviour, Behaviour, Renderer, Collider, etc.
        if (c is Behaviour b) return b.enabled;
        if (c is Renderer r) return r.enabled;
        if (c is Collider col) return col.enabled;
        return null;
    }

    private static Dictionary<string, string> TrySerializeFields(MonoBehaviour mb)
    {
        // Field snapshot is optional and intentionally shallow (stringified values).
        // This avoids exploding the JSON and avoids Unity object recursion.
        var dict = new Dictionary<string, string>();
        try
        {
            var so = new SerializedObject(mb);
            var prop = so.GetIterator();
            bool enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;

                // Skip Unity internal fields that add noise.
                if (prop.name == "m_Script") continue;

                dict[prop.propertyPath] = SerializedPropertyToString(prop);
            }
        }
        catch
        {
            // ignore
        }
        return dict.Count > 0 ? dict : null;
    }

    private static string SerializedPropertyToString(SerializedProperty p)
    {
        try
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer: return p.intValue.ToString();
                case SerializedPropertyType.Boolean: return p.boolValue.ToString();
                case SerializedPropertyType.Float: return p.floatValue.ToString("G");
                case SerializedPropertyType.String: return p.stringValue ?? "";
                case SerializedPropertyType.Enum: return p.enumDisplayNames != null && p.enumValueIndex >= 0
                    ? p.enumDisplayNames[p.enumValueIndex]
                    : p.enumValueIndex.ToString();
                case SerializedPropertyType.ObjectReference:
                    return p.objectReferenceValue ? AssetDatabase.GetAssetPath(p.objectReferenceValue) : "null";
                case SerializedPropertyType.Vector2: return p.vector2Value.ToString();
                case SerializedPropertyType.Vector3: return p.vector3Value.ToString();
                case SerializedPropertyType.Vector4: return p.vector4Value.ToString();
                case SerializedPropertyType.Color: return p.colorValue.ToString();
                case SerializedPropertyType.Quaternion: return p.quaternionValue.eulerAngles.ToString();
                default:
                    return p.propertyType.ToString();
            }
        }
        catch
        {
            return "<unreadable>";
        }
    }

    private static string BuildMarkdownIndex(Dictionary<string, List<ScriptAttachment>> scriptIndex)
    {
        var lines = new List<string>();
        lines.Add("# Script → Attachments Index");
        lines.Add("");
        lines.Add($"Exported: {DateTime.Now:O}");
        lines.Add("");
        lines.Add($"Total scripts (keys): {scriptIndex.Count}");
        lines.Add("");

        foreach (var kv in scriptIndex.OrderByDescending(k => k.Value.Count).ThenBy(k => k.Key))
        {
            lines.Add($"## {kv.Key}");
            lines.Add($"Attachments: **{kv.Value.Count}**");
            lines.Add("");

            // Group by scene/prefab to make it readable
            var grouped = kv.Value
                .GroupBy(a => $"{a.locationType}:{a.locationPath}")
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key);

            foreach (var g in grouped)
            {
                lines.Add($"- **{g.Key}** ({g.Count()})");
                foreach (var a in g.OrderBy(x => x.gameObjectPath))
                {
                    lines.Add($"  - `{a.gameObjectPath}` ({a.componentType}) enabled={a.enabled?.ToString() ?? "n/a"}");
                }
            }

            lines.Add("");
        }

        return string.Join("\n", lines);
    }

    private static List<string> GetOpenScenePaths()
    {
        var paths = new List<string>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            if (s.IsValid() && s.isLoaded && !string.IsNullOrEmpty(s.path))
                paths.Add(s.path);
        }
        return paths;
    }

    private static void RestoreScenes(List<string> openScenePaths)
    {
        if (openScenePaths == null || openScenePaths.Count == 0)
            return;

        // Open first in Single, then add others additively
        EditorSceneManager.OpenScene(openScenePaths[0], OpenSceneMode.Single);
        for (int i = 1; i < openScenePaths.Count; i++)
            EditorSceneManager.OpenScene(openScenePaths[i], OpenSceneMode.Additive);
    }

    [Serializable]
    private class ProjectDump
    {
        public string unityVersion;
        public string exportedAtLocal;
        public List<SceneDump> scenes;
        public List<PrefabDump> prefabs;
    }

    [Serializable]
    private class SceneDump
    {
        public string scenePath;
        public List<GameObjectDump> rootObjects;
    }

    [Serializable]
    private class PrefabDump
    {
        public string prefabPath;
        public GameObjectDump root;
    }

    [Serializable]
    private class GameObjectDump
    {
        public string name;
        public string path;
        public bool activeSelf;
        public string tag;
        public string layer;
        public List<ComponentDump> components;
        public List<GameObjectDump> children;
    }

    [Serializable]
    private class ComponentDump
    {
        public string typeName;
        public bool? enabled;
        public string scriptAssetPath; // only set for MonoBehaviours
        public Dictionary<string, string> fields; // optional
    }

    [Serializable]
    private class ScriptAttachment
    {
        public string locationType; // "scene" or "prefab"
        public string locationPath; // asset path
        public string gameObjectPath; // hierarchy path
        public string componentType; // full type name
        public bool? enabled;
    }

    [Serializable]
    private class ScriptIndexWrapper
    {
        public List<ScriptIndexEntry> entries;

        public ScriptIndexWrapper(Dictionary<string, List<ScriptAttachment>> dict)
        {
            entries = dict.Select(kv => new ScriptIndexEntry
            {
                scriptKey = kv.Key,
                attachments = kv.Value
            }).ToList();
        }
    }

    [Serializable]
    private class ScriptIndexEntry
    {
        public string scriptKey;
        public List<ScriptAttachment> attachments;
    }
}
#endif
