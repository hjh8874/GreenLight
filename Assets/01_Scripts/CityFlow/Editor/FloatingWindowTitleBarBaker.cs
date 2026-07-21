#if UNITY_EDITOR
using System.Collections.Generic;
using CityFlow.View;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CityFlow.Editor
{
    public static class FloatingWindowTitleBarBaker
    {
        private const string TargetCanvasName = "UI_MainCanvas";
        private const string ContentRootName = "FloatingWindowContentRoot";
        private const string TitleBarCanvasName = "FloatingWindowTitleBarCanvas";

        [MenuItem("Tools/GreenLight/UI/Bake Floating Window Title Bar")]
        public static void Bake()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError("[FloatingWindowTitleBarBaker] Open a gameplay scene before baking.");
                return;
            }

            Canvas mainCanvas = FindCanvas(scene, TargetCanvasName);
            if (mainCanvas == null)
            {
                Debug.LogError(
                    $"[FloatingWindowTitleBarBaker] Canvas '{TargetCanvasName}' was not found in {scene.name}.");
                return;
            }

            Undo.SetCurrentGroupName("Bake Floating Window Title Bar");
            int undoGroup = Undo.GetCurrentGroup();

            RectTransform contentRoot = EnsureContentRoot(mainCanvas);
            MoveCanvasChildrenIntoContentRoot(mainCanvas, contentRoot);
            GameObject titleBarCanvas = RebuildTitleBarCanvas(
                scene,
                mainCanvas,
                contentRoot);

            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeGameObject = titleBarCanvas;

            Debug.Log(
                "[FloatingWindowTitleBarBaker] Baked the title bar and reserved the top content area. " +
                "Save the scene before making a Windows build.");
        }

        private static RectTransform EnsureContentRoot(Canvas canvas)
        {
            RectTransform contentRoot = canvas.transform.Find(ContentRootName) as RectTransform;
            if (contentRoot == null)
            {
                GameObject root = new GameObject(ContentRootName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(root, "Create floating content root");
                contentRoot = root.GetComponent<RectTransform>();
                contentRoot.SetParent(canvas.transform, false);
            }

            Undo.RecordObject(contentRoot, "Configure floating content root");
            contentRoot.anchorMin = Vector2.zero;
            contentRoot.anchorMax = Vector2.one;
            contentRoot.pivot = new Vector2(0.5f, 0.5f);
            contentRoot.offsetMin = Vector2.zero;
            contentRoot.offsetMax = new Vector2(
                0f,
                -FloatingWindowTitleBarController.TitleBarHeight);
            contentRoot.SetAsFirstSibling();
            return contentRoot;
        }

        private static void MoveCanvasChildrenIntoContentRoot(
            Canvas canvas,
            RectTransform contentRoot)
        {
            List<Transform> children = new List<Transform>();
            for (int i = 0; i < canvas.transform.childCount; i++)
            {
                Transform child = canvas.transform.GetChild(i);
                if (child != contentRoot)
                {
                    children.Add(child);
                }
            }

            if (children.Count == 0)
            {
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(canvas.gameObject, "Move UI under floating content root");
            for (int i = 0; i < children.Count; i++)
            {
                children[i].SetParent(contentRoot, false);
            }
        }

        private static GameObject RebuildTitleBarCanvas(
            Scene scene,
            Canvas contentCanvas,
            RectTransform contentRoot)
        {
            GameObject existing = FindRootObject(scene, TitleBarCanvasName);
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
            }

            GameObject canvasObject = new GameObject(
                TitleBarCanvasName,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(FloatingWindowTitleBarController));
            Undo.RegisterCreatedObjectUndo(canvasObject, "Create floating title bar canvas");
            SceneManager.MoveGameObjectToScene(canvasObject, scene);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32760;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            GameObject panel = CreateUiObject(
                "TitleBar",
                canvasObject.transform,
                typeof(Image),
                typeof(CanvasGroup));
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            SetTopStretch(panelRect, FloatingWindowTitleBarController.TitleBarHeight);

            Image panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color32(25, 30, 35, 248);
            panelImage.raycastTarget = true;

            CanvasGroup panelGroup = panel.GetComponent<CanvasGroup>();
            panelGroup.alpha = 0f;
            panelGroup.interactable = false;
            panelGroup.blocksRaycasts = false;

            GameObject dragArea = CreateUiObject(
                "DragArea",
                panel.transform,
                typeof(Image),
                typeof(FloatingWindowDragHandle));
            RectTransform dragRect = dragArea.GetComponent<RectTransform>();
            dragRect.anchorMin = Vector2.zero;
            dragRect.anchorMax = Vector2.one;
            dragRect.offsetMin = new Vector2(42f, 0f);
            dragRect.offsetMax = new Vector2(-108f, 0f);
            dragArea.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.001f);

            TMP_Text titleText = CreateText(
                "Title",
                dragArea.transform,
                "GreenLight",
                13f,
                TextAlignmentOptions.MidlineLeft,
                new Color32(225, 231, 235, 255));
            SetStretch(titleText.rectTransform, new Vector2(10f, 0f), new Vector2(-4f, 0f));

            Button topmostButton = CreateButton(
                "TopmostButton",
                panel.transform,
                "TOP",
                new Vector2(18f, 0f),
                anchorRight: false,
                closeButton: false,
                out Image topmostImage,
                out TMP_Text topmostLabel);
            Button minimizeButton = CreateButton(
                "MinimizeButton",
                panel.transform,
                "_",
                new Vector2(-90f, 0f),
                anchorRight: true,
                closeButton: false,
                out _,
                out _);
            Button maximizeButton = CreateButton(
                "MaximizeButton",
                panel.transform,
                "[]",
                new Vector2(-54f, 0f),
                anchorRight: true,
                closeButton: false,
                out _,
                out TMP_Text maximizeLabel);
            Button closeButton = CreateButton(
                "CloseButton",
                panel.transform,
                "X",
                new Vector2(-18f, 0f),
                anchorRight: true,
                closeButton: true,
                out _,
                out _);

            GameObject accent = CreateUiObject("Accent", panel.transform, typeof(Image));
            RectTransform accentRect = accent.GetComponent<RectTransform>();
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(1f, 0f);
            accentRect.pivot = new Vector2(0.5f, 0f);
            accentRect.anchoredPosition = Vector2.zero;
            accentRect.sizeDelta = new Vector2(0f, 2f);
            Image accentImage = accent.GetComponent<Image>();
            accentImage.color = new Color32(43, 216, 169, 210);
            accentImage.raycastTarget = false;

            FloatingWindowTitleBarController controller =
                canvasObject.GetComponent<FloatingWindowTitleBarController>();
            SerializedObject serializedController = new SerializedObject(controller);
            SetReference(serializedController, "titleBarGroup", panelGroup);
            SetReference(serializedController, "moveHandle", dragArea.GetComponent<FloatingWindowDragHandle>());
            SetReference(serializedController, "contentCanvas", contentCanvas);
            SetReference(serializedController, "contentRoot", contentRoot);
            SetReference(serializedController, "topmostButton", topmostButton);
            SetReference(serializedController, "minimizeButton", minimizeButton);
            SetReference(serializedController, "maximizeButton", maximizeButton);
            SetReference(serializedController, "closeButton", closeButton);
            SetReference(serializedController, "topmostButtonImage", topmostImage);
            SetReference(serializedController, "topmostLabel", topmostLabel);
            SetReference(serializedController, "maximizeLabel", maximizeLabel);
            serializedController.ApplyModifiedPropertiesWithoutUndo();

            return canvasObject;
        }

        private static Button CreateButton(
            string objectName,
            Transform parent,
            string label,
            Vector2 anchoredPosition,
            bool anchorRight,
            bool closeButton,
            out Image image,
            out TMP_Text labelText)
        {
            GameObject buttonObject = CreateUiObject(
                objectName,
                parent,
                typeof(Image),
                typeof(Button));
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            Vector2 anchor = new Vector2(anchorRight ? 1f : 0f, 0.5f);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(34f, 28f);

            image = buttonObject.GetComponent<Image>();
            image.color = new Color32(48, 55, 62, 255);

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = closeButton
                ? new Color32(239, 68, 68, 255)
                : new Color32(83, 94, 105, 255);
            colors.pressedColor = closeButton
                ? new Color32(185, 28, 28, 255)
                : new Color32(35, 42, 48, 255);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;

            labelText = CreateText(
                "Label",
                buttonObject.transform,
                label,
                label == "TOP" ? 9f : 13f,
                TextAlignmentOptions.Center,
                Color.white);
            SetStretch(labelText.rectTransform, Vector2.zero, Vector2.zero);
            return button;
        }

        private static TMP_Text CreateText(
            string objectName,
            Transform parent,
            string textValue,
            float fontSize,
            TextAlignmentOptions alignment,
            Color color)
        {
            GameObject textObject = CreateUiObject(
                objectName,
                parent,
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            TMP_Text text = textObject.GetComponent<TMP_Text>();
            text.text = textValue;
            text.fontSize = fontSize;
            text.fontStyle = FontStyles.Bold;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return text;
        }

        private static GameObject CreateUiObject(
            string objectName,
            Transform parent,
            params System.Type[] components)
        {
            List<System.Type> componentTypes = new List<System.Type> { typeof(RectTransform) };
            componentTypes.AddRange(components);
            GameObject gameObject = new GameObject(objectName, componentTypes.ToArray());
            Undo.RegisterCreatedObjectUndo(gameObject, $"Create {objectName}");
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        private static void SetTopStretch(RectTransform rect, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, height);
        }

        private static void SetStretch(
            RectTransform rect,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static void SetReference(
            SerializedObject serializedObject,
            string propertyName,
            Object value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogError($"[FloatingWindowTitleBarBaker] Serialized property '{propertyName}' was not found.");
                return;
            }

            property.objectReferenceValue = value;
        }

        private static Canvas FindCanvas(Scene scene, string canvasName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Canvas[] canvases = root.GetComponentsInChildren<Canvas>(true);
                for (int i = 0; i < canvases.Length; i++)
                {
                    if (canvases[i].name == canvasName)
                    {
                        return canvases[i];
                    }
                }
            }

            return null;
        }

        private static GameObject FindRootObject(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == objectName)
                {
                    return root;
                }
            }

            return null;
        }
    }
}
#endif
