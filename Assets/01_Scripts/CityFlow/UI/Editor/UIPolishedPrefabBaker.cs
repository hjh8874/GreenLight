using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CityFlow.UI.Editor
{
    /// <summary>
    /// GUI-MonoRound 에셋 팩과 NanumGothic 폰트가 적용된 고품질 UI 프리팹을 자동 생성합니다.
    /// 메뉴: CityFlow/UI/🎨 Polished UI 프리팹 굽기
    /// 
    /// 생성되는 프리팹:
    ///   - Assets/02_Prefabs/UI/UI_MainCanvas_Polished.prefab  (전체 캔버스)
    ///   - Assets/02_Prefabs/UI/Elements/UI_StandardPanel.prefab
    ///   - Assets/02_Prefabs/UI/Elements/UI_StandardButton.prefab
    ///   - Assets/02_Prefabs/UI/Elements/UI_StandardPopup.prefab
    /// </summary>
    public static class UIPolishedPrefabBaker
    {
        // ── 에셋 경로 상수 ─────────────────────────────────────
        private const string SpriteRoot =
            "Assets/99_Download/Layer Lab/GUI-MonoRound/ResourcesData/Sprites/Components/";
        private const string FontPath =
            "Assets/99_Download/Fonts/NanumGothic SDF.asset";
        private const string PrefabOutputDir =
            "Assets/02_Prefabs/UI";
        private const string ElementOutputDir =
            "Assets/02_Prefabs/UI/Elements";

        // ── 스프라이트 서브패스 ─────────────────────────────────
        // 패널 배경
        private const string PanelBg         = "Frame/Frame_Basic_Rectangle.png";
        private const string TopBarBg        = "Frame/Frame_Panel_TopBar.png";
        private const string ListFrameBg     = "Frame/Frame_ListFrame01_White1.png";
        // 버튼
        private const string BtnGreen        = "Button/Btn_Rectangle01_n_Green.png";
        private const string BtnBlue         = "Button/Btn_Rectangle01_n_Blue.png";
        private const string BtnOrange       = "Button/Btn_Rectangle01_n_Orange.png";
        private const string BtnWhite        = "Button/Btn_Rectangle01_White.png";
        private const string BtnGray         = "Button/Btn_Basic_Square_Gray.png";
        private const string BtnDark         = "Button/Btn_Rectangle02_Dark.png";
        // 팝업
        private const string PopupBg         = "Popup/Popup00.png";
        // 탭
        private const string TabActive       = "Frame/Frame_TabMenu_n.png";
        private const string TabInactive     = "Frame/Frame_TabMenu_d.png";
        // 아이콘
        private const string IconCoin        = "UI_Etc/Status_Icon_Coin.png";
        private const string IconStar        = "UI_Etc/Status_Icon_Star.png";
        private const string IconLife        = "UI_Etc/Status_Icon_Life.png";
        // 슬라이더
        private const string SliderFrame     = "Slider/Slider02_FrontFrame.png";
        private const string SliderFill      = "Slider/Slider02_Fill.png";
        // 토글
        private const string ToggleOn        = "UI_Etc/Toggle_Square_l_on.png";
        private const string ToggleOff       = "UI_Etc/Toggle_Square_l_off.png";
        // 추가 아이콘
        private const string IconSettings    = "UI_Etc/Status_Btn_Icon.png";

        // ── 색상 팔레트 (GreenLight Dark & Neon Green) ──────────────
        private static Color HexColor(string hex) {
            ColorUtility.TryParseHtmlString(hex, out Color c);
            return c;
        }
        private static readonly Color ColorTopBarBg    = HexColor("#1A1C20"); // City Night
        private static readonly Color ColorPanelBg     = HexColor("#2C303A"); // Asphalt Gray
        private static readonly Color ColorDockBg      = HexColor("#1A1C20"); // City Night
        private static readonly Color ColorTextDark    = HexColor("#1A1C20"); 
        private static readonly Color ColorTextLight   = HexColor("#F5F7FA"); // Cloud White
        private static readonly Color ColorTextMuted   = HexColor("#8B95A6"); 
        private static readonly Color ColorAccentGreen = HexColor("#2ECC71"); // GreenLight Mint
        private static readonly Color ColorAccentBlue  = HexColor("#3498DB"); // Blue
        private static readonly Color ColorTrafficRed  = HexColor("#E74C3C"); // Traffic Red

        // ── 캐시 ───────────────────────────────────────────────
        private static TMP_FontAsset _font;

        // ================================================================
        //  메뉴 엔트리
        // ================================================================

        [MenuItem("CityFlow/UI/🎨 Polished UI 프리팹 굽기")]
        public static void BakeAll()
        {
            _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (_font == null)
            {
                Debug.LogError($"[UIPolishedPrefabBaker] 폰트를 찾을 수 없습니다: {FontPath}");
                return;
            }

            EnsureDirectory(PrefabOutputDir);
            EnsureDirectory(ElementOutputDir);

            // 1) 표준 요소 프리팹 (재사용 가능한 미니 프리팹)
            BakeStandardPanel();
            BakeStandardButton();
            BakeStandardPopup();

            // 2) 메인 캔버스 프리팹 (전체 UI)
            BakeMainCanvas();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("🎨 [UIPolishedPrefabBaker] 모든 프리팹 생성 완료!");
        }

        // ================================================================
        //  표준 요소 프리팹 (향후 콘텐츠에서도 재사용)
        // ================================================================

        private static void BakeStandardPanel()
        {
            GameObject go = new GameObject("UI_StandardPanel");
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(320, 400);

            Image bg = go.AddComponent<Image>();
            bg.sprite = LoadSprite(ListFrameBg);
            bg.type = Image.Type.Sliced;
            bg.color = ColorPanelBg;

            // 타이틀 바
            GameObject titleBar = CreateChild("TitleBar", go.transform);
            RectTransform titleRect = titleBar.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 1);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.pivot = new Vector2(0.5f, 1);
            titleRect.sizeDelta = new Vector2(0, 44);
            titleRect.anchoredPosition = Vector2.zero;

            Image titleBg = titleBar.AddComponent<Image>();
            titleBg.sprite = LoadSprite(TopBarBg);
            titleBg.type = Image.Type.Sliced;
            titleBg.color = ColorAccentGreen; // 테마 변경

            TextMeshProUGUI titleText = CreateTMP("TitleText", titleBar.transform,
                "패널 제목", 18, ColorTextLight, TextAlignmentOptions.Center);
            StretchFill(titleText.rectTransform, 12, 0, 12, 0);

            // 콘텐츠 영역
            GameObject content = CreateChild("Content", go.transform);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = Vector2.one;
            contentRect.offsetMin = new Vector2(8, 8);
            contentRect.offsetMax = new Vector2(-8, -48);

            VerticalLayoutGroup vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.spacing = 6;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            SavePrefab(go, $"{ElementOutputDir}/UI_StandardPanel.prefab");
            Object.DestroyImmediate(go);
        }

        private static void BakeStandardButton()
        {
            GameObject go = new GameObject("UI_StandardButton");
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(160, 42);

            Image bg = go.AddComponent<Image>();
            bg.sprite = LoadSprite(BtnGreen);
            bg.type = Image.Type.Sliced;
            bg.color = Color.white;

            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;

            // 호버/클릭 색상 트랜지션
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(0.9f, 0.95f, 0.9f, 1f);
            cb.pressedColor = new Color(0.75f, 0.85f, 0.75f, 1f);
            cb.selectedColor = Color.white;
            cb.fadeDuration = 0.1f;
            btn.colors = cb;

            TextMeshProUGUI label = CreateTMP("Label", go.transform,
                "버튼", 16, ColorTextLight, TextAlignmentOptions.Center);
            StretchFill(label.rectTransform, 8, 4, 8, 4);
            label.fontStyle = FontStyles.Bold;

            SavePrefab(go, $"{ElementOutputDir}/UI_StandardButton.prefab");
            Object.DestroyImmediate(go);
        }

        private static void BakeStandardPopup()
        {
            // 어둡게 깔리는 딤드 배경
            GameObject go = new GameObject("UI_StandardPopup");
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image dimmed = go.AddComponent<Image>();
            dimmed.color = new Color(0, 0, 0, 0.5f);

            // 팝업 프레임
            GameObject frame = CreateChild("Frame", go.transform);
            RectTransform frameRect = frame.GetComponent<RectTransform>();
            frameRect.anchorMin = new Vector2(0.5f, 0.5f);
            frameRect.anchorMax = new Vector2(0.5f, 0.5f);
            frameRect.pivot = new Vector2(0.5f, 0.5f);
            frameRect.sizeDelta = new Vector2(440, 240);

            Image frameBg = frame.AddComponent<Image>();
            frameBg.sprite = LoadSprite(PopupBg);
            frameBg.type = Image.Type.Sliced;
            frameBg.color = ColorPanelBg;

            // 메시지 텍스트
            TextMeshProUGUI msg = CreateTMP("Message", frame.transform,
                "팝업 메시지", 16, ColorTextLight, TextAlignmentOptions.Center);
            RectTransform msgRect = msg.rectTransform;
            msgRect.anchorMin = new Vector2(0, 0.45f);
            msgRect.anchorMax = new Vector2(1, 0.9f);
            msgRect.offsetMin = new Vector2(24, 0);
            msgRect.offsetMax = new Vector2(-24, -8);

            // 확인 버튼
            GameObject btnYes = CreateButtonChild("BtnYes", frame.transform,
                "확인", BtnGreen, new Vector2(-80, 30), new Vector2(140, 44));

            // 취소 버튼
            GameObject btnNo = CreateButtonChild("BtnNo", frame.transform,
                "취소", BtnDark, new Vector2(80, 30), new Vector2(140, 44));

            SavePrefab(go, $"{ElementOutputDir}/UI_StandardPopup.prefab");
            Object.DestroyImmediate(go);
        }

        // ================================================================
        //  메인 캔버스 프리팹 (전체 게임 UI)
        // ================================================================

        private static void BakeMainCanvas()
        {
            // 외부 베이커들이 타겟팅할 수 있도록 씬에 'UI_MainCanvas'로 생성
            // 단, 현재 씬에 이미 'UI_MainCanvas'가 있으면 외부 베이커가 그쪽으로 붙어버리므로 임시로 이름 변경
            var existingCanvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var c in existingCanvases)
            {
                if (c.gameObject.name == "UI_MainCanvas")
                    c.gameObject.name = "UI_MainCanvas_Hidden_Temp";
            }

            GameObject root = new GameObject("UI_MainCanvas");

            // Canvas 설정
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            root.AddComponent<GraphicRaycaster>();

            // ── 1) 상단 HUD ─────────────────────────────────────
            BuildHUD(root.transform);

            // ── 2) 우측 도크 ─────────────────────────────────────
            BuildDock(root.transform);

            // ── 3) 서브패널 컨테이너 ────────────────────────────
            BuildSubPanels(root.transform);

            // ── 4) 좌하단 분석 카드 ─────────────────────────────
            BuildAnalysisCard(root.transform);

            // ── 5) 외부 베이커 연동 (SNS피드, 오프라인 정산) ─────
            try {
                CityFlow.EditorTools.GreenFeedUiBaker.Bake();
            } catch (System.Exception e) { Debug.LogWarning(e); }
            
            try {
                CityFlow.EditorTools.OfflineSettlementPopupBaker.Bake();
            } catch (System.Exception e) { Debug.LogWarning(e); }

            // 외부 UI 스킨 교체
            ApplySkinToExternalUI(root.transform);

            // ── 6) 스크립트 바인딩 ──────────────────────────────
            AttachScripts(root);

            // 이름 복구 후 프리팹 저장
            root.name = "UI_MainCanvas_Polished";
            SavePrefab(root, $"{PrefabOutputDir}/UI_MainCanvas_Polished.prefab");
            Object.DestroyImmediate(root);

            // 기존 캔버스 이름 복구
            foreach (var c in existingCanvases)
            {
                if (c != null && c.gameObject.name == "UI_MainCanvas_Hidden_Temp")
                    c.gameObject.name = "UI_MainCanvas";
            }
        }

        // ── HUD (상단 바) ───────────────────────────────────────
        private static void BuildHUD(Transform parent)
        {
            GameObject hud = CreateChild("HUD_TopBar", parent);
            RectTransform hudRect = hud.GetComponent<RectTransform>();
            hudRect.anchorMin = new Vector2(0, 1);
            hudRect.anchorMax = new Vector2(1, 1);
            hudRect.pivot = new Vector2(0.5f, 1);
            hudRect.sizeDelta = new Vector2(0, 52);
            hudRect.anchoredPosition = Vector2.zero;

            Image hudBg = hud.AddComponent<Image>();
            hudBg.sprite = LoadSprite(TopBarBg);
            hudBg.type = Image.Type.Sliced;
            hudBg.color = ColorTopBarBg;

            HorizontalLayoutGroup hlg = hud.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(16, 16, 6, 6);
            hlg.spacing = 20;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = false;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            // 시간 표시
            BuildHUDStat(hud.transform, "TimeGroup", IconStar, "TimeText", "00:00");
            // 차량 수
            BuildHUDStat(hud.transform, "VehicleGroup", IconLife, "VehicleCountText", "🚗 0");
            // 코인
            BuildHUDStat(hud.transform, "CoinGroup", IconCoin, "CoinText", "💰 0");
            // 인구 (Spacer + 우측 정렬)
            GameObject spacer = CreateChild("Spacer", hud.transform);
            spacer.AddComponent<LayoutElement>().flexibleWidth = 1;
            BuildHUDStat(hud.transform, "PopulationGroup", IconLife, "PopulationText", "👤 0");
        }

        private static void BuildHUDStat(Transform parent, string groupName,
            string iconPath, string textName, string defaultValue)
        {
            GameObject group = CreateChild(groupName, parent);
            group.AddComponent<LayoutElement>().preferredWidth = 140;

            HorizontalLayoutGroup hlg = group.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = false;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            // 아이콘
            GameObject iconObj = CreateChild("Icon", group.transform);
            iconObj.AddComponent<LayoutElement>().preferredWidth = 28;
            Image icon = iconObj.AddComponent<Image>();
            icon.sprite = LoadSprite(iconPath);
            icon.preserveAspect = true;

            // 텍스트
            TextMeshProUGUI txt = CreateTMP(textName, group.transform,
                defaultValue, 18, ColorTextLight, TextAlignmentOptions.MidlineLeft);
            txt.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        }

        // ── 우측 도크 ───────────────────────────────────────────
        private static void BuildDock(Transform parent)
        {
            GameObject dock = CreateChild("Dock_Right", parent);
            RectTransform dockRect = dock.GetComponent<RectTransform>();
            dockRect.anchorMin = new Vector2(1, 0.5f);
            dockRect.anchorMax = new Vector2(1, 0.5f);
            dockRect.pivot = new Vector2(1, 0.5f);
            dockRect.sizeDelta = new Vector2(72, 320);
            dockRect.anchoredPosition = new Vector2(-12, 0);

            Image dockBg = dock.AddComponent<Image>();
            dockBg.sprite = LoadSprite(ListFrameBg);
            dockBg.type = Image.Type.Sliced;
            dockBg.color = ColorDockBg;

            VerticalLayoutGroup vlg = dock.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(6, 6, 10, 10);
            vlg.spacing = 8;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // 도크 버튼 5개
            BuildDockButton(dock.transform, "Btn_Build",       "건설", BtnGreen);
            BuildDockButton(dock.transform, "Btn_Research",    "연구", BtnBlue);
            BuildDockButton(dock.transform, "Btn_Stats",       "통계", BtnOrange);
            BuildDockButton(dock.transform, "Btn_Settings",    "설정", BtnGray);
            BuildDockButton(dock.transform, "Btn_FloatingMode","플로팅", BtnDark);
        }

        private static void BuildDockButton(Transform parent, string name,
            string label, string spritePath)
        {
            GameObject btnObj = CreateChild(name, parent);
            btnObj.AddComponent<LayoutElement>().preferredHeight = 50;

            Image bg = btnObj.AddComponent<Image>();
            bg.sprite = LoadSprite(spritePath);
            bg.type = Image.Type.Sliced;
            bg.color = Color.white;

            Button btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = bg;

            TextMeshProUGUI txt = CreateTMP("Label", btnObj.transform,
                label, 13, ColorTextLight, TextAlignmentOptions.Center);
            StretchFill(txt.rectTransform, 2, 2, 2, 2);
            txt.fontStyle = FontStyles.Bold;
        }

        // ── 서브패널 컨테이너 ───────────────────────────────────
        private static void BuildSubPanels(Transform parent)
        {
            GameObject container = CreateChild("SubPanels_Right", parent);
            RectTransform cRect = container.GetComponent<RectTransform>();
            cRect.anchorMin = new Vector2(1, 0);
            cRect.anchorMax = new Vector2(1, 1);
            cRect.pivot = new Vector2(1, 0.5f);
            cRect.sizeDelta = new Vector2(320, 0);
            cRect.offsetMin = new Vector2(cRect.offsetMin.x, 80);
            cRect.offsetMax = new Vector2(-92, -60);

            // Build_Panel
            BuildSubPanel(container.transform, "Build_Panel",    "건설",   ColorAccentGreen);
            BuildSubPanel(container.transform, "Research_Panel", "연구",   ColorAccentBlue);
            BuildSubPanel(container.transform, "Stats_Panel",    "통계",   new Color(0.92f, 0.55f, 0.20f));
            BuildSubPanel(container.transform, "Settings_Panel", "설정",   new Color(0.50f, 0.50f, 0.55f));
            BuildSubPanel(container.transform, "Floating_Panel", "플로팅", new Color(0.35f, 0.35f, 0.45f));
        }

        private static void BuildSubPanel(Transform parent, string name,
            string title, Color titleColor)
        {
            GameObject panel = CreateChild(name, parent);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            Image bg = panel.AddComponent<Image>();
            bg.sprite = LoadSprite(ListFrameBg);
            bg.type = Image.Type.Sliced;
            bg.color = ColorPanelBg;

            // 타이틀 바
            GameObject titleBar = CreateChild("TitleBar", panel.transform);
            RectTransform titleRect = titleBar.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 1);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.pivot = new Vector2(0.5f, 1);
            titleRect.sizeDelta = new Vector2(0, 36);
            titleRect.anchoredPosition = Vector2.zero;

            Image titleBg = titleBar.AddComponent<Image>();
            titleBg.sprite = LoadSprite(TopBarBg);
            titleBg.type = Image.Type.Sliced;
            titleBg.color = titleColor;

            CreateTMP("Title", titleBar.transform,
                title, 16, ColorTextLight, TextAlignmentOptions.Center);

            // 콘텐츠 영역
            GameObject content = CreateChild("Content", panel.transform);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = Vector2.one;
            contentRect.offsetMin = new Vector2(6, 6);
            contentRect.offsetMax = new Vector2(-6, -40);

            VerticalLayoutGroup vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(6, 6, 6, 6);
            vlg.spacing = 4;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            panel.SetActive(false); // 초기에는 모두 닫힘
        }

        // ── 좌하단 분석 카드 ────────────────────────────────────
        private static void BuildAnalysisCard(Transform parent)
        {
            GameObject card = CreateChild("AnalysisCard_BottomLeft", parent);
            RectTransform cardRect = card.GetComponent<RectTransform>();
            cardRect.anchorMin = Vector2.zero;
            cardRect.anchorMax = Vector2.zero;
            cardRect.pivot = Vector2.zero;
            cardRect.sizeDelta = new Vector2(450, 290);
            cardRect.anchoredPosition = new Vector2(16, 16);

            Image bg = card.AddComponent<Image>();
            bg.sprite = LoadSprite(ListFrameBg);
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.12f, 0.14f, 0.20f, 0.94f);

            // 제목
            TextMeshProUGUI title = CreateTMP("CardTitle", card.transform,
                "도시 분석", 18, ColorTextLight, TextAlignmentOptions.TopLeft);
            RectTransform titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0, 1);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.pivot = new Vector2(0, 1);
            titleRect.sizeDelta = new Vector2(0, 32);
            titleRect.anchoredPosition = new Vector2(16, -10);
            title.fontStyle = FontStyles.Bold;

            // 콘텐츠 영역
            GameObject content = CreateChild("Content", card.transform);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = Vector2.one;
            contentRect.offsetMin = new Vector2(12, 12);
            contentRect.offsetMax = new Vector2(-12, -48);

            card.SetActive(false); // 초기에는 숨김
        }

        // ================================================================
        //  유틸리티
        // ================================================================

        private static Sprite LoadSprite(string subPath)
        {
            string fullPath = SpriteRoot + subPath;
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(fullPath);
            if (sprite == null)
            {
                Debug.LogWarning($"[UIPolishedPrefabBaker] 스프라이트 없음: {fullPath}");
            }
            return sprite;
        }

        private static GameObject CreateChild(string name, Transform parent)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            return go;
        }

        private static TextMeshProUGUI CreateTMP(string name, Transform parent,
            string text, float fontSize, Color color, TextAlignmentOptions alignment)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.font = _font;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.enableAutoSizing = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            return tmp;
        }

        private static void StretchFill(RectTransform rt,
            float left, float bottom, float right, float top)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        private static GameObject CreateButtonChild(string name, Transform parent,
            string label, string spritePath, Vector2 anchoredPos, Vector2 size)
        {
            GameObject go = CreateChild(name, parent);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0);
            rt.anchorMax = new Vector2(0.5f, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            Image bg = go.AddComponent<Image>();
            bg.sprite = LoadSprite(spritePath);
            bg.type = Image.Type.Sliced;
            bg.color = Color.white;

            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;

            TextMeshProUGUI txt = CreateTMP("Label", go.transform,
                label, 16, ColorTextLight, TextAlignmentOptions.Center);
            StretchFill(txt.rectTransform, 4, 4, 4, 4);
            txt.fontStyle = FontStyles.Bold;

            return go;
        }

        private static void SavePrefab(GameObject instance, string path)
        {
            // 기존 프리팹이 있으면 덮어쓰기
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                PrefabUtility.SaveAsPrefabAsset(instance, path);
                Debug.Log($"  ↻ 프리팹 업데이트: {path}");
            }
            else
            {
                PrefabUtility.SaveAsPrefabAsset(instance, path);
                Debug.Log($"  ✦ 프리팹 신규 생성: {path}");
            }
        }

        private static void EnsureDirectory(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
                string folder = System.IO.Path.GetFileName(path);
                if (!AssetDatabase.IsValidFolder(parent))
                {
                    EnsureDirectory(parent);
                }
                AssetDatabase.CreateFolder(parent, folder);
            }
        }
        private static void ApplySkinToExternalUI(Transform root)
        {
            // 1) GreenSNSFeedDock
            Transform feed = root.Find("GreenSNSFeedDock");
            if (feed != null)
            {
                // 배경 패널 색상 변경 (기존에 흰색/투명 등이었다면 Asphalt Gray로)
                var panel = feed.Find("Panel")?.GetComponent<Image>();
                if (panel != null)
                {
                    panel.sprite = LoadSprite(ListFrameBg);
                    panel.type = Image.Type.Sliced;
                    panel.color = ColorPanelBg;
                }

                // 텍스트 색상들 
                foreach (var tmp in feed.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    tmp.font = _font;
                    // 제목 류는 Light, 본문은 Muted
                    if (tmp.name.Contains("Title") || tmp.name.Contains("Name")) 
                        tmp.color = ColorTextLight;
                    else
                        tmp.color = ColorTextMuted;
                }
            }

            // 2) OfflineSettlementPopup
            Transform popup = root.Find("OfflineSettlementPopup");
            if (popup != null)
            {
                var card = popup.Find("OfflineReportCard")?.GetComponent<Image>();
                if (card != null)
                {
                    card.sprite = LoadSprite(PopupBg);
                    card.type = Image.Type.Sliced;
                    card.color = ColorPanelBg;
                }
                
                var accent = popup.Find("OfflineReportCard/TopAccent")?.GetComponent<Image>();
                if (accent != null)
                {
                    accent.sprite = LoadSprite(TopBarBg);
                    accent.color = ColorAccentGreen;
                }

                // 버튼들
                var buttons = popup.GetComponentsInChildren<Button>(true);
                foreach (var btn in buttons)
                {
                    var bg = btn.GetComponent<Image>();
                    if (bg != null)
                    {
                        bg.sprite = LoadSprite(BtnGreen);
                        bg.type = Image.Type.Sliced;
                        bg.color = Color.white;
                    }
                }

                foreach (var tmp in popup.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    tmp.font = _font;
                    if (tmp.name.Contains("Title") || tmp.name.Contains("Amount"))
                        tmp.color = ColorAccentGreen;
                    else
                        tmp.color = ColorTextLight;
                }
            }
        }

        private static void AttachScripts(GameObject root)
        {
            Transform t = root.transform;

            // 1. HUDDashboard
            Transform hud = t.Find("HUD_TopBar");
            if (hud != null)
            {
                var dash = hud.gameObject.AddComponent<HUDDashboard>();
                var time = hud.Find("TimeGroup/TimeText")?.GetComponent<TextMeshProUGUI>();
                var vehicle = hud.Find("VehicleGroup/VehicleCountText")?.GetComponent<TextMeshProUGUI>();
                var coin = hud.Find("CoinGroup/CoinText")?.GetComponent<TextMeshProUGUI>();
                dash.Configure(time, vehicle, coin, null);
            }

            // 2. UIDockController
            Transform dock = t.Find("Dock_Right");
            Transform subPanels = t.Find("SubPanels_Right");
            if (dock != null && subPanels != null)
            {
                var dockCtrl = dock.gameObject.AddComponent<UIDockController>();
                
                var btnBuild = dock.Find("Btn_Build")?.GetComponent<Button>();
                var btnResearch = dock.Find("Btn_Research")?.GetComponent<Button>();
                var btnStats = dock.Find("Btn_Stats")?.GetComponent<Button>();
                var btnSettings = dock.Find("Btn_Settings")?.GetComponent<Button>();
                
                var pBuild = subPanels.Find("Build_Panel")?.gameObject;
                var pResearch = subPanels.Find("Research_Panel")?.gameObject;
                var pStats = subPanels.Find("Stats_Panel")?.gameObject;
                var pSettings = subPanels.Find("Settings_Panel")?.gameObject;

                dockCtrl.Configure(btnBuild, btnResearch, btnStats, btnSettings, pBuild, pResearch, pStats, pSettings, null);

                // Configure에 없는 필드는 SerializedObject로 주입
                var so = new SerializedObject(dockCtrl);
                var propBtnFloat = so.FindProperty("btnFloatingMode");
                if (propBtnFloat != null && dock.Find("Btn_FloatingMode") != null)
                    propBtnFloat.objectReferenceValue = dock.Find("Btn_FloatingMode").GetComponent<Button>();
                
                var propPFloat = so.FindProperty("panelFloating");
                if (propPFloat != null && subPanels.Find("Floating_Panel") != null)
                    propPFloat.objectReferenceValue = subPanels.Find("Floating_Panel").gameObject;

                so.ApplyModifiedProperties();
            }

            // 3. SettingsPanelController (껍데기만 연결)
            if (subPanels != null)
            {
                Transform settingsPanel = subPanels.Find("Settings_Panel");
                if (settingsPanel != null)
                {
                    settingsPanel.gameObject.AddComponent<SettingsPanelController>();
                }
                
                Transform statsPanel = subPanels.Find("Stats_Panel");
                if (statsPanel != null)
                {
                    statsPanel.gameObject.AddComponent<StatsPanelController>();
                }
            }
        }
    }
}
