// Assets/Editor/FindMissingScripts.cs
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public static class FindMissingScripts
{
    [MenuItem("Tools/Missing Scripts/Scan Scene")]
    public static void ScanActiveScene()
    {
        int goCount = 0, compCount = 0, missingCount = 0;
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            ScanGO(root, ref goCount, ref compCount, ref missingCount);
        }
        Debug.Log($"[Missing Scripts] Scene: GameObjects={goCount}, Components={compCount}, Missing={missingCount}");
    }

    [MenuItem("Tools/Missing Scripts/Scan Project (Prefabs)")]
    public static void ScanProjectPrefabs()
    {
        var guids = AssetDatabase.FindAssets("t:Prefab");
        int goCount = 0, compCount = 0, missingCount = 0;

        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!go) continue;

            int before = missingCount;
            ScanGO(go, ref goCount, ref compCount, ref missingCount, path);
            if (missingCount > before)
                Debug.LogWarning($"[Missing Scripts] ➜ {path}");
        }

        Debug.Log($"[Missing Scripts] Prefabs: GameObjects={goCount}, Components={compCount}, Missing={missingCount}");
    }

    [MenuItem("Tools/Missing Scripts/Remove Missing On Selected")]
    public static void RemoveMissingOnSelected()
    {
        foreach (var o in Selection.gameObjects)
        {
            int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(o);
            if (removed > 0)
                Debug.Log($"Removed {removed} missing script(s) on {o.name}");
        }
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    static void ScanGO(GameObject go, ref int goCount, ref int compCount, ref int missingCount, string contextPath = null)
    {
        goCount++;
        var comps = go.GetComponents<Component>();
        foreach (var c in comps)
        {
            compCount++;
            if (c == null)
            {
                missingCount++;
                var path = GetHierarchyPath(go);
                if (!string.IsNullOrEmpty(contextPath))
                    Debug.LogWarning($"Missing script on Prefab: {contextPath} -> {path}");
                else
                    Debug.LogWarning($"Missing script on Scene object: {path}");
            }
        }
        // enfants
        for (int i = 0; i < go.transform.childCount; i++)
        {
            ScanGO(go.transform.GetChild(i).gameObject, ref goCount, ref compCount, ref missingCount, contextPath);
        }
    }

    static string GetHierarchyPath(GameObject go)
    {
        var stack = new List<string>();
        var t = go.transform;
        while (t != null)
        {
            stack.Add(t.name);
            t = t.parent;
        }
        stack.Reverse();
        return string.Join("/", stack);
    }
}
