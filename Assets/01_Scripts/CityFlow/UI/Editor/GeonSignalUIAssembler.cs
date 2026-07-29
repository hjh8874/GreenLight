using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEditor.SceneManagement;
using CityFlow.UI;

namespace CityFlow.UI.Editor
{
    /// <summary>
    /// 에디터 메뉴에서 한 번 클릭으로 Signal Control UI 전체를 자동 생성·배치·연결합니다.
    /// GUI-MonoRound 에셋 테마를 적용하며, Anchor/LayoutGroup 기반으로
    /// 해상도 변경·카메라 줌에도 UI가 깨지지 않는 Responsive 구조를 보장합니다.
    /// </summary>
    public static class GeonSignalUIAssembler
    {
        // ──────────────────────────── Asset Paths ────────────────────────────
        private const string MONO_ROUND = "Assets/99_Download/Layer Lab/GUI-MonoRound/ResourcesData/Sprites/Components/";
        private const string PATH_POPUP_BG       = MONO_ROUND + "Popup/Popup00.png";
        private const string PATH_SLIDER_FRAME   = MONO_ROUND + "Slider/Slider00_Frame.png";
        private const string PATH_SLIDER_FILL    = MONO_ROUND + "Slider/Slider00_Fill.png";
        private const string PATH_SLIDER_HANDLE  = MONO_ROUND + "Slider/Slider03_Handle.png";
        private const string PATH_BTN_BLUE       = MONO_ROUND + "Button/Btn_Rectangle02_Blue.png";
        private const string PATH_BTN_GREEN      = MONO_ROUND + "Button/Btn_Rectangle02_Green.png";
        private const string PATH_FRAME_RECT     = MONO_ROUND + "Frame/Frame_Basic_Rectangle.png";
        private const string PATH_GAUGE_BG       = MONO_ROUND + "Slider/Slider02_FillArea.png";

        // ──────────────────────────── Colors (Premium Theme) ────────────────────────────
        private static readonly Color COLOR_PANEL_BG       = new Color(0.10f, 0.11f, 0.14f, 0.95f);
        private static readonly Color COLOR_SECTION_BG     = new Color(0.13f, 0.15f, 0.18f, 0.90f);
        private static readonly Color COLOR_TITLE          = new Color(0.90f, 0.92f, 0.95f);
        private static readonly Color COLOR_LABEL          = new Color(0.58f, 0.64f, 0.72f);
        private static readonly Color COLOR_VALUE          = Color.white;
        private static readonly Color COLOR_GAUGE_GREEN    = new Color(0.20f, 0.83f, 0.58f); // Pastel Emerald
        private static readonly Color COLOR_GAUGE_YELLOW   = new Color(0.98f, 0.80f, 0.08f); // Pastel Amber
        private static readonly Color COLOR_GAUGE_RED      = new Color(0.97f, 0.44f, 0.44f); // Pastel Rose
        private static readonly Color COLOR_CURSOR         = new Color(1f, 1f, 1f, 0.95f);
        private static readonly Color COLOR_CROSS_BG       = new Color(0.13f, 0.15f, 0.18f, 0.9f);
        private static readonly Color COLOR_CROSS_LINE     = new Color(0.4f, 0.45f, 0.55f, 0.6f);

        [MenuItem("CityFlow/UI/Assemble Signal Control UI")]
        public static void Assemble()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name != "CityFlowIntegrated_Geon")
            {
                Debug.LogError("[CityFlow] Error: Please open the CityFlowIntegrated_Geon scene first.");
                return;
            }

            GameObject analysisCardObj = FindObjectIncludingInactive("AnalysisCard_BottomLeft");
            if (analysisCardObj == null)
            {
                Debug.LogError("[CityFlow] Error: AnalysisCard_BottomLeft not found in scene.");
                return;
            }

            AnalysisCardController controller = analysisCardObj.GetComponent<AnalysisCardController>();
            if (controller == null)
            {
                Debug.LogError("[CityFlow] Error: AnalysisCardController not found on AnalysisCard_BottomLeft.");
                return;
            }

            // ── Font 추출 (기존 UI에서 폰트를 물려받음) ──
            TMP_FontAsset fontToUse = null;
            var existingText = analysisCardObj.GetComponentInChildren<TextMeshProUGUI>(true);
            if (existingText != null) fontToUse = existingText.font;

            // ── NormalInfoContainer 확보 ──
            Transform normalContainer = analysisCardObj.transform.Find("NormalInfoContainer");
            if (normalContainer == null)
            {
                GameObject normalObj = CreateChild(analysisCardObj.transform, "NormalInfoContainer");
                StretchFull(normalObj);
                normalContainer = normalObj.transform;

                int childCount = analysisCardObj.transform.childCount;
                for (int i = childCount - 1; i >= 0; i--)
                {
                    Transform child = analysisCardObj.transform.GetChild(i);
                    if (child != normalContainer)
                    {
                        child.SetParent(normalContainer, false);
                    }
                }
            }

            // ── 기존 SignalControlContainer 제거 후 재생성 ──
            Transform oldSignal = analysisCardObj.transform.Find("SignalControlContainer");
            if (oldSignal != null) Object.DestroyImmediate(oldSignal.gameObject);

            GameObject signalRoot = CreateChild(analysisCardObj.transform, "SignalControlContainer");
            // 타이틀 텍스트가 가려지지 않도록 상단 여백(45px)을 확보합니다
            var signalRootRT = signalRoot.GetComponent<RectTransform>();
            signalRootRT.anchorMin = Vector2.zero;
            signalRootRT.anchorMax = Vector2.one;
            signalRootRT.offsetMin = Vector2.zero;        // Left, Bottom = 0
            signalRootRT.offsetMax = new Vector2(0f, -45f); // Top = -45px 여백

            // ═══════════════════════════════════════════════════════════════
            //  메인 레이아웃: 좌우 분할 (HorizontalLayoutGroup)
            // ═══════════════════════════════════════════════════════════════
            var hlg = signalRoot.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.padding = new RectOffset(12, 12, 10, 10);
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            // ═══════════════════════════════════════════════════════════════
            //  [좌측] 제어부 (VerticalLayoutGroup)
            // ═══════════════════════════════════════════════════════════════
            GameObject leftPanel = CreateChild(signalRoot.transform, "LeftControlPanel");
            var leftLE = leftPanel.AddComponent<LayoutElement>();
            leftLE.flexibleWidth = 3f;
            leftLE.minWidth = 200f;

            var leftVLG = leftPanel.AddComponent<VerticalLayoutGroup>();
            leftVLG.spacing = 6f;
            leftVLG.padding = new RectOffset(4, 4, 4, 4);
            leftVLG.childAlignment = TextAnchor.MiddleCenter;
            leftVLG.childControlWidth = true;
            leftVLG.childControlHeight = true;
            leftVLG.childForceExpandWidth = true;
            leftVLG.childForceExpandHeight = false;

            // ── 1) 신호 주기 게이지 막대 ──
            GameObject gaugeSection = CreateGaugeSection(leftPanel.transform, fontToUse,
                out RectTransform cursor,
                out LayoutElement leHG, out LayoutElement leHY, out LayoutElement leHC,
                out LayoutElement leVG, out LayoutElement leVY, out LayoutElement leVC);

            // ── 2) Offset 슬라이더 ──
            Slider sliderOffset = CreateThemedSlider(leftPanel.transform, "SliderOffset", "신호 타이밍", fontToUse);

            // ── 3) Green 슬라이더 ──
            Slider sliderGreen = CreateThemedSlider(leftPanel.transform, "SliderGreen", "파란불 길이", fontToUse);

            // ── 4) 가로/세로 오버라이드 버튼 행 ──
            GameObject btnRow = CreateChild(leftPanel.transform, "ButtonRow");
            var btnRowLE = btnRow.AddComponent<LayoutElement>();
            btnRowLE.preferredHeight = 38f;
            var btnRowHLG = btnRow.AddComponent<HorizontalLayoutGroup>();
            btnRowHLG.spacing = 8f;
            btnRowHLG.childAlignment = TextAnchor.MiddleCenter;
            btnRowHLG.childControlWidth = true;
            btnRowHLG.childControlHeight = true;
            btnRowHLG.childForceExpandWidth = true;
            btnRowHLG.childForceExpandHeight = true;

            Button btnH = CreateThemedButton(btnRow.transform, "BtnOverrideH", "가로", fontToUse, new Color(0.23f, 0.61f, 0.94f));
            CreateCooldownOverlay(btnH, fontToUse, out Image imgCooldownH, out TextMeshProUGUI txtCooldownH);

            Button btnV = CreateThemedButton(btnRow.transform, "BtnOverrideV", "세로", fontToUse, COLOR_GAUGE_GREEN);
            CreateCooldownOverlay(btnV, fontToUse, out Image imgCooldownV, out TextMeshProUGUI txtCooldownV);

            // ═══════════════════════════════════════════════════════════════
            //  [우측] 미니맵 (RenderTexture) + 대기 차량 오버레이
            // ═══════════════════════════════════════════════════════════════
            GameObject rightPanel = CreateChild(signalRoot.transform, "RightMinimapPanel");
            var rightLE = rightPanel.AddComponent<LayoutElement>();
            rightLE.flexibleWidth = 2f;
            rightLE.minWidth = 120f;

            // 미니맵 테두리 (둥근 프레임) 및 마스크
            Sprite frameSprite = LoadSprite(PATH_FRAME_RECT);
            GameObject minimapMaskObj = CreateChild(rightPanel.transform, "MinimapMask");
            StretchFull(minimapMaskObj);
            var maskImg = minimapMaskObj.AddComponent<Image>();
            if (frameSprite != null) { maskImg.sprite = frameSprite; maskImg.type = Image.Type.Sliced; }
            maskImg.color = COLOR_SECTION_BG;
            var mask = minimapMaskObj.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            // 미니맵 배경 (RawImage: 마스크 자식으로 배치)
            GameObject minimapObj = CreateChild(minimapMaskObj.transform, "MinimapRawImage");
            StretchFull(minimapObj);
            var minimapRawImage = minimapObj.AddComponent<RawImage>();
            minimapRawImage.color = Color.white; // 렌더 텍스처를 원본 색상으로 표시
            minimapRawImage.enabled = false; // 기본적으론 꺼둠 (카메라 연동 전)
            
            // 미니맵 위에 대기 차량 숫자를 오버레이 (N, S, E, W) - 코루틴이 화면 좌표로 실시간 추적할 예정
            TMP_Text txtWaitN = CreateCrossText(minimapObj.transform, "TxtWaitN", "0", fontToUse,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(44f, 24f));
            TMP_Text txtWaitS = CreateCrossText(minimapObj.transform, "TxtWaitS", "0", fontToUse,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(44f, 24f));
            TMP_Text txtWaitE = CreateCrossText(minimapObj.transform, "TxtWaitE", "0", fontToUse,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(44f, 24f));
            TMP_Text txtWaitW = CreateCrossText(minimapObj.transform, "TxtWaitW", "0", fontToUse,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(44f, 24f));

            // 에디터 뷰에서 중앙에 까만 박스로 뭉쳐보이는 것을 방지하기 위해 기본은 꺼둠
            txtWaitN.gameObject.SetActive(false);
            txtWaitS.gameObject.SetActive(false);
            txtWaitE.gameObject.SetActive(false);
            txtWaitW.gameObject.SetActive(false);

            // 미니맵 테두리 외곽선 덧씌우기 (옵션)
            GameObject frameBorder = CreateChild(rightPanel.transform, "MinimapBorderOverlay");
            StretchFull(frameBorder);
            var borderImg = frameBorder.AddComponent<Image>();
            if (frameSprite != null) { borderImg.sprite = frameSprite; borderImg.type = Image.Type.Sliced; }
            borderImg.color = new Color(0.3f, 0.35f, 0.4f, 0.6f);
            borderImg.raycastTarget = false;

            // ═══════════════════════════════════════════════════════════════
            //  자동 Wire-Up (SerializedObject)
            // ═══════════════════════════════════════════════════════════════
            var so = new SerializedObject(controller);

            // Title Text 확보
            TextMeshProUGUI titleText = FindOrCreateTitle(analysisCardObj, fontToUse);
            if (titleText != null) so.FindProperty("txtTitle").objectReferenceValue = titleText;

            so.FindProperty("normalInfoContainer").objectReferenceValue = normalContainer.gameObject;
            so.FindProperty("signalControlContainer").objectReferenceValue = signalRoot;
            so.FindProperty("sliderOffset").objectReferenceValue = sliderOffset;
            so.FindProperty("sliderGreen").objectReferenceValue = sliderGreen;
            so.FindProperty("btnOverrideH").objectReferenceValue = btnH;
            so.FindProperty("btnOverrideV").objectReferenceValue = btnV;
            so.FindProperty("imgCooldownH").objectReferenceValue = imgCooldownH;
            so.FindProperty("txtCooldownH").objectReferenceValue = txtCooldownH;
            so.FindProperty("imgCooldownV").objectReferenceValue = imgCooldownV;
            so.FindProperty("txtCooldownV").objectReferenceValue = txtCooldownV;

            // 게이지 UI
            so.FindProperty("cycleGaugeCursor").objectReferenceValue = cursor;
            so.FindProperty("leHG").objectReferenceValue = leHG;
            so.FindProperty("leHY").objectReferenceValue = leHY;
            so.FindProperty("leHC").objectReferenceValue = leHC;
            so.FindProperty("leVG").objectReferenceValue = leVG;
            so.FindProperty("leVY").objectReferenceValue = leVY;
            so.FindProperty("leVC").objectReferenceValue = leVC;

            // 대기 차량 UI
            so.FindProperty("txtWaitN").objectReferenceValue = txtWaitN;
            so.FindProperty("txtWaitS").objectReferenceValue = txtWaitS;
            so.FindProperty("txtWaitE").objectReferenceValue = txtWaitE;
            so.FindProperty("txtWaitW").objectReferenceValue = txtWaitW;

            // 미니맵 RawImage
            so.FindProperty("minimapRawImage").objectReferenceValue = minimapRawImage;

            so.ApplyModifiedProperties();

            // 초기 상태: 비활성화 (교차로 클릭 시 코드에서 활성화)
            signalRoot.SetActive(false);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[CityFlow] ✅ Signal Control UI assembled successfully with GUI-MonoRound theme!");
        }

        // ═══════════════════════════════════════════════════════════════════
        //  게이지 섹션 생성
        // ═══════════════════════════════════════════════════════════════════
        private static GameObject CreateGaugeSection(
            Transform parent, TMP_FontAsset font,
            out RectTransform cursor,
            out LayoutElement leHG, out LayoutElement leHY, out LayoutElement leHC,
            out LayoutElement leVG, out LayoutElement leVY, out LayoutElement leVC)
        {
            // 게이지 전체 래퍼
            GameObject wrapper = CreateChild(parent, "CycleGaugeWrapper");
            var wrapperLE = wrapper.AddComponent<LayoutElement>();
            wrapperLE.preferredHeight = 36f;

            var wrapperVLG = wrapper.AddComponent<VerticalLayoutGroup>();
            wrapperVLG.spacing = 2f;
            wrapperVLG.childAlignment = TextAnchor.MiddleCenter;
            wrapperVLG.childControlWidth = true;
            wrapperVLG.childControlHeight = true;
            wrapperVLG.childForceExpandWidth = true;
            wrapperVLG.childForceExpandHeight = false;

            // 라벨
            GameObject lblRow = CreateChild(wrapper.transform, "GaugeLabelRow");
            var lblRowLE = lblRow.AddComponent<LayoutElement>();
            lblRowLE.preferredHeight = 16f;
            var lblTxt = lblRow.AddComponent<TextMeshProUGUI>();
            lblTxt.text = "신호 주기";
            lblTxt.fontSize = 11f;
            lblTxt.color = COLOR_LABEL;
            lblTxt.alignment = TextAlignmentOptions.Left;
            if (font != null) lblTxt.font = font;

            // 게이지 바 컨테이너 (HorizontalLayoutGroup + 고정 높이)
            GameObject gaugeBg = CreateChild(wrapper.transform, "CycleGauge_Bg");
            var gaugeBgLE = gaugeBg.AddComponent<LayoutElement>();
            gaugeBgLE.preferredHeight = 14f;

            // 배경 이미지 (둥근 테두리)
            var gaugeBgImg = gaugeBg.AddComponent<Image>();
            Sprite gaugeBgSprite = LoadSprite(PATH_GAUGE_BG);
            if (gaugeBgSprite != null)
            {
                gaugeBgImg.sprite = gaugeBgSprite;
                gaugeBgImg.type = Image.Type.Sliced;
            }
            gaugeBgImg.color = COLOR_SECTION_BG;

            var gaugeHLG = gaugeBg.AddComponent<HorizontalLayoutGroup>();
            gaugeHLG.spacing = 0f;
            gaugeHLG.padding = new RectOffset(1, 1, 1, 1);
            gaugeHLG.childAlignment = TextAnchor.MiddleCenter;
            gaugeHLG.childControlWidth = true;
            gaugeHLG.childControlHeight = true;
            gaugeHLG.childForceExpandWidth = false;
            gaugeHLG.childForceExpandHeight = true;

            // 6개의 신호 색상 블록
            leHG = CreateGaugeBlock(gaugeBg.transform, "HG", COLOR_GAUGE_GREEN, 3f);
            leHY = CreateGaugeBlock(gaugeBg.transform, "HY", COLOR_GAUGE_YELLOW, 0.5f);
            leHC = CreateGaugeBlock(gaugeBg.transform, "HC", COLOR_GAUGE_RED, 2f);
            leVG = CreateGaugeBlock(gaugeBg.transform, "VG", COLOR_GAUGE_GREEN, 3f);
            leVY = CreateGaugeBlock(gaugeBg.transform, "VY", COLOR_GAUGE_YELLOW, 0.5f);
            leVC = CreateGaugeBlock(gaugeBg.transform, "VC", COLOR_GAUGE_RED, 2f);

            // 커서 (IgnoreLayout으로 게이지 위에 자유 배치)
            GameObject cursorObj = CreateChild(gaugeBg.transform, "Cursor");
            var cursorRT = cursorObj.GetComponent<RectTransform>();
            cursorRT.anchorMin = new Vector2(0f, 0f);
            cursorRT.anchorMax = new Vector2(0f, 1f);
            cursorRT.pivot = new Vector2(0.5f, 0.5f);
            cursorRT.sizeDelta = new Vector2(3f, 0f);
            cursorRT.anchoredPosition = new Vector2(0f, 0f);

            var cursorImg = cursorObj.AddComponent<Image>();
            cursorImg.color = COLOR_CURSOR;

            // IgnoreLayout으로 HorizontalLayoutGroup 정렬에서 제외
            var cursorLayoutElem = cursorObj.AddComponent<LayoutElement>();
            cursorLayoutElem.ignoreLayout = true;

            // Canvas에 Outline 효과 추가 (시인성 향상)
            var cursorOutline = cursorObj.AddComponent<Outline>();
            cursorOutline.effectColor = new Color(0f, 0f, 0f, 0.5f);
            cursorOutline.effectDistance = new Vector2(1f, 0f);

            cursor = cursorRT;
            return wrapper;
        }

        private static LayoutElement CreateGaugeBlock(Transform parent, string name, Color color, float flexWidth)
        {
            GameObject block = CreateChild(parent, name);
            var img = block.AddComponent<Image>();
            img.color = color;

            var le = block.AddComponent<LayoutElement>();
            le.flexibleWidth = flexWidth;
            le.minWidth = 4f;

            return le;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  슬라이더 생성 (GUI-MonoRound 테마)
        // ═══════════════════════════════════════════════════════════════════
        private static Slider CreateThemedSlider(Transform parent, string name, string labelText, TMP_FontAsset font)
        {
            // 슬라이더 행 전체 래퍼
            GameObject row = CreateChild(parent, name + "_Row");
            var rowLE = row.AddComponent<LayoutElement>();
            rowLE.preferredHeight = 36f;

            var rowHLG = row.AddComponent<HorizontalLayoutGroup>();
            rowHLG.spacing = 8f;
            rowHLG.childAlignment = TextAnchor.MiddleLeft;
            rowHLG.childControlWidth = true;
            rowHLG.childControlHeight = true;
            rowHLG.childForceExpandWidth = false;
            rowHLG.childForceExpandHeight = true;

            // 라벨
            GameObject lblObj = CreateChild(row.transform, "Lbl" + name);
            var lblLE = lblObj.AddComponent<LayoutElement>();
            lblLE.preferredWidth = 72f;
            lblLE.minWidth = 56f;
            var lblTxt = lblObj.AddComponent<TextMeshProUGUI>();
            lblTxt.text = labelText;
            lblTxt.fontSize = 13f;
            lblTxt.color = COLOR_LABEL;
            lblTxt.alignment = TextAlignmentOptions.Right;
            if (font != null) lblTxt.font = font;

            // 슬라이더 본체
            GameObject sliderObj = CreateChild(row.transform, name);
            var sliderLE = sliderObj.AddComponent<LayoutElement>();
            sliderLE.flexibleWidth = 1f;
            sliderLE.minWidth = 100f;
            sliderLE.preferredHeight = 24f;

            Sprite frameSprite = LoadSprite(PATH_SLIDER_FRAME);
            Sprite fillSprite = LoadSprite(PATH_SLIDER_FILL);
            Sprite handleSprite = LoadSprite(PATH_SLIDER_HANDLE);

            // Background (Thinner Track)
            GameObject bgObj = CreateChild(sliderObj.transform, "Background");
            var bgRT = bgObj.GetComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0f, 0.35f);
            bgRT.anchorMax = new Vector2(1f, 0.65f);
            bgRT.sizeDelta = Vector2.zero;
            var bgImg = bgObj.AddComponent<Image>();
            if (frameSprite != null) { bgImg.sprite = frameSprite; bgImg.type = Image.Type.Sliced; }
            bgImg.color = new Color(0.2f, 0.22f, 0.28f);

            // Fill Area (Thinner Track)
            GameObject fillAreaObj = CreateChild(sliderObj.transform, "Fill Area");
            var fillAreaRT = fillAreaObj.GetComponent<RectTransform>();
            fillAreaRT.anchorMin = new Vector2(0f, 0.35f);
            fillAreaRT.anchorMax = new Vector2(1f, 0.65f);
            fillAreaRT.offsetMin = new Vector2(5f, 0f);
            fillAreaRT.offsetMax = new Vector2(-15f, 0f);

            // Fill
            GameObject fillObj = CreateChild(fillAreaObj.transform, "Fill");
            var fillRT = fillObj.GetComponent<RectTransform>();
            fillRT.sizeDelta = Vector2.zero;
            var fillImg = fillObj.AddComponent<Image>();
            if (fillSprite != null) { fillImg.sprite = fillSprite; fillImg.type = Image.Type.Sliced; }
            fillImg.color = COLOR_GAUGE_GREEN;

            // Handle Slide Area
            GameObject handleAreaObj = CreateChild(sliderObj.transform, "Handle Slide Area");
            var handleAreaRT = handleAreaObj.GetComponent<RectTransform>();
            handleAreaRT.anchorMin = new Vector2(0f, 0.5f);
            handleAreaRT.anchorMax = new Vector2(1f, 0.5f);
            handleAreaRT.sizeDelta = new Vector2(0f, 20f);
            handleAreaRT.anchoredPosition = Vector2.zero;
            handleAreaRT.offsetMin = new Vector2(10f, handleAreaRT.offsetMin.y);
            handleAreaRT.offsetMax = new Vector2(-10f, handleAreaRT.offsetMax.y);

            // Handle
            GameObject handleObj = CreateChild(handleAreaObj.transform, "Handle");
            var handleRT = handleObj.GetComponent<RectTransform>();
            handleRT.anchorMin = new Vector2(0f, 0.5f);
            handleRT.anchorMax = new Vector2(0f, 0.5f);
            handleRT.pivot = new Vector2(0.5f, 0.5f);
            handleRT.sizeDelta = new Vector2(20f, 20f);
            handleRT.anchoredPosition = Vector2.zero;
            var handleImg = handleObj.AddComponent<Image>();
            
            // 완벽한 원형 보장을 위해 유니티 기본 Knob 스프라이트 강제 사용
            Sprite knobSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            if (knobSprite != null) { handleImg.sprite = knobSprite; handleImg.type = Image.Type.Simple; }
            else if (handleSprite != null) { handleImg.sprite = handleSprite; }
            
            handleImg.color = Color.white;

            // Slider 컴포넌트 세팅
            var slider = sliderObj.AddComponent<Slider>();
            slider.fillRect = fillRT;
            slider.handleRect = handleRT;
            slider.targetGraphic = handleImg;
            slider.wholeNumbers = true;
            slider.direction = Slider.Direction.LeftToRight;

            return slider;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  버튼 생성 (Pill Design)
        // ═══════════════════════════════════════════════════════════════════
        private static Button CreateThemedButton(Transform parent, string name, string text, TMP_FontAsset font, Color btnColor)
        {
            GameObject obj = CreateChild(parent, name);
            var le = obj.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.preferredHeight = 34f;

            var img = obj.AddComponent<Image>();
            Sprite btnSprite = LoadSprite(PATH_FRAME_RECT);
            if (btnSprite != null) { img.sprite = btnSprite; img.type = Image.Type.Sliced; }
            img.color = btnColor;

            var btn = obj.AddComponent<Button>();
            btn.targetGraphic = img;

            // 버튼 텍스트
            GameObject textObj = CreateChild(obj.transform, "Text");
            StretchFull(textObj);
            var txt = textObj.AddComponent<TextMeshProUGUI>();
            txt.text = text;
            txt.color = Color.white;
            txt.alignment = TextAlignmentOptions.Center;
            txt.fontSize = 15f;
            txt.fontStyle = FontStyles.Bold;
            if (font != null) txt.font = font;

            return btn;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  쿨다운 오버레이 (기존 구현 유지)
        // ═══════════════════════════════════════════════════════════════════
        private static void CreateCooldownOverlay(Button parentBtn, TMP_FontAsset font, out Image img, out TextMeshProUGUI txt)
        {
            GameObject overlayObj = CreateChild(parentBtn.transform, "CooldownOverlay");
            StretchFull(overlayObj);

            img = overlayObj.AddComponent<Image>();
            img.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;
            img.fillOrigin = 2; // Top
            img.color = new Color(0f, 0f, 0f, 0.7f);
            img.fillAmount = 0f;

            GameObject textObj = CreateChild(overlayObj.transform, "CooldownText");
            StretchFull(textObj);

            txt = textObj.AddComponent<TextMeshProUGUI>();
            txt.text = "";
            txt.color = Color.yellow;
            txt.alignment = TextAlignmentOptions.Center;
            txt.fontSize = 16f;
            if (font != null) txt.font = font;

            overlayObj.SetActive(false);
            textObj.SetActive(false);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  교차로 십자 UI (우측 패널)
        // ═══════════════════════════════════════════════════════════════════
        private static TMP_Text CreateCrossText(
            Transform parent, string name, string defaultText, TMP_FontAsset font,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
        {
            GameObject obj = CreateChild(parent, name);
            var rt = obj.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            // 배경 (반투명 둥근 사각형)
            var bg = obj.AddComponent<Image>();
            Sprite rectSprite = LoadSprite(PATH_FRAME_RECT);
            if (rectSprite != null) { bg.sprite = rectSprite; bg.type = Image.Type.Sliced; }
            bg.color = COLOR_SECTION_BG;

            // ContentSizeFitter로 텍스트 길이에 따라 자동 확장 (3자리수 대응)
            var csf = obj.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            // LayoutElement로 최소/최대 폭 제한
            var le = obj.AddComponent<LayoutElement>();
            le.minWidth = 36f;

            // 텍스트
            GameObject textChild = CreateChild(obj.transform, "Value");
            StretchFull(textChild);
            var txt = textChild.AddComponent<TextMeshProUGUI>();
            txt.text = defaultText;
            txt.color = COLOR_VALUE;
            txt.alignment = TextAlignmentOptions.Center;
            txt.fontSize = 18f;
            txt.fontStyle = FontStyles.Bold;
            if (font != null) txt.font = font;

            return txt;
        }


        // ═══════════════════════════════════════════════════════════════════
        //  Title Text 확보
        // ═══════════════════════════════════════════════════════════════════
        private static TextMeshProUGUI FindOrCreateTitle(GameObject analysisCardObj, TMP_FontAsset font)
        {
            foreach (var txt in analysisCardObj.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (txt.name.IndexOf("Title", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    txt.name.IndexOf("Header", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return txt;
                }
            }

            GameObject textObj = CreateChild(analysisCardObj.transform, "TitleText");
            textObj.transform.SetAsFirstSibling();
            var textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0, 1);
            textRect.anchorMax = new Vector2(1, 1);
            textRect.pivot = new Vector2(0.5f, 1);
            textRect.anchoredPosition = new Vector2(0, -15);
            textRect.sizeDelta = new Vector2(-30, 40);

            var titleText = textObj.AddComponent<TextMeshProUGUI>();
            titleText.text = "교차로 신호 제어";
            titleText.color = COLOR_TITLE;
            titleText.alignment = TextAlignmentOptions.TopLeft;
            titleText.fontSize = 22;
            titleText.fontStyle = FontStyles.Bold;
            if (font != null) titleText.font = font;

            return titleText;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  유틸리티
        // ═══════════════════════════════════════════════════════════════════
        private static GameObject CreateChild(Transform parent, string name)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            return obj;
        }

        private static void StretchFull(GameObject obj)
        {
            var rt = obj.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
        }

        private static Sprite LoadSprite(string path)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static GameObject FindObjectIncludingInactive(string name)
        {
            var scene = EditorSceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                var children = root.GetComponentsInChildren<Transform>(true);
                foreach (var child in children)
                {
                    if (child.name == name) return child.gameObject;
                }
            }
            return null;
        }
    }
}
