using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using CityFlow.TilemapTest;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace JsonCanvas.Editor
{
    public sealed class JsonCanvasSceneBakerWindow : EditorWindow
    {
        private const string DefaultJsonPath = "Assets/JsonCanvas/Resources/UiCanvasSpec.json";
        private const string CanvasName = "Canvas_Main";
        private const string SafeAreaRootName = "SafeAreaRoot";
        private const string GameViewRootName = "GameViewRoot";
        private const string OverlayRootName = "OverlayRoot";

        [SerializeField] private TextAsset jsonSpec;
        [SerializeField] private SceneAsset targetScene;
        [SerializeField] private CanvasTargetMode canvasTargetMode = CanvasTargetMode.DeleteExistingAndCreateNew;
        [SerializeField] private SafeAreaRootHandling safeAreaRootHandling = SafeAreaRootHandling.DeleteAndRecreate;
        [SerializeField] private bool saveSceneAfterBake = true;

        public enum CanvasTargetMode
        {
            UseExistingCanvas,
            DeleteExistingAndCreateNew,
            CreateNewCanvas,
            AbortIfCanvasExists
        }

        public enum SafeAreaRootHandling
        {
            ReuseExisting,
            DeleteAndRecreate,
            AbortIfExists
        }

        [MenuItem("Tools/Json Canvas/Open Scene Baker")]
        public static void Open()
        {
            GetWindow<JsonCanvasSceneBakerWindow>("Json Canvas Baker");
        }

        [MenuItem("Tools/Json Canvas/Fix Current Scene Input Modules")]
        public static void FixCurrentSceneInputModules()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                Debug.LogError("JsonCanvas input module fix aborted. Active scene is invalid.");
                return;
            }

            EnsureEventSystem(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("JsonCanvas fixed EventSystem input modules in the active scene.");
        }

        private void OnEnable()
        {
            if (jsonSpec == null)
            {
                jsonSpec = AssetDatabase.LoadAssetAtPath<TextAsset>(DefaultJsonPath);
            }

            if (targetScene == null)
            {
                targetScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/structure-test.unity");
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Json Canvas Scene Baker", EditorStyles.boldLabel);
            EditorGUILayout.Space(6f);

            jsonSpec = (TextAsset)EditorGUILayout.ObjectField("JSON Spec", jsonSpec, typeof(TextAsset), false);
            targetScene = (SceneAsset)EditorGUILayout.ObjectField("Target Scene", targetScene, typeof(SceneAsset), false);
            canvasTargetMode = (CanvasTargetMode)EditorGUILayout.EnumPopup("Canvas Target Mode", canvasTargetMode);
            safeAreaRootHandling = (SafeAreaRootHandling)EditorGUILayout.EnumPopup("SafeAreaRoot Handling", safeAreaRootHandling);
            saveSceneAfterBake = EditorGUILayout.Toggle("Save Scene After Bake", saveSceneAfterBake);

            EditorGUILayout.Space(12f);

            using (new EditorGUI.DisabledScope(jsonSpec == null))
            {
                if (GUILayout.Button("Bake Canvas To Scene", GUILayout.Height(32f)))
                {
                    BakeCanvasToScene();
                }
            }
        }

        private void BakeCanvasToScene()
        {
            if (jsonSpec == null)
            {
                Debug.LogError("JsonCanvas bake aborted. JSON Spec is required.");
                return;
            }

            JsonCanvasSpec spec = JsonUtility.FromJson<JsonCanvasSpec>(jsonSpec.text);
            if (spec == null)
            {
                Debug.LogError("JsonCanvas bake aborted. Failed to parse JSON Spec.");
                return;
            }

            if (!TryResolveTargetScene(spec, out string scenePath))
            {
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("JsonCanvas bake aborted. Current modified scenes were not saved.");
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"JsonCanvas bake aborted. Could not open scene: {scenePath}");
                return;
            }

            Canvas canvas = ResolveCanvas(scene);
            if (canvas == null)
            {
                return;
            }

            RectTransform safeAreaRoot = ResolveSafeAreaRoot(canvas);
            if (safeAreaRoot == null)
            {
                return;
            }

            RectTransform gameViewRoot = null;
            RectTransform overlayRoot = null;
            if (spec.viewportSettings != null && spec.viewportSettings.mode == "SplitGameViewAndCanvas")
            {
                gameViewRoot = EnsureRectChild(safeAreaRoot, GameViewRootName);
                overlayRoot = EnsureRectChild(safeAreaRoot, OverlayRootName);
                ApplyNormalizedRect(gameViewRoot, spec.viewportSettings.gameViewRect);
                ApplyFullRect(overlayRoot);
            }

            BakePanels(spec, canvas.transform, safeAreaRoot, gameViewRoot, overlayRoot);
            EnsureEventSystem(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            if (saveSceneAfterBake)
            {
                EditorSceneManager.SaveScene(scene);
            }

            Debug.Log($"JsonCanvas bake complete. Scene: {scenePath}");
        }

        private bool TryResolveTargetScene(JsonCanvasSpec spec, out string scenePath)
        {
            scenePath = null;

            if (targetScene != null)
            {
                scenePath = NormalizeAssetPath(AssetDatabase.GetAssetPath(targetScene));
                if (string.IsNullOrWhiteSpace(scenePath) || !File.Exists(scenePath))
                {
                    Debug.LogError("JsonCanvas bake aborted. Selected Target Scene does not exist.");
                    return false;
                }

                string specScenePath = NormalizeAssetPath(spec.sceneBakeTarget != null ? spec.sceneBakeTarget.targetSceneInput : null);
                if (!string.IsNullOrWhiteSpace(specScenePath) && scenePath == specScenePath)
                {
                    return ValidateNoMultipleSceneNameMatches(specScenePath);
                }

                return true;
            }

            string requiredPath = NormalizeAssetPath(spec.sceneBakeTarget != null ? spec.sceneBakeTarget.targetSceneInput : null);
            if (string.IsNullOrWhiteSpace(requiredPath))
            {
                Debug.LogError("JsonCanvas bake aborted. Target Scene is required and no scene was selected.");
                return false;
            }

            if (!File.Exists(requiredPath))
            {
                Debug.LogError($"JsonCanvas bake aborted. Required scene not found: {requiredPath}");
                return false;
            }

            if (!ValidateNoMultipleSceneNameMatches(requiredPath))
            {
                return false;
            }

            scenePath = requiredPath;
            return true;
        }

        private static bool ValidateNoMultipleSceneNameMatches(string requiredPath)
        {
            string sceneName = Path.GetFileNameWithoutExtension(requiredPath);
            string[] matches = AssetDatabase.FindAssets($"{sceneName} t:Scene");
            int exactNameMatches = 0;
            for (int i = 0; i < matches.Length; i++)
            {
                string matchPath = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(matches[i]));
                if (Path.GetFileNameWithoutExtension(matchPath) == sceneName)
                {
                    exactNameMatches++;
                }
            }

            if (exactNameMatches > 1)
            {
                Debug.LogError($"JsonCanvas bake aborted. Multiple scenes match name: {sceneName}");
                return false;
            }

            return true;
        }

        private Canvas ResolveCanvas(Scene scene)
        {
            List<Canvas> existingCanvases = FindSceneComponents<Canvas>(scene);

            switch (canvasTargetMode)
            {
                case CanvasTargetMode.UseExistingCanvas:
                    if (existingCanvases.Count == 0)
                    {
                        Debug.LogError("JsonCanvas bake aborted. UseExistingCanvas selected, but no Canvas exists.");
                        return null;
                    }

                    return existingCanvases[0];
                case CanvasTargetMode.DeleteExistingAndCreateNew:
                    for (int i = existingCanvases.Count - 1; i >= 0; i--)
                    {
                        DestroyImmediate(existingCanvases[i].gameObject);
                    }

                    return CreateCanvas(scene);
                case CanvasTargetMode.CreateNewCanvas:
                    return CreateCanvas(scene);
                case CanvasTargetMode.AbortIfCanvasExists:
                    if (existingCanvases.Count > 0)
                    {
                        Debug.LogError("JsonCanvas bake aborted. AbortIfCanvasExists selected and a Canvas already exists.");
                        return null;
                    }

                    return CreateCanvas(scene);
                default:
                    return null;
            }
        }

        private RectTransform ResolveSafeAreaRoot(Canvas canvas)
        {
            Transform existing = canvas.transform.Find(SafeAreaRootName);

            switch (safeAreaRootHandling)
            {
                case SafeAreaRootHandling.ReuseExisting:
                    return existing != null ? EnsureRectTransform(existing.gameObject) : CreateSafeAreaRoot(canvas.transform);
                case SafeAreaRootHandling.DeleteAndRecreate:
                    if (existing != null)
                    {
                        DestroyImmediate(existing.gameObject);
                    }

                    return CreateSafeAreaRoot(canvas.transform);
                case SafeAreaRootHandling.AbortIfExists:
                    if (existing != null)
                    {
                        Debug.LogError("JsonCanvas bake aborted. SafeAreaRoot already exists.");
                        return null;
                    }

                    return CreateSafeAreaRoot(canvas.transform);
                default:
                    return null;
            }
        }

        private Canvas CreateCanvas(Scene scene)
        {
            GameObject canvasObject = new GameObject(CanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(canvasObject, scene);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            ApplyFullRect(canvasObject.GetComponent<RectTransform>());
            return canvas;
        }

        private RectTransform CreateSafeAreaRoot(Transform canvasTransform)
        {
            RectTransform root = EnsureRectChild(canvasTransform, SafeAreaRootName);
            ApplyFullRect(root);
            return root;
        }

        private void BakePanels(
            JsonCanvasSpec spec,
            Transform canvasTransform,
            RectTransform safeAreaRoot,
            RectTransform gameViewRoot,
            RectTransform overlayRoot)
        {
            Dictionary<string, PanelSpec> panelsById = new Dictionary<string, PanelSpec>();
            if (spec.panels != null)
            {
                for (int i = 0; i < spec.panels.Length; i++)
                {
                    PanelSpec panel = spec.panels[i];
                    panelsById[panel.id] = panel;
                }
            }

            List<BakedPanel> bakedPanels = new List<BakedPanel>();
            if (spec.generatedUnityRectTransform == null)
            {
                return;
            }

            for (int i = 0; i < spec.generatedUnityRectTransform.Length; i++)
            {
                GeneratedRectSpec generated = spec.generatedUnityRectTransform[i];
                if (generated.screenMode != spec.selectedScreenMode)
                {
                    continue;
                }

                panelsById.TryGetValue(generated.panelId, out PanelSpec panel);
                RectTransform root = SelectViewportRoot(generated.viewportSpace, safeAreaRoot, gameViewRoot, overlayRoot);
                string hierarchyPath = ResolveHierarchyPath(spec, panel, generated);
                RectTransform panelRect = EnsureHierarchy(canvasTransform, root, hierarchyPath, generated.hierarchyName);

                ApplyAuthoritativeRect(panelRect, generated.unityRectTransform.authoritative);
                CanvasGroup canvasGroup = EnsureComponent<CanvasGroup>(panelRect.gameObject);
                ApplyVisibleCondition(canvasGroup, panel != null ? panel.visibleCondition : null);

                Image image = EnsureComponent<Image>(panelRect.gameObject);
                image.color = new Color(0.05f, 0.08f, 0.1f, 0.65f);

                if (generated.hierarchyName == "Panel_Debug")
                {
                    EnsureDebugStatusPanel(panelRect);
                }

                bakedPanels.Add(new BakedPanel(panelRect, panel != null ? panel.zIndex : 0));
            }

            bakedPanels.Sort((a, b) => a.ZIndex.CompareTo(b.ZIndex));
            for (int i = 0; i < bakedPanels.Count; i++)
            {
                bakedPanels[i].Rect.SetSiblingIndex(i);
            }
        }

        private static string ResolveHierarchyPath(JsonCanvasSpec spec, PanelSpec panel, GeneratedRectSpec generated)
        {
            if (spec.viewportSettings != null &&
                spec.viewportSettings.mode == "SplitGameViewAndCanvas" &&
                !string.IsNullOrWhiteSpace(generated.resolvedHierarchyPath))
            {
                return generated.resolvedHierarchyPath;
            }

            if (!string.IsNullOrWhiteSpace(generated.hierarchyPath))
            {
                return generated.hierarchyPath;
            }

            return panel != null ? panel.hierarchyPath : generated.hierarchyName;
        }

        private static RectTransform SelectViewportRoot(
            string viewportSpace,
            RectTransform safeAreaRoot,
            RectTransform gameViewRoot,
            RectTransform overlayRoot)
        {
            if (viewportSpace == "GameView" && gameViewRoot != null)
            {
                return gameViewRoot;
            }

            if (viewportSpace == "Canvas" && overlayRoot != null)
            {
                return overlayRoot;
            }

            return safeAreaRoot;
        }

        private static RectTransform EnsureHierarchy(
            Transform canvasTransform,
            RectTransform fallbackRoot,
            string hierarchyPath,
            string panelName)
        {
            string[] rawSegments = hierarchyPath.Split('/');
            RectTransform current = fallbackRoot;

            for (int i = 0; i < rawSegments.Length; i++)
            {
                string segment = rawSegments[i];
                if (string.IsNullOrWhiteSpace(segment) ||
                    segment == CanvasName ||
                    segment == SafeAreaRootName)
                {
                    continue;
                }

                if (segment == GameViewRootName || segment == OverlayRootName)
                {
                    Transform root = canvasTransform.Find($"{SafeAreaRootName}/{segment}");
                    if (root != null)
                    {
                        current = EnsureRectTransform(root.gameObject);
                    }

                    continue;
                }

                string objectName = i == rawSegments.Length - 1 ? panelName : segment;
                current = EnsureRectChild(current, objectName);
            }

            return current;
        }

        private static RectTransform EnsureRectChild(Transform parent, string childName)
        {
            Transform existing = parent.Find(childName);
            if (existing != null)
            {
                return EnsureRectTransform(existing.gameObject);
            }

            GameObject child = new GameObject(childName, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            RectTransform rect = child.GetComponent<RectTransform>();
            ApplyFullRect(rect);
            return rect;
        }

        private static RectTransform EnsureRectTransform(GameObject gameObject)
        {
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            return rect != null ? rect : gameObject.AddComponent<RectTransform>();
        }

        private static T EnsureComponent<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();
            return component != null ? component : gameObject.AddComponent<T>();
        }

        private static void ApplyFullRect(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private static void ApplyNormalizedRect(RectTransform rect, NormalizedRect normalizedRect)
        {
            rect.anchorMin = new Vector2(normalizedRect.x, normalizedRect.y);
            rect.anchorMax = new Vector2(normalizedRect.x + normalizedRect.width, normalizedRect.y + normalizedRect.height);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private static void ApplyAuthoritativeRect(RectTransform rect, AuthoritativeRect authoritative)
        {
            rect.anchorMin = ToVector2(authoritative.anchorMin);
            rect.anchorMax = ToVector2(authoritative.anchorMax);
            rect.pivot = ToVector2(authoritative.pivot);
            rect.offsetMin = ToVector2(authoritative.offsetMin);
            rect.offsetMax = ToVector2(authoritative.offsetMax);
            rect.localScale = Vector3.one;
        }

        private static void ApplyVisibleCondition(CanvasGroup canvasGroup, string visibleCondition)
        {
            bool visible = string.IsNullOrWhiteSpace(visibleCondition) ||
                           visibleCondition == "Always" ||
                           visibleCondition == "True" ||
                           visibleCondition == "Visible" ||
                           visibleCondition == "DebugMode";

            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
        }

        private static void EnsureDebugStatusPanel(RectTransform panelRect)
        {
            RectTransform textRect = EnsureRectChild(panelRect, "Text_Status");
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.offsetMin = new Vector2(12f, 10f);
            textRect.offsetMax = new Vector2(-12f, -10f);

            TextMeshProUGUI text = EnsureComponent<TextMeshProUGUI>(textRect.gameObject);
            text.text = "Traffic status";
            text.fontSize = 24f;
            text.enableAutoSizing = true;
            text.fontSizeMin = 12f;
            text.fontSizeMax = 24f;
            text.color = new Color(0.9f, 0.97f, 1f, 1f);
            text.alignment = TextAlignmentOptions.TopLeft;
            text.raycastTarget = false;

            TilemapTestTrafficDebugPanel debugPanel = EnsureComponent<TilemapTestTrafficDebugPanel>(panelRect.gameObject);
            TilemapTestTrafficFlowManager manager = FindFirstObjectByType<TilemapTestTrafficFlowManager>();
            debugPanel.Configure(manager, text);
        }

        private static void EnsureEventSystem(Scene scene)
        {
            List<EventSystem> eventSystems = FindSceneComponents<EventSystem>(scene);
            if (eventSystems.Count > 0)
            {
                for (int i = 0; i < eventSystems.Count; i++)
                {
                    ConfigureEventSystemInputModule(eventSystems[i].gameObject);
                }

                return;
            }

            GameObject eventSystemObject = new GameObject("EventSystem", typeof(EventSystem));
            SceneManager.MoveGameObjectToScene(eventSystemObject, scene);
            ConfigureEventSystemInputModule(eventSystemObject);
        }

        private static void ConfigureEventSystemInputModule(GameObject eventSystemObject)
        {
#if ENABLE_INPUT_SYSTEM
            StandaloneInputModule[] standaloneInputModules = eventSystemObject.GetComponents<StandaloneInputModule>();
            for (int i = standaloneInputModules.Length - 1; i >= 0; i--)
            {
                DestroyImmediate(standaloneInputModules[i]);
            }

            if (eventSystemObject.GetComponent<InputSystemUIInputModule>() == null)
            {
                eventSystemObject.AddComponent<InputSystemUIInputModule>();
            }
#else
            if (eventSystemObject.GetComponent<StandaloneInputModule>() == null)
            {
                eventSystemObject.AddComponent<StandaloneInputModule>();
            }
#endif
        }

        private static List<T> FindSceneComponents<T>(Scene scene) where T : Component
        {
            List<T> components = new List<T>();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                components.AddRange(roots[i].GetComponentsInChildren<T>(true));
            }

            return components;
        }

        private static Vector2 ToVector2(JsonVector2 vector)
        {
            return new Vector2(vector.x, vector.y);
        }

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/');
        }

        private readonly struct BakedPanel
        {
            public BakedPanel(RectTransform rect, int zIndex)
            {
                Rect = rect;
                ZIndex = zIndex;
            }

            public RectTransform Rect { get; }
            public int ZIndex { get; }
        }

        [Serializable]
        private sealed class JsonCanvasSpec
        {
            public string selectedScreenMode;
            public SceneBakeTarget sceneBakeTarget;
            public ViewportSettings viewportSettings;
            public PanelSpec[] panels;
            public GeneratedRectSpec[] generatedUnityRectTransform;
        }

        [Serializable]
        private sealed class SceneBakeTarget
        {
            public string targetSceneInput;
        }

        [Serializable]
        private sealed class ViewportSettings
        {
            public string mode;
            public NormalizedRect gameViewRect;
        }

        [Serializable]
        private sealed class PanelSpec
        {
            public string id;
            public string hierarchyName;
            public string hierarchyPath;
            public string visibleCondition;
            public int zIndex;
        }

        [Serializable]
        private sealed class GeneratedRectSpec
        {
            public string panelId;
            public string hierarchyName;
            public string hierarchyPath;
            public string resolvedHierarchyPath;
            public string viewportSpace;
            public string screenMode;
            public GeneratedUnityRectTransform unityRectTransform;
        }

        [Serializable]
        private sealed class GeneratedUnityRectTransform
        {
            public AuthoritativeRect authoritative;
        }

        [Serializable]
        private sealed class AuthoritativeRect
        {
            public JsonVector2 anchorMin;
            public JsonVector2 anchorMax;
            public JsonVector2 pivot;
            public JsonVector2 offsetMin;
            public JsonVector2 offsetMax;
        }

        [Serializable]
        private sealed class JsonVector2
        {
            public float x;
            public float y;
        }

        [Serializable]
        private sealed class NormalizedRect
        {
            public float x;
            public float y;
            public float width;
            public float height;
        }
    }
}

/*
Unity implementation:
1. Open Tools/Json Canvas/Open Scene Baker.
2. Select Assets/JsonCanvas/Resources/UiCanvasSpec.json and the exact target scene.
3. Choose Canvas and SafeAreaRoot handling, then click Bake Canvas To Scene to create persistent UGUI objects.
*/
