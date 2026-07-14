using CityFlow.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CityFlow.EditorTools
{
    public static class WeeklySettlementPopupBaker
    {
        private const string TargetCanvasName = "UI_MainCanvas";
        private const string PopupName = "WeeklySettlementPopup";

        [MenuItem("Tools/GreenLight/UI/Bake Weekly Settlement Popup")]
        public static void Bake()
        {
            Scene activeScene = SceneManager.GetActiveScene();

            if (!activeScene.IsValid() || !activeScene.isLoaded)
            {
                Debug.LogWarning("[WeeklySettlementPopupBaker] Open a target scene before baking the UI.");
                return;
            }

            WeeklySettlementPopup existingPopup = FindInActiveScene<WeeklySettlementPopup>(activeScene);

            if (existingPopup != null)
            {
                Selection.activeGameObject = existingPopup.gameObject;
                EditorGUIUtility.PingObject(existingPopup.gameObject);
                Debug.Log("[WeeklySettlementPopupBaker] The active scene already contains a weekly settlement popup.");
                return;
            }

            Canvas canvas = FindTargetCanvas(activeScene);

            if (canvas == null)
            {
                Debug.LogError(
                    $"[WeeklySettlementPopupBaker] Canvas '{TargetCanvasName}' was not found in " +
                    $"scene '{activeScene.name}'.");
                return;
            }

            GameObject root = CreateUiObject(PopupName, canvas.transform, typeof(CanvasGroup));
            Undo.RegisterCreatedObjectUndo(root, "Bake Weekly Settlement Popup");
            SetStretch(root.GetComponent<RectTransform>());
            root.transform.SetAsLastSibling();

            CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();
            // Keep the baked result visible in edit mode. Awake hides it at runtime.
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            Button backdropButton = CreateBackdrop(root.transform);
            RectTransform card = CreateCard(root.transform);
            TextMeshProUGUI period = CreateText(
                card,
                "PeriodText",
                "7-DAY REVENUE",
                new Vector2(0f, 53f),
                new Vector2(430f, 30f),
                18f,
                FontStyles.Bold,
                new Color(0.42f, 0.79f, 0.94f, 1f));
            TextMeshProUGUI amount = CreateText(
                card,
                "AmountText",
                "+0 COINS",
                new Vector2(0f, 3f),
                new Vector2(440f, 54f),
                38f,
                FontStyles.Bold,
                new Color(0.42f, 0.91f, 0.58f, 1f));
            TextMeshProUGUI balance = CreateText(
                card,
                "BalanceText",
                "BALANCE  0",
                new Vector2(0f, -63f),
                new Vector2(430f, 28f),
                17f,
                FontStyles.Normal,
                new Color(0.76f, 0.8f, 0.83f, 1f));

            WeeklySettlementPopup popup = root.AddComponent<WeeklySettlementPopup>();
            popup.Configure(canvasGroup, card, backdropButton, period, amount, balance);

            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(activeScene);
            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log(
                $"[WeeklySettlementPopupBaker] Weekly settlement popup baked under " +
                $"'{canvas.name}' in scene '{activeScene.name}'.");
        }

        private static Button CreateBackdrop(Transform parent)
        {
            GameObject backdrop = CreateUiObject("DismissBackdrop", parent, typeof(Image), typeof(Button));
            SetStretch(backdrop.GetComponent<RectTransform>());

            Image image = backdrop.GetComponent<Image>();
            image.color = new Color(0.025f, 0.035f, 0.04f, 0.48f);

            Button button = backdrop.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            return button;
        }

        private static RectTransform CreateCard(Transform parent)
        {
            GameObject cardObject = CreateUiObject("SettlementCard", parent, typeof(Image), typeof(Shadow));
            RectTransform card = cardObject.GetComponent<RectTransform>();
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = Vector2.zero;
            card.sizeDelta = new Vector2(520f, 270f);

            Image cardImage = cardObject.GetComponent<Image>();
            cardImage.color = new Color(0.075f, 0.09f, 0.105f, 0.98f);
            cardImage.raycastTarget = true;

            Shadow shadow = cardObject.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
            shadow.effectDistance = new Vector2(0f, -8f);
            shadow.useGraphicAlpha = true;

            GameObject accent = CreateUiObject("TopAccent", card, typeof(Image));
            RectTransform accentRect = accent.GetComponent<RectTransform>();
            accentRect.anchorMin = new Vector2(0f, 1f);
            accentRect.anchorMax = new Vector2(1f, 1f);
            accentRect.pivot = new Vector2(0.5f, 1f);
            accentRect.anchoredPosition = Vector2.zero;
            accentRect.sizeDelta = new Vector2(0f, 6f);
            accent.GetComponent<Image>().color = new Color(0.25f, 0.76f, 0.7f, 1f);
            accent.GetComponent<Image>().raycastTarget = false;

            CreateText(
                card,
                "TitleText",
                "WEEKLY SETTLEMENT",
                new Vector2(0f, 99f),
                new Vector2(430f, 28f),
                15f,
                FontStyles.Bold,
                new Color(0.93f, 0.95f, 0.96f, 1f));

            GameObject divider = CreateUiObject("Divider", card, typeof(Image));
            RectTransform dividerRect = divider.GetComponent<RectTransform>();
            dividerRect.anchorMin = new Vector2(0.5f, 0.5f);
            dividerRect.anchorMax = new Vector2(0.5f, 0.5f);
            dividerRect.pivot = new Vector2(0.5f, 0.5f);
            dividerRect.anchoredPosition = new Vector2(0f, -37f);
            dividerRect.sizeDelta = new Vector2(430f, 1f);
            divider.GetComponent<Image>().color = new Color(0.29f, 0.33f, 0.36f, 0.8f);
            divider.GetComponent<Image>().raycastTarget = false;

            return card;
        }

        private static TextMeshProUGUI CreateText(
            Transform parent,
            string name,
            string value,
            Vector2 anchoredPosition,
            Vector2 size,
            float fontSize,
            FontStyles fontStyle,
            Color color)
        {
            GameObject textObject = CreateUiObject(name, parent, typeof(TextMeshProUGUI));
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            text.characterSpacing = 0f;
            return text;
        }

        private static GameObject CreateUiObject(string name, Transform parent, params System.Type[] components)
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
