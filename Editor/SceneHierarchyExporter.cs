#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SceneHierarchyExporter
{
    [Serializable]
    private class HierarchyExport
    {
        public string unityVersion;
        public string exportedAtUtc;
        public List<SceneExport> scenes = new List<SceneExport>();
    }

    [Serializable]
    private class SceneExport
    {
        public string sceneName;
        public string scenePath;
        public bool isLoaded;
        public List<GameObjectExport> roots = new List<GameObjectExport>();
    }

    [Serializable]
    private class GameObjectExport
    {
        public string name;
        public string path;                 // Full path like "Canvas/PartyHUD/StatusIcons"
        public bool activeSelf;
        public bool activeInHierarchy;
        public int siblingIndex;
        public string tag;
        public int layer;
        public string layerName;

        public TransformExport transform;
        public List<string> components = new List<string>();
        public PrefabExport prefab;

        public List<GameObjectExport> children = new List<GameObjectExport>();
    }

    [Serializable]
    private class TransformExport
    {
        public float[] localPosition;  // [x,y,z]
        public float[] localRotation;  // quaternion [x,y,z,w]
        public float[] localScale;     // [x,y,z]
    }

    [Serializable]
    private class PrefabExport
    {
        public bool isPrefabInstance;
        public string assetPath;       // Prefab asset path if available
        public string status;          // Connected / Missing / NotAPrefab
    }

    [MenuItem("Tools/Export/Scene Hierarchy (JSON)...", priority = 2000)]
    public static void ExportHierarchyJson()
    {
        var defaultName = $"SceneHierarchy_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
        var savePath = EditorUtility.SaveFilePanel(
            "Export Scene Hierarchy (JSON)",
            Application.dataPath,
            defaultName,
            "json"
        );

        if (string.IsNullOrWhiteSpace(savePath))
            return;

        var export = BuildExport();
        var json = JsonUtility.ToJson(export, prettyPrint: true);

        File.WriteAllText(savePath, json, Encoding.UTF8);
        Debug.Log($"[SceneHierarchyExporter] Exported JSON to: {savePath}");

        // Optional: also export a simple tree text file next to it
        var txtPath = Path.ChangeExtension(savePath, ".txt");
        try
        {
            File.WriteAllText(txtPath, BuildTreeText(export), Encoding.UTF8);
            Debug.Log($"[SceneHierarchyExporter] Exported TXT tree to: {txtPath}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SceneHierarchyExporter] Failed to write TXT tree: {e.Message}");
        }

        AssetDatabase.Refresh();
    }

    private static HierarchyExport BuildExport()
    {
        var result = new HierarchyExport
        {
            unityVersion = Application.unityVersion,
            exportedAtUtc = DateTime.UtcNow.ToString("O")
        };

        // Ensure we look at all loaded scenes in the editor
        int sceneCount = SceneManager.sceneCount;
        for (int i = 0; i < sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);

            var sceneExport = new SceneExport
            {
                sceneName = scene.name,
                scenePath = scene.path,
                isLoaded = scene.isLoaded
            };

            if (scene.isLoaded)
            {
                var roots = scene.GetRootGameObjects();
                foreach (var root in roots)
                {
                    sceneExport.roots.Add(ExportGameObjectRecursive(root, parentPath: null));
                }
            }

            result.scenes.Add(sceneExport);
        }

        return result;
    }

    private static GameObjectExport ExportGameObjectRecursive(GameObject go, string parentPath)
    {
        var thisPath = string.IsNullOrEmpty(parentPath) ? go.name : $"{parentPath}/{go.name}";

        var node = new GameObjectExport
        {
            name = go.name,
            path = thisPath,
            activeSelf = go.activeSelf,
            activeInHierarchy = go.activeInHierarchy,
            siblingIndex = go.transform.GetSiblingIndex(),
            tag = SafeGetTag(go),
            layer = go.layer,
            layerName = LayerMask.LayerToName(go.layer),
            transform = new TransformExport
            {
                localPosition = ToArr(go.transform.localPosition),
                localRotation = ToArr(go.transform.localRotation),
                localScale = ToArr(go.transform.localScale)
            },
            prefab = GetPrefabInfo(go)
        };

        // Components (type names)
        var comps = go.GetComponents<Component>();
        for (int i = 0; i < comps.Length; i++)
        {
            var c = comps[i];
            if (c == null)
            {
                node.components.Add("Missing (null)");
                continue;
            }
            node.components.Add(c.GetType().FullName);
        }

        // Children
        var t = go.transform;
        for (int i = 0; i < t.childCount; i++)
        {
            var child = t.GetChild(i).gameObject;
            node.children.Add(ExportGameObjectRecursive(child, thisPath));
        }

        return node;
    }

    private static string SafeGetTag(GameObject go)
    {
        try { return go.tag; }
        catch { return "Untagged"; }
    }

    private static PrefabExport GetPrefabInfo(GameObject go)
    {
        var prefab = new PrefabExport();

        var status = PrefabUtility.GetPrefabInstanceStatus(go);
        prefab.isPrefabInstance = status != PrefabInstanceStatus.NotAPrefab;
        prefab.status = status.ToString();

        if (prefab.isPrefabInstance)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
            if (source != null)
            {
                var path = AssetDatabase.GetAssetPath(source);
                prefab.assetPath = path;
            }
        }

        return prefab;
    }

    private static float[] ToArr(Vector3 v) => new[] { v.x, v.y, v.z };
    private static float[] ToArr(Quaternion q) => new[] { q.x, q.y, q.z, q.w };

    private static string BuildTreeText(HierarchyExport export)
    {
        var sb = new StringBuilder(1024);
        sb.AppendLine($"Unity: {export.unityVersion}");
        sb.AppendLine($"Exported (UTC): {export.exportedAtUtc}");
        sb.AppendLine();

        foreach (var scene in export.scenes)
        {
            sb.AppendLine($"Scene: {scene.sceneName}");
            if (!string.IsNullOrEmpty(scene.scenePath))
                sb.AppendLine($"  Path: {scene.scenePath}");
            sb.AppendLine($"  Loaded: {scene.isLoaded}");
            sb.AppendLine();

            foreach (var root in scene.roots)
                AppendNode(sb, root, indent: 2);

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void AppendNode(StringBuilder sb, GameObjectExport node, int indent)
    {
        sb.Append(' ', indent);
        sb.Append("- ");
        sb.Append(node.name);

        if (!node.activeInHierarchy)
            sb.Append(" [inactive]");

        if (node.prefab != null && node.prefab.isPrefabInstance)
            sb.Append($" [prefab:{node.prefab.status}]");

        sb.AppendLine();

        for (int i = 0; i < node.children.Count; i++)
            AppendNode(sb, node.children[i], indent + 2);
    }
}
#endif
