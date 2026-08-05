using CityFlow.UI;
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

        // 화면에 노출되는 SNS 이름. 클래스·오브젝트 이름(GreenFeed*)은 그대로 둔다 —
        // 브랜딩만 바꾸는 데 파일 전체를 개명할 이유가 없다.
        private const string AppName = "아이들북";
        private const string AppTagline = "우리 동네 소식";

        // 다른 베이커(SignalControlBaker·GeonSignalUIAssembler)와 같은 팩·같은 상수명이다.
        private const string MonoRound =
            "Assets/99_Download/Layer Lab/GUI-MonoRound/ResourcesData/Sprites/Components/";
        private const string PanelFramePath = MonoRound + "Popup/Popup01_Frame1.png";
        private const string HeaderBarPath = MonoRound + "Popup/Popup01_TopBar.png";
        private const string RowFramePath = MonoRound + "UI_Etc/Label_Rectangle_White2.png";
        private const string TickerFramePath = MonoRound + "UI_Etc/Label_Rectangle_White1.png";
        private const string AvatarCirclePath = MonoRound + "Frame/Frame_Basic_Circle_112.png";
        private const string CloseButtonPath = MonoRound + "Button/Btn_Basic_Circle_White.png";
        private const string CloseIconPath = MonoRound + "Icon_PictoIcons/pictoicon_close.png";
        private const string AppIconPath = MonoRound + "Icon_PictoIcons/pictoicon_book.png";

        // 네이비 + 연두. 표면은 기존 UI와 같은 계열로 맞춘다 —
        // 타이틀바 Color32(25,30,35,248), 퀘스트 말풍선 (0.075,0.10,0.12,0.97).
        // MonoRound 프레임은 전부 흰색이라 틴트로 그대로 네이비가 된다.
        private static readonly Color AccentColor = new Color32(154, 222, 84, 255);
        private static readonly Color PanelColor = new Color32(23, 29, 38, 250);
        private static readonly Color HeaderColor = new Color32(32, 40, 52, 255);
        private static readonly Color RowColor = new Color32(31, 38, 49, 255);
        private static readonly Color ScrollColor = new Color32(17, 22, 30, 255);
        private static readonly Color TextStrong = new Color32(228, 234, 240, 255);
        private static readonly Color TextMuted = new Color32(152, 164, 177, 255);
        private static readonly Color TextFaint = new Color32(106, 118, 132, 255);

        // 같은 부모(FloatingWindowContentRoot)에 붙는 Dock_Right의 실측값이다.
        // anchor·pivot (1,0) / anchoredPosition (-20,20) / sizeDelta (60,240)
        // → 도크가 먹는 영역은 x -80~-20, y 20~260.
        // 티커는 그 왼쪽에 나란히 앉힌다(겹치면 클릭이 서로 먹는다).
        private const float DockLeftEdge = -80f;
        private const float DockBottomEdge = 20f;
        // 본문이 잘리지 않을 만큼 넓게. 520일 때 본문이 342px(≈28자)라
        // 대부분의 템플릿 문장이 중간에서 끊겼다. 760이면 582px(≈48자)로
        // 현재 가장 긴 문장(45자)까지 들어간다.
        private static readonly Vector2 TickerSize = new Vector2(760f, 54f);
        // 도크 왼쪽에 서므로 세로는 자유롭다 — 바닥에서 조금 띄워 올린다.
        private static readonly Vector2 TickerPosition =
            new Vector2(DockLeftEdge - 12f, DockBottomEdge + 58f);

        // 히스토리는 화면 중앙 팝업이다 — 자리를 옮기지 않으므로 열림/숨김
        // 좌표가 없다. 씬에서 잡아둔 위치가 곧 표시 위치다.
        // 폭 460일 때 행 본문이 202px(≈17자)까지 좁아져 티커보다 심하게 잘렸다.
        // 640이면 382px(≈32자)로 대부분 들어가면서 화면도 덜 가린다.
        private static readonly Vector2 PanelSize = new Vector2(640f, 520f);

        private const float RowHeight = 44f;
        private const float HeaderHeight = 52f;
        // Label_Rectangle의 9-slice 보더는 28px이라 44px 행에선 위아래가 겹친다.
        // 배율 2로 나눠 유효 보더를 14px로 낮춘다.
        private const float RowBorderScale = 2f;
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

            uiFont = LoadRequiredUiFont();
            if (uiFont == null)
            {
                return;
            }

            Transform parent = FindTransform(scene, ContentRootName) ?? canvas.transform;
            // 손으로 맞춘 배치는 재베이킹으로 날리지 않는다.
            bool hasSavedLayout = TryCaptureLayout(scene, out SavedLayout savedLayout);
            RemoveExistingFeed(scene);

            GameObject root = CreateUiObject(FeedRootName, parent);
            Undo.RegisterCreatedObjectUndo(root, "Bake Green SNS Feed");
            RectTransform rootRect = root.GetComponent<RectTransform>();
            // 티커와 패널이 각자 화면 우측중앙 기준으로 앉는다 — 루트를 늘려 두면
            // 자식 좌표가 곧 화면 좌표라 도크 y값과 직접 맞출 수 있다.
            SetStretch(rootRect);
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
            RectTransform panel = CreatePanel(
                root.transform,
                controller,
                out CanvasGroup panelCanvasGroup);
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
            // 히스토리 행만 클릭하면 그 지점으로 간다. 템플릿에 붙이면
            // AddPost가 Instantiate할 때 런타임 글에 그대로 따라간다.
            // 미리보기 행도 복사본이라 같이 붙지만 좌표가 없어 아무 일도 하지 않고,
            // 런타임엔 어차피 지워진다.
            TileSelectionController tileSelection =
                FindInActiveScene<TileSelectionController>(scene);
            if (tileSelection == null)
            {
                Debug.LogWarning(
                    "[GreenFeedUiBaker] TileSelectionController를 찾지 못했습니다. " +
                    "글을 눌러도 지점으로 이동하지 않습니다(나머지 기능은 정상).");
            }

            template.gameObject.AddComponent<GreenFeedHoverRelay>().Configure(
                controller,
                GreenFeedHoverRelay.ClickAction.Locate,
                false,
                tileSelection);

            template.gameObject.SetActive(false);
            CreatePreviewPosts(content, template);

            // 패널은 스크롤되므로 휠 가드를 잡는다. 클릭은 아무 동작도 하지 않는다 —
            // 패널 안을 누르다가 닫히면 곤란하다.
            panel.gameObject.AddComponent<GreenFeedHoverRelay>().Configure(
                controller,
                GreenFeedHoverRelay.ClickAction.None,
                true);
            GreenFeedPostView ticker = CreateTicker(root.transform, controller);

            if (hasSavedLayout)
            {
                savedLayout.ApplyTo(panel, ticker.GetComponent<RectTransform>());
                Debug.Log("[GreenFeedUiBaker] 이전 베이킹의 티커·패널 배치를 이어받았습니다.");
            }

            controller.Configure(
                panel,
                panelCanvasGroup,
                scrollRect,
                content,
                template,
                ticker);

            EditorUtility.SetDirty(root);
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            Debug.Log(
                $"[GreenFeedUiBaker] '{AppName}' 피드를 '{scene.name}'에 구웠습니다. " +
                "우하단 도크 왼쪽의 한 줄 티커를 누르면 중앙에 히스토리가 열립니다.");
        }

        [MenuItem("Tools/GreenLight/UI/Bake Green SNS Feed", true)]
        private static bool ValidateBake()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private static RectTransform CreatePanel(
            Transform parent,
            GreenFeedPanelController controller,
            out CanvasGroup canvasGroup)
        {
            GameObject panelObject = CreateUiObject(
                "GreenSNSPanel",
                parent,
                typeof(Image),
                typeof(Shadow),
                typeof(CanvasGroup));
            RectTransform panel = panelObject.GetComponent<RectTransform>();
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = PanelSize;

            Image background = panelObject.GetComponent<Image>();
            ApplySprite(background, PanelFramePath);
            background.color = PanelColor;
            background.raycastTarget = true;

            Shadow shadow = panelObject.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
            shadow.effectDistance = new Vector2(0f, -6f);
            shadow.useGraphicAlpha = true;

            canvasGroup = panelObject.GetComponent<CanvasGroup>();
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;

            CreateHeader(panel, controller);
            return panel;
        }

        private static void CreateHeader(
            RectTransform panel,
            GreenFeedPanelController controller)
        {
            GameObject header = CreateUiObject("Header", panel, typeof(Image));
            RectTransform headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.anchoredPosition = Vector2.zero;
            headerRect.sizeDelta = new Vector2(0f, HeaderHeight);
            Image headerImage = header.GetComponent<Image>();
            // TopBar는 가로 보더만(45/0/46/0) 있어서 높이를 바꿔도 안 뭉개진다.
            ApplySprite(headerImage, HeaderBarPath);
            headerImage.color = HeaderColor;
            headerImage.raycastTarget = false;

            Image appIcon = CreateIcon(
                header.transform,
                "AppIcon",
                AppIconPath,
                new Vector2(18f, -26f),
                new Vector2(22f, 22f),
                AccentColor);
            SetLeftCenterOfTop(appIcon.rectTransform);

            TMP_Text title = CreateText(
                header.transform,
                "Title",
                AppName,
                new Vector2(48f, -20f),
                new Vector2(200f, 22f),
                18f,
                FontStyles.Bold,
                TextStrong,
                TextAlignmentOptions.MidlineLeft);
            SetTopLeft(title.rectTransform);

            TMP_Text tagline = CreateText(
                header.transform,
                "Tagline",
                AppTagline,
                new Vector2(48f, -38f),
                new Vector2(200f, 16f),
                10f,
                FontStyles.Normal,
                TextFaint,
                TextAlignmentOptions.MidlineLeft);
            SetTopLeft(tagline.rectTransform);

            CreateCloseButton(header.transform, controller);

            GameObject divider = CreateUiObject("HeaderDivider", panel, typeof(Image));
            RectTransform dividerRect = divider.GetComponent<RectTransform>();
            dividerRect.anchorMin = new Vector2(0f, 1f);
            dividerRect.anchorMax = new Vector2(1f, 1f);
            dividerRect.pivot = new Vector2(0.5f, 1f);
            dividerRect.anchoredPosition = new Vector2(0f, -HeaderHeight);
            dividerRect.sizeDelta = new Vector2(0f, 2f);
            Image dividerImage = divider.GetComponent<Image>();
            // 헤더 밑 연두 선 하나가 브랜드 컬러를 잡아준다 — 헤더 전체를 연두로
            // 칠하면 어두운 다른 UI 사이에서 혼자 튄다.
            dividerImage.color = AccentColor;
            dividerImage.raycastTarget = false;
        }

        private static void CreateCloseButton(
            Transform header,
            GreenFeedPanelController controller)
        {
            GameObject closeObject = CreateUiObject(
                "CloseButton",
                header,
                typeof(Image),
                typeof(Button));
            RectTransform closeRect = closeObject.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 0.5f);
            closeRect.anchorMax = new Vector2(1f, 0.5f);
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.anchoredPosition = new Vector2(-14f, 0f);
            closeRect.sizeDelta = new Vector2(32f, 32f);

            Image closeBackground = closeObject.GetComponent<Image>();
            ApplySprite(closeBackground, CloseButtonPath, Image.Type.Simple);
            closeBackground.color = new Color32(56, 66, 80, 255);
            closeBackground.raycastTarget = true;

            CreateIcon(
                closeObject.transform,
                "CloseIcon",
                CloseIconPath,
                Vector2.zero,
                new Vector2(14f, 14f),
                TextMuted);

            Button close = closeObject.GetComponent<Button>();
            close.targetGraphic = closeBackground;
            ColorBlock closeColors = close.colors;
            closeColors.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1f);
            closeColors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            closeColors.fadeDuration = 0.08f;
            close.colors = closeColors;
            closeObject.AddComponent<GreenFeedHoverRelay>().Configure(
                controller,
                GreenFeedHoverRelay.ClickAction.Close,
                false);
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
            scrollTransform.offsetMin = new Vector2(10f, 8f);
            scrollTransform.offsetMax = new Vector2(-10f, -(HeaderHeight + 8f));
            Image scrollImage = scrollObject.GetComponent<Image>();
            scrollImage.color = ScrollColor;
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
            layout.padding = new RectOffset(2, 2, 2, 2);
            layout.spacing = 5f;
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
            ApplySprite(background, RowFramePath, Image.Type.Sliced, RowBorderScale);
            background.color = RowColor;
            // 행을 눌러 지점으로 가려면 레이캐스트를 받아야 한다.
            // 클릭만 처리하고 드래그는 안 잡으므로 스크롤은 그대로 동작한다.
            background.raycastTarget = true;

            LayoutElement layout = root.GetComponent<LayoutElement>();
            layout.preferredHeight = RowHeight;
            layout.flexibleWidth = 1f;

            // 작성자 색을 3px 막대 대신 원형 아바타가 들고 간다 — 둥근 행 배경에
            // 각진 막대는 안 어울리고, 아바타 원이 SNS 문법에 맞다.
            // 뷰의 accent 슬롯에 이 원을 넣으면 accentColor가 그대로 칠해진다
            // (avatar 슬롯은 Bind가 0.32배 어둡게 만들어서 라이트 테마엔 안 맞다).
            GameObject avatarObject = CreateUiObject("Avatar", root.transform, typeof(Image));
            RectTransform avatarRect = avatarObject.GetComponent<RectTransform>();
            avatarRect.anchoredPosition = new Vector2(10f, 0f);
            avatarRect.sizeDelta = new Vector2(28f, 28f);
            SetLeftCenter(avatarRect);
            Image accent = avatarObject.GetComponent<Image>();
            ApplySprite(accent, AvatarCirclePath, Image.Type.Simple);
            accent.raycastTarget = false;

            TMP_Text initialText = CreateText(
                avatarObject.transform,
                "Initial",
                avatarInitial,
                Vector2.zero,
                new Vector2(28f, 28f),
                13f,
                FontStyles.Bold,
                Color.white,
                TextAlignmentOptions.Center);

            TMP_Text authorText = CreateText(
                root.transform,
                "AuthorName",
                authorName,
                new Vector2(46f, 0f),
                new Vector2(74f, 18f),
                12f,
                FontStyles.Bold,
                TextStrong,
                TextAlignmentOptions.MidlineLeft);
            SetLeftCenter(authorText.rectTransform);

            TMP_Text occupationText = CreateText(
                root.transform,
                "Occupation",
                occupation,
                new Vector2(122f, 0f),
                new Vector2(52f, 16f),
                10f,
                FontStyles.Normal,
                TextFaint,
                TextAlignmentOptions.MidlineLeft);
            SetLeftCenter(occupationText.rectTransform);

            TMP_Text timeText = CreateText(
                root.transform,
                "Timestamp",
                timestamp,
                new Vector2(-12f, 0f),
                // "17:24" 5글자가 잘리지 않을 만큼만. 예전 "Y1 M01 D03 17:00"은
                // 어떤 폭을 줘도 한 줄 UI에 안 들어갔다.
                new Vector2(42f, 16f),
                10f,
                FontStyles.Normal,
                TextFaint,
                TextAlignmentOptions.MidlineRight);
            RectTransform timeRect = timeText.rectTransform;
            timeRect.anchorMin = new Vector2(1f, 0.5f);
            timeRect.anchorMax = new Vector2(1f, 0.5f);
            timeRect.pivot = new Vector2(1f, 0.5f);

            TMP_Text messageText = CreateText(
                root.transform,
                "Message",
                message,
                Vector2.zero,
                Vector2.zero,
                12f,
                FontStyles.Normal,
                TextMuted,
                TextAlignmentOptions.MidlineLeft);
            RectTransform messageRect = messageText.rectTransform;
            // 이름·직업(왼쪽 180)과 시각(오른쪽 74) 사이를 본문이 다 먹는다.
            messageRect.anchorMin = new Vector2(0f, 0.5f);
            messageRect.anchorMax = new Vector2(1f, 0.5f);
            messageRect.pivot = new Vector2(0.5f, 0.5f);
            messageRect.offsetMin = new Vector2(180f, -9f);
            messageRect.offsetMax = new Vector2(-58f, 9f);
            messageText.textWrappingMode = TextWrappingModes.NoWrap;
            messageText.overflowMode = TextOverflowModes.Ellipsis;

            // 행마다 둥근 흰 카드가 서고 레이아웃이 간격을 주므로 구분선은 없앴다.
            GreenFeedPostView view = root.GetComponent<GreenFeedPostView>();
            view.Configure(
                accent,
                null,
                initialText,
                authorText,
                occupationText,
                messageText,
                timeText,
                layout,
                true);
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

        // 티커는 히스토리 행과 같은 뷰다 — 한 줄 레이아웃을 두 번 만들 이유가 없다.
        private static GreenFeedPostView CreateTicker(
            Transform parent,
            GreenFeedPanelController controller)
        {
            GreenFeedPostView ticker = CreatePost(
                parent,
                "FeedTicker",
                "김시민",
                "시민",
                "도시의 새로운 소식이 여기에 표시됩니다.",
                "방금",
                "김",
                new Color(0.20f, 0.78f, 0.55f, 1f));

            GameObject tickerObject = ticker.gameObject;
            RectTransform tickerRect = tickerObject.GetComponent<RectTransform>();
            tickerRect.anchorMin = new Vector2(1f, 0f);
            tickerRect.anchorMax = new Vector2(1f, 0f);
            tickerRect.pivot = new Vector2(1f, 0f);
            tickerRect.anchoredPosition = TickerPosition;
            tickerRect.sizeDelta = TickerSize;

            Image background = tickerObject.GetComponent<Image>();
            ApplySprite(background, TickerFramePath, Image.Type.Sliced, RowBorderScale);
            background.color = PanelColor;
            background.raycastTarget = true;

            // 티커는 본문이 전부다. 직업(122~174)을 빼고 시각 칸을 줄여
            // 본문에 약 120px을 더 준다 — 안 그러면 문장이 중간에서 잘린다.
            Transform occupation = tickerObject.transform.Find("Occupation");
            if (occupation != null) occupation.gameObject.SetActive(false);

            Transform message = tickerObject.transform.Find("Message");
            if (message != null)
            {
                RectTransform messageRect = message.GetComponent<RectTransform>();
                messageRect.offsetMin = new Vector2(120f, -9f);
                messageRect.offsetMax = new Vector2(-58f, 9f);
            }

            Button button = tickerObject.AddComponent<Button>();
            button.targetGraphic = background;
            // ColorBlock은 Image.color에 곱해진다 — 네이비 바탕이라 흰색 계열로
            // 밝히지 않으면 눌린 표시가 안 보인다.
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            // Button은 눌림 색만 담당하고, 실제 동작은 릴레이가 맡는다.
            // 휠 가드는 스크롤되는 패널에만 필요하므로 티커에선 끈다.
            tickerObject.AddComponent<GreenFeedHoverRelay>().Configure(
                controller,
                GreenFeedHoverRelay.ClickAction.Toggle,
                false);
            return ticker;
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

        private static void SetLeftCenter(RectTransform rect)
        {
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
        }

        // 헤더는 위쪽 스트레치라 아이콘도 top-left 기준으로 잡아야 세로가 맞는다.
        private static void SetLeftCenterOfTop(RectTransform rect)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
        }

        /// <summary>
        /// 팩이 설치돼 있지 않으면 스프라이트 없이 단색 배경으로 떨어진다.
        /// 폰트와 달리 베이킹을 막지는 않는다 — 모양만 밋밋해질 뿐 동작은 같다.
        /// </summary>
        private static void ApplySprite(
            Image image,
            string path,
            Image.Type type = Image.Type.Sliced,
            float borderScale = 1f)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
            {
                Debug.LogWarning($"[GreenFeedUiBaker] 스프라이트를 찾지 못했습니다: {path}");
                return;
            }

            image.sprite = sprite;
            image.type = type;
            if (type == Image.Type.Sliced)
            {
                image.pixelsPerUnitMultiplier = borderScale;
            }
        }

        private static Image CreateIcon(
            Transform parent,
            string name,
            string spritePath,
            Vector2 anchoredPosition,
            Vector2 size,
            Color color)
        {
            GameObject iconObject = CreateUiObject(name, parent, typeof(Image));
            RectTransform rect = iconObject.GetComponent<RectTransform>();
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            Image icon = iconObject.GetComponent<Image>();
            ApplySprite(icon, spritePath, Image.Type.Simple);
            icon.color = color;
            icon.raycastTarget = false;
            return icon;
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

        private static T FindInActiveScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T found = root.GetComponentInChildren<T>(true);
                if (found != null) return found;
            }

            return null;
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

        private static TMP_FontAsset LoadRequiredUiFont()
        {
            TMP_FontAsset font = ExternalKoreanFontAsset.LoadConfigured();
            if (font != null)
            {
                return font;
            }

            Debug.LogError(
                $"[GreenFeedUiBaker] Required font is missing or invalid: " +
                $"'{ExternalKoreanFontAsset.FontAssetPath}'. " +
                "Bake was cancelled to prevent a fallback font reference.");
            return null;
        }

        private readonly struct SavedLayout
        {
            private readonly RectSnapshot panel;
            private readonly RectSnapshot ticker;

            public SavedLayout(RectTransform panelRect, RectTransform tickerRect)
            {
                panel = new RectSnapshot(panelRect);
                ticker = new RectSnapshot(tickerRect);
            }

            public void ApplyTo(RectTransform panelRect, RectTransform tickerRect)
            {
                panel.ApplyTo(panelRect);
                ticker.ApplyTo(tickerRect);
            }
        }

        /// <summary>
        /// 위치만 물려받는다. 크기까지 보존하면 베이커에서 디자인 치수를 바꿔도
        /// 재베이킹이 옛 값으로 되돌려버린다 — 크기는 디자인, 위치는 사람 몫이다.
        /// </summary>
        private readonly struct RectSnapshot
        {
            private readonly Vector2 anchorMin;
            private readonly Vector2 anchorMax;
            private readonly Vector2 pivot;
            private readonly Vector2 anchoredPosition;

            public RectSnapshot(RectTransform source)
            {
                anchorMin = source.anchorMin;
                anchorMax = source.anchorMax;
                pivot = source.pivot;
                anchoredPosition = source.anchoredPosition;
            }

            public void ApplyTo(RectTransform target)
            {
                if (target == null) return;
                target.anchorMin = anchorMin;
                target.anchorMax = anchorMax;
                target.pivot = pivot;
                target.anchoredPosition = anchoredPosition;
            }
        }

        private static bool TryCaptureLayout(Scene scene, out SavedLayout layout)
        {
            layout = default;
            Transform existing = FindTransform(scene, FeedRootName);
            if (existing == null) return false;

            GreenFeedPanelController controller =
                existing.GetComponent<GreenFeedPanelController>();
            if (controller == null || controller.TickerView == null) return false;

            Transform panel = FindChild(existing, "GreenSNSPanel");
            if (panel == null) return false;

            layout = new SavedLayout(
                panel.GetComponent<RectTransform>(),
                controller.TickerView.GetComponent<RectTransform>());
            return true;
        }

        private static Transform FindChild(Transform root, string childName)
        {
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name == childName) return candidate;
            }

            return null;
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
