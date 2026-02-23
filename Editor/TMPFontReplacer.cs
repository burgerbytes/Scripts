// Assets/Editor/TMPFontReplacer.cs
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

public class TMPFontReplacer : EditorWindow
{
    private TMP_FontAsset oldFont;
    private TMP_FontAsset newFont;
    private bool onlyReplaceMatching = true;

    [MenuItem("Tools/TextMeshPro/Replace Font In Project")]
    public static void ShowWindow()
    {
        GetWindow<TMPFontReplacer>("TMP Font Replacer");
    }

    private void OnGUI()
    {
        GUILayout.Space(10);
        GUILayout.Label("Replace TextMeshPro Fonts Project-Wide", EditorStyles.boldLabel);
        GUILayout.Space(10);

        newFont = (TMP_FontAsset)EditorGUILayout.ObjectField(
            "New Font (Required)",
            newFont,
            typeof(TMP_FontAsset),
            false);

        oldFont = (TMP_FontAsset)EditorGUILayout.ObjectField(
            "Old Font (Optional)",
            oldFont,
            typeof(TMP_FontAsset),
            false);

        onlyReplaceMatching = EditorGUILayout.ToggleLeft(
            "Only replace if font matches Old Font",
            onlyReplaceMatching);

        GUILayout.Space(15);

        GUI.enabled = newFont != null;

        if (GUILayout.Button("Replace In Prefabs + Scenes"))
        {
            ReplaceFonts();
        }

        GUI.enabled = true;
    }

    private void ReplaceFonts()
    {
        int changed = 0;

        // ---------- Prefabs ----------
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");

        foreach (string guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab) continue;

            bool dirty = false;
            var tmps = prefab.GetComponentsInChildren<TMP_Text>(true);

            foreach (var tmp in tmps)
            {
                if (onlyReplaceMatching && oldFont != null && tmp.font != oldFont)
                    continue;

                if (tmp.font == newFont)
                    continue;

                tmp.font = newFont;
                EditorUtility.SetDirty(tmp);
                dirty = true;
                changed++;
            }

            if (dirty)
                PrefabUtility.SavePrefabAsset(prefab);
        }

    // ---------- Scenes ----------
    string[] sceneGuids = AssetDatabase.FindAssets("t:Scene");

    var activeScene = EditorSceneManager.GetActiveScene();

    foreach (string guid in sceneGuids)
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);

        // Skip anything not in the project's Assets folder (Packages, Library, etc.)
        if (!path.StartsWith("Assets/"))
            continue;

        // Optional: skip any scene templates or editor-only scenes if you have them
        // if (path.Contains("/Editor/")) continue;

        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

        bool dirty = false;

        foreach (var root in scene.GetRootGameObjects())
        {
            var tmps = root.GetComponentsInChildren<TMP_Text>(true);

            foreach (var tmp in tmps)
            {
                if (onlyReplaceMatching && oldFont != null && tmp.font != oldFont)
                    continue;

                if (tmp.font == newFont)
                    continue;

                tmp.font = newFont;
                EditorUtility.SetDirty(tmp);
                dirty = true;
                changed++;
            }
        }

        if (dirty)
            EditorSceneManager.SaveScene(scene);

        EditorSceneManager.CloseScene(scene, true);
    }

    // Restore active scene (nice-to-have)
    if (activeScene.IsValid())
        EditorSceneManager.SetActiveScene(activeScene);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"TMP Font Replacer complete. Updated {changed} components.");
    }
}