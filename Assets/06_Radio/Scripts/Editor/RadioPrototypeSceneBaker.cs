using GreenLight.Radio.Runtime;
using GreenLight.Radio.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GreenLight.Radio.EditorTools
{
    public static class RadioPrototypeSceneBaker
    {
        private const string RadioRootName = "Radio";
        private const string CanvasName = "RadioPrototypeCanvas";
        private const string PanelName = "RadioPrototypePanel";
        private const string EventSystemName = "EventSystem";

        [MenuItem("Tools/GreenLight Radio/Bake Prototype Radio Rig")]
        public static void BakePrototypeRadioRig()
        {
            Scene activeScene = SceneManager.GetActiveScene();

            if (!activeScene.IsValid() || !activeScene.isLoaded)
            {
                Debug.LogWarning("[RadioPrototypeSceneBaker] Bake skipped because no scene is loaded.");
                return;
            }

            GameObject radioRoot = FindRoot(RadioRootName) ?? new GameObject(RadioRootName);
            RadioService radioService = radioRoot.GetComponent<RadioService>() ?? radioRoot.AddComponent<RadioService>();
            if (radioRoot.GetComponent<RadioUnlockService>() == null)
            {
                radioRoot.AddComponent<RadioUnlockService>();
            }

            RadioDummyPlayer dummyPlayer = radioRoot.GetComponent<RadioDummyPlayer>() ?? radioRoot.AddComponent<RadioDummyPlayer>();
            if (radioRoot.GetComponent<RadioGreeWebViewPlayer>() == null)
            {
                radioRoot.AddComponent<RadioGreeWebViewPlayer>();
            }

            GameObject canvasObject = FindRoot(CanvasName) ?? CreateCanvas();
            Canvas canvas = canvasObject.GetComponent<Canvas>() ?? canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>() ?? canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            if (canvasObject.GetComponent<GraphicRaycaster>() == null)
            {
                canvasObject.AddComponent<GraphicRaycaster>();
            }

            EnsureEventSystem();
            GameObject panelObject = FindChild(canvasObject.transform, PanelName);

            if (panelObject != null)
            {
                Object.DestroyImmediate(panelObject);
            }

            RadioPrototypePanel panel = CreatePanel(canvasObject.transform);
            TMP_Text statusText = FindChild(panel.transform, "StatusText").GetComponent<TMP_Text>();
            dummyPlayer.Configure(radioService, statusText);

            SerializedObject serializedPanel = new SerializedObject(panel);
            serializedPanel.FindProperty("radioService").objectReferenceValue = radioService;
            serializedPanel.FindProperty("dummyPlayer").objectReferenceValue = dummyPlayer;
            serializedPanel.FindProperty("slotIndexInput").objectReferenceValue = FindChild(panel.transform, "SlotIndexInput").GetComponent<TMP_InputField>();
            serializedPanel.FindProperty("youtubeInput").objectReferenceValue = FindChild(panel.transform, "YoutubeInput").GetComponent<TMP_InputField>();
            serializedPanel.FindProperty("displayNameInput").objectReferenceValue = FindChild(panel.transform, "DisplayNameInput").GetComponent<TMP_InputField>();
            serializedPanel.FindProperty("themeIdInput").objectReferenceValue = FindChild(panel.transform, "ThemeIdInput").GetComponent<TMP_InputField>();
            serializedPanel.FindProperty("unlockButton").objectReferenceValue = FindChild(panel.transform, "UnlockButton").GetComponent<Button>();
            serializedPanel.FindProperty("registerButton").objectReferenceValue = FindChild(panel.transform, "RegisterButton").GetComponent<Button>();
            serializedPanel.FindProperty("playButton").objectReferenceValue = FindChild(panel.transform, "PlayButton").GetComponent<Button>();
            serializedPanel.FindProperty("pauseButton").objectReferenceValue = FindChild(panel.transform, "PauseButton").GetComponent<Button>();
            serializedPanel.FindProperty("snapshotButton").objectReferenceValue = FindChild(panel.transform, "SnapshotButton").GetComponent<Button>();
            serializedPanel.FindProperty("restoreButton").objectReferenceValue = FindChild(panel.transform, "RestoreButton").GetComponent<Button>();
            serializedPanel.FindProperty("statusText").objectReferenceValue = statusText;
            serializedPanel.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(radioRoot);
            EditorUtility.SetDirty(canvasObject);
            EditorSceneManager.MarkSceneDirty(activeScene);
            Selection.activeGameObject = radioRoot;
            Debug.Log("[RadioPrototypeSceneBaker] Prototype radio rig baked into the active scene.");
        }

        private static GameObject CreateCanvas()
        {
            GameObject canvasObject = new GameObject(CanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            return canvasObject;
        }

        private static RadioPrototypePanel CreatePanel(Transform parent)
        {
            GameObject panelObject = CreatePanelObject(parent, PanelName, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 24f), new Vector2(430f, 400f), new Color(0.07f, 0.08f, 0.09f, 0.94f));
            RadioPrototypePanel panel = panelObject.AddComponent<RadioPrototypePanel>();

            CreateText(panelObject.transform, "Title", "Radio Prototype", new Vector2(18f, -18f), new Vector2(390f, 28f), 22f, TextAlignmentOptions.Left);
            CreateText(panelObject.transform, "Hint", "Register videoId or YouTube URL, then request play.", new Vector2(18f, -48f), new Vector2(390f, 34f), 13f, TextAlignmentOptions.Left);

            CreateText(panelObject.transform, "SlotLabel", "Slot", new Vector2(18f, -90f), new Vector2(84f, 24f), 14f, TextAlignmentOptions.Left);
            CreateInput(panelObject.transform, "SlotIndexInput", "0", new Vector2(112f, -86f), new Vector2(80f, 32f));

            CreateText(panelObject.transform, "YoutubeLabel", "YouTube", new Vector2(18f, -132f), new Vector2(84f, 24f), 14f, TextAlignmentOptions.Left);
            CreateInput(panelObject.transform, "YoutubeInput", "https://youtu.be/dQw4w9WgXcQ", new Vector2(112f, -128f), new Vector2(288f, 32f));

            CreateText(panelObject.transform, "NameLabel", "Name", new Vector2(18f, -174f), new Vector2(84f, 24f), 14f, TextAlignmentOptions.Left);
            CreateInput(panelObject.transform, "DisplayNameInput", "Prototype Track", new Vector2(112f, -170f), new Vector2(288f, 32f));

            CreateText(panelObject.transform, "ThemeLabel", "Theme", new Vector2(18f, -216f), new Vector2(84f, 24f), 14f, TextAlignmentOptions.Left);
            CreateInput(panelObject.transform, "ThemeIdInput", "city-night", new Vector2(112f, -212f), new Vector2(288f, 32f));

            CreateButton(panelObject.transform, "UnlockButton", "Unlock", new Vector2(18f, -264f), new Vector2(120f, 34f));
            CreateButton(panelObject.transform, "RegisterButton", "Register", new Vector2(150f, -264f), new Vector2(120f, 34f));
            CreateButton(panelObject.transform, "PlayButton", "Play", new Vector2(282f, -264f), new Vector2(118f, 34f));
            CreateButton(panelObject.transform, "PauseButton", "Pause", new Vector2(18f, -306f), new Vector2(120f, 34f));
            CreateButton(panelObject.transform, "SnapshotButton", "Snapshot", new Vector2(150f, -306f), new Vector2(120f, 34f));
            CreateButton(panelObject.transform, "RestoreButton", "Restore", new Vector2(282f, -306f), new Vector2(118f, 34f));

            CreateText(panelObject.transform, "StatusText", "Radio prototype panel ready.", new Vector2(18f, -350f), new Vector2(382f, 34f), 13f, TextAlignmentOptions.Left);
            return panel;
        }

        private static GameObject CreatePanelObject(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 size,
            Color color)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);

            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            Image image = panel.GetComponent<Image>();
            image.color = color;

            return panel;
        }

        private static TMP_Text CreateText(
            Transform parent,
            string name,
            string value,
            Vector2 anchoredPosition,
            Vector2 size,
            float fontSize,
            TextAlignmentOptions alignment)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);

            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = new Color(0.9f, 0.94f, 0.96f, 1f);
            text.enableWordWrapping = true;

            return text;
        }

        private static TMP_InputField CreateInput(Transform parent, string name, string value, Vector2 anchoredPosition, Vector2 size)
        {
            GameObject inputObject = CreatePanelObject(parent, name, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), anchoredPosition, size, new Color(0.13f, 0.15f, 0.17f, 1f));
            TMP_InputField input = inputObject.AddComponent<TMP_InputField>();

            TMP_Text text = CreateText(inputObject.transform, "Text", value, new Vector2(10f, -6f), new Vector2(size.x - 20f, size.y - 10f), 14f, TextAlignmentOptions.Left);
            TMP_Text placeholder = CreateText(inputObject.transform, "Placeholder", value, new Vector2(10f, -6f), new Vector2(size.x - 20f, size.y - 10f), 14f, TextAlignmentOptions.Left);
            placeholder.color = new Color(0.55f, 0.6f, 0.62f, 1f);

            input.textComponent = text;
            input.placeholder = placeholder;
            input.text = value;
            input.targetGraphic = inputObject.GetComponent<Image>();
            return input;
        }

        private static Button CreateButton(Transform parent, string name, string label, Vector2 anchoredPosition, Vector2 size)
        {
            GameObject buttonObject = CreatePanelObject(parent, name, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), anchoredPosition, size, new Color(0.18f, 0.29f, 0.33f, 1f));
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = buttonObject.GetComponent<Image>();

            CreateText(buttonObject.transform, "Label", label, Vector2.zero, size, 14f, TextAlignmentOptions.Center);
            RectTransform labelRect = FindChild(buttonObject.transform, "Label").GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.anchoredPosition = Vector2.zero;
            labelRect.sizeDelta = Vector2.zero;
            return button;
        }

        private static void EnsureEventSystem()
        {
            EventSystem eventSystem = Object.FindFirstObjectByType<EventSystem>();

            if (eventSystem == null)
            {
                GameObject eventSystemObject = new GameObject(EventSystemName, typeof(EventSystem), typeof(InputSystemUIInputModule));
                eventSystem = eventSystemObject.GetComponent<EventSystem>();
            }

            if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            }
        }

        private static GameObject FindRoot(string name)
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name == name)
                {
                    return root;
                }
            }

            return null;
        }

        private static GameObject FindChild(Transform parent, string name)
        {
            if (parent.name == name)
            {
                return parent.gameObject;
            }

            foreach (Transform child in parent)
            {
                GameObject found = FindChild(child, name);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
