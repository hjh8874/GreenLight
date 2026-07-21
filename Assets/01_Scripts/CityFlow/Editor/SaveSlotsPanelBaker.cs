using CityFlow.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CityFlow.EditorTools
{
    public static class SaveSlotsPanelBaker
    {
        private const string HanScenePath = "Assets/00_Scenes/CityFlowIntegrated_han.unity";
        private const string SettingsPanelName = "Setting_Panel ";
        private const string TargetCanvasName = "UI_MainCanvas";
        private const string ControllerRootName = "SaveSlotsUiRoot";
        private const string OpenButtonName = "OpenSaveSlotsButton";
        private const string LoadButtonName = "OpenLoadSlotsButton";
        private const string DeleteButtonName = "DeleteSaveButton";
        private const string FontPath = "Assets/99_Download/Fonts/NanumGothic SDF.asset";

        private static TMP_FontAsset uiFont;

        [MenuItem("Tools/GreenLight/UI/Bake Save Slots Panel")]
        public static void Bake()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning(
                    "[SaveSlotsPanelBaker] Exit Play Mode before baking the save slot UI.");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();

            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning("[SaveSlotsPanelBaker] Open a target scene before baking.");
                return;
            }

            BakeIntoScene(scene);

            if (scene.isDirty && EditorSceneManager.SaveScene(scene))
            {
                Debug.Log($"[SaveSlotsPanelBaker] Saved baked scene: {scene.path}");
            }
        }

        [MenuItem("Tools/GreenLight/UI/Bake Save Slots Panel", true)]
        private static bool ValidateBake()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        public static void BakeHanScene()
        {
            Scene scene = EditorSceneManager.OpenScene(HanScenePath, OpenSceneMode.Single);
            BakeIntoScene(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[SaveSlotsPanelBaker] Saved baked scene: {HanScenePath}");
        }

        private static void BakeIntoScene(Scene scene)
        {
            uiFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

            if (uiFont == null)
            {
                Debug.LogError(
                    $"[SaveSlotsPanelBaker] NanumGothic SDF.asset was not found at '{FontPath}'. " +
                    "Install the external font assets before baking the save slot UI.");
                return;
            }

            Transform settingsPanel = FindTransform(scene, SettingsPanelName)
                ?? FindTransform(scene, SettingsPanelName.Trim());
            Canvas canvas = FindCanvas(scene);

            if (settingsPanel == null || canvas == null)
            {
                Debug.LogError(
                    $"[SaveSlotsPanelBaker] Settings panel or canvas was not found in '{scene.name}'.");
                return;
            }

            RemoveLegacyDeleteUi(scene);
            RemoveExistingBakedUi(scene);

            CanvasGroup settingsCanvasGroup = settingsPanel.GetComponent<CanvasGroup>()
                ?? Undo.AddComponent<CanvasGroup>(settingsPanel.gameObject);
            Button openButton = CreateButton(
                OpenButtonName,
                settingsPanel,
                "SAVE SLOTS",
                new Vector2(0f, 40f),
                new Vector2(180f, 34f),
                new Color(0.12f, 0.48f, 0.50f, 1f),
                15f);
            Button loadButton = CreateButton(
                LoadButtonName,
                settingsPanel,
                "LOAD SLOTS",
                new Vector2(0f, 80f),
                new Vector2(180f, 34f),
                new Color(0.18f, 0.36f, 0.58f, 1f),
                15f);

            GameObject controllerRoot = CreateUiObject(ControllerRootName, canvas.transform);
            Undo.RegisterCreatedObjectUndo(controllerRoot, "Bake Save Slots Panel");
            SetStretch(controllerRoot.GetComponent<RectTransform>());
            controllerRoot.transform.SetAsLastSibling();

            SaveSlotsPanelController controller = controllerRoot.AddComponent<SaveSlotsPanelController>();
            GameObject panelRoot = CreateUiObject(
                "SaveSlotsPanel",
                controllerRoot.transform,
                typeof(Image),
                typeof(CanvasGroup));
            SetStretch(panelRoot.GetComponent<RectTransform>());
            panelRoot.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.035f, 0.78f);
            CanvasGroup panelCanvasGroup = panelRoot.GetComponent<CanvasGroup>();

            RectTransform card = CreateCard(panelRoot.transform);
            TMP_Text titleText = CreateText(
                card,
                "Title",
                "SAVE SLOTS",
                new Vector2(-310f, 292f),
                new Vector2(420f, 42f),
                27f,
                FontStyles.Bold,
                new Color(0.94f, 0.96f, 0.97f, 1f),
                TextAlignmentOptions.MidlineLeft);
            TMP_Text subtitleText = CreateText(
                card,
                "Subtitle",
                "MANUAL 25  |  AUTO 5",
                new Vector2(-310f, 258f),
                new Vector2(420f, 26f),
                13f,
                FontStyles.Normal,
                new Color(0.56f, 0.64f, 0.67f, 1f),
                TextAlignmentOptions.MidlineLeft);

            Button closeButton = CreateButton(
                "CloseButton",
                card,
                "X",
                new Vector2(343f, 287f),
                new Vector2(42f, 42f),
                new Color(0.18f, 0.21f, 0.23f, 1f),
                18f);
            Button createButton = CreateButton(
                "CreateSaveButton",
                card,
                "+  새로운 저장 하기",
                new Vector2(0f, 214f),
                new Vector2(700f, 50f),
                new Color(0.10f, 0.50f, 0.46f, 1f),
                17f);
            TMP_Text countText = CreateText(
                card,
                "SlotCountText",
                "0/30  MANUAL 0/25",
                new Vector2(240f, 174f),
                new Vector2(220f, 24f),
                12f,
                FontStyles.Normal,
                new Color(0.62f, 0.68f, 0.71f, 1f),
                TextAlignmentOptions.MidlineRight);

            Transform slotContainer = CreateScrollList(card, out TMP_Text emptyText);
            SaveSlotView slotTemplate = CreateSlotTemplate(slotContainer);
            TMP_Text statusText = CreateText(
                card,
                "StatusText",
                string.Empty,
                new Vector2(0f, -303f),
                new Vector2(700f, 24f),
                12f,
                FontStyles.Normal,
                new Color(0.70f, 0.76f, 0.78f, 1f),
                TextAlignmentOptions.Center);

            GameObject nameDialog = CreateNameDialog(
                panelRoot.transform,
                out TMP_InputField nameInput,
                out Button confirmNameButton,
                out Button cancelNameButton);

            ConfirmPopupController confirmPopup = FindComponent<ConfirmPopupController>(scene);
            UIDockController dockController = FindComponent<UIDockController>(scene);
            SerializedObject serializedController = new SerializedObject(controller);
            SetReference(serializedController, "openPanelButton", openButton);
            SetReference(serializedController, "loadPanelButton", loadButton);
            SetReference(serializedController, "closePanelButton", closeButton);
            SetReference(serializedController, "panelRoot", panelRoot);
            SetReference(serializedController, "panelCanvasGroup", panelCanvasGroup);
            SetReference(serializedController, "settingsCanvasGroup", settingsCanvasGroup);
            SetReference(serializedController, "panelTitleText", titleText);
            SetReference(serializedController, "panelSubtitleText", subtitleText);
            SetReference(serializedController, "dockController", dockController);
            SetReference(serializedController, "createSaveButton", createButton);
            SetReference(serializedController, "slotContainer", slotContainer);
            SetReference(serializedController, "slotTemplate", slotTemplate);
            SetReference(serializedController, "slotCountText", countText);
            SetReference(serializedController, "emptyStateText", emptyText);
            SetReference(serializedController, "statusText", statusText);
            SetReference(serializedController, "nameDialog", nameDialog);
            SetReference(serializedController, "nameInput", nameInput);
            SetReference(serializedController, "confirmNameButton", confirmNameButton);
            SetReference(serializedController, "cancelNameButton", cancelNameButton);
            SetReference(serializedController, "confirmPopup", confirmPopup);
            serializedController.ApplyModifiedPropertiesWithoutUndo();

            nameDialog.SetActive(false);
            panelRoot.SetActive(false);
            EditorUtility.SetDirty(controllerRoot);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = openButton.gameObject;
            EditorGUIUtility.PingObject(openButton.gameObject);
            Debug.Log(
                $"[SaveSlotsPanelBaker] Save slot UI baked into '{scene.name}'. " +
                "Save and load entry buttons are ready, and the legacy delete-save button was removed.");
        }

        private static RectTransform CreateCard(Transform parent)
        {
            GameObject cardObject = CreateUiObject(
                "SaveSlotsCard",
                parent,
                typeof(Image),
                typeof(Shadow));
            RectTransform card = cardObject.GetComponent<RectTransform>();
            SetCentered(card, Vector2.zero, new Vector2(780f, 680f));
            cardObject.GetComponent<Image>().color = new Color(0.055f, 0.068f, 0.074f, 1f);

            Shadow shadow = cardObject.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.65f);
            shadow.effectDistance = new Vector2(0f, -8f);
            return card;
        }

        private static Transform CreateScrollList(RectTransform card, out TMP_Text emptyText)
        {
            GameObject scrollObject = CreateUiObject(
                "SlotScrollView",
                card,
                typeof(Image),
                typeof(ScrollRect));
            RectTransform scrollRectTransform = scrollObject.GetComponent<RectTransform>();
            SetCentered(scrollRectTransform, new Vector2(0f, -57f), new Vector2(700f, 420f));
            scrollObject.GetComponent<Image>().color = new Color(0.035f, 0.043f, 0.047f, 1f);

            GameObject viewport = CreateUiObject(
                "Viewport",
                scrollObject.transform,
                typeof(Image),
                typeof(RectMask2D));
            SetStretch(viewport.GetComponent<RectTransform>(), 8f);
            viewport.GetComponent<Image>().color = Color.clear;

            GameObject content = CreateUiObject(
                "Content",
                viewport.transform,
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;

            VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scrollRect = scrollObject.GetComponent<ScrollRect>();
            scrollRect.viewport = viewport.GetComponent<RectTransform>();
            scrollRect.content = contentRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 32f;

            emptyText = CreateText(
                viewport.transform,
                "EmptyStateText",
                "저장된 게임이 없습니다",
                Vector2.zero,
                new Vector2(500f, 40f),
                16f,
                FontStyles.Normal,
                new Color(0.48f, 0.54f, 0.57f, 1f),
                TextAlignmentOptions.Center);
            return content.transform;
        }

        private static SaveSlotView CreateSlotTemplate(Transform parent)
        {
            GameObject root = CreateUiObject(
                "SaveSlotTemplate",
                parent,
                typeof(Image),
                typeof(LayoutElement));
            root.GetComponent<LayoutElement>().preferredHeight = 126f;
            Image background = root.GetComponent<Image>();
            background.color = new Color(0.08f, 0.14f, 0.15f, 1f);

            GameObject accentObject = CreateUiObject("TypeAccent", root.transform, typeof(Image));
            RectTransform accentRect = accentObject.GetComponent<RectTransform>();
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(0f, 1f);
            accentRect.pivot = new Vector2(0f, 0.5f);
            accentRect.anchoredPosition = Vector2.zero;
            accentRect.sizeDelta = new Vector2(5f, 0f);
            Image accent = accentObject.GetComponent<Image>();
            accent.color = new Color(0.18f, 0.72f, 0.66f, 1f);

            GameObject previewObject = CreateUiObject("Preview", root.transform, typeof(RawImage));
            RectTransform previewRect = previewObject.GetComponent<RectTransform>();
            previewRect.anchorMin = new Vector2(0f, 0.5f);
            previewRect.anchorMax = new Vector2(0f, 0.5f);
            previewRect.pivot = new Vector2(0f, 0.5f);
            previewRect.anchoredPosition = new Vector2(16f, 0f);
            previewRect.sizeDelta = new Vector2(184f, 104f);
            RawImage preview = previewObject.GetComponent<RawImage>();
            preview.color = new Color(0.15f, 0.17f, 0.18f, 1f);

            TMP_Text typeText = CreateAnchoredText(
                root.transform,
                "TypeText",
                "MANUAL SAVE",
                new Vector2(216f, 40f),
                new Vector2(210f, 22f),
                11f,
                FontStyles.Bold,
                new Color(0.18f, 0.72f, 0.66f, 1f));
            TMP_Text nameText = CreateAnchoredText(
                root.transform,
                "NameText",
                "Save name",
                new Vector2(216f, 8f),
                new Vector2(320f, 30f),
                18f,
                FontStyles.Bold,
                Color.white);
            TMP_Text timeText = CreateAnchoredText(
                root.transform,
                "SavedAtText",
                "yyyy-MM-dd  HH:mm:ss",
                new Vector2(216f, -22f),
                new Vector2(300f, 24f),
                12f,
                FontStyles.Normal,
                new Color(0.62f, 0.68f, 0.71f, 1f));

            Button loadButton = CreateAnchoredButton(
                "LoadButton",
                root.transform,
                "LOAD",
                new Vector2(-58f, -40f),
                new Vector2(88f, 34f),
                new Color(0.16f, 0.48f, 0.55f, 1f));
            Button deleteButton = CreateAnchoredButton(
                "DeleteButton",
                root.transform,
                "X",
                new Vector2(-14f, -40f),
                new Vector2(34f, 34f),
                new Color(0.48f, 0.18f, 0.20f, 1f));

            SaveSlotView view = root.AddComponent<SaveSlotView>();
            SerializedObject serializedView = new SerializedObject(view);
            SetReference(serializedView, "background", background);
            SetReference(serializedView, "typeAccent", accent);
            SetReference(serializedView, "previewImage", preview);
            SetReference(serializedView, "typeText", typeText);
            SetReference(serializedView, "nameText", nameText);
            SetReference(serializedView, "savedAtText", timeText);
            SetReference(serializedView, "loadButton", loadButton);
            SetReference(serializedView, "deleteButton", deleteButton);
            serializedView.ApplyModifiedPropertiesWithoutUndo();
            root.SetActive(false);
            return view;
        }

        private static GameObject CreateNameDialog(
            Transform parent,
            out TMP_InputField input,
            out Button confirmButton,
            out Button cancelButton)
        {
            GameObject dialog = CreateUiObject("SaveNameDialog", parent, typeof(Image));
            SetStretch(dialog.GetComponent<RectTransform>());
            dialog.GetComponent<Image>().color = new Color(0.015f, 0.02f, 0.024f, 0.82f);

            GameObject cardObject = CreateUiObject("NameCard", dialog.transform, typeof(Image));
            RectTransform card = cardObject.GetComponent<RectTransform>();
            SetCentered(card, Vector2.zero, new Vector2(470f, 230f));
            cardObject.GetComponent<Image>().color = new Color(0.075f, 0.09f, 0.10f, 1f);

            CreateText(
                card,
                "Title",
                "저장 이름",
                new Vector2(0f, 76f),
                new Vector2(410f, 32f),
                20f,
                FontStyles.Bold,
                Color.white,
                TextAlignmentOptions.Center);
            CreateText(
                card,
                "Hint",
                "비워두면 저장 시각이 이름으로 사용됩니다",
                new Vector2(0f, 48f),
                new Vector2(410f, 24f),
                12f,
                FontStyles.Normal,
                new Color(0.58f, 0.64f, 0.67f, 1f),
                TextAlignmentOptions.Center);

            input = CreateInputField(card);
            confirmButton = CreateButton(
                "ConfirmButton",
                card,
                "저장",
                new Vector2(105f, -76f),
                new Vector2(170f, 40f),
                new Color(0.10f, 0.52f, 0.47f, 1f),
                15f);
            cancelButton = CreateButton(
                "CancelButton",
                card,
                "취소",
                new Vector2(-105f, -76f),
                new Vector2(170f, 40f),
                new Color(0.22f, 0.25f, 0.27f, 1f),
                15f);
            return dialog;
        }

        private static TMP_InputField CreateInputField(RectTransform parent)
        {
            GameObject root = CreateUiObject("NameInput", parent, typeof(Image), typeof(TMP_InputField));
            RectTransform rect = root.GetComponent<RectTransform>();
            SetCentered(rect, new Vector2(0f, 4f), new Vector2(410f, 46f));
            root.GetComponent<Image>().color = new Color(0.035f, 0.045f, 0.05f, 1f);

            TMP_Text placeholder = CreateText(
                root.transform,
                "Placeholder",
                "예: 강변 도시",
                Vector2.zero,
                Vector2.zero,
                15f,
                FontStyles.Normal,
                new Color(0.42f, 0.48f, 0.50f, 1f),
                TextAlignmentOptions.MidlineLeft,
                true,
                14f);
            TMP_Text inputText = CreateText(
                root.transform,
                "Text",
                string.Empty,
                Vector2.zero,
                Vector2.zero,
                15f,
                FontStyles.Normal,
                Color.white,
                TextAlignmentOptions.MidlineLeft,
                true,
                14f);

            TMP_InputField input = root.GetComponent<TMP_InputField>();
            input.textComponent = inputText;
            input.placeholder = placeholder;
            input.characterLimit = 40;
            input.lineType = TMP_InputField.LineType.SingleLine;
            return input;
        }

        private static Button CreateButton(
            string name,
            Transform parent,
            string label,
            Vector2 position,
            Vector2 size,
            Color color,
            float fontSize)
        {
            GameObject root = CreateUiObject(name, parent, typeof(Image), typeof(Button));
            RectTransform rect = root.GetComponent<RectTransform>();
            SetCentered(rect, position, size);
            Image image = root.GetComponent<Image>();
            image.color = color;

            Button button = root.GetComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor = new Color(0.76f, 0.82f, 0.82f, 1f);
            button.colors = colors;

            CreateText(
                root.transform,
                "Label",
                label,
                Vector2.zero,
                Vector2.zero,
                fontSize,
                FontStyles.Bold,
                Color.white,
                TextAlignmentOptions.Center,
                true);
            return button;
        }

        private static Button CreateAnchoredButton(
            string name,
            Transform parent,
            string label,
            Vector2 positionFromRight,
            Vector2 size,
            Color color)
        {
            Button button = CreateButton(name, parent, label, Vector2.zero, size, color, 12f);
            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = positionFromRight;
            return button;
        }

        private static TMP_Text CreateAnchoredText(
            Transform parent,
            string name,
            string value,
            Vector2 position,
            Vector2 size,
            float fontSize,
            FontStyles style,
            Color color)
        {
            TMP_Text text = CreateText(
                parent,
                name,
                value,
                Vector2.zero,
                size,
                fontSize,
                style,
                color,
                TextAlignmentOptions.MidlineLeft);
            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = position;
            return text;
        }

        private static TMP_Text CreateText(
            Transform parent,
            string name,
            string value,
            Vector2 position,
            Vector2 size,
            float fontSize,
            FontStyles style,
            Color color,
            TextAlignmentOptions alignment,
            bool stretch = false,
            float horizontalPadding = 0f)
        {
            GameObject root = CreateUiObject(name, parent, typeof(TextMeshProUGUI));
            RectTransform rect = root.GetComponent<RectTransform>();

            if (stretch)
            {
                SetStretch(rect);
                rect.offsetMin = new Vector2(horizontalPadding, 0f);
                rect.offsetMax = new Vector2(-horizontalPadding, 0f);
            }
            else
            {
                SetCentered(rect, position, size);
            }

            TextMeshProUGUI text = root.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.font = uiFont;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            text.characterSpacing = 0f;
            return text;
        }

        private static GameObject CreateUiObject(
            string name,
            Transform parent,
            params System.Type[] components)
        {
            GameObject root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);

            foreach (System.Type component in components)
            {
                root.AddComponent(component);
            }

            root.layer = parent.gameObject.layer;
            return root;
        }

        private static void SetCentered(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void SetStretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static void SetReference(
            SerializedObject serializedObject,
            string propertyName,
            Object value)
        {
            serializedObject.FindProperty(propertyName).objectReferenceValue = value;
        }

        private static void RemoveLegacyDeleteUi(Scene scene)
        {
            SaveDataSettingsController[] controllers = FindComponents<SaveDataSettingsController>(scene);

            foreach (SaveDataSettingsController controller in controllers)
            {
                if (controller.gameObject.name == DeleteButtonName)
                {
                    Undo.DestroyObjectImmediate(controller.gameObject);
                }
                else
                {
                    Undo.DestroyObjectImmediate(controller);
                }
            }

            Transform deleteButton = FindTransform(scene, DeleteButtonName);

            if (deleteButton != null)
            {
                Undo.DestroyObjectImmediate(deleteButton.gameObject);
            }
        }

        private static void RemoveExistingBakedUi(Scene scene)
        {
            Transform controllerRoot = FindTransform(scene, ControllerRootName);

            if (controllerRoot != null)
            {
                Undo.DestroyObjectImmediate(controllerRoot.gameObject);
            }

            Transform openButton = FindTransform(scene, OpenButtonName);

            if (openButton != null)
            {
                Undo.DestroyObjectImmediate(openButton.gameObject);
            }

            Transform loadButton = FindTransform(scene, LoadButtonName);

            if (loadButton != null)
            {
                Undo.DestroyObjectImmediate(loadButton.gameObject);
            }
        }

        private static Canvas FindCanvas(Scene scene)
        {
            Canvas fallback = null;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
                {
                    if (canvas.name == TargetCanvasName)
                    {
                        return canvas;
                    }

                    fallback ??= canvas;
                }
            }

            return fallback;
        }

        private static Transform FindTransform(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (transform.name == objectName)
                    {
                        return transform;
                    }
                }
            }

            return null;
        }

        private static T FindComponent<T>(Scene scene) where T : Component
        {
            T[] components = FindComponents<T>(scene);
            return components.Length > 0 ? components[0] : null;
        }

        private static T[] FindComponents<T>(Scene scene) where T : Component
        {
            var results = new System.Collections.Generic.List<T>();

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                results.AddRange(root.GetComponentsInChildren<T>(true));
            }

            return results.ToArray();
        }

        // Unity setup: Open CityFlowIntegrated_han and run Tools > GreenLight > UI > Bake Save Slots Panel.
    }
}
