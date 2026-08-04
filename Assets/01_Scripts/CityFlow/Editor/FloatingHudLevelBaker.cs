using CityFlow.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CityFlow.EditorTools
{
    public static class FloatingHudLevelBaker
    {
        private const string CanvasName = "UI_MainCanvas";
        private const string TopBarName = "HUD_TopBar";
        private const string MAnalysisName = "AnalysisCard_BottomLeft";
        private const string MSubPanelsName = "SubPanels_Right";
        private const string LBuildName = "Build_Panel";
        private const string LDockName = "Dock_Right";
        private const string DotName = "CongestionDot";

        [MenuItem("Tools/GreenLight/UI/Bake Floating HUD Level")]
        public static void Bake()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !activeScene.isLoaded)
            {
                Debug.LogWarning(
                    "[FloatingHudLevelBaker] Open a target scene before baking the UI.");
                return;
            }

            Transform canvas = FindTransform(activeScene, CanvasName);
            Transform topBar = FindTransform(activeScene, TopBarName);
            Transform mAnalysis = FindTransform(activeScene, MAnalysisName);
            Transform mSubPanels = FindTransform(activeScene, MSubPanelsName);
            Transform lBuild = FindTransform(activeScene, LBuildName);
            Transform lDock = FindTransform(activeScene, LDockName);

            if (canvas == null) { Missing(CanvasName, activeScene); return; }
            if (topBar == null) { Missing(TopBarName, activeScene); return; }
            if (mAnalysis == null) { Missing(MAnalysisName, activeScene); return; }
            if (mSubPanels == null) { Missing(MSubPanelsName, activeScene); return; }
            if (lBuild == null) { Missing(LBuildName, activeScene); return; }
            if (lDock == null) { Missing(LDockName, activeScene); return; }

            FloatingHudLevelController controller =
                canvas.GetComponent<FloatingHudLevelController>();
            if (controller == null)
            {
                controller = Undo.AddComponent<FloatingHudLevelController>(
                    canvas.gameObject);
            }
            else
            {
                Undo.RecordObject(controller, "Update Floating HUD Level Controller");
            }

            CanvasGroup minimal = topBar.GetComponent<CanvasGroup>();
            if (minimal == null)
            {
                minimal = Undo.AddComponent<CanvasGroup>(topBar.gameObject);
            }
            else
            {
                Undo.RecordObject(minimal, "Update Floating HUD Minimal Overlay");
            }

            controller.Configure(
                minimal,
                new[] { mAnalysis.gameObject, mSubPanels.gameObject },
                new[] { lBuild.gameObject, lDock.gameObject });
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(minimal);

            FloatingCongestionDot dot = BakeDot(topBar, activeScene);
            if (dot != null)
            {
                EditorUtility.SetDirty(dot);
            }

            EditorSceneManager.MarkSceneDirty(activeScene);
            Selection.activeGameObject = canvas.gameObject;
            EditorGUIUtility.PingObject(canvas.gameObject);
            Debug.Log(
                $"[FloatingHudLevelBaker] Floating HUD level baked/updated in " +
                $"scene '{activeScene.name}': controller, minimal overlay, " +
                "M/L level objects, and congestion dot.");
        }

        private static FloatingCongestionDot BakeDot(
            Transform topBar,
            Scene activeScene)
        {
            Transform existing = topBar.Find(DotName);
            GameObject dotObject;
            if (existing == null)
            {
                dotObject = CreateUiObject(DotName, topBar, typeof(Image));
                Undo.RegisterCreatedObjectUndo(dotObject, "Bake Floating HUD Congestion Dot");
            }
            else
            {
                dotObject = existing.gameObject;
                Undo.RecordObject(dotObject, "Update Floating HUD Congestion Dot");
            }

            dotObject.layer = topBar.gameObject.layer;
            RectTransform rect = dotObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(12f, -12f);
            rect.sizeDelta = new Vector2(16f, 16f);

            Image image = dotObject.GetComponent<Image>();
            if (image == null)
            {
                image = Undo.AddComponent<Image>(dotObject);
            }

            image.color = Color.green;
            image.raycastTarget = false;
            FloatingCongestionDot dot = dotObject.GetComponent<FloatingCongestionDot>();
            if (dot == null)
            {
                dot = Undo.AddComponent<FloatingCongestionDot>(dotObject);
            }

            dot.Configure(image);
            EditorUtility.SetDirty(dotObject);
            EditorUtility.SetDirty(image);
            return dot;
        }

        private static void Missing(string targetName, Scene activeScene)
        {
            Debug.LogError(
                $"[FloatingHudLevelBaker] '{targetName}' was not found in " +
                $"scene '{activeScene.name}'. Baking stopped.");
        }

        private static GameObject CreateUiObject(
            string name,
            Transform parent,
            params System.Type[] components)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            foreach (System.Type component in components)
            {
                gameObject.AddComponent(component);
            }

            return gameObject;
        }

        private static Transform FindTransform(Scene scene, string targetName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
                foreach (Transform candidate in transforms)
                {
                    if (candidate.name == targetName)
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        private static T FindInActiveScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }

        // Unity setup: Open the target scene, then use Tools > GreenLight > UI > Bake Floating HUD Level.
    }
}
