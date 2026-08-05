using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.IO;

public class UIDumpScript
{
    [MenuItem("Tools/Dump UI Structure")]
    public static void Execute()
    {
        var scene = EditorSceneManager.GetActiveScene();
        string report = "=== SCENE UIs ===\n";
        var roots = scene.GetRootGameObjects();
        foreach (var r in roots)
        {
            if (r.name.Contains("UI") || r.name.Contains("Canvas"))
            {
                report += DumpHierarchy(r.transform, "", true);
            }
        }
        
        File.WriteAllText("UIDump_Scene.txt", report);
        
        string prefabPath = "Assets/02_Prefabs/UI/UI_MainCanvas_Polished.prefab";
        using (var scope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
        {
            var pRoot = scope.prefabContentsRoot;
            string pReport = "=== PREFAB UI ===\n" + DumpHierarchy(pRoot.transform, "", true);
            File.WriteAllText("UIDump_Prefab.txt", pReport);
        }
        
        Debug.Log("Dumped UI structures.");
    }

    static string DumpHierarchy(Transform t, string indent, bool includeComponents)
    {
        string res = indent + t.name;
        if (includeComponents)
        {
            var comps = t.GetComponents<MonoBehaviour>();
            if (comps.Length > 0)
            {
                res += " [";
                foreach(var c in comps) {
                    if (c != null) res += c.GetType().Name + ", ";
                }
                res += "]";
            }
        }
        res += "\n";
        foreach(Transform child in t)
        {
            res += DumpHierarchy(child, indent + "  ", includeComponents);
        }
        return res;
    }
}
