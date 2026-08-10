using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

namespace CityFlow.UI.Editor
{
    public static class AudioSettingsPrefabBaker
    {
        [MenuItem("CityFlow/Bake UI/UI_AudioSettings")]
        public static void Bake()
        {
            AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(
                LayerLabUiAssetCatalog.AudioMixerPath);
            if (mixer == null)
            {
                Debug.LogError(
                    "[AudioSettingsPrefabBaker] CityAudioMixer를 찾을 수 " +
                    $"없습니다: {LayerLabUiAssetCatalog.AudioMixerPath}");
                return;
            }

            EnsurePrefabFolder();
            GameObject root = new GameObject(
                "UI_AudioSettings",
                typeof(RectTransform),
                typeof(AudioSettingsPanelController));
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(330f, 126f);

            Toggle muteToggle = CreateMuteToggle(root.transform);
            CreateVolumeRow(
                root.transform,
                "BGM_Group",
                "BGM",
                -61f,
                out Slider bgmSlider,
                out TMP_InputField bgmInput);
            CreateVolumeRow(
                root.transform,
                "SFX_Group",
                "SFX",
                -104f,
                out Slider sfxSlider,
                out TMP_InputField sfxInput);

            root.GetComponent<AudioSettingsPanelController>().Configure(
                muteToggle,
                bgmSlider,
                bgmInput,
                sfxSlider,
                sfxInput,
                mixer);

            PrefabUtility.SaveAsPrefabAsset(
                root,
                LayerLabUiAssetCatalog.AudioSettingsPrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log(
                "[AudioSettingsPrefabBaker] Baked " +
                LayerLabUiAssetCatalog.AudioSettingsPrefabPath);
        }

        private static Toggle CreateMuteToggle(Transform parent)
        {
            GameObject target = new GameObject(
                "MuteToggle",
                typeof(RectTransform),
                typeof(Toggle));
            target.transform.SetParent(parent, false);
            SetTopRect(
                target.GetComponent<RectTransform>(),
                new Vector2(0f, -18f),
                new Vector2(320f, 32f));

            Toggle toggle = target.GetComponent<Toggle>();
            LayerLabUiAssetCatalog.StyleToggle(toggle, "전체 음소거");
            toggle.SetIsOnWithoutNotify(false);
            return toggle;
        }

        private static void CreateVolumeRow(
            Transform parent,
            string rowName,
            string labelText,
            float topPosition,
            out Slider slider,
            out TMP_InputField input)
        {
            RectTransform row = CreateRectObject(parent, rowName);
            SetTopRect(
                row,
                new Vector2(0f, topPosition),
                new Vector2(330f, 38f));

            TMP_Text label = CreateText(row, "Label", labelText);
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

            slider = CreateSlider(row);
            SetFixedRect(
                slider.transform as RectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(-3f, 0f),
                new Vector2(208f, 26f));

            input = CreateInput(row);
            SetFixedRect(
                input.transform as RectTransform,
                new Vector2(1f, 0.5f),
                new Vector2(-28f, 0f),
                new Vector2(56f, 30f));
            input.SetTextWithoutNotify("50");
        }

        private static Slider CreateSlider(Transform parent)
        {
            GameObject root = CreateRectObject(parent, "Slider").gameObject;
            Slider slider = root.AddComponent<Slider>();

            RectTransform background = CreateRectObject(
                root.transform,
                "Background");
            StretchRect(background, 0f, 7f);
            Image backgroundImage = AddImage(
                background.gameObject,
                "Slider/Slider02_FrontFrame.png");
            backgroundImage.raycastTarget = false;

            RectTransform fillArea = CreateRectObject(
                root.transform,
                "Fill Area");
            StretchRect(fillArea, 8f, 7f);
            RectTransform fill = CreateRectObject(fillArea, "Fill");
            StretchRect(fill, 0f, 0f);
            Image fillImage = AddImage(
                fill.gameObject,
                "Slider/Slider02_Fill.png");
            fillImage.raycastTarget = false;

            RectTransform handleArea = CreateRectObject(
                root.transform,
                "Handle Slide Area");
            StretchRect(handleArea, 9f, 0f);
            RectTransform handle = CreateRectObject(handleArea, "Handle");
            SetFixedRect(
                handle,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(24f, 24f));
            Image handleImage = AddImage(
                handle.gameObject,
                "Slider/Slider03_Handle.png",
                false);
            handleImage.preserveAspect = true;

            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 0.5f;
            return slider;
        }

        private static TMP_InputField CreateInput(Transform parent)
        {
            GameObject root = CreateRectObject(parent, "Input").gameObject;
            Image background = AddImage(
                root,
                "UI_Etc/Inputfield_Frame_Normal.png");
            TMP_InputField input = root.AddComponent<TMP_InputField>();

            RectTransform viewport = CreateRectObject(
                root.transform,
                "Text Area");
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(6f, 2f);
            viewport.offsetMax = new Vector2(-6f, -2f);
            viewport.gameObject.AddComponent<RectMask2D>();

            TMP_Text placeholder = CreateText(viewport, "Placeholder", "50");
            StretchRect(placeholder.rectTransform, 0f, 0f);
            placeholder.alignment = TextAlignmentOptions.Center;
            LayerLabUiAssetCatalog.StyleText(
                placeholder,
                14f,
                new Color(0.2f, 0.2f, 0.2f, 0.7f));

            TMP_Text text = CreateText(viewport, "Text", string.Empty);
            StretchRect(text.rectTransform, 0f, 0f);
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            LayerLabUiAssetCatalog.StyleText(text, 14f, Color.black);

            input.textViewport = viewport;
            input.textComponent = (TextMeshProUGUI)text;
            input.placeholder = (TextMeshProUGUI)placeholder;
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 3;
            input.targetGraphic = background;
            return input;
        }

        private static Image AddImage(
            GameObject target,
            string spritePath,
            bool sliced = true)
        {
            Image image = target.AddComponent<Image>();
            LayerLabUiAssetCatalog.ApplyImage(
                image,
                LayerLabUiAssetCatalog.LoadSprite(spritePath),
                Color.white,
                sliced);
            return image;
        }

        private static TMP_Text CreateText(
            Transform parent,
            string name,
            string value)
        {
            GameObject target = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            target.transform.SetParent(parent, false);
            TextMeshProUGUI text = target.GetComponent<TextMeshProUGUI>();
            text.text = value;
            return text;
        }

        private static RectTransform CreateRectObject(
            Transform parent,
            string name)
        {
            GameObject target = new GameObject(name, typeof(RectTransform));
            target.transform.SetParent(parent, false);
            return target.GetComponent<RectTransform>();
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

        private static void EnsurePrefabFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/02_Prefabs"))
            {
                AssetDatabase.CreateFolder("Assets", "02_Prefabs");
            }

            if (!AssetDatabase.IsValidFolder("Assets/02_Prefabs/UI"))
            {
                AssetDatabase.CreateFolder("Assets/02_Prefabs", "UI");
            }
        }
    }
}
