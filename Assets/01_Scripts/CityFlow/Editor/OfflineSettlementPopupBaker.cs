using CityFlow.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CityFlow.EditorTools
{
    public static class OfflineSettlementPopupBaker
    {
        private const string TargetCanvasName = "UI_MainCanvas";
        private const string PopupName = "OfflineSettlementPopup";

        [MenuItem("Tools/GreenLight/UI/Bake Offline Settlement Popup")]
        public static void Bake()
        {
            Scene activeScene = SceneManager.GetActiveScene();

            if (!activeScene.IsValid() || !activeScene.isLoaded)
            {
                Debug.LogWarning("[OfflineSettlementPopupBaker] Open a target scene before baking the UI.");
                return;
            }

            OfflineSettlementPopup existingPopup = FindInActiveScene<OfflineSettlementPopup>(activeScene);

            if (existingPopup != null)
            {
                Selection.activeGameObject = existingPopup.gameObject;
                EditorGUIUtility.PingObject(existingPopup.gameObject);
                Debug.Log("[OfflineSettlementPopupBaker] The active scene already contains an offline settlement popup.");
                return;
            }

            Canvas canvas = FindTargetCanvas(activeScene);

            if (canvas == null)
            {
                Debug.LogError(
                    $"[OfflineSettlementPopupBaker] Canvas '{TargetCanvasName}' was not found in " +
                    $"scene '{activeScene.name}'.");
                return;
            }

            GameObject root = CreateUiObject(PopupName, canvas.transform, typeof(CanvasGroup));
            Undo.RegisterCreatedObjectUndo(root, "Bake Offline Settlement Popup");
            SetStretch(root.GetComponent<RectTransform>());
            root.transform.SetAsLastSibling();

            CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            CreateBackdrop(root.transform);
            RectTransform card = CreateCard(root.transform);
            TextMeshProUGUI initialCoins = CreateValueRow(
                card,
                "InitialCoins",
                "INITIAL COINS",
                "0",
                69f,
                new Color(0.8f, 0.84f, 0.87f, 1f));
            TextMeshProUGUI earnedCoins = CreateValueRow(
                card,
                "EarnedCoins",
                "OFFLINE EARNINGS",
                "+0",
                9f,
                new Color(0.35f, 0.88f, 0.58f, 1f));
            TextMeshProUGUI currentCoins = CreateValueRow(
                card,
                "CurrentCoins",
                "CURRENT COINS",
                "0",
                -51f,
                new Color(0.35f, 0.72f, 0.94f, 1f));
            Button closeButton = CreateCloseButton(card);

            OfflineSettlementPopup popup = root.AddComponent<OfflineSettlementPopup>();
            popup.Configure(
                canvasGroup,
                card,
                closeButton,
                initialCoins,
                earnedCoins,
                currentCoins);

            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(activeScene);
            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log(
                $"[OfflineSettlementPopupBaker] Offline settlement popup baked under " +
                $"'{canvas.name}' in scene '{activeScene.name}'.");
        }

        private static void CreateBackdrop(Transform parent)
        {
            GameObject backdrop = CreateUiObject("ModalBackdrop", parent, typeof(Image));
            SetStretch(backdrop.GetComponent<RectTransform>());

            Image image = backdrop.GetComponent<Image>();
            image.color = new Color(0.025f, 0.035f, 0.04f, 0.58f);
            image.raycastTarget = true;
        }

        private static RectTransform CreateCard(Transform parent)
        {
            GameObject cardObject = CreateUiObject("OfflineReportCard", parent, typeof(Image), typeof(Shadow));
            RectTransform card = cardObject.GetComponent<RectTransform>();
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = Vector2.zero;
            card.sizeDelta = new Vector2(540f, 390f);

            Image cardImage = cardObject.GetComponent<Image>();
            cardImage.color = new Color(0.075f, 0.09f, 0.105f, 0.99f);
            cardImage.raycastTarget = true;

            Shadow shadow = cardObject.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(0f, -8f);
            shadow.useGraphicAlpha = true;

            GameObject accent = CreateUiObject("TopAccent", card, typeof(Image));
            RectTransform accentRect = accent.GetComponent<RectTransform>();
            accentRect.anchorMin = new Vector2(0f, 1f);
            accentRect.anchorMax = new Vector2(1f, 1f);
            accentRect.pivot = new Vector2(0.5f, 1f);
            accentRect.anchoredPosition = Vector2.zero;
            accentRect.sizeDelta = new Vector2(0f, 6f);
            accent.GetComponent<Image>().color = new Color(0.22f, 0.7f, 0.78f, 1f);
            accent.GetComponent<Image>().raycastTarget = false;

            CreateText(
                card,
                "TitleText",
                "OFFLINE REPORT",
                new Vector2(0f, 147f),
                new Vector2(450f, 34f),
                23f,
                FontStyles.Bold,
                new Color(0.94f, 0.96f, 0.97f, 1f),
                TextAlignmentOptions.Center);

            CreateDivider(card, 112f);
            CreateDivider(card, -82f);
            return card;
        }

        private static TextMeshProUGUI CreateValueRow(
            RectTransform parent,
            string name,
            string label,
            string value,
            float y,
            Color valueColor)
        {
            CreateText(
                parent,
                $"{name}Label",
                label,
                new Vector2(-182f, y),
                new Vector2(210f, 30f),
                15f,
                FontStyles.Normal,
                new Color(0.62f, 0.68f, 0.72f, 1f),
                TextAlignmentOptions.MidlineLeft);

            return CreateText(
                parent,
                $"{name}Value",
                value,
                new Vector2(148f, y),
                new Vector2(250f, 36f),
                24f,
                FontStyles.Bold,
                valueColor,
                TextAlignmentOptions.MidlineRight);
        }

        private static Button CreateCloseButton(RectTransform parent)
        {
            GameObject buttonObject = CreateUiObject("CloseButton", parent, typeof(Image), typeof(Button));
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -137f);
            rect.sizeDelta = new Vector2(220f, 48f);

            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.18f, 0.57f, 0.66f, 1f);

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor = new Color(0.78f, 0.84f, 0.87f, 1f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;

            CreateText(
                buttonObject.transform,
                "Label",
                "CLOSE",
                Vector2.zero,
                Vector2.zero,
                16f,
                FontStyles.Bold,
                Color.white,
                TextAlignmentOptions.Center,
                true);
            return button;
        }

        private static void CreateDivider(RectTransform parent, float y)
        {
            GameObject divider = CreateUiObject("Divider", parent, typeof(Image));
            RectTransform rect = divider.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(450f, 1f);
            divider.GetComponent<Image>().color = new Color(0.27f, 0.31f, 0.34f, 0.9f);
            divider.GetComponent<Image>().raycastTarget = false;
        }

        private static TextMeshProUGUI CreateText(
            Transform parent,
            string name,
            string value,
            Vector2 anchoredPosition,
            Vector2 size,
            float fontSize,
            FontStyles fontStyle,
            Color color,
            TextAlignmentOptions alignment,
            bool stretch = false)
        {
            GameObject textObject = CreateUiObject(name, parent, typeof(TextMeshProUGUI));
            RectTransform rect = textObject.GetComponent<RectTransform>();

            if (stretch)
            {
                SetStretch(rect);
            }
            else
            {
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = anchoredPosition;
                rect.sizeDelta = size;
            }

            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
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
            GameObject gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);

            foreach (System.Type component in components)
            {
                gameObject.AddComponent(component);
            }

            return gameObject;
        }

        private static void SetStretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }

        private static Canvas FindTargetCanvas(Scene scene)
        {
            Canvas fallback = null;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Canvas[] canvases = root.GetComponentsInChildren<Canvas>(true);

                foreach (Canvas canvas in canvases)
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
    }
}
