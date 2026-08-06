using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

namespace CityFlow.UI.Editor
{
    public static class GeonSettingsPanelPolishApplier
    {
        private const string PanelPath =
            "UI_MainCanvas/FloatingWindowContentRoot/SubPanels_Right/Setting_Panel ";

        [MenuItem("CityFlow/UI/Geon/Apply Settings Panel Polish")]
        public static void Apply()
        {
            Transform panel = LayerLabUiAssetCatalog.FindInGeonScene(PanelPath);
            if (panel == null)
            {
                return;
            }

            SettingsPanelController controller =
                panel.GetComponent<SettingsPanelController>();
            if (controller == null)
            {
                Debug.LogError(
                    "[GeonSettingsPanelPolishApplier] " +
                    "SettingsPanelController is missing.");
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(
                panel.gameObject,
                "Apply Geon Settings Panel Polish");

            StylePanel(panel);
            EnsureTitle(panel);
            Toggle muteToggle = StyleMuteToggle(panel);
            Button quitButton = StyleQuitButton(panel);
            EnsureCongestionToggle(panel);
            CreateOrUpdateVolumeRow(
                panel,
                "BGM_Group",
                "BGM",
                -126f,
                out Slider bgmSlider,
                out TMP_InputField bgmInput);
            CreateOrUpdateVolumeRow(
                panel,
                "SFX_Group",
                "SFX",
                -169f,
                out Slider sfxSlider,
                out TMP_InputField sfxInput);
            BindController(
                controller,
                muteToggle,
                quitButton,
                bgmSlider,
                bgmInput,
                sfxSlider,
                sfxInput);

            LayerLabUiAssetCatalog.CompleteSceneChange(
                panel.gameObject,
                nameof(GeonSettingsPanelPolishApplier));
        }

        private static void StylePanel(Transform panel)
        {
            RectTransform rect = panel as RectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(-180f, 0f);
            rect.offsetMax = Vector2.zero;

            Image background =
                LayerLabUiAssetCatalog.GetOrAddComponent<Image>(panel.gameObject);
            LayerLabUiAssetCatalog.ApplyImage(
                background,
                LayerLabUiAssetCatalog.LoadSprite(
                    "Frame/Frame_ListFrame01_White1.png"),
                new Color(0.1f, 0.12f, 0.16f, 0.98f));
            background.raycastTarget = true;
        }

        private static void EnsureTitle(Transform panel)
        {
            TMP_Text title = EnsureText(panel, "Title", "설정");
            RectTransform rect = title.rectTransform;
            SetTopRect(rect, new Vector2(0f, -19f), new Vector2(330f, 30f));
            title.alignment = TextAlignmentOptions.Center;
            LayerLabUiAssetCatalog.StyleText(
                title,
                22f,
                new Color(1f, 0.84f, 0.3f, 1f),
                FontStyles.Bold);
        }

        private static Toggle StyleMuteToggle(Transform panel)
        {
            Transform target = panel.Find("Sound");
            Toggle toggle = target != null ? target.GetComponent<Toggle>() : null;
            if (toggle == null)
            {
                Debug.LogError(
                    "[GeonSettingsPanelPolishApplier] Legacy Sound toggle is missing.");
                return null;
            }

            SetTopRect(
                toggle.transform as RectTransform,
                new Vector2(0f, -52f),
                new Vector2(320f, 30f));
            LayerLabUiAssetCatalog.StyleToggle(toggle, "전체 음소거");
            return toggle;
        }

        private static Button StyleQuitButton(Transform panel)
        {
            Transform target = panel.Find("EndButton");
            Button button = target != null ? target.GetComponent<Button>() : null;
            if (button == null)
            {
                Debug.LogError(
                    "[GeonSettingsPanelPolishApplier] Legacy EndButton is missing.");
                return null;
            }

            SetTopRect(
                button.transform as RectTransform,
                new Vector2(0f, -218f),
                new Vector2(180f, 30f));
            LayerLabUiAssetCatalog.StyleButton(
                button,
                LayerLabUiAssetCatalog.LoadSprite(
                    "Button/Btn_Rectangle02_Dark.png"),
                "게임 종료");
            return button;
        }

        private static void EnsureCongestionToggle(Transform panel)
        {
            Transform existing = panel.Find("CongestionViewToggle");
            GameObject instance = existing != null
                ? existing.gameObject
                : InstantiateCongestionToggle(panel);
            if (instance == null)
            {
                return;
            }

            instance.name = "CongestionViewToggle";
            SetTopRect(
                instance.transform as RectTransform,
                new Vector2(0f, -86f),
                new Vector2(320f, 34f));
            LayerLabUiAssetCatalog.StyleToggle(
                instance.GetComponent<Toggle>(),
                "정체 뷰");
        }

        private static GameObject InstantiateCongestionToggle(Transform parent)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                LayerLabUiAssetCatalog.CongestionTogglePrefabPath);
            if (prefab == null)
            {
                Debug.LogError(
                    "[GeonSettingsPanelPolishApplier] Congestion toggle prefab " +
                    "is missing. Run its baker first.");
                return null;
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(
                prefab,
                parent) as GameObject;
            if (instance != null)
            {
                Undo.RegisterCreatedObjectUndo(
                    instance,
                    "Create Congestion View Toggle");
            }

            return instance;
        }

        private static void CreateOrUpdateVolumeRow(
            Transform panel,
            string rowName,
            string labelText,
            float topPosition,
            out Slider slider,
            out TMP_InputField input)
        {
            RectTransform row = EnsureRectTransform(panel, rowName);
            SetTopRect(
                row,
                new Vector2(0f, topPosition),
                new Vector2(330f, 38f));

            TMP_Text label = EnsureText(row, "Label", labelText);
            SetFixedRect(
                label.rectTransform,
                new Vector2(0f, 0.5f),
                new Vector2(26f, 0f),
                new Vector2(52f, 30f));
            label.alignment = TextAlignmentOptions.MidlineLeft;
            LayerLabUiAssetCatalog.StyleText(
                label,
                15f,
                Color.white,
                FontStyles.Bold);

            slider = EnsureSlider(row);
            SetFixedRect(
                slider.transform as RectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(-3f, 0f),
                new Vector2(208f, 26f));

            input = EnsureInput(row);
            SetFixedRect(
                input.transform as RectTransform,
                new Vector2(1f, 0.5f),
                new Vector2(-28f, 0f),
                new Vector2(56f, 30f));
            if (string.IsNullOrEmpty(input.text))
            {
                input.SetTextWithoutNotify(
                    Mathf.RoundToInt(slider.value * 100f).ToString());
            }
        }

        private static Slider EnsureSlider(Transform parent)
        {
            Transform existing = parent.Find("Slider");
            GameObject root = existing != null
                ? existing.gameObject
                : CreateRectObject(parent, "Slider");
            Slider slider = LayerLabUiAssetCatalog.GetOrAddComponent<Slider>(root);

            RectTransform background = EnsureRectTransform(root.transform, "Background");
            StretchRect(background, 0f, 7f);
            Image backgroundImage =
                LayerLabUiAssetCatalog.GetOrAddComponent<Image>(background.gameObject);
            LayerLabUiAssetCatalog.ApplyImage(
                backgroundImage,
                LayerLabUiAssetCatalog.LoadSprite(
                    "Slider/Slider02_FrontFrame.png"),
                Color.white);
            backgroundImage.raycastTarget = false;

            RectTransform fillArea = EnsureRectTransform(root.transform, "Fill Area");
            StretchRect(fillArea, 8f, 7f);
            RectTransform fill = EnsureRectTransform(fillArea, "Fill");
            StretchRect(fill, 0f, 0f);
            Image fillImage =
                LayerLabUiAssetCatalog.GetOrAddComponent<Image>(fill.gameObject);
            LayerLabUiAssetCatalog.ApplyImage(
                fillImage,
                LayerLabUiAssetCatalog.LoadSprite("Slider/Slider02_Fill.png"),
                Color.white);
            fillImage.raycastTarget = false;

            RectTransform handleArea = EnsureRectTransform(
                root.transform,
                "Handle Slide Area");
            StretchRect(handleArea, 9f, 0f);
            RectTransform handle = EnsureRectTransform(handleArea, "Handle");
            SetFixedRect(
                handle,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(24f, 24f));
            Image handleImage =
                LayerLabUiAssetCatalog.GetOrAddComponent<Image>(handle.gameObject);
            LayerLabUiAssetCatalog.ApplyImage(
                handleImage,
                LayerLabUiAssetCatalog.LoadSprite("Slider/Slider03_Handle.png"),
                Color.white,
                false);
            handleImage.preserveAspect = true;

            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            if (Mathf.Approximately(slider.value, 0f))
            {
                slider.value = 0.5f;
            }

            return slider;
        }

        private static TMP_InputField EnsureInput(Transform parent)
        {
            Transform existing = parent.Find("Input");
            GameObject root = existing != null
                ? existing.gameObject
                : CreateRectObject(parent, "Input");
            Image background =
                LayerLabUiAssetCatalog.GetOrAddComponent<Image>(root);
            LayerLabUiAssetCatalog.ApplyImage(
                background,
                LayerLabUiAssetCatalog.LoadSprite(
                    "UI_Etc/Inputfield_Frame_Normal.png"),
                Color.white);

            TMP_InputField input =
                LayerLabUiAssetCatalog.GetOrAddComponent<TMP_InputField>(root);
            RectTransform viewport = EnsureRectTransform(root.transform, "Text Area");
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(6f, 2f);
            viewport.offsetMax = new Vector2(-6f, -2f);
            LayerLabUiAssetCatalog.GetOrAddComponent<RectMask2D>(viewport.gameObject);

            TMP_Text placeholder = EnsureText(viewport, "Placeholder", "50");
            StretchRect(placeholder.rectTransform, 0f, 0f);
            placeholder.alignment = TextAlignmentOptions.Center;
            LayerLabUiAssetCatalog.StyleText(
                placeholder,
                14f,
                new Color(0.55f, 0.6f, 0.66f, 1f));

            TMP_Text text = EnsureText(viewport, "Text", string.Empty);
            StretchRect(text.rectTransform, 0f, 0f);
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            LayerLabUiAssetCatalog.StyleText(text, 14f, Color.white);

            input.textViewport = viewport;
            input.textComponent = text as TextMeshProUGUI;
            input.placeholder = placeholder as TextMeshProUGUI;
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 3;
            input.targetGraphic = background;
            return input;
        }

        private static void BindController(
            SettingsPanelController controller,
            Toggle muteToggle,
            Button quitButton,
            Slider bgmSlider,
            TMP_InputField bgmInput,
            Slider sfxSlider,
            TMP_InputField sfxInput)
        {
            Undo.RecordObject(controller, "Bind Settings Panel Controls");
            SerializedObject serialized = new SerializedObject(controller);
            SetObjectReference(serialized, "tglMuteAudio", muteToggle);
            SetObjectReference(serialized, "btnQuitGame", quitButton);
            SetObjectReference(serialized, "sldBgm", bgmSlider);
            SetObjectReference(serialized, "inputBgm", bgmInput);
            SetObjectReference(serialized, "sldSfx", sfxSlider);
            SetObjectReference(serialized, "inputSfx", sfxInput);
            SetObjectReference(
                serialized,
                "audioMixer",
                AssetDatabase.LoadAssetAtPath<AudioMixer>(
                    LayerLabUiAssetCatalog.AudioMixerPath));
            serialized.ApplyModifiedProperties();
        }

        private static void SetObjectReference(
            SerializedObject serialized,
            string propertyName,
            Object value)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogError(
                    $"[GeonSettingsPanelPolishApplier] Missing property: " +
                    propertyName);
                return;
            }

            property.objectReferenceValue = value;
        }

        private static TMP_Text EnsureText(
            Transform parent,
            string name,
            string initialText)
        {
            Transform existing = parent.Find(name);
            GameObject target = existing != null
                ? existing.gameObject
                : CreateRectObject(parent, name);
            TextMeshProUGUI text =
                LayerLabUiAssetCatalog.GetOrAddComponent<TextMeshProUGUI>(target);
            if (existing == null)
            {
                text.text = initialText;
            }

            return text;
        }

        private static RectTransform EnsureRectTransform(
            Transform parent,
            string name)
        {
            Transform existing = parent.Find(name);
            return existing != null
                ? existing as RectTransform
                : CreateRectObject(parent, name).GetComponent<RectTransform>();
        }

        private static GameObject CreateRectObject(Transform parent, string name)
        {
            GameObject target = new GameObject(name, typeof(RectTransform));
            target.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(target, $"Create {name}");
            return target;
        }

        private static void SetTopRect(
            RectTransform rect,
            Vector2 position,
            Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void SetFixedRect(
            RectTransform rect,
            Vector2 anchor,
            Vector2 position,
            Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void StretchRect(
            RectTransform rect,
            float horizontalInset,
            float verticalInset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(horizontalInset, verticalInset);
            rect.offsetMax = new Vector2(-horizontalInset, -verticalInset);
        }
    }
}
