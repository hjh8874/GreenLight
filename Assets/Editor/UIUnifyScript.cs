using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UIUnifyScript
{
    [MenuItem("Tools/Unify Polished UI Prefab")]
    public static void Execute()
    {
        string prefabPath = "Assets/02_Prefabs/UI/UI_MainCanvas_Polished.prefab";
        using (var editingScope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
        {
            var root = editingScope.prefabContentsRoot;
            
            // 1. Unify Fonts
            var defaultFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/99_Download/Fonts/NanumGothic SDF.asset");
            if (defaultFont != null)
            {
                var allTexts = root.GetComponentsInChildren<TextMeshProUGUI>(true);
                foreach (var t in allTexts)
                {
                    if (t != null && !t.Equals(null)) t.font = defaultFont;
                }
                Debug.Log($"Unified font on {allTexts.Length} TextMeshProUGUI components.");
            }
            else { Debug.LogError("Could not find NanumGothic SDF"); }

            // 2. Find Floating Panel
            Transform floatingPanel = FindRecursive(root.transform, "Floating_Panel");
            Transform topRightDock = FindRecursive(root.transform, "Dock_TopRight");

            if (topRightDock == null)
            {
                var dockObj = new GameObject("Dock_TopRight");
                topRightDock = dockObj.transform;
                topRightDock.SetParent(root.transform, false);
                var rect = dockObj.AddComponent<RectTransform>();
                rect.anchorMin = new Vector2(1, 1);
                rect.anchorMax = new Vector2(1, 1);
                rect.pivot = new Vector2(1, 1);
                rect.anchoredPosition = new Vector2(-20, -20);
                
                var hl = dockObj.AddComponent<HorizontalLayoutGroup>();
                hl.childAlignment = TextAnchor.MiddleRight;
                hl.spacing = 10;
                hl.childControlWidth = false;
                hl.childControlHeight = false;
            }

            if (floatingPanel != null && topRightDock != null && !floatingPanel.Equals(null) && !topRightDock.Equals(null) && floatingPanel.parent != topRightDock)
            {
                floatingPanel.SetParent(topRightDock, false);
                Debug.Log("Moved Floating_Panel to Dock_TopRight.");
            }
            
            // 3. Create Camera Button
            if (topRightDock != null && !topRightDock.Equals(null) && topRightDock.Find("Camera_Button") == null)
            {
                var camBtnObj = new GameObject("Camera_Button");
                camBtnObj.transform.SetParent(topRightDock, false);
                camBtnObj.AddComponent<RectTransform>().sizeDelta = new Vector2(60, 60);
                camBtnObj.AddComponent<Image>();
                camBtnObj.AddComponent<Button>();
                Debug.Log("Created Camera_Button in Dock_TopRight.");
            }

            // 4. Auto-wire components using local and then global search
            int wiredCount = 0;
            var components = root.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var c in components)
            {
                if (c == null || c.Equals(null)) continue;
                
                // ONLY AUTO-WIRE CITYFLOW COMPONENTS TO AVOID UNITY BUILT-IN CIRCULAR REFERENCES
                if (c.GetType().Namespace == null || !c.GetType().Namespace.StartsWith("CityFlow")) continue;

                SerializedObject so = new SerializedObject(c);
                SerializedProperty sp = so.GetIterator();
                bool enterChildren = true;
                bool modified = false;

                while (sp.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (sp.propertyType == SerializedPropertyType.ObjectReference && sp.objectReferenceValue == null)
                    {
                        var fieldTypeString = sp.type.Replace("PPtr<$", "").Replace(">", "");
                        var searchType = System.Type.GetType(fieldTypeString + ", Assembly-CSharp");
                        if (searchType == null) searchType = System.Type.GetType(fieldTypeString + ", UnityEngine.UI");
                        if (searchType == null) searchType = typeof(UnityEngine.Component).Assembly.GetType("UnityEngine." + fieldTypeString);
                        if (searchType == null) searchType = typeof(UnityEngine.UI.Button).Assembly.GetType("UnityEngine.UI." + fieldTypeString);
                        if (searchType == null) searchType = typeof(TextMeshProUGUI).Assembly.GetType("TMPro." + fieldTypeString);

                        if (searchType != null && typeof(Component).IsAssignableFrom(searchType))
                        {
                            Component found = c.GetComponentInChildren(searchType, true);
                            if (found == null || found.Equals(null)) found = root.GetComponentInChildren(searchType, true);
                            if (found != null && !found.Equals(null))
                            {
                                sp.objectReferenceValue = found;
                                modified = true;
                                wiredCount++;
                            }
                        }
                    }
                }
                if (modified) so.ApplyModifiedProperties();
            }
            Debug.Log($"Auto-wired {wiredCount} missing references.");
        }
        Debug.Log("Prefab unification complete.");
    }

    static Transform FindRecursive(Transform parent, string name)
    {
        if (parent == null || parent.Equals(null)) return null;
        if (parent.name == name) return parent;
        foreach (Transform child in parent)
        {
            if (child == null || child.Equals(null)) continue;
            var found = FindRecursive(child, name);
            if (found != null && !found.Equals(null)) return found;
        }
        return null;
    }
}
