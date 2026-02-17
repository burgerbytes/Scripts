#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine.SceneManagement;

public class ProjectUIInteractivityExporter : EditorWindow
{
    private bool includePrefabs = true;
    private bool includeScenes = true;

    private string pathFilter = "Assets";     // You can narrow to "Assets/Prefabs" etc.
    private string outputFolder = "UI_Exports";
    private bool exportCsvToo = true;

    [MenuItem("Tools/Slots & Sorcery/Export UI Interactivity Report")]
    public static void Open()
    {
        GetWindow<ProjectUIInteractivityExporter>("UI Interactivity Export");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Export UI Interactivity Report", EditorStyles.boldLabel);
        EditorGUILayout.Space(6);

        includePrefabs = EditorGUILayout.ToggleLeft("Include Prefabs", includePrefabs);
        includeScenes  = EditorGUILayout.ToggleLeft("Include Scenes", includeScenes);

        EditorGUILayout.Space(6);
        pathFilter = EditorGUILayout.TextField(new GUIContent("Path Filter", "Only scan assets under this path."), pathFilter);
        outputFolder = EditorGUILayout.TextField(new GUIContent("Output Folder", "Relative to project root."), outputFolder);
        exportCsvToo = EditorGUILayout.ToggleLeft("Also export CSV summary", exportCsvToo);

        EditorGUILayout.Space(10);

        EditorGUILayout.HelpBox(
            "This export finds why UI is non-clickable by recording CanvasGroup, raycastTarget, Selectable.interactable, " +
            "GraphicRaycaster, and active state. Scenes are opened one-by-one and your current scene setup is restored.",
            MessageType.Info
        );

        EditorGUILayout.Space(10);

        using (new EditorGUI.DisabledScope(!includePrefabs && !includeScenes))
        {
            if (GUILayout.Button("Export Now", GUILayout.Height(34)))
            {
                Export();
            }
        }
    }

    [Serializable]
    private class ExportRoot
    {
        public string exportedAt;
        public string unityVersion;
        public List<AssetReport> assets = new List<AssetReport>();
    }

    [Serializable]
    private class AssetReport
    {
        public string assetPath;            // prefab or scene path
        public string assetType;            // "Prefab" or "Scene"
        public List<NodeReport> nodes = new List<NodeReport>();
    }

    [Serializable]
    private class NodeReport
    {
        public string path;                 // GameObject hierarchy path
        public string name;
        public bool activeSelf;
        public bool activeInHierarchy;
        public int layer;
        public string tag;

        // Canvas / Raycaster
        public bool hasCanvas;
        public bool canvasEnabled;
        public int canvasSortingOrder;
        public string canvasSortingLayer;

        public bool hasGraphicRaycaster;
        public bool graphicRaycasterEnabled;

        // CanvasGroup (can be multiple in parent chain, but we record local + parent chain)
        public CanvasGroupReport localCanvasGroup;
        public List<CanvasGroupReport> parentCanvasGroups = new List<CanvasGroupReport>();

        // Selectable (Button, Toggle, etc.)
        public bool hasSelectable;
        public string selectableType;
        public bool selectableInteractable;

        // Graphics (Image/TMP/etc.)
        public List<GraphicReport> graphics = new List<GraphicReport>();
    }

    [Serializable]
    private class CanvasGroupReport
    {
        public string onObjectPath;
        public bool interactable;
        public bool blocksRaycasts;
        public bool ignoreParentGroups;
        public float alpha;
        public bool enabled;
    }

    [Serializable]
    private class GraphicReport
    {
        public string type;
        public bool raycastTarget;
        public bool enabled;
        public string material;
        public string spriteOrFont;
    }

    private void Export()
    {
        // Create output directory relative to project root.
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string outDir = Path.Combine(projectRoot, outputFolder);
        Directory.CreateDirectory(outDir);

        var root = new ExportRoot
        {
            exportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            unityVersion = Application.unityVersion
        };

        // Prefabs
        if (includePrefabs)
        {
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { pathFilter });
            foreach (var guid in prefabGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!assetPath.StartsWith(pathFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                var report = new AssetReport { assetPath = assetPath, assetType = "Prefab" };
                try
                {
                    var prefabRoot = PrefabUtility.LoadPrefabContents(assetPath);
                    CollectFromRoot(prefabRoot, report.nodes);
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
                catch (Exception ex)
                {
                    report.nodes.Add(new NodeReport
                    {
                        path = assetPath,
                        name = "ERROR_LOADING_PREFAB",
                        activeSelf = false,
                        activeInHierarchy = false,
                        layer = 0,
                        tag = "",
                        graphics = new List<GraphicReport> { new GraphicReport { type = "Exception", raycastTarget = false, enabled = false, material = ex.Message, spriteOrFont = "" } }
                    });
                }

                root.assets.Add(report);
            }
        }

        // Scenes
        if (includeScenes)
        {
            // Save current setup so we can restore it.
            var originalSetup = EditorSceneManager.GetSceneManagerSetup();

            string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { pathFilter });
            foreach (var guid in sceneGuids)
            {
                string scenePath = AssetDatabase.GUIDToAssetPath(guid);
                if (!scenePath.StartsWith(pathFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                var report = new AssetReport { assetPath = scenePath, assetType = "Scene" };

                try
                {
                    var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                    foreach (var go in scene.GetRootGameObjects())
                    {
                        CollectFromRoot(go, report.nodes);
                    }
                }
                catch (Exception ex)
                {
                    report.nodes.Add(new NodeReport
                    {
                        path = scenePath,
                        name = "ERROR_LOADING_SCENE",
                        activeSelf = false,
                        activeInHierarchy = false,
                        layer = 0,
                        tag = "",
                        graphics = new List<GraphicReport> { new GraphicReport { type = "Exception", raycastTarget = false, enabled = false, material = ex.Message, spriteOrFont = "" } }
                    });
                }

                root.assets.Add(report);
            }

            // Restore original editor scene setup
            EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
        }

        // Write JSON
        string json = JsonUtility.ToJson(root, prettyPrint: true);
        string jsonPath = Path.Combine(outDir, $"UIInteractivityReport_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        File.WriteAllText(jsonPath, json);

        // Optional CSV summary
        if (exportCsvToo)
        {
            string csvPath = Path.Combine(outDir, $"UIInteractivitySummary_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            WriteCsv(root, csvPath);
        }

        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "Export Complete",
            $"Wrote:\n{jsonPath}\n" + (exportCsvToo ? $"and CSV summary in:\n{outDir}" : $"in:\n{outDir}"),
            "OK"
        );
    }

    private static void CollectFromRoot(GameObject root, List<NodeReport> outNodes)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            var go = t.gameObject;

            // Only record objects that are likely to matter for UI interactivity:
            // - Has CanvasGroup OR Selectable OR Graphic OR Canvas OR GraphicRaycaster
            var hasCanvasGroup = go.GetComponent<CanvasGroup>() != null;
            var selectable = go.GetComponent<Selectable>();
            var graphics = go.GetComponents<Graphic>();
            var tmpText = go.GetComponent<TMP_Text>(); // TMP derives from Graphic in newer versions, but keep safe
            var canvas = go.GetComponent<Canvas>();
            var raycaster = go.GetComponent<GraphicRaycaster>();

            bool relevant =
                hasCanvasGroup ||
                selectable != null ||
                (graphics != null && graphics.Length > 0) ||
                tmpText != null ||
                canvas != null ||
                raycaster != null;

            if (!relevant)
                continue;

            var node = new NodeReport
            {
                path = GetPath(go),
                name = go.name,
                activeSelf = go.activeSelf,
                activeInHierarchy = go.activeInHierarchy,
                layer = go.layer,
                tag = SafeTag(go),

                graphics = new List<GraphicReport>()
            };

            // Canvas info
            if (canvas != null)
            {
                node.hasCanvas = true;
                node.canvasEnabled = canvas.enabled;
                node.canvasSortingOrder = canvas.sortingOrder;
                node.canvasSortingLayer = canvas.sortingLayerName;
            }

            // Raycaster info
            if (raycaster != null)
            {
                node.hasGraphicRaycaster = true;
                node.graphicRaycasterEnabled = raycaster.enabled;
            }

            // Local CanvasGroup
            var localCg = go.GetComponent<CanvasGroup>();
            if (localCg != null)
            {
                node.localCanvasGroup = ToCgReport(localCg, node.path);
            }

            // Parent CanvasGroups
            var parentCgs = go.GetComponentsInParent<CanvasGroup>(true);
            if (parentCgs != null && parentCgs.Length > 0)
            {
                foreach (var cg in parentCgs)
                {
                    // include parent chain; local may already be included, that's fine (it helps debugging)
                    node.parentCanvasGroups.Add(ToCgReport(cg, GetPath(cg.gameObject)));
                }
            }

            // Selectable
            if (selectable != null)
            {
                node.hasSelectable = true;
                node.selectableType = selectable.GetType().Name;
                node.selectableInteractable = selectable.interactable;
            }

            // Graphics
            if (graphics != null && graphics.Length > 0)
            {
                foreach (var g in graphics)
                {
                    node.graphics.Add(ToGraphicReport(g));
                }
            }

            outNodes.Add(node);
        }
    }

    private static CanvasGroupReport ToCgReport(CanvasGroup cg, string onPath)
    {
        return new CanvasGroupReport
        {
            onObjectPath = onPath,
            interactable = cg.interactable,
            blocksRaycasts = cg.blocksRaycasts,
            ignoreParentGroups = cg.ignoreParentGroups,
            alpha = cg.alpha,
            enabled = cg.enabled
        };
    }

    private static GraphicReport ToGraphicReport(Graphic g)
    {
        string spriteOrFont = "";
        if (g is Image img && img.sprite != null) spriteOrFont = img.sprite.name;
        else if (g is TMP_Text tmp && tmp.font != null) spriteOrFont = tmp.font.name;

        string matName = g.material != null ? g.material.name : "";

        return new GraphicReport
        {
            type = g.GetType().Name,
            raycastTarget = g.raycastTarget,
            enabled = g.enabled,
            material = matName,
            spriteOrFont = spriteOrFont
        };
    }

    private static string GetPath(GameObject go)
    {
        if (go == null) return "";
        var t = go.transform;
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    private static string SafeTag(GameObject go)
    {
        try { return go.tag; }
        catch { return ""; }
    }

    private static void WriteCsv(ExportRoot root, string csvPath)
    {
        // CSV columns focused on "why isn't this clickable?"
        // One line per node.
        using var sw = new StreamWriter(csvPath);

        sw.WriteLine(string.Join(",",
            "assetType",
            "assetPath",
            "nodePath",
            "activeSelf",
            "activeInHierarchy",
            "hasSelectable",
            "selectableType",
            "selectableInteractable",
            "localCanvasGroup.interactable",
            "localCanvasGroup.blocksRaycasts",
            "localCanvasGroup.alpha",
            "parentCanvasGroupCount",
            "anyParentBlocksRaycastsFalse",
            "anyParentInteractableFalse",
            "anyGraphicRaycastTargetTrue",
            "hasGraphicRaycaster",
            "graphicRaycasterEnabled"
        ));

        foreach (var asset in root.assets)
        {
            foreach (var node in asset.nodes)
            {
                bool anyParentBlocksFalse = node.parentCanvasGroups != null && node.parentCanvasGroups.Any(cg => cg.blocksRaycasts == false);
                bool anyParentInteractFalse = node.parentCanvasGroups != null && node.parentCanvasGroups.Any(cg => cg.interactable == false);

                bool anyRaycastTargetTrue = node.graphics != null && node.graphics.Any(gr => gr.raycastTarget);

                string localCgInteract = node.localCanvasGroup != null ? node.localCanvasGroup.interactable.ToString() : "";
                string localCgBlocks = node.localCanvasGroup != null ? node.localCanvasGroup.blocksRaycasts.ToString() : "";
                string localCgAlpha = node.localCanvasGroup != null ? node.localCanvasGroup.alpha.ToString("0.###") : "";

                sw.WriteLine(string.Join(",",
                    Escape(asset.assetType),
                    Escape(asset.assetPath),
                    Escape(node.path),
                    node.activeSelf,
                    node.activeInHierarchy,
                    node.hasSelectable,
                    Escape(node.selectableType ?? ""),
                    node.hasSelectable ? node.selectableInteractable.ToString() : "",
                    Escape(localCgInteract),
                    Escape(localCgBlocks),
                    Escape(localCgAlpha),
                    node.parentCanvasGroups != null ? node.parentCanvasGroups.Count.ToString() : "0",
                    anyParentBlocksFalse,
                    anyParentInteractFalse,
                    anyRaycastTargetTrue,
                    node.hasGraphicRaycaster,
                    node.hasGraphicRaycaster ? node.graphicRaycasterEnabled.ToString() : ""
                ));
            }
        }
    }

    private static string Escape(string s)
    {
        if (s == null) return "";
        if (s.Contains(",") || s.Contains("\"") || s.Contains("\n"))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
#endif
