using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Gameplay.Quests;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI.Quests
{
    public sealed class QuestBubbleUI : MonoBehaviour
    {
        private static readonly Vector2 BubbleSize =
            new(430f, 172f);
        private static readonly Vector2 QuestControlOffset =
            new(-29f, -29f);
        private const float QuestControlSize = 58f;
        private const float CloseTouchPadding = 7f;
        private const float CloseLineThickness = 4f;
        private const float CloseLineCornerRatio = 0.3f;

        private static readonly Color BubbleColor = new(0.075f, 0.10f, 0.12f, 0.97f);
        private static readonly Color AccentColor = new(0.25f, 0.76f, 0.70f, 1f);

        private CityQuestSystem questSystem;
        private CityFlowServices services;
        private IGameCalendarService calendar;
        private readonly Dictionary<
            int,
            EmergencyIncidentAlertEvent>
            emergencyAlerts = new();
        private CityQuestViewState questViewState;
        private EmergencyIncidentOutcomeEvent latestOutcome;
        private bool hasLatestOutcome;
        private bool emergencyMinimized;
        private float outcomeDisplayRemaining;
        private GameObject bubble;
        private GameObject minimizedButtonObject;
        private Button closeButton;
        private Button minimizedButton;
        private GameObject actionButtonObject;
        private Button actionButton;
        private TextMeshProUGUI actionButtonLabel;
        private TextMeshProUGUI titleText;
        private TextMeshProUGUI messageText;
        private TextMeshProUGUI categoryText;
        private TextMeshProUGUI minimizedLabel;
        private RectTransform anchorTopBar;
        private RectTransform bubbleRect;

        // 축하 연출이 "팝업이 사라진 자리"를 알아야 한다. 루트 RectTransform 은
        // 캔버스 전체로 stretch 되므로 그걸 쓰면 화면 중앙에서 터진다
        // (리뷰 #251). 실제 말풍선 Rect 를 읽기 전용으로 준다.
        public RectTransform BubbleRect => bubbleRect;
        private RectTransform minimizedButtonRect;

        public static QuestBubbleUI Create(Transform canvasTransform)
        {
            return Create(canvasTransform, null);
        }

        public static QuestBubbleUI Create(
            Transform canvasTransform,
            RectTransform topBar)
        {
            GameObject root = CreateUiObject("QuestBubbleUI", canvasTransform);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            root.transform.SetAsLastSibling();

            QuestBubbleUI controller = root.AddComponent<QuestBubbleUI>();
            controller.anchorTopBar = topBar;
            controller.BuildVisuals(FindSceneFont());
            controller.ApplyAnchorLayout();
            return controller;
        }

        private void LateUpdate()
        {
            ApplyAnchorLayout();
        }

        public void Bind(CityQuestSystem system)
        {
            Bind(system, null);
        }

        public void Bind(
            CityQuestSystem system,
            CityFlowServices cityServices)
        {
            if (questSystem != null)
            {
                questSystem.ViewStateChanged -=
                    OnQuestViewStateChanged;
            }

            UnbindServices();
            questSystem = system;
            services = cityServices;

            if (questSystem != null)
            {
                questSystem.ViewStateChanged +=
                    OnQuestViewStateChanged;
                questViewState =
                    questSystem.CurrentViewState;
            }
            else
            {
                questViewState = default;
            }

            if (services?.Events != null)
            {
                services.Events.EmergencyIncidentAlerted +=
                    OnEmergencyIncidentAlerted;
                services.Events
                    .EmergencyIncidentOutcomeReported +=
                    OnEmergencyIncidentOutcome;
                services.GameCalendarRegistered +=
                    OnGameCalendarRegistered;
                BindCalendar(services.GameCalendar);
            }

            Render();
        }

        private void BuildVisuals(TMP_FontAsset font)
        {
            bubble = CreateBubble(font);
            minimizedButtonObject = CreateMinimizedButton(font);

            closeButton.onClick.AddListener(OnCloseClicked);
            minimizedButton.onClick.AddListener(OnMinimizedButtonClicked);
            actionButton.onClick.AddListener(OnActionButtonClicked);

            bubble.SetActive(false);
            minimizedButtonObject.SetActive(false);
            actionButtonObject.SetActive(false);
        }

        private GameObject CreateBubble(TMP_FontAsset font)
        {
            GameObject panel = CreateUiObject("QuestBubble", transform, typeof(Image), typeof(Shadow));
            RectTransform rect = panel.GetComponent<RectTransform>();
            bubbleRect = rect;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = BubbleSize;

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

            categoryText = CreateText(
                "Category",
                panel.transform,
                font,
                "시민 요청",
                new Vector2(28f, -18f),
                new Vector2(310f, 22f),
                13f,
                FontStyles.Bold,
                AccentColor);
            categoryText.alignment =
                TextAlignmentOptions.Left;

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

            actionButtonObject = CreateUiObject(
                "ActionButton",
                panel.transform,
                typeof(Image),
                typeof(Button));
            RectTransform actionRect =
                actionButtonObject.GetComponent<RectTransform>();
            actionRect.anchorMin = new Vector2(1f, 0f);
            actionRect.anchorMax = new Vector2(1f, 0f);
            actionRect.pivot = new Vector2(1f, 0f);
            actionRect.anchoredPosition = new Vector2(-18f, 14f);
            actionRect.sizeDelta = new Vector2(92f, 34f);

            Image actionImage = actionButtonObject.GetComponent<Image>();
            actionImage.color = AccentColor;
            actionButton = actionButtonObject.GetComponent<Button>();
            actionButton.targetGraphic = actionImage;
            actionButtonLabel = CreateCenteredText(
                "Label",
                actionButtonObject.transform,
                font,
                "다음",
                15f,
                Color.white);
            actionButtonLabel.fontStyle = FontStyles.Bold;
            actionButtonLabel.raycastTarget = false;

            GameObject close = CreateUiObject("CloseButton", panel.transform, typeof(Image), typeof(Button));
            RectTransform closeRect = close.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(0.5f, 0.5f);
            closeRect.anchoredPosition = QuestControlOffset;
            closeRect.sizeDelta = new Vector2(
                QuestControlSize,
                QuestControlSize);

            Image closeImage = close.GetComponent<Image>();
            Color closeNormalColor =
                new(0.18f, 0.25f, 0.27f, 1f);
            closeImage.color = closeNormalColor;
            closeImage.raycastPadding = new Vector4(
                -CloseTouchPadding,
                -CloseTouchPadding,
                -CloseTouchPadding,
                -CloseTouchPadding);
            closeButton = close.GetComponent<Button>();
            closeButton.targetGraphic = closeImage;
            ColorBlock closeColors = closeButton.colors;
            closeColors.normalColor = closeNormalColor;
            closeColors.highlightedColor =
                new Color(0.25f, 0.55f, 0.52f, 1f);
            closeColors.pressedColor =
                new Color(0.15f, 0.70f, 0.60f, 1f);
            closeColors.selectedColor = closeColors.highlightedColor;
            closeColors.fadeDuration = 0.08f;
            closeButton.colors = closeColors;

            float closeLineLength =
                QuestControlSize *
                (CloseLineCornerRatio * 2f) /
                Mathf.Sqrt(2f);
            CreateCloseLine(
                "XLineForward",
                close.transform,
                closeLineLength,
                45f);
            CreateCloseLine(
                "XLineBackward",
                close.transform,
                closeLineLength,
                -45f);
            return panel;
        }

        private static void CreateCloseLine(
            string name,
            Transform parent,
            float length,
            float rotation)
        {
            GameObject line = CreateUiObject(
                name,
                parent,
                typeof(Image));
            RectTransform rect = line.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax =
                new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(
                length,
                CloseLineThickness);
            rect.localRotation = Quaternion.Euler(
                0f,
                0f,
                rotation);

            Image image = line.GetComponent<Image>();
            image.color = Color.white;
            image.raycastTarget = false;
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
            minimizedButtonRect = rect;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = QuestControlOffset;
            rect.sizeDelta = new Vector2(
                QuestControlSize,
                QuestControlSize);

            Image image = buttonObject.GetComponent<Image>();
            image.color = AccentColor;

            Shadow shadow = buttonObject.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
            shadow.effectDistance = new Vector2(0f, -4f);

            minimizedButton = buttonObject.GetComponent<Button>();
            minimizedButton.targetGraphic = image;
            minimizedLabel = CreateCenteredText(
                "Label",
                buttonObject.transform,
                font,
                "!",
                30f,
                Color.white);
            minimizedLabel.fontStyle = FontStyles.Bold;
            minimizedLabel.raycastTarget = false;
            return buttonObject;
        }

        private void ApplyAnchorLayout()
        {
            float topBarHeight = anchorTopBar != null
                ? anchorTopBar.rect.height
                : 60f;

            if (bubbleRect != null)
            {
                bubbleRect.anchorMin = new Vector2(1f, 1f);
                bubbleRect.anchorMax = new Vector2(1f, 1f);
                bubbleRect.pivot = new Vector2(1f, 1f);
                bubbleRect.anchoredPosition =
                    new Vector2(
                        0f,
                        -topBarHeight);
            }

            if (minimizedButtonRect != null)
            {
                minimizedButtonRect.anchorMin = new Vector2(1f, 1f);
                minimizedButtonRect.anchorMax = new Vector2(1f, 1f);
                minimizedButtonRect.pivot = new Vector2(0.5f, 0.5f);
                minimizedButtonRect.anchoredPosition =
                    new Vector2(
                        QuestControlOffset.x,
                        -topBarHeight + QuestControlOffset.y);
            }
        }

        private void Update()
        {
            if (!hasLatestOutcome)
            {
                return;
            }

            outcomeDisplayRemaining -=
                Time.unscaledDeltaTime;
            if (outcomeDisplayRemaining <= 0f)
            {
                hasLatestOutcome = false;
                Render();
            }
        }

        private void OnQuestViewStateChanged(
            CityQuestViewState state)
        {
            questViewState = state;
            Render();
        }

        private void OnEmergencyIncidentAlerted(
            EmergencyIncidentAlertEvent alert)
        {
            emergencyAlerts[alert.IncidentId] = alert;
            emergencyMinimized = false;
            Render();
        }

        private void OnEmergencyIncidentOutcome(
            EmergencyIncidentOutcomeEvent outcome)
        {
            emergencyAlerts.Remove(outcome.IncidentId);
            latestOutcome = outcome;
            hasLatestOutcome = true;
            outcomeDisplayRemaining = 5f;
            emergencyMinimized = false;
            Render();
        }

        private void OnGameCalendarRegistered(
            IGameCalendarService gameCalendar)
        {
            BindCalendar(gameCalendar);
            Render();
        }

        private void BindCalendar(
            IGameCalendarService gameCalendar)
        {
            if (ReferenceEquals(calendar, gameCalendar))
            {
                return;
            }

            if (calendar != null)
            {
                calendar.HourChanged -= OnCalendarChanged;
            }

            calendar = gameCalendar;

            if (calendar != null)
            {
                calendar.HourChanged += OnCalendarChanged;
            }
        }

        private void OnCalendarChanged(int _)
        {
            Render();
        }

        private void Render()
        {
            if (hasLatestOutcome)
            {
                SetActionVisible(false);
                SetVisible(
                    visible: true,
                    minimized: false);
                categoryText.text =
                    latestOutcome.Outcome ==
                    EmergencyIncidentOutcome.Resolved
                        ? "응급 구조 성공"
                        : "응급 구조 실패";
                titleText.text = latestOutcome.Title;
                messageText.text = latestOutcome.Message;
                return;
            }

            if (TryGetUrgentAlert(
                    out EmergencyIncidentAlertEvent
                        alert))
            {
                SetActionVisible(false);
                SetVisible(
                    visible: true,
                    minimized: emergencyMinimized);
                categoryText.text = "긴급 출동 요청";
                titleText.text = alert.Title;
                messageText.text =
                    BuildEmergencyMessage(alert);
                minimizedLabel.text =
                    emergencyAlerts.Count > 1
                        ? emergencyAlerts.Count.ToString()
                        : "!";
                return;
            }

            bool hasQuest = questViewState.Quest != null;
            SetVisible(
                hasQuest,
                hasQuest && questViewState.IsMinimized);

            if (!hasQuest)
            {
                SetActionVisible(false);
                return;
            }

            categoryText.text = "도시 요청";
            titleText.text = questViewState.Quest.Title;
            messageText.text =
                questViewState.Quest.Message;
            minimizedLabel.text = "!";
            SetActionVisible(
                questViewState.Quest.CanAcknowledge &&
                !questViewState.IsMinimized,
                questViewState.Quest.ActionLabel);
        }

        private void SetActionVisible(
            bool visible,
            string label = "")
        {
            actionButtonObject?.SetActive(visible);
            if (actionButtonLabel != null && visible)
            {
                actionButtonLabel.text = string.IsNullOrWhiteSpace(label)
                    ? "확인"
                    : label;
            }

            if (messageText != null)
            {
                messageText.rectTransform.sizeDelta =
                    new Vector2(visible ? 270f : 368f, 72f);
            }
        }

        private void SetVisible(
            bool visible,
            bool minimized)
        {
            bubble?.SetActive(visible && !minimized);
            minimizedButtonObject?.SetActive(
                visible && minimized);
        }

        private bool TryGetUrgentAlert(
            out EmergencyIncidentAlertEvent result)
        {
            result = default;
            bool found = false;

            foreach (EmergencyIncidentAlertEvent alert
                     in emergencyAlerts.Values)
            {
                if (!found ||
                    alert.DeadlineAbsoluteHour <
                    result.DeadlineAbsoluteHour)
                {
                    result = alert;
                    found = true;
                }
            }

            return found;
        }

        private string BuildEmergencyMessage(
            EmergencyIncidentAlertEvent alert)
        {
            if (calendar == null)
            {
                return alert.Message;
            }

            long currentHour =
                calendar.TotalDays *
                Math.Max(1, calendar.HoursPerDay) +
                Math.Max(0, calendar.Hour);
            long remaining = Math.Max(
                0L,
                alert.DeadlineAbsoluteHour -
                currentHour);
            string suffix =
                $"제한 시간: {remaining}시간 남음";
            return string.IsNullOrWhiteSpace(alert.Message)
                ? suffix
                : $"{alert.Message}\n{suffix}";
        }

        private void OnCloseClicked()
        {
            if (hasLatestOutcome)
            {
                hasLatestOutcome = false;
                Render();
            }
            else if (emergencyAlerts.Count > 0)
            {
                emergencyMinimized = true;
                Render();
            }
            else
            {
                questSystem?.MinimizeCurrentQuest();
            }
        }

        private void OnMinimizedButtonClicked()
        {
            if (emergencyAlerts.Count > 0)
            {
                emergencyMinimized = false;
                Render();
            }
            else
            {
                questSystem?.RestoreCurrentQuest();
            }
        }

        private void OnActionButtonClicked()
        {
            questSystem?.AcknowledgeCurrentQuest();
        }

        private void OnDestroy()
        {
            closeButton?.onClick.RemoveListener(OnCloseClicked);
            minimizedButton?.onClick.RemoveListener(OnMinimizedButtonClicked);
            actionButton?.onClick.RemoveListener(OnActionButtonClicked);

            if (questSystem != null)
            {
                questSystem.ViewStateChanged -=
                    OnQuestViewStateChanged;
            }

            UnbindServices();
        }

        private void UnbindServices()
        {
            if (services?.Events != null)
            {
                services.Events.EmergencyIncidentAlerted -=
                    OnEmergencyIncidentAlerted;
                services.Events
                    .EmergencyIncidentOutcomeReported -=
                    OnEmergencyIncidentOutcome;
                services.GameCalendarRegistered -=
                    OnGameCalendarRegistered;
            }

            BindCalendar(null);
            services = null;
            emergencyAlerts.Clear();
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
