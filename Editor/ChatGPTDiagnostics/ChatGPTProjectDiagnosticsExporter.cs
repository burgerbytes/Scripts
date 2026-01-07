// Assets/Editor/ChatGPTDiagnostics/ChatGPTProjectDiagnosticsExporter.cs
// Full file (updated): replaces EditorJsonUtility.FromJson<T> with JsonUtility.FromJson<T>
// Unity 6 compatible.

#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

using UnityEngine;
using UnityEngine.SceneManagement;

public static class ChatGPTProjectDiagnosticsExporter
{
    // =========================
    // Menu
    // =========================

    private const string MENU_ROOT = "Tools/ChatGPT/";
    private const string MENU_FULL = MENU_ROOT + "Export Project Diagnostics (Full)";
    private const string MENU_OPEN_SCENES = MENU_ROOT + "Export Project Diagnostics (Open Scenes Only)";
    private const string MENU_ACTIVE_SCENE = MENU_ROOT + "Export Project Diagnostics (Active Scene Only)";

    [MenuItem(MENU_FULL)]
    public static void ExportFull()
    {
        try
        {
            Export(ExportMode.FullProject);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ChatGPTDiagnostics] ExportFull failed:\n{ex}");
        }
    }

    [MenuItem(MENU_OPEN_SCENES)]
    public static void ExportOpenScenesOnly()
    {
        try
        {
            Export(ExportMode.OpenScenesOnly);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ChatGPTDiagnostics] ExportOpenScenesOnly failed:\n{ex}");
        }
    }

    [MenuItem(MENU_ACTIVE_SCENE)]
    public static void ExportActiveSceneOnly()
    {
        try
        {
            Export(ExportMode.ActiveSceneOnly);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ChatGPTDiagnostics] ExportActiveSceneOnly failed:\n{ex}");
        }
    }

    // =========================
    // Export Modes
    // =========================

    private enum ExportMode
    {
        FullProject,
        OpenScenesOnly,
        ActiveSceneOnly
    }

    // =========================
    // Public entry
    // =========================

    private static void Export(ExportMode mode)
    {
        string exportDir = ResolveExportDirectory();
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        EnsureDir(exportDir);

        var producedFiles = new List<string>();

        // 1) Build settings scenes
        producedFiles.Add(WriteJson(Path.Combine(exportDir, $"BuildSettingsScenes_{stamp}.json"), BuildBuildSettingsScenesDump()));

        // 2) Script execution order
        producedFiles.Add(WriteJson(Path.Combine(exportDir, $"ScriptExecutionOrder_{stamp}.json"), BuildScriptExecutionOrderDump()));

        // 3) Packages / manifest
        var manifestDump = BuildPackagesManifestDump(stamp, exportDir);
        producedFiles.Add(manifestDump.summaryPath);
        if (!string.IsNullOrEmpty(manifestDump.rawPath))
            producedFiles.Add(manifestDump.rawPath);

        // 4) Addressables summary (if installed)
        string addrPath = TryWriteAddressablesSummary(exportDir, stamp);
        if (!string.IsNullOrEmpty(addrPath))
            producedFiles.Add(addrPath);

        // 5) Existing tools integration (best-effort reflection)
        producedFiles.AddRange(TryRunExistingExporters(exportDir));

        // 6) Scene deep snapshots / prefab overrides
        var scenePaths = GetScenePathsToExport(mode);
        foreach (var sp in scenePaths)
        {
            if (!string.IsNullOrEmpty(sp))
            {
                // Ensure scene is loaded (in additive if not already, but don’t disturb user more than needed)
                var scene = EnsureSceneLoaded(sp, out bool loadedAdditively);
                if (!scene.IsValid())
                    continue;

                string sceneName = SanitizeFileName(scene.name);
                producedFiles.Add(WriteJson(Path.Combine(exportDir, $"SceneDeepSnapshot_{sceneName}_{stamp}.json"), BuildSceneDeepSnapshot(scene)));
                producedFiles.Add(WriteJson(Path.Combine(exportDir, $"PrefabOverrides_{sceneName}_{stamp}.json"), BuildPrefabOverrides(scene)));

                if (loadedAdditively)
                {
                    EditorSceneManager.CloseScene(scene, removeScene: true);
                }
            }
        }

        // 7) Index markdown
        string indexPath = Path.Combine(exportDir, $"DiagnosticsIndex_{stamp}.md");
        File.WriteAllText(indexPath, BuildIndexMarkdown(exportDir, stamp, producedFiles), Encoding.UTF8);
        producedFiles.Add(indexPath);

        AssetDatabase.Refresh();

        Debug.Log($"[ChatGPTDiagnostics] Export complete. Wrote {producedFiles.Count} file(s) to:\n{exportDir}");
        EditorUtility.RevealInFinder(exportDir);
    }

    // =========================
    // Export Directory Resolution
    // =========================

    private static string ResolveExportDirectory()
    {
        // Goal: "output to the same directory that it currently does"
        // We detect a directory that already contains ProjectLayout_export_*.json or ScriptToObjects_index_*.json/.md
        // Starting from project root, search a few common locations, then fallback to project root / ChatGPT_Exports.

        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var candidates = new List<string>
        {
            projectRoot,
            Path.Combine(projectRoot, "Exports"),
            Path.Combine(projectRoot, "Export"),
            Path.Combine(projectRoot, "ChatGPT_Exports"),
            Path.Combine(projectRoot, "Diagnostics"),
            Path.Combine(projectRoot, "Tools"),
            Path.Combine(projectRoot, "Temp"),
            Path.Combine(projectRoot, "Logs"),
        };

        // Also search within project root, shallow.
        try
        {
            var shallowDirs = Directory.GetDirectories(projectRoot, "*", SearchOption.TopDirectoryOnly);
            candidates.AddRange(shallowDirs);
        }
        catch { }

        foreach (var dir in candidates.Distinct())
        {
            if (string.IsNullOrEmpty(dir)) continue;
            if (!Directory.Exists(dir)) continue;

            if (Directory.GetFiles(dir, "ProjectLayout_export_*.json", SearchOption.TopDirectoryOnly).Length > 0)
                return dir;

            if (Directory.GetFiles(dir, "ScriptToObjects_index_*.json", SearchOption.TopDirectoryOnly).Length > 0)
                return dir;

            if (Directory.GetFiles(dir, "ScriptToObjects_index_*.md", SearchOption.TopDirectoryOnly).Length > 0)
                return dir;
        }

        // Deep-ish search for any matching files (bounded)
        try
        {
            var hits = Directory.GetFiles(projectRoot, "ProjectLayout_export_*.json", SearchOption.AllDirectories);
            if (hits != null && hits.Length > 0)
                return Path.GetDirectoryName(hits.OrderByDescending(File.GetLastWriteTimeUtc).First());

            hits = Directory.GetFiles(projectRoot, "ScriptToObjects_index_*.json", SearchOption.AllDirectories);
            if (hits != null && hits.Length > 0)
                return Path.GetDirectoryName(hits.OrderByDescending(File.GetLastWriteTimeUtc).First());

            hits = Directory.GetFiles(projectRoot, "ScriptToObjects_index_*.md", SearchOption.AllDirectories);
            if (hits != null && hits.Length > 0)
                return Path.GetDirectoryName(hits.OrderByDescending(File.GetLastWriteTimeUtc).First());
        }
        catch { }

        // Fallback
        return Path.Combine(projectRoot, "ChatGPT_Exports");
    }

    private static void EnsureDir(string dir)
    {
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    private static string SanitizeFileName(string s)
    {
        if (string.IsNullOrEmpty(s)) return "Scene";
        foreach (char c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s;
    }

    // =========================
    // Scene selection / loading
    // =========================

    private static List<string> GetScenePathsToExport(ExportMode mode)
    {
        var list = new List<string>();

        if (mode == ExportMode.ActiveSceneOnly)
        {
            var a = SceneManager.GetActiveScene();
            if (a.IsValid() && !string.IsNullOrEmpty(a.path))
                list.Add(a.path);
            return list;
        }

        if (mode == ExportMode.OpenScenesOnly)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.IsValid() && !string.IsNullOrEmpty(s.path))
                    list.Add(s.path);
            }
            return list.Distinct().ToList();
        }

        // FullProject: export all enabled build settings scenes if possible; else open scenes.
        var buildScenes = EditorBuildSettings.scenes;
        if (buildScenes != null && buildScenes.Length > 0)
        {
            foreach (var bs in buildScenes)
            {
                if (bs != null && bs.enabled && !string.IsNullOrEmpty(bs.path))
                    list.Add(bs.path);
            }
        }

        if (list.Count == 0)
        {
            // fallback to open scenes
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.IsValid() && !string.IsNullOrEmpty(s.path))
                    list.Add(s.path);
            }
        }

        return list.Distinct().ToList();
    }

    private static Scene EnsureSceneLoaded(string scenePath, out bool loadedAdditively)
    {
        loadedAdditively = false;

        // If already loaded, return it.
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            if (s.IsValid() && s.path == scenePath)
                return s;
        }

        // Load additively to avoid stomping current user context.
        try
        {
            var s = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            loadedAdditively = true;
            return s;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ChatGPTDiagnostics] Failed to load scene '{scenePath}': {ex.Message}");
            return default;
        }
    }

    // =========================
    // Build Settings scenes dump
    // =========================

    [Serializable]
    private class BuildSettingsScenesDump
    {
        public string unityVersion;
        public string generatedUtc;
        public List<BuildSceneEntry> scenes = new List<BuildSceneEntry>();
    }

    [Serializable]
    private class BuildSceneEntry
    {
        public string path;
        public bool enabled;
        public int buildIndex;
    }

    private static BuildSettingsScenesDump BuildBuildSettingsScenesDump()
    {
        var dump = new BuildSettingsScenesDump
        {
            unityVersion = Application.unityVersion,
            generatedUtc = DateTime.UtcNow.ToString("o"),
        };

        var scenes = EditorBuildSettings.scenes;
        if (scenes != null)
        {
            for (int i = 0; i < scenes.Length; i++)
            {
                dump.scenes.Add(new BuildSceneEntry
                {
                    path = scenes[i].path,
                    enabled = scenes[i].enabled,
                    buildIndex = i
                });
            }
        }

        return dump;
    }

    // =========================
    // Script execution order dump
    // =========================

    [Serializable]
    private class ScriptExecutionOrderDump
    {
        public string unityVersion;
        public string generatedUtc;
        public List<ScriptOrderEntry> entries = new List<ScriptOrderEntry>();
    }

    [Serializable]
    private class ScriptOrderEntry
    {
        public string scriptName;
        public string assetPath;
        public int order;
    }

    private static ScriptExecutionOrderDump BuildScriptExecutionOrderDump()
    {
        var dump = new ScriptExecutionOrderDump
        {
            unityVersion = Application.unityVersion,
            generatedUtc = DateTime.UtcNow.ToString("o")
        };

        var scripts = MonoImporter.GetAllRuntimeMonoScripts();
        foreach (var s in scripts)
        {
            if (s == null) continue;
            int order = MonoImporter.GetExecutionOrder(s);
            if (order == 0) continue;

            dump.entries.Add(new ScriptOrderEntry
            {
                scriptName = s.name,
                assetPath = AssetDatabase.GetAssetPath(s),
                order = order
            });
        }

        dump.entries = dump.entries.OrderBy(e => e.order).ThenBy(e => e.scriptName).ToList();
        return dump;
    }

    // =========================
    // Packages manifest dump
    // =========================

    [Serializable]
    private class PackagesManifestDump
    {
        public string unityVersion;
        public string generatedUtc;
        public string projectRoot;
        public string packagesManifestPath;
        public Dictionary<string, string> dependencies = new Dictionary<string, string>();
    }

    private struct ManifestWriteResult
    {
        public string summaryPath;
        public string rawPath;
    }

    private static ManifestWriteResult BuildPackagesManifestDump(string stamp, string exportDir)
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");

        var dump = new PackagesManifestDump
        {
            unityVersion = Application.unityVersion,
            generatedUtc = DateTime.UtcNow.ToString("o"),
            projectRoot = projectRoot,
            packagesManifestPath = manifestPath
        };

        string rawPath = null;

        if (File.Exists(manifestPath))
        {
            try
            {
                string raw = File.ReadAllText(manifestPath, Encoding.UTF8);
                rawPath = Path.Combine(exportDir, $"manifest_raw_{stamp}.json");
                File.WriteAllText(rawPath, raw, Encoding.UTF8);

                // Parse minimal dependencies
                var data = ParseJson(raw);
                if (data != null && data.TryGetValue("dependencies", out object depsObj) && depsObj is Dictionary<string, object> depsDict)
                {
                    foreach (var kvp in depsDict)
                    {
                        dump.dependencies[kvp.Key] = kvp.Value != null ? kvp.Value.ToString() : "";
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ChatGPTDiagnostics] Failed to read/parse manifest.json: {ex.Message}");
            }
        }

        string summaryPath = Path.Combine(exportDir, $"PackagesManifest_{stamp}.json");
        WriteJson(summaryPath, dump);

        return new ManifestWriteResult { summaryPath = summaryPath, rawPath = rawPath };
    }

    // =========================
    // Addressables summary (optional)
    // =========================

    [Serializable]
    private class AddressablesSummaryDump
    {
        public string unityVersion;
        public string generatedUtc;
        public bool addressablesPresent;
        public List<AddressablesGroupEntry> groups = new List<AddressablesGroupEntry>();
    }

    [Serializable]
    private class AddressablesGroupEntry
    {
        public string groupName;
        public int entryCount;
    }

    private static string TryWriteAddressablesSummary(string exportDir, string stamp)
    {
        try
        {
            // Try load Addressables types via reflection so this compiles without Addressables installed.
            var settingsType = Type.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetSettings, Unity.Addressables.Editor");
            if (settingsType == null)
                return null;

            var settings = GetAddressablesSettings(settingsType);
            var dump = new AddressablesSummaryDump
            {
                unityVersion = Application.unityVersion,
                generatedUtc = DateTime.UtcNow.ToString("o"),
                addressablesPresent = true
            };

            if (settings != null)
            {
                var groupsProp = settingsType.GetProperty("groups", BindingFlags.Instance | BindingFlags.Public);
                var groups = groupsProp?.GetValue(settings) as IEnumerable;
                if (groups != null)
                {
                    foreach (var g in groups)
                    {
                        if (g == null) continue;
                        var gType = g.GetType();
                        string name = (string)gType.GetProperty("Name", BindingFlags.Instance | BindingFlags.Public)?.GetValue(g) ?? gType.Name;

                        // entries
                        int count = 0;
                        var entriesProp = gType.GetProperty("entries", BindingFlags.Instance | BindingFlags.Public);
                        var entries = entriesProp?.GetValue(g) as IEnumerable;
                        if (entries != null)
                        {
                            foreach (var _ in entries) count++;
                        }

                        dump.groups.Add(new AddressablesGroupEntry { groupName = name, entryCount = count });
                    }
                }
            }

            string path = Path.Combine(exportDir, $"AddressablesSummary_{stamp}.json");
            WriteJson(path, dump);
            return path;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ChatGPTDiagnostics] Addressables summary skipped: {ex.Message}");
            return null;
        }
    }

    private static object GetAddressablesSettings(Type settingsType)
    {
        // AddressableAssetSettingsDefaultObject.Settings
        var defObjType = Type.GetType("UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject, Unity.Addressables.Editor");
        if (defObjType == null) return null;

        var settingsProp = defObjType.GetProperty("Settings", BindingFlags.Static | BindingFlags.Public);
        return settingsProp?.GetValue(null);
    }

    // =========================
    // Existing exporters integration (best-effort)
    // =========================

    private static List<string> TryRunExistingExporters(string exportDir)
    {
        var produced = new List<string>();

        // Look for methods that smell like your existing exporters:
        // - method names containing "Export" and "ProjectLayout" or "ScriptToObjects"
        // - optionally accept a string directory parameter
        // This is best-effort; if you provide explicit class/method names, we can hard-wire.

        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                if (asm == null) continue;
                string an = asm.GetName().Name;
                if (an.StartsWith("Unity") || an.StartsWith("System") || an.StartsWith("mscorlib"))
                    continue;

                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t == null) continue;
                    if (!t.IsClass) continue;

                    var methods = t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    foreach (var m in methods)
                    {
                        if (m == null) continue;
                        string n = m.Name ?? "";
                        string tn = t.FullName ?? "";

                        bool looksRelevant =
                            (n.IndexOf("Export", StringComparison.OrdinalIgnoreCase) >= 0) &&
                            (tn.IndexOf("ProjectLayout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             tn.IndexOf("ScriptToObjects", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             n.IndexOf("ProjectLayout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             n.IndexOf("ScriptToObjects", StringComparison.OrdinalIgnoreCase) >= 0);

                        if (!looksRelevant) continue;

                        var ps = m.GetParameters();
                        if (ps.Length == 0)
                        {
                            TryInvokeExporter(m, null);
                            produced.Add($"(invoked exporter) {tn}.{n}()");
                        }
                        else if (ps.Length == 1 && ps[0].ParameterType == typeof(string))
                        {
                            TryInvokeExporter(m, new object[] { exportDir });
                            produced.Add($"(invoked exporter) {tn}.{n}(\"{exportDir}\")");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ChatGPTDiagnostics] Existing exporter integration encountered an issue: {ex.Message}");
        }

        return produced;
    }

    private static void TryInvokeExporter(MethodInfo m, object[] args)
    {
        try
        {
            m.Invoke(null, args);
        }
        catch (TargetInvocationException tie)
        {
            Debug.LogWarning($"[ChatGPTDiagnostics] Exporter threw: {tie.InnerException?.Message ?? tie.Message}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ChatGPTDiagnostics] Exporter invoke failed: {ex.Message}");
        }
    }

    // =========================
    // Scene Deep Snapshot
    // =========================

    [Serializable]
    private class SceneDeepSnapshotDump
    {
        public string unityVersion;
        public string generatedUtc;
        public string scenePath;
        public string sceneName;

        public List<GameObjectDump> gameObjects = new List<GameObjectDump>();
    }

    [Serializable]
    private class GameObjectDump
    {
        public string path;
        public bool activeSelf;
        public int layer;
        public string tag;
        public int siblingIndex;

        public bool isPrefabInstanceRoot;
        public string prefabAssetPath;

        public List<ComponentDump> components = new List<ComponentDump>();
        public List<string> children = new List<string>();
    }

    [Serializable]
    private class ComponentDump
    {
        public string type;
        public string assemblyQualifiedType;
        public Dictionary<string, object> fields = new Dictionary<string, object>();
    }

    private static SceneDeepSnapshotDump BuildSceneDeepSnapshot(Scene scene)
    {
        var dump = new SceneDeepSnapshotDump
        {
            unityVersion = Application.unityVersion,
            generatedUtc = DateTime.UtcNow.ToString("o"),
            scenePath = scene.path,
            sceneName = scene.name
        };

        var roots = scene.GetRootGameObjects();
        foreach (var root in roots)
        {
            WalkGO(root, dump);
        }

        return dump;
    }

    private static void WalkGO(GameObject go, SceneDeepSnapshotDump dump)
    {
        string path = GetGameObjectPath(go.transform);

        var god = new GameObjectDump
        {
            path = path,
            activeSelf = go.activeSelf,
            layer = go.layer,
            tag = SafeTag(go),
            siblingIndex = go.transform.GetSiblingIndex(),
        };

        // Prefab instance info
        try
        {
            if (PrefabUtility.IsAnyPrefabInstanceRoot(go))
            {
                god.isPrefabInstanceRoot = true;
                var asset = PrefabUtility.GetCorrespondingObjectFromSource(go);
                god.prefabAssetPath = asset != null ? AssetDatabase.GetAssetPath(asset) : null;
            }
        }
        catch { }

        // Components
        var comps = go.GetComponents<Component>();
        foreach (var c in comps)
        {
            if (c == null)
            {
                god.components.Add(new ComponentDump
                {
                    type = "MissingComponent",
                    assemblyQualifiedType = "MissingComponent",
                    fields = new Dictionary<string, object> { { "warning", "Component reference is null (missing script?)" } }
                });
                continue;
            }

            god.components.Add(DumpComponent(c));
        }

        // Children paths
        for (int i = 0; i < go.transform.childCount; i++)
        {
            var ch = go.transform.GetChild(i);
            if (ch != null)
                god.children.Add(GetGameObjectPath(ch));
        }

        dump.gameObjects.Add(god);

        // Recurse
        for (int i = 0; i < go.transform.childCount; i++)
        {
            var ch = go.transform.GetChild(i);
            if (ch != null)
                WalkGO(ch.gameObject, dump);
        }
    }

    private static string SafeTag(GameObject go)
    {
        try { return go.tag; }
        catch { return "Untagged"; }
    }

    private static string GetGameObjectPath(Transform t)
    {
        if (t == null) return "<null>";
        var sb = new StringBuilder();
        BuildPath(t, sb);
        return sb.ToString();
    }

    private static void BuildPath(Transform t, StringBuilder sb)
    {
        if (t.parent != null)
        {
            BuildPath(t.parent, sb);
            sb.Append("/");
        }
        sb.Append(t.name);
    }

    private static ComponentDump DumpComponent(Component c)
    {
        var cd = new ComponentDump();
        var t = c.GetType();
        cd.type = t.Name;
        cd.assemblyQualifiedType = t.AssemblyQualifiedName;

        var fields = new Dictionary<string, object>();

        // Use SerializedObject to traverse serialized fields
        try
        {
            var so = new SerializedObject(c);
            var it = so.GetIterator();
            bool enterChildren = true;

            int arrayExpansionCap = 200;

            while (it.NextVisible(enterChildren))
            {
                enterChildren = false;

                string propPath = it.propertyPath;
                object val = ExtractSerializedPropertyValue(it, arrayExpansionCap);
                fields[propPath] = val;
            }
        }
        catch (Exception ex)
        {
            fields["__error"] = $"Failed dumping component fields: {ex.Message}";
        }

        cd.fields = fields;
        return cd;
    }

    private static object ExtractSerializedPropertyValue(SerializedProperty p, int arrayExpansionCap)
    {
        if (p == null) return null;

        try
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return p.intValue;
                case SerializedPropertyType.Boolean:
                    return p.boolValue;
                case SerializedPropertyType.Float:
                    return p.floatValue;
                case SerializedPropertyType.String:
                    return p.stringValue;
                case SerializedPropertyType.Color:
                    return new Dictionary<string, object> {
                        { "r", p.colorValue.r }, { "g", p.colorValue.g }, { "b", p.colorValue.b }, { "a", p.colorValue.a }
                    };
                case SerializedPropertyType.ObjectReference:
                    return DumpObjectReference(p.objectReferenceValue);
                case SerializedPropertyType.LayerMask:
                    return p.intValue;
                case SerializedPropertyType.Enum:
                    return new Dictionary<string, object> {
                        { "index", p.enumValueIndex },
                        { "displayName", SafeEnumDisplayName(p) }
                    };
                case SerializedPropertyType.Vector2:
                    return new Dictionary<string, object> { { "x", p.vector2Value.x }, { "y", p.vector2Value.y } };
                case SerializedPropertyType.Vector3:
                    return new Dictionary<string, object> { { "x", p.vector3Value.x }, { "y", p.vector3Value.y }, { "z", p.vector3Value.z } };
                case SerializedPropertyType.Vector4:
                    return new Dictionary<string, object> { { "x", p.vector4Value.x }, { "y", p.vector4Value.y }, { "z", p.vector4Value.z }, { "w", p.vector4Value.w } };
                case SerializedPropertyType.Rect:
                    return new Dictionary<string, object> {
                        { "x", p.rectValue.x }, { "y", p.rectValue.y }, { "w", p.rectValue.width }, { "h", p.rectValue.height }
                    };
                case SerializedPropertyType.ArraySize:
                    return p.arraySize;
                case SerializedPropertyType.Character:
                    return p.intValue;
                case SerializedPropertyType.AnimationCurve:
                    return "(AnimationCurve)";
                case SerializedPropertyType.Bounds:
                    var b = p.boundsValue;
                    return new Dictionary<string, object> {
                        { "center", new Dictionary<string, object>{{"x", b.center.x},{"y", b.center.y},{"z", b.center.z}} },
                        { "extents", new Dictionary<string, object>{{"x", b.extents.x},{"y", b.extents.y},{"z", b.extents.z}} }
                    };
                case SerializedPropertyType.Gradient:
                    return "(Gradient)";
                case SerializedPropertyType.Quaternion:
                    var q = p.quaternionValue;
                    return new Dictionary<string, object> { { "x", q.x }, { "y", q.y }, { "z", q.z }, { "w", q.w } };
                case SerializedPropertyType.ExposedReference:
                    return "(ExposedReference)";
                case SerializedPropertyType.FixedBufferSize:
                    return p.fixedBufferSize;
                case SerializedPropertyType.Vector2Int:
                    return new Dictionary<string, object> { { "x", p.vector2IntValue.x }, { "y", p.vector2IntValue.y } };
                case SerializedPropertyType.Vector3Int:
                    return new Dictionary<string, object> { { "x", p.vector3IntValue.x }, { "y", p.vector3IntValue.y }, { "z", p.vector3IntValue.z } };
                case SerializedPropertyType.RectInt:
                    var ri = p.rectIntValue;
                    return new Dictionary<string, object> { { "x", ri.x }, { "y", ri.y }, { "w", ri.width }, { "h", ri.height } };
                case SerializedPropertyType.BoundsInt:
                    var bi = p.boundsIntValue;
                    return new Dictionary<string, object> {
                        { "position", new Dictionary<string, object>{{"x", bi.position.x},{"y", bi.position.y},{"z", bi.position.z}} },
                        { "size", new Dictionary<string, object>{{"x", bi.size.x},{"y", bi.size.y},{"z", bi.size.z}} }
                    };

                case SerializedPropertyType.ManagedReference:
                    return new Dictionary<string, object> {
                        { "managedReferenceFullTypename", p.managedReferenceFullTypename },
                        { "managedReferenceId", p.managedReferenceId }
                    };

                case SerializedPropertyType.Generic:
                default:
                    if (p.isArray && p.propertyType != SerializedPropertyType.String)
                    {
                        int n = p.arraySize;
                        if (n > arrayExpansionCap)
                        {
                            return new Dictionary<string, object> {
                                { "arraySize", n },
                                { "note", $"Array too large to expand (cap={arrayExpansionCap})" }
                            };
                        }

                        var arr = new List<object>(n);
                        for (int i = 0; i < n; i++)
                        {
                            var el = p.GetArrayElementAtIndex(i);
                            arr.Add(ExtractSerializedPropertyValue(el, arrayExpansionCap));
                        }
                        return arr;
                    }

                    // For generic structs/classes: provide a brief marker
                    return "(Generic)";
            }
        }
        catch (Exception ex)
        {
            return new Dictionary<string, object> { { "__error", ex.Message } };
        }
    }

    private static object DumpObjectReference(UnityEngine.Object obj)
    {
        if (obj == null) return null;

        var dict = new Dictionary<string, object>();
        dict["name"] = obj.name;
        dict["type"] = obj.GetType().FullName;

        string assetPath = AssetDatabase.GetAssetPath(obj);
        if (!string.IsNullOrEmpty(assetPath))
        {
            dict["assetPath"] = assetPath;
        }
        else
        {
            // Scene object
            if (obj is Component comp)
                dict["scenePath"] = GetGameObjectPath(comp.transform);
            else if (obj is GameObject go)
                dict["scenePath"] = GetGameObjectPath(go.transform);
        }

        return dict;
    }

    private static string SafeEnumDisplayName(SerializedProperty p)
    {
        try
        {
            if (p == null) return null;
            int i = p.enumValueIndex;
            var names = p.enumDisplayNames;
            if (names != null && i >= 0 && i < names.Length)
                return names[i];
        }
        catch { }
        return null;
    }

    // =========================
    // Prefab overrides dump
    // =========================

    [Serializable]
    private class PrefabOverridesDump
    {
        public string unityVersion;
        public string generatedUtc;
        public string scenePath;
        public string sceneName;
        public List<PrefabOverrideEntry> prefabInstanceRoots = new List<PrefabOverrideEntry>();
    }

    [Serializable]
    private class PrefabOverrideEntry
    {
        public string instanceRootPath;
        public string prefabAssetPath;
        public List<PrefabPropertyModification> modifications = new List<PrefabPropertyModification>();
    }

    [Serializable]
    private class PrefabPropertyModification
    {
        public string targetPath;
        public string propertyPath;
        public string value;
        public string objectReference;
    }

    private static PrefabOverridesDump BuildPrefabOverrides(Scene scene)
    {
        var dump = new PrefabOverridesDump
        {
            unityVersion = Application.unityVersion,
            generatedUtc = DateTime.UtcNow.ToString("o"),
            scenePath = scene.path,
            sceneName = scene.name
        };

        var roots = scene.GetRootGameObjects();
        foreach (var root in roots)
        {
            GatherPrefabInstanceRoots(root, dump);
        }

        return dump;
    }

    private static void GatherPrefabInstanceRoots(GameObject go, PrefabOverridesDump dump)
    {
        if (go == null) return;

        try
        {
            if (PrefabUtility.IsAnyPrefabInstanceRoot(go))
            {
                var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
                string prefabPath = src != null ? AssetDatabase.GetAssetPath(src) : null;

                var entry = new PrefabOverrideEntry
                {
                    instanceRootPath = GetGameObjectPath(go.transform),
                    prefabAssetPath = prefabPath
                };

                var mods = PrefabUtility.GetPropertyModifications(go);
                if (mods != null)
                {
                    foreach (var m in mods)
                    {
                        if (m == null) continue;

                        string targetPath = null;
                        if (m.target is Component c)
                            targetPath = GetGameObjectPath(c.transform);
                        else if (m.target is GameObject g)
                            targetPath = GetGameObjectPath(g.transform);

                        entry.modifications.Add(new PrefabPropertyModification
                        {
                            targetPath = targetPath,
                            propertyPath = m.propertyPath,
                            value = m.value,
                            objectReference = m.objectReference != null ? $"{m.objectReference.GetType().Name}:{m.objectReference.name}:{AssetDatabase.GetAssetPath(m.objectReference)}" : null
                        });
                    }
                }

                dump.prefabInstanceRoots.Add(entry);
            }
        }
        catch { }

        for (int i = 0; i < go.transform.childCount; i++)
        {
            var ch = go.transform.GetChild(i);
            if (ch != null)
                GatherPrefabInstanceRoots(ch.gameObject, dump);
        }
    }

    // =========================
    // JSON writing
    // =========================

    private static string WriteJson<T>(string path, T obj)
    {
        try
        {
            string json = EditorJsonUtility.ToJson(obj, prettyPrint: true);
            File.WriteAllText(path, json, Encoding.UTF8);
            return path;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ChatGPTDiagnostics] Failed to write json '{path}': {ex.Message}");
            return path;
        }
    }

    // =========================
    // Index markdown
    // =========================

    private static string BuildIndexMarkdown(string exportDir, string stamp, List<string> producedFiles)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# ChatGPT Project Diagnostics Export");
        sb.AppendLine();
        sb.AppendLine($"- Generated: `{DateTime.Now:O}`");
        sb.AppendLine($"- Unity: `{Application.unityVersion}`");
        sb.AppendLine($"- Output Dir: `{exportDir}`");
        sb.AppendLine();

        sb.AppendLine("## Files");
        sb.AppendLine();

        foreach (var f in producedFiles)
        {
            if (string.IsNullOrEmpty(f)) continue;
            string rel = f.Replace(exportDir, "").TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            sb.AppendLine($"- `{rel}`");
        }

        sb.AppendLine();
        sb.AppendLine("## Notes");
        sb.AppendLine();
        sb.AppendLine("- SceneDeepSnapshot files include all visible serialized properties per component (via SerializedObject).");
        sb.AppendLine("- Arrays larger than the expansion cap are summarized instead of fully expanded.");
        sb.AppendLine("- Addressables summary is included only if Addressables editor assembly is present.");
        sb.AppendLine("- Existing exporters are invoked best-effort via reflection; for guaranteed calls, provide explicit class/method names.");

        return sb.ToString();
    }

    // =========================
    // Minimal JSON parsing helpers
    // (No external dependency; supports the specific manifest.json shape.)
    // =========================

    // NOTE: This is intentionally small and permissive; it's for manifest dependencies only.

    private static Dictionary<string, object> ParseJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;

        try
        {
            // Wrap to allow JsonUtility to parse top-level dictionary-like structures
            // using a serializable wrapper class.
            var root = MiniJson.Deserialize(json) as Dictionary<string, object>;
            return root;
        }
        catch
        {
            return null;
        }
    }

    // =========================
    // MiniJson (Unity-style) - minimal embedded
    // =========================

    private static class MiniJson
    {
        // Based on Unity MiniJSON pattern, simplified.

        public static object Deserialize(string json)
        {
            if (json == null) return null;
            return Parser.Parse(json);
        }

        private sealed class Parser : IDisposable
        {
            private const string WORD_BREAK = "{}[],:\"";

            private StringReader json;

            private Parser(string jsonString)
            {
                json = new StringReader(jsonString);
            }

            public static object Parse(string jsonString)
            {
                using (var instance = new Parser(jsonString))
                {
                    return instance.ParseValue();
                }
            }

            public void Dispose()
            {
                json.Dispose();
                json = null;
            }

            private Dictionary<string, object> ParseObject()
            {
                var table = new Dictionary<string, object>();

                // {
                json.Read();

                while (true)
                {
                    switch (NextToken)
                    {
                        case TOKEN.NONE:
                            return null;
                        case TOKEN.CURLY_CLOSE:
                            json.Read();
                            return table;
                        default:
                            // key
                            string name = ParseString();
                            if (name == null) return null;

                            // :
                            if (NextToken != TOKEN.COLON) return null;
                            json.Read();

                            // value
                            table[name] = ParseValue();
                            break;
                    }

                    switch (NextToken)
                    {
                        case TOKEN.COMMA:
                            json.Read();
                            continue;
                        case TOKEN.CURLY_CLOSE:
                            json.Read();
                            return table;
                        default:
                            return null;
                    }
                }
            }

            private List<object> ParseArray()
            {
                var array = new List<object>();

                // [
                json.Read();

                var parsing = true;
                while (parsing)
                {
                    TOKEN nextToken = NextToken;

                    switch (nextToken)
                    {
                        case TOKEN.NONE:
                            return null;
                        case TOKEN.SQUARE_CLOSE:
                            json.Read();
                            return array;
                        case TOKEN.COMMA:
                            json.Read();
                            break;
                        default:
                            object value = ParseValue();
                            array.Add(value);
                            break;
                    }
                }

                return array;
            }

            private object ParseValue()
            {
                switch (NextToken)
                {
                    case TOKEN.STRING:
                        return ParseString();
                    case TOKEN.NUMBER:
                        return ParseNumber();
                    case TOKEN.CURLY_OPEN:
                        return ParseObject();
                    case TOKEN.SQUARE_OPEN:
                        return ParseArray();
                    case TOKEN.TRUE:
                        json.Read(); json.Read(); json.Read(); json.Read();
                        return true;
                    case TOKEN.FALSE:
                        json.Read(); json.Read(); json.Read(); json.Read(); json.Read();
                        return false;
                    case TOKEN.NULL:
                        json.Read(); json.Read(); json.Read(); json.Read();
                        return null;
                    default:
                        return null;
                }
            }

            private string ParseString()
            {
                var s = new StringBuilder();
                char c;

                // "
                json.Read();

                bool parsing = true;
                while (parsing)
                {
                    if (json.Peek() == -1) break;

                    c = NextChar;
                    switch (c)
                    {
                        case '"':
                            parsing = false;
                            break;
                        case '\\':
                            if (json.Peek() == -1) parsing = false;
                            else
                            {
                                c = NextChar;
                                switch (c)
                                {
                                    case '"': s.Append('"'); break;
                                    case '\\': s.Append('\\'); break;
                                    case '/': s.Append('/'); break;
                                    case 'b': s.Append('\b'); break;
                                    case 'f': s.Append('\f'); break;
                                    case 'n': s.Append('\n'); break;
                                    case 'r': s.Append('\r'); break;
                                    case 't': s.Append('\t'); break;
                                    case 'u':
                                        var hex = new char[4];
                                        for (int i = 0; i < 4; i++)
                                            hex[i] = NextChar;
                                        s.Append((char)Convert.ToInt32(new string(hex), 16));
                                        break;
                                }
                            }
                            break;
                        default:
                            s.Append(c);
                            break;
                    }
                }

                return s.ToString();
            }

            private object ParseNumber()
            {
                string number = NextWord;

                if (number.IndexOf('.') == -1)
                {
                    if (long.TryParse(number, out long parsedInt))
                        return parsedInt;
                }

                if (double.TryParse(number, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedDouble))
                    return parsedDouble;

                return 0;
            }

            private void EatWhitespace()
            {
                while (char.IsWhiteSpace(PeekChar))
                {
                    json.Read();
                    if (json.Peek() == -1) break;
                }
            }

            private char PeekChar => Convert.ToChar(json.Peek());

            private char NextChar => Convert.ToChar(json.Read());

            private string NextWord
            {
                get
                {
                    var sb = new StringBuilder();
                    while (json.Peek() != -1 && !IsWordBreak(PeekChar))
                    {
                        sb.Append(NextChar);
                    }
                    return sb.ToString();
                }
            }

            private TOKEN NextToken
            {
                get
                {
                    EatWhitespace();
                    if (json.Peek() == -1) return TOKEN.NONE;

                    char c = PeekChar;
                    switch (c)
                    {
                        case '{': return TOKEN.CURLY_OPEN;
                        case '}': return TOKEN.CURLY_CLOSE;
                        case '[': return TOKEN.SQUARE_OPEN;
                        case ']': return TOKEN.SQUARE_CLOSE;
                        case ',': return TOKEN.COMMA;
                        case '"': return TOKEN.STRING;
                        case ':': return TOKEN.COLON;
                        case '0':
                        case '1':
                        case '2':
                        case '3':
                        case '4':
                        case '5':
                        case '6':
                        case '7':
                        case '8':
                        case '9':
                        case '-': return TOKEN.NUMBER;
                    }

                    string word = NextWord;

                    switch (word)
                    {
                        case "false": return TOKEN.FALSE;
                        case "true": return TOKEN.TRUE;
                        case "null": return TOKEN.NULL;
                    }

                    return TOKEN.NONE;
                }
            }

            private static bool IsWordBreak(char c)
            {
                return char.IsWhiteSpace(c) || WORD_BREAK.IndexOf(c) != -1;
            }

            private enum TOKEN
            {
                NONE,
                CURLY_OPEN,
                CURLY_CLOSE,
                SQUARE_OPEN,
                SQUARE_CLOSE,
                COLON,
                COMMA,
                STRING,
                NUMBER,
                TRUE,
                FALSE,
                NULL
            }
        }
    }

    // =========================
    // Wrapper JSON helper
    // =========================

    [Serializable]
    private class Wrapper
    {
        public string data;
    }

    private static string WrapJson(string json)
    {
        // Make a JSON payload compatible with JsonUtility
        // by wrapping raw JSON string as a field.
        return "{\"data\":" + EscapeAsJsonString(json) + "}";
    }

    private static string EscapeAsJsonString(string s)
    {
        if (s == null) return "null";
        return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    // =========================
    // The specific fix you hit:
    // EditorJsonUtility.FromJson<T> DOES NOT EXIST.
    // Use JsonUtility.FromJson<T> instead.
    // =========================

    private static string UnwrapJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;

        try
        {
            // FIX: JsonUtility has FromJson<T>, EditorJsonUtility does not.
            return JsonUtility.FromJson<Wrapper>(WrapJson(json))?.data;
        }
        catch
        {
            return null;
        }
    }
}
#endif
