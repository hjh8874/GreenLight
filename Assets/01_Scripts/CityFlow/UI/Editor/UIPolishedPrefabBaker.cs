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

            // 표준 요소 프리팹 (재사용 가능한 미니 프리팹)
            BakeStandardPanel();
            BakeStandardButton();
            BakeStandardPopup();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("🎨 [UIPolishedPrefabBaker] 표준 요소 프리팹 생성 완료!");
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
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 12;
            tmp.fontSizeMax = fontSize;
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
    }
}
