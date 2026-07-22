using CityFlow.Gameplay.Quests;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI.Quests
{
    public sealed class QuestBubbleUI : MonoBehaviour
    {
        private static readonly Color BubbleColor = new(0.075f, 0.10f, 0.12f, 0.97f);
        private static readonly Color AccentColor = new(0.25f, 0.76f, 0.70f, 1f);

        private CityQuestSystem questSystem;
        private GameObject bubble;
        private GameObject minimizedButtonObject;
        private Button closeButton;
        private Button minimizedButton;
        private TextMeshProUGUI titleText;
        private TextMeshProUGUI messageText;

        public static QuestBubbleUI Create(Transform canvasTransform)
        {
            GameObject root = CreateUiObject("QuestBubbleUI", canvasTransform);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            root.transform.SetAsLastSibling();

            QuestBubbleUI controller = root.AddComponent<QuestBubbleUI>();
            controller.BuildVisuals(FindSceneFont());
            return controller;
        }

        public void Bind(CityQuestSystem system)
        {
            if (questSystem != null)
            {
                questSystem.ViewStateChanged -= Refresh;
            }

            questSystem = system;

            if (questSystem != null)
            {
                questSystem.ViewStateChanged += Refresh;
                Refresh(questSystem.CurrentViewState);
            }
            else
            {
                Refresh(default);
            }
        }

        private void BuildVisuals(TMP_FontAsset font)
        {
            bubble = CreateBubble(font);
            minimizedButtonObject = CreateMinimizedButton(font);

            closeButton.onClick.AddListener(OnCloseClicked);
            minimizedButton.onClick.AddListener(OnMinimizedButtonClicked);

            bubble.SetActive(false);
            minimizedButtonObject.SetActive(false);
        }

        private GameObject CreateBubble(TMP_FontAsset font)
        {
            GameObject panel = CreateUiObject("QuestBubble", transform, typeof(Image), typeof(Shadow));
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(28f, 34f);
            rect.sizeDelta = new Vector2(430f, 172f);

            Image background = panel.GetComponent<Image>();
            background.color = BubbleColor;

            Shadow shadow = panel.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
            shadow.effectDistance = new Vector2(0f, -5f);
            shadow.useGraphicAlpha = true;

            GameObject accent = CreateUiObject("Accent", panel.transform, typeof(Image));
            RectTransform accentRect = accent.GetComponent<RectTransform>();
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(0f, 1f);
            accentRect.pivot = new Vector2(0f, 0.5f);
            accentRect.anchoredPosition = Vector2.zero;
            accentRect.sizeDelta = new Vector2(6f, 0f);
            accent.GetComponent<Image>().color = AccentColor;

            GameObject tail = CreateUiObject("BubbleTail", panel.transform, typeof(Image));
            RectTransform tailRect = tail.GetComponent<RectTransform>();
            tailRect.anchorMin = Vector2.zero;
            tailRect.anchorMax = Vector2.zero;
            tailRect.pivot = new Vector2(0.5f, 0.5f);
            tailRect.anchoredPosition = new Vector2(34f, -8f);
            tailRect.sizeDelta = new Vector2(20f, 20f);
            tailRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
            tail.GetComponent<Image>().color = BubbleColor;

            TextMeshProUGUI category = CreateText(
                "Category",
                panel.transform,
                font,
                "시민 요청",
                new Vector2(28f, -18f),
                new Vector2(310f, 22f),
                13f,
                FontStyles.Bold,
                AccentColor);
            category.alignment = TextAlignmentOptions.Left;

            titleText = CreateText(
                "Title",
                panel.transform,
                font,
                string.Empty,
                new Vector2(28f, -46f),
                new Vector2(344f, 32f),
                20f,
                FontStyles.Bold,
                Color.white);
            titleText.alignment = TextAlignmentOptions.Left;

            messageText = CreateText(
                "Message",
                panel.transform,
                font,
                string.Empty,
                new Vector2(28f, -82f),
                new Vector2(368f, 72f),
                16f,
                FontStyles.Normal,
                new Color(0.84f, 0.88f, 0.90f, 1f));
            messageText.alignment = TextAlignmentOptions.TopLeft;
            messageText.textWrappingMode = TextWrappingModes.Normal;
            messageText.overflowMode = TextOverflowModes.Ellipsis;

            GameObject close = CreateUiObject("CloseButton", panel.transform, typeof(Image), typeof(Button));
            RectTransform closeRect = close.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-10f, -10f);
            closeRect.sizeDelta = new Vector2(34f, 34f);

            Image closeImage = close.GetComponent<Image>();
            closeImage.color = new Color(0.18f, 0.22f, 0.24f, 0.95f);
            closeButton = close.GetComponent<Button>();
            closeButton.targetGraphic = closeImage;

            TextMeshProUGUI closeLabel = CreateCenteredText("Label", close.transform, font, "×", 24f, Color.white);
            closeLabel.raycastTarget = false;
            return panel;
        }

        private GameObject CreateMinimizedButton(TMP_FontAsset font)
        {
            GameObject buttonObject = CreateUiObject(
                "QuestMinimizedButton",
                transform,
                typeof(Image),
                typeof(Button),
                typeof(Shadow));
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(28f, 34f);
            rect.sizeDelta = new Vector2(58f, 58f);

            Image image = buttonObject.GetComponent<Image>();
            image.color = AccentColor;

            Shadow shadow = buttonObject.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
            shadow.effectDistance = new Vector2(0f, -4f);

            minimizedButton = buttonObject.GetComponent<Button>();
            minimizedButton.targetGraphic = image;
            TextMeshProUGUI label = CreateCenteredText("Label", buttonObject.transform, font, "!", 30f, Color.white);
            label.fontStyle = FontStyles.Bold;
            label.raycastTarget = false;
            return buttonObject;
        }

        private void Refresh(CityQuestViewState state)
        {
            bool hasQuest = state.Quest != null;
            bubble?.SetActive(hasQuest && !state.IsMinimized);
            minimizedButtonObject?.SetActive(hasQuest && state.IsMinimized);

            if (!hasQuest)
            {
                return;
            }

            titleText.text = state.Quest.Title;
            messageText.text = state.Quest.Message;
        }

        private void OnCloseClicked()
        {
            questSystem?.MinimizeCurrentQuest();
        }

        private void OnMinimizedButtonClicked()
        {
            questSystem?.RestoreCurrentQuest();
        }

        private void OnDestroy()
        {
            closeButton?.onClick.RemoveListener(OnCloseClicked);
            minimizedButton?.onClick.RemoveListener(OnMinimizedButtonClicked);

            if (questSystem != null)
            {
                questSystem.ViewStateChanged -= Refresh;
            }
        }

        private static TextMeshProUGUI CreateText(
            string name,
            Transform parent,
            TMP_FontAsset font,
            string value,
            Vector2 anchoredPosition,
            Vector2 size,
            float fontSize,
            FontStyles style,
            Color color)
        {
            GameObject textObject = CreateUiObject(name, parent, typeof(TextMeshProUGUI));
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        private static TextMeshProUGUI CreateCenteredText(
            string name,
            Transform parent,
            TMP_FontAsset font,
            string value,
            float fontSize,
            Color color)
        {
            GameObject textObject = CreateUiObject(name, parent, typeof(TextMeshProUGUI));
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = value;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            return text;
        }

        private static TMP_FontAsset FindSceneFont()
        {
            TextMeshProUGUI[] texts = FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include);

            foreach (TextMeshProUGUI text in texts)
            {
                if (text.font != null)
                {
                    return text.font;
                }
            }

            return TMP_Settings.defaultFontAsset;
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
    }
}
