#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class ChatGPTExportTool
{
    [MenuItem("Tools/ChatGPT/Export Scripts Bundle (Zip)")]
    public static void ExportScriptsBundleZip()
    {
        // Write to the same export directory used by ProjectLayoutExporter
        string exportDir = ProjectLayoutExporter.GetExportDirectory();

        // Build output zip path (timestamped)
        string zipFileName = $"ChatGPT_ScriptsBundle_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
        string zipPath = Path.Combine(exportDir, zipFileName);

        // Gather scripts (Assets only by default)
        string[] guids = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" });

        // Build ScriptsDump.txt
        var dump = new StringBuilder(4 * 1024 * 1024);
        dump.AppendLine("=== ChatGPT Scripts Dump ===");
        dump.AppendLine($"UnityVersion: {Application.unityVersion}");
        dump.AppendLine($"ExportedAt: {DateTime.Now:O}");
        dump.AppendLine();

        var scriptEntries = new List<ScriptEntry>(guids.Length);

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                continue;

            string fullPath = Path.GetFullPath(path);
            string text = SafeReadAllText(fullPath);

            scriptEntries.Add(new ScriptEntry
            {
                guid = guid,
                path = path,
                length = text?.Length ?? 0
            });

            dump.AppendLine("////////////////////////////////////////////////////////////");
            dump.AppendLine($"// PATH: {path}");
            dump.AppendLine($"// GUID: {guid}");
            dump.AppendLine("////////////////////////////////////////////////////////////");
            dump.AppendLine(text ?? $"// [READ FAILED] Could not read: {fullPath}");
            dump.AppendLine();
        }

        // Build ProjectIndex.json (simple: asset path + main type name)
        string[] allAssetPaths = AssetDatabase.GetAllAssetPaths();
        var indexEntries = new List<ProjectIndexEntry>(allAssetPaths.Length);

        foreach (string p in allAssetPaths)
        {
            if (!p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                continue;

            Type mainType = AssetDatabase.GetMainAssetTypeAtPath(p);
            indexEntries.Add(new ProjectIndexEntry
            {
                path = p,
                type = mainType != null ? mainType.FullName : "Unknown"
            });
        }

        string projectIndexJson = JsonUtility.ToJson(new ProjectIndexRoot { entries = indexEntries }, true);

        // Write everything to a temp folder then zip
        string tempDir = Path.Combine(Path.GetTempPath(), "ChatGPTExport_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        string dumpPath = Path.Combine(tempDir, "ScriptsDump.txt");
        string indexPath = Path.Combine(tempDir, "ProjectIndex.json");

        File.WriteAllText(dumpPath, dump.ToString(), Encoding.UTF8);
        File.WriteAllText(indexPath, projectIndexJson, Encoding.UTF8);

        if (File.Exists(zipPath))
            File.Delete(zipPath);

        ZipFile.CreateFromDirectory(tempDir, zipPath);

        // Cleanup
        try { Directory.Delete(tempDir, true); } catch { /* ignore */ }

        // Reveal using absolute path for reliability
        EditorUtility.RevealInFinder(Path.GetFullPath(zipPath));
        Debug.Log($"ChatGPT scripts bundle exported: {zipPath}");
    }

    private static string SafeReadAllText(string path)
    {
        try
        {
            return File.ReadAllText(path, Encoding.UTF8);
        }
        catch
        {
            try { return File.ReadAllText(path); } catch { return null; }
        }
    }

    [Serializable]
    private class ScriptEntry
    {
        public string guid;
        public string path;
        public int length;
    }

    [Serializable]
    private class ProjectIndexEntry
    {
        public string path;
        public string type;
    }

    [Serializable]
    private class ProjectIndexRoot
    {
        public List<ProjectIndexEntry> entries;
    }
}
#endif
