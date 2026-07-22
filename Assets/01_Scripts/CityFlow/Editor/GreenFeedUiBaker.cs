using CityFlow.UI.Feed;
using CityFlow.Feed;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CityFlow.EditorTools
{
    public static class GreenFeedUiBaker
    {
        private const string TargetCanvasName = "UI_MainCanvas";
        private const string ContentRootName = "FloatingWindowContentRoot";
        private const string FeedRootName = "GreenSNSFeedDock";
        private const string KoreanFontPath = "Assets/03_Art/Fonts/NanumGothic SDF.asset";
        private const string LegacyKoreanFontPath = "Assets/99_Download/Fonts/NanumGothic SDF.asset";

        private static TMP_FontAsset uiFont;

        [MenuItem("Tools/GreenLight/UI/Bake Green SNS Feed")]
        public static void Bake()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[GreenFeedUiBaker] Exit Play Mode before baking the feed UI.");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning("[GreenFeedUiBaker] Open a target scene before baking the feed UI.");
                return;
            }

            Canvas canvas = FindCanvas(scene);
            if (canvas == null)
            {
                Debug.LogError(
                    $"[GreenFeedUiBaker] Canvas '{TargetCanvasName}' was not found in '{scene.name}'.");
                return;
            }

            uiFont = LoadUiFont(scene);
            Transform parent = FindTransform(scene, ContentRootName) ?? canvas.transform;
            RemoveExistingFeed(scene);

            GameObject root = CreateUiObject(FeedRootName, parent);
            Undo.RegisterCreatedObjectUndo(root, "Bake Green SNS Feed");
            RectTransform rootRect = root.GetComponent<RectTransform>();
            SetRightCenter(rootRect, new Vector2(-8f, 0f), new Vector2(374f, 520f));
            root.transform.SetAsLastSibling();

            GreenFeedPanelController controller = root.AddComponent<GreenFeedPanelController>();
            CitizenFeedV1Assets feedAssets = CitizenFeedDataGenerator.CreateOrLoadDefaults();
            CitizenFeedService feedService = root.AddComponent<CitizenFeedService>();
            feedService.Configure(
                feedAssets.Settings,
                feedAssets.Rules,
                feedAssets.Authors,
                feedAssets.TemplateCollections);
            GreenFeedPresenter presenter = root.AddComponent<GreenFeedPresenter>();
            presenter.Configure(feedService, controller);
            RectTransform panel = CreatePanel(root.transform, out CanvasGroup panelCanvasGroup);
            ScrollRect scrollRect = CreateFeedScroll(panel, out RectTransform content);
            GreenFeedPostView template = CreatePost(
                content,
                "FeedPostTemplate",
                "김시민",
                "시민",
                "도시의 새로운 소식이 여기에 표시됩니다.",
                "방금",
                "김",
                new Color(0.20f, 0.78f, 0.55f, 1f));
            template.gameObject.SetActive(false);
            CreatePreviewPosts(content, template);

            GreenFeedHoverRelay panelRelay = panel.gameObject.AddComponent<GreenFeedHoverRelay>();
            panelRelay.Configure(controller);
            CreateHoverHandle(root.transform, controller);

            Vector2 shownPosition = new Vector2(-12f, 0f);
            Vector2 hiddenPosition = new Vector2(354f, 0f);
            panel.anchoredPosition = shownPosition;
            controller.Configure(
                panel,
                panelCanvasGroup,
                scrollRect,
                content,
                template,
                shownPosition,
                hiddenPosition);

            EditorUtility.SetDirty(root);
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            Debug.Log(
                $"[GreenFeedUiBaker] Green SNS feed baked into '{scene.name}'. " +
                "Hover the right-center edge in Play Mode to open it; mouse-wheel input stays inside the feed.");
        }

        [MenuItem("Tools/GreenLight/UI/Bake Green SNS Feed", true)]
        private static bool ValidateBake()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private static RectTransform CreatePanel(Transform parent, out CanvasGroup canvasGroup)
        {
            GameObject panelObject = CreateUiObject(
                "GreenSNSPanel",
                parent,
                typeof(Image),
                typeof(Shadow),
                typeof(CanvasGroup));
            RectTransform panel = panelObject.GetComponent<RectTransform>();
            SetRightCenter(panel, new Vector2(-12f, 0f), new Vector2(350f, 516f));

            Image background = panelObject.GetComponent<Image>();
            background.color = new Color(0.028f, 0.045f, 0.041f, 0.985f);
            background.raycastTarget = true;

            Shadow shadow = panelObject.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
            shadow.effectDistance = new Vector2(-8f, -2f);
            shadow.useGraphicAlpha = true;

            canvasGroup = panelObject.GetComponent<CanvasGroup>();
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;

            CreateHeader(panel);
            return panel;
        }

        private static void CreateHeader(RectTransform panel)
        {
            GameObject header = CreateUiObject("Header", panel, typeof(Image));
            RectTransform headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.anchoredPosition = Vector2.zero;
            headerRect.sizeDelta = new Vector2(0f, 78f);
            Image headerImage = header.GetComponent<Image>();
            headerImage.color = new Color(0.045f, 0.085f, 0.071f, 1f);
            headerImage.raycastTarget = false;

            TMP_Text title = CreateText(
                header.transform,
                "Title",
                "GREEN SNS",
                new Vector2(22f, -17f),
                new Vector2(210f, 30f),
                22f,
                FontStyles.Bold,
                new Color(0.91f, 0.97f, 0.94f, 1f),
                TextAlignmentOptions.MidlineLeft);
            SetTopLeft(title.rectTransform);

            TMP_Text subtitle = CreateText(
                header.transform,
                "Subtitle",
                "CITY PULSE",
                new Vector2(23f, -48f),
                new Vector2(180f, 20f),
                11f,
                FontStyles.Bold,
                new Color(0.36f, 0.70f, 0.58f, 1f),
                TextAlignmentOptions.MidlineLeft);
            SetTopLeft(subtitle.rectTransform);

            GameObject liveDot = CreateUiObject("LiveDot", header.transform, typeof(Image));
            RectTransform dotRect = liveDot.GetComponent<RectTransform>();
            dotRect.anchorMin = new Vector2(1f, 1f);
            dotRect.anchorMax = new Vector2(1f, 1f);
            dotRect.pivot = new Vector2(1f, 1f);
            dotRect.anchoredPosition = new Vector2(-53f, -28f);
            dotRect.sizeDelta = new Vector2(7f, 7f);
            Image dotImage = liveDot.GetComponent<Image>();
            dotImage.color = new Color(0.32f, 0.92f, 0.52f, 1f);
            dotImage.raycastTarget = false;

            TMP_Text live = CreateText(
                header.transform,
                "LiveText",
                "LIVE",
                new Vector2(-18f, -30f),
                new Vector2(40f, 18f),
                10f,
                FontStyles.Bold,
                new Color(0.63f, 0.84f, 0.73f, 1f),
                TextAlignmentOptions.Center);
            RectTransform liveRect = live.rectTransform;
            liveRect.anchorMin = new Vector2(1f, 1f);
            liveRect.anchorMax = new Vector2(1f, 1f);
            liveRect.pivot = new Vector2(1f, 1f);

            GameObject divider = CreateUiObject("HeaderDivider", panel, typeof(Image));
            RectTransform dividerRect = divider.GetComponent<RectTransform>();
            dividerRect.anchorMin = new Vector2(0f, 1f);
            dividerRect.anchorMax = new Vector2(1f, 1f);
            dividerRect.pivot = new Vector2(0.5f, 1f);
            dividerRect.anchoredPosition = new Vector2(0f, -78f);
            dividerRect.sizeDelta = new Vector2(0f, 1f);
            Image dividerImage = divider.GetComponent<Image>();
            dividerImage.color = new Color(0.16f, 0.32f, 0.26f, 0.85f);
            dividerImage.raycastTarget = false;
        }

        private static ScrollRect CreateFeedScroll(RectTransform panel, out RectTransform content)
        {
            GameObject scrollObject = CreateUiObject(
                "FeedScrollView",
                panel,
                typeof(Image),
                typeof(ScrollRect));
            RectTransform scrollTransform = scrollObject.GetComponent<RectTransform>();
            scrollTransform.anchorMin = Vector2.zero;
            scrollTransform.anchorMax = Vector2.one;
            scrollTransform.pivot = new Vector2(0.5f, 0.5f);
            scrollTransform.offsetMin = new Vector2(10f, 10f);
            scrollTransform.offsetMax = new Vector2(-10f, -88f);
            Image scrollImage = scrollObject.GetComponent<Image>();
            scrollImage.color = new Color(0.02f, 0.035f, 0.032f, 0.4f);
            scrollImage.raycastTarget = true;

            GameObject viewportObject = CreateUiObject(
                "Viewport",
                scrollObject.transform,
                typeof(Image),
                typeof(RectMask2D));
            RectTransform viewport = viewportObject.GetComponent<RectTransform>();
            SetStretch(viewport);
            Image viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.color = Color.clear;
            viewportImage.raycastTarget = true;

            GameObject contentObject = CreateUiObject(
                "Content",
                viewport,
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            content = contentObject.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            VerticalLayoutGroup layout = contentObject.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.spacing = 0f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = contentObject.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scrollRect = scrollObject.GetComponent<ScrollRect>();
            scrollRect.viewport = viewport;
            scrollRect.content = content;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.inertia = true;
            scrollRect.decelerationRate = 0.12f;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 28f;
            scrollRect.horizontalScrollbar = null;
            scrollRect.verticalScrollbar = null;
            return scrollRect;
        }

        private static GreenFeedPostView CreatePost(
            Transform parent,
            string objectName,
            string authorName,
            string occupation,
            string message,
            string timestamp,
            string avatarInitial,
            Color accentColor)
        {
            GameObject root = CreateUiObject(
                objectName,
                parent,
                typeof(Image),
                typeof(LayoutElement),
                typeof(GreenFeedPostView));
            Image background = root.GetComponent<Image>();
            background.color = new Color(0.038f, 0.060f, 0.054f, 0.92f);
            background.raycastTarget = false;

            LayoutElement layout = root.GetComponent<LayoutElement>();
            layout.preferredHeight = 126f;
            layout.flexibleWidth = 1f;

            GameObject accentObject = CreateUiObject("Accent", root.transform, typeof(Image));
            RectTransform accentRect = accentObject.GetComponent<RectTransform>();
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(0f, 1f);
            accentRect.pivot = new Vector2(0f, 0.5f);
            accentRect.anchoredPosition = Vector2.zero;
            accentRect.sizeDelta = new Vector2(3f, 0f);
            Image accent = accentObject.GetComponent<Image>();
            accent.raycastTarget = false;

            GameObject avatarObject = CreateUiObject("Avatar", root.transform, typeof(Image));
            RectTransform avatarRect = avatarObject.GetComponent<RectTransform>();
            avatarRect.anchorMin = new Vector2(0f, 1f);
            avatarRect.anchorMax = new Vector2(0f, 1f);
            avatarRect.pivot = new Vector2(0f, 1f);
            avatarRect.anchoredPosition = new Vector2(16f, -16f);
            avatarRect.sizeDelta = new Vector2(38f, 38f);
            Image avatar = avatarObject.GetComponent<Image>();
            avatar.raycastTarget = false;

            TMP_Text initialText = CreateText(
                avatarObject.transform,
                "Initial",
                avatarInitial,
                Vector2.zero,
                new Vector2(38f, 38f),
                15f,
                FontStyles.Bold,
                new Color(0.90f, 0.97f, 0.93f, 1f),
                TextAlignmentOptions.Center);

            TMP_Text authorText = CreateText(
                root.transform,
                "AuthorName",
                authorName,
                new Vector2(66f, -14f),
                new Vector2(148f, 22f),
                14f,
                FontStyles.Bold,
                new Color(0.92f, 0.96f, 0.94f, 1f),
                TextAlignmentOptions.MidlineLeft);
            SetTopLeft(authorText.rectTransform);

            TMP_Text occupationText = CreateText(
                root.transform,
                "Occupation",
                occupation,
                new Vector2(66f, -36f),
                new Vector2(160f, 18f),
                10f,
                FontStyles.Normal,
                new Color(0.42f, 0.68f, 0.57f, 1f),
                TextAlignmentOptions.MidlineLeft);
            SetTopLeft(occupationText.rectTransform);

            TMP_Text timeText = CreateText(
                root.transform,
                "Timestamp",
                timestamp,
                new Vector2(-14f, -17f),
                new Vector2(92f, 18f),
                10f,
                FontStyles.Normal,
                new Color(0.45f, 0.53f, 0.50f, 1f),
                TextAlignmentOptions.MidlineRight);
            RectTransform timeRect = timeText.rectTransform;
            timeRect.anchorMin = new Vector2(1f, 1f);
            timeRect.anchorMax = new Vector2(1f, 1f);
            timeRect.pivot = new Vector2(1f, 1f);

            TMP_Text messageText = CreateText(
                root.transform,
                "Message",
                message,
                new Vector2(16f, -66f),
                new Vector2(-32f, 54f),
                13f,
                FontStyles.Normal,
                new Color(0.78f, 0.84f, 0.81f, 1f),
                TextAlignmentOptions.TopLeft);
            RectTransform messageRect = messageText.rectTransform;
            messageRect.anchorMin = new Vector2(0f, 1f);
            messageRect.anchorMax = new Vector2(1f, 1f);
            messageRect.pivot = new Vector2(0f, 1f);
            messageText.textWrappingMode = TextWrappingModes.Normal;
            messageText.overflowMode = TextOverflowModes.Ellipsis;

            GameObject separator = CreateUiObject("Separator", root.transform, typeof(Image));
            RectTransform separatorRect = separator.GetComponent<RectTransform>();
            separatorRect.anchorMin = new Vector2(0f, 0f);
            separatorRect.anchorMax = new Vector2(1f, 0f);
            separatorRect.pivot = new Vector2(0.5f, 0f);
            separatorRect.anchoredPosition = Vector2.zero;
            separatorRect.sizeDelta = new Vector2(0f, 1f);
            Image separatorImage = separator.GetComponent<Image>();
            separatorImage.color = new Color(0.13f, 0.22f, 0.19f, 0.85f);
            separatorImage.raycastTarget = false;

            GreenFeedPostView view = root.GetComponent<GreenFeedPostView>();
            view.Configure(
                accent,
                avatar,
                initialText,
                authorText,
                occupationText,
                messageText,
                timeText,
                layout);
            view.Bind(authorName, occupation, message, timestamp, avatarInitial, accentColor);
            return view;
        }

        private static void CreatePreviewPosts(Transform content, GreenFeedPostView template)
        {
            CreatePreviewPost(
                content,
                template,
                "이서준",
                "출근러",
                "동쪽 교차로, 오늘도 신호를 두 번 기다렸네요. 지각 직전입니다.",
                "1분 전",
                "이",
                new Color(0.30f, 0.75f, 0.92f, 1f));
            CreatePreviewPost(
                content,
                template,
                "박하린",
                "학부모",
                "학교 앞 도로가 한결 편해졌어요. 아이 데려다주기 훨씬 낫네요.",
                "4분 전",
                "박",
                new Color(0.93f, 0.68f, 0.35f, 1f));
            CreatePreviewPost(
                content,
                template,
                "최민호",
                "가게 사장",
                "상가 앞 흐름이 살아났습니다. 점심 손님도 조금씩 늘겠네요.",
                "8분 전",
                "최",
                new Color(0.35f, 0.83f, 0.56f, 1f));
            CreatePreviewPost(
                content,
                template,
                "윤태식",
                "택시기사",
                "중앙대로는 지금 정체입니다. 우회하실 분은 서쪽길을 추천합니다.",
                "12분 전",
                "윤",
                new Color(0.83f, 0.48f, 0.42f, 1f));
            CreatePreviewPost(
                content,
                template,
                "정수아",
                "동네 주민",
                "새 신호 덕분인지 퇴근길이 조용하네요. 이 흐름 오래 갔으면 좋겠어요.",
                "18분 전",
                "정",
                new Color(0.63f, 0.56f, 0.91f, 1f));
        }

        private static void CreatePreviewPost(
            Transform content,
            GreenFeedPostView template,
            string authorName,
            string occupation,
            string message,
            string timestamp,
            string initial,
            Color accentColor)
        {
            GreenFeedPostView post = Object.Instantiate(template, content);
            post.gameObject.name = $"Preview_{authorName}";
            post.gameObject.SetActive(true);
            post.Bind(authorName, occupation, message, timestamp, initial, accentColor);
        }

        private static void CreateHoverHandle(Transform parent, GreenFeedPanelController controller)
        {
            GameObject handleObject = CreateUiObject(
                "HoverHandle",
                parent,
                typeof(Image),
                typeof(GreenFeedHoverRelay));
            RectTransform handle = handleObject.GetComponent<RectTransform>();
            SetRightCenter(handle, Vector2.zero, new Vector2(22f, 112f));
            Image hitArea = handleObject.GetComponent<Image>();
            hitArea.color = new Color(0f, 0f, 0f, 0.002f);
            hitArea.raycastTarget = true;
            handleObject.GetComponent<GreenFeedHoverRelay>().Configure(controller);

            GameObject railObject = CreateUiObject("EdgeSignal", handle, typeof(Image));
            RectTransform rail = railObject.GetComponent<RectTransform>();
            rail.anchorMin = new Vector2(1f, 0.5f);
            rail.anchorMax = new Vector2(1f, 0.5f);
            rail.pivot = new Vector2(1f, 0.5f);
            rail.anchoredPosition = Vector2.zero;
            rail.sizeDelta = new Vector2(4f, 58f);
            Image railImage = railObject.GetComponent<Image>();
            railImage.color = new Color(0.22f, 0.82f, 0.56f, 0.92f);
            railImage.raycastTarget = false;
        }

        private static TMP_Text CreateText(
            Transform parent,
            string name,
            string value,
            Vector2 anchoredPosition,
            Vector2 size,
            float fontSize,
            FontStyles style,
            Color color,
            TextAlignmentOptions alignment)
        {
            GameObject textObject = CreateUiObject(name, parent, typeof(TextMeshProUGUI));
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            if (uiFont != null)
            {
                text.font = uiFont;
            }

            text.text = value;
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

        private static void SetTopLeft(RectTransform rect)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
        }

        private static void SetRightCenter(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void SetStretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
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

        private static Canvas FindCanvas(Scene scene)
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

        private static Transform FindTransform(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
                foreach (Transform candidate in transforms)
                {
                    if (candidate.name == objectName)
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        private static TMP_FontAsset LoadUiFont(Scene scene)
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KoreanFontPath)
                ?? AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LegacyKoreanFontPath);
            if (font != null)
            {
                return font;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                TMP_Text existingText = root.GetComponentInChildren<TMP_Text>(true);
                if (existingText != null && existingText.font != null)
                {
                    return existingText.font;
                }
            }

            Debug.LogWarning(
                "[GreenFeedUiBaker] A Korean TMP font was not found. " +
                "The current scene font will be used where available.");
            return TMP_Settings.defaultFontAsset;
        }

        private static void RemoveExistingFeed(Scene scene)
        {
            Transform existing = FindTransform(scene, FeedRootName);
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing.gameObject);
            }
        }

        // Unity setup: Open the target scene and run Tools > GreenLight > UI > Bake Green SNS Feed.
    }
}
