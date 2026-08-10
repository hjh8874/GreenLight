using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

namespace CityFlow.UI.Editor
{
    public static class TitleSceneEnhancementsPrefabBaker
    {
        private const string PrefabPath =
            "Assets/Resources/CityFlow/UI/" +
            "UI_TitleSceneEnhancements.prefab";

        [MenuItem("CityFlow/Bake UI/UI_TitleSceneEnhancements")]
        public static void Bake()
        {
            GameObject audioPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                LayerLabUiAssetCatalog.AudioSettingsPrefabPath);
            AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(
                LayerLabUiAssetCatalog.AudioMixerPath);
            if (audioPrefab == null || mixer == null)
            {
                Debug.LogError(
                    "[TitleSceneEnhancementsPrefabBaker] Audio settings " +
                    "Prefab 또는 AudioMixer가 없습니다.");
                return;
            }

            EnsurePrefabFolder();
            GameObject root = CreateRectObject(
                null,
                "UI_TitleSceneEnhancements").gameObject;
            Stretch(root.GetComponent<RectTransform>());
            TitleSceneEnhancementsView view =
                root.AddComponent<TitleSceneEnhancementsView>();

            GameObject logoBackdrop = CreateLogoBackdrop(root.transform);
            GameObject settingsPanel = CreateSettingsLayer(
                root.transform,
                audioPrefab,
                mixer,
                out Button closeButton);
            view.Configure(logoBackdrop, settingsPanel, closeButton);
            settingsPanel.SetActive(false);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log(
                "[TitleSceneEnhancementsPrefabBaker] Baked " + PrefabPath);
        }

        private static GameObject CreateLogoBackdrop(Transform parent)
        {
            GameObject layer = CreateCanvasLayer(
                parent,
                "LogoBackdropLayer",
                -10);
            RectTransform panel = CreateRectObject(
                layer.transform,
                "LogoBackdrop");
            panel.anchorMin = new Vector2(0.5f, 1f);
            panel.anchorMax = new Vector2(0.5f, 1f);
            panel.pivot = new Vector2(0.5f, 1f);
            panel.anchoredPosition = new Vector2(0f, -45f);
            panel.sizeDelta = new Vector2(840f, 260f);

            Image image = panel.gameObject.AddComponent<Image>();
            LayerLabUiAssetCatalog.ApplyImage(
                image,
                LayerLabUiAssetCatalog.LoadSprite(
                    "Frame/Frame_ListFrame01_White1.png"),
                new Color(0.9f, 0.96f, 0.82f, 0.96f));
            image.raycastTarget = false;

            Shadow shadow = panel.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.05f, 0.12f, 0.18f, 0.36f);
            shadow.effectDistance = new Vector2(8f, -8f);
            shadow.useGraphicAlpha = true;

            Outline outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.95f, 0.72f, 0.24f, 0.95f);
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = false;
            return layer;
        }

        private static GameObject CreateSettingsLayer(
            Transform parent,
            GameObject audioPrefab,
            AudioMixer mixer,
            out Button closeButton)
        {
            GameObject layer = CreateCanvasLayer(
                parent,
                "TitleSettingsLayer",
                20);
            layer.AddComponent<GraphicRaycaster>();

            Image blocker = layer.AddComponent<Image>();
            blocker.color = new Color(0f, 0f, 0f, 0.28f);
            blocker.raycastTarget = true;

            RectTransform card = CreateRectObject(
                layer.transform,
                "TitleSettingsPanel");
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = Vector2.zero;
            card.sizeDelta = new Vector2(390f, 260f);

            Image cardImage = card.gameObject.AddComponent<Image>();
            LayerLabUiAssetCatalog.ApplyImage(
                cardImage,
                LayerLabUiAssetCatalog.LoadSprite(
                    "Frame/Frame_ListFrame01_White1.png"),
                new Color(0.12f, 0.14f, 0.18f, 0.98f));
            cardImage.raycastTarget = true;

            TMP_Text title = CreateText(card, "Title", "설정");
            SetFixedRect(
                title.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0f, -28f),
                new Vector2(330f, 34f));
            title.alignment = TextAlignmentOptions.Center;
            LayerLabUiAssetCatalog.StyleText(
                title,
                22f,
                Color.white,
                FontStyles.Bold);

            GameObject audioObject = PrefabUtility.InstantiatePrefab(
                audioPrefab) as GameObject;
            if (audioObject == null)
            {
                closeButton = null;
                return layer;
            }

            audioObject.name = "UI_AudioSettings_Title";
            audioObject.transform.SetParent(card, false);
            PrefabUtility.UnpackPrefabInstance(
                audioObject,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            RectTransform audioRect = audioObject.GetComponent<RectTransform>();
            SetFixedRect(
                audioRect,
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, -10f),
                new Vector2(330f, 126f));

            Transform sfxGroup = audioObject.transform.Find("SFX_Group");
            if (sfxGroup != null)
            {
                Object.DestroyImmediate(sfxGroup.gameObject);
            }

            Toggle mute = audioObject.transform.Find("MuteToggle")
                ?.GetComponent<Toggle>();
            Slider bgm = audioObject.transform.Find("BGM_Group/Slider")
                ?.GetComponent<Slider>();
            TMP_InputField bgmInput = audioObject.transform
                .Find("BGM_Group/Input")?.GetComponent<TMP_InputField>();
            audioObject.GetComponent<AudioSettingsPanelController>()
                ?.Configure(mute, bgm, bgmInput, null, null, mixer);

            closeButton = CreateButton(card, "CloseButton", "닫기");
            SetFixedRect(
                closeButton.transform as RectTransform,
                new Vector2(0.5f, 0f),
                new Vector2(0f, 28f),
                new Vector2(128f, 42f));
            return layer;
        }

        private static GameObject CreateCanvasLayer(
            Transform parent,
            string name,
            int sortingOrder)
        {
            RectTransform rect = CreateRectObject(parent, name);
            Stretch(rect);
            Canvas canvas = rect.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;
            return rect.gameObject;
        }

        private static Button CreateButton(
            Transform parent,
            string name,
            string label)
        {
            RectTransform rect = CreateRectObject(parent, name);
            Image image = rect.gameObject.AddComponent<Image>();
            LayerLabUiAssetCatalog.ApplyImage(
                image,
                LayerLabUiAssetCatalog.LoadSprite(
                    "Button/Btn_Rectangle02_Dark.png"),
                Color.white);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            TMP_Text text = CreateText(rect, "Label", label);
            Stretch(text.rectTransform);
            text.alignment = TextAlignmentOptions.Center;
            LayerLabUiAssetCatalog.StyleText(
                text,
                16f,
                Color.white,
                FontStyles.Bold);
            return button;
        }

        private static TMP_Text CreateText(
            Transform parent,
            string name,
            string value)
        {
            RectTransform rect = CreateRectObject(parent, name);
            TextMeshProUGUI text =
                rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform CreateRectObject(
            Transform parent,
            string name)
        {
            GameObject target = new GameObject(name, typeof(RectTransform));
            if (parent != null)
            {
                target.transform.SetParent(parent, false);
            }

            return target.GetComponent<RectTransform>();
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

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void EnsurePrefabFolder()
        {
            EnsureFolder("Assets", "Resources");
            EnsureFolder("Assets/Resources", "CityFlow");
            EnsureFolder("Assets/Resources/CityFlow", "UI");
        }

        private static void EnsureFolder(string parent, string name)
        {
            string path = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, name);
            }
        }
    }
}
