using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CityFlow.UI.Editor
{
    public static class TitleSceneBaker
    {
        static readonly Color TextDark = new Color(0.15f, 0.2f, 0.25f);
        static readonly Color BtnGreen = new Color(0.35f, 0.77f, 0.43f);
        static readonly Color BtnBlue = new Color(0.25f, 0.60f, 0.88f);
        static readonly Color BtnOrange = new Color(0.96f, 0.61f, 0.20f);
        static readonly Color BtnPurple = new Color(0.60f, 0.35f, 0.90f);

        [MenuItem("Hwan Tools/🏁 타이틀 씬 생성")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Layer Lab 에셋 로드
            string fontPath = "Assets/99_Download/Layer Lab/GUI-MonoRound/ResourcesData/Fonts/UbuntuTitling-Bold SDF.asset";
            var customFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);

            string btnWhitePath = "Assets/99_Download/Layer Lab/GUI-MonoRound/ResourcesData/Sprites/Components/Button/Btn_Rectangle02_White.png";
            var btnSolidSprite = AssetDatabase.LoadAssetAtPath<Sprite>(btnWhitePath);

            string dotPath = "Assets/99_Download/Layer Lab/GUI-MonoRound/ResourcesData/Sprites/Components/UI_Etc/Etc_Circle.png";
            var dotSprite = AssetDatabase.LoadAssetAtPath<Sprite>(dotPath);

            // 배경 환경은 런타임에 TitleSceneController가 메인 게임 씬을 Additive로 로드하여 처리합니다.
            // (카메라와 조명 등도 메인 씬의 것을 그대로 사용)

            // 2. 캔버스 및 기본 UI
            var canvasGo = new GameObject("TitleCanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();

            var ctrlGo = new GameObject("TitleController");
            var ctrl = ctrlGo.AddComponent<TitleSceneController>();

            // 3. 로고 영역 (Top Center)
            var logoContainer = new GameObject("LogoContainer").AddComponent<RectTransform>();
            logoContainer.SetParent(canvasGo.transform, false);
            logoContainer.anchorMin = new Vector2(0.5f, 1f);
            logoContainer.anchorMax = new Vector2(0.5f, 1f);
            logoContainer.pivot = new Vector2(0.5f, 1f);
            logoContainer.anchoredPosition = new Vector2(0f, -60f);
            logoContainer.sizeDelta = new Vector2(800f, 300f);

            var logoVlg = logoContainer.gameObject.AddComponent<VerticalLayoutGroup>();
            logoVlg.childAlignment = TextAnchor.UpperCenter;
            logoVlg.spacing = -15f;
            logoVlg.childControlWidth = false;
            logoVlg.childControlHeight = false;

            var subTitle = Label("SubTitle", logoContainer.transform, "TRAFFIC MANAGER SIM", 32f, TextDark, FontStyles.Bold, customFont);
            
            var mainTitleGroup = new GameObject("MainTitleGroup").AddComponent<RectTransform>();
            mainTitleGroup.SetParent(logoContainer.transform, false);
            mainTitleGroup.gameObject.AddComponent<LayoutElement>().preferredHeight = 150f;
            var mtgHlg = mainTitleGroup.gameObject.AddComponent<HorizontalLayoutGroup>();
            mtgHlg.spacing = 15f;
            mtgHlg.childAlignment = TextAnchor.MiddleCenter;
            mtgHlg.childControlWidth = false;
            mtgHlg.childControlHeight = false;

            // 로고: 신호등 아이콘
            var tlIcon = Panel("TrafficLightIcon", mainTitleGroup.transform, TextDark, btnSolidSprite);
            tlIcon.rectTransform.sizeDelta = new Vector2(60f, 140f);
            var tlVlg = tlIcon.gameObject.AddComponent<VerticalLayoutGroup>();
            tlVlg.padding = new RectOffset(10, 10, 15, 15);
            tlVlg.spacing = 8f;
            tlVlg.childAlignment = TextAnchor.MiddleCenter;
            tlVlg.childControlWidth = false;
            tlVlg.childControlHeight = false;
            Dot(tlIcon.transform, dotSprite, new Color(0.9f, 0.3f, 0.3f), 30f);
            Dot(tlIcon.transform, dotSprite, new Color(0.9f, 0.8f, 0.2f), 30f);
            Dot(tlIcon.transform, dotSprite, new Color(0.3f, 0.8f, 0.4f), 30f);

            var mainTitle = Label("MainTitle", mainTitleGroup.transform, "IDLE\nCITY", 110f, TextDark, FontStyles.Bold, customFont);
            mainTitle.lineSpacing = -25f;
            var outline = mainTitle.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.white;
            outline.effectDistance = new Vector2(3f, -3f);

            // 로고 중앙 정렬을 맞추기 위한 우측 더미 스페이서 (좌측 신호등과 너비 60f 동일)
            var dummySpacer = new GameObject("DummySpacer").AddComponent<RectTransform>();
            dummySpacer.SetParent(mainTitleGroup.transform, false);
            dummySpacer.sizeDelta = new Vector2(60f, 140f);
            var dsLayout = dummySpacer.gameObject.AddComponent<LayoutElement>();
            dsLayout.preferredWidth = 60f;
            dsLayout.preferredHeight = 140f;

            // 4. 메인 버튼 영역 (Bottom Center)
            var menuContainer = new GameObject("MenuContainer").AddComponent<RectTransform>();
            menuContainer.SetParent(canvasGo.transform, false);
            menuContainer.anchorMin = new Vector2(0.5f, 0f);
            menuContainer.anchorMax = new Vector2(0.5f, 0f);
            menuContainer.pivot = new Vector2(0.5f, 0f);
            menuContainer.anchoredPosition = new Vector2(0f, 60f);
            menuContainer.sizeDelta = new Vector2(600f, 320f);

            var vlg = menuContainer.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 15f;
            vlg.childAlignment = TextAnchor.LowerCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // Row 1: Start Game (Big)
            var startBtnRow = new GameObject("StartRow").AddComponent<RectTransform>();
            startBtnRow.SetParent(menuContainer.transform, false);
            var sHlg = startBtnRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            sHlg.childControlWidth = true; sHlg.childControlHeight = true;
            sHlg.childForceExpandWidth = true; sHlg.childForceExpandHeight = false;
            var startBtn = MakeButton("StartButton", startBtnRow.transform, "▶ START GAME", BtnGreen, Color.white, btnSolidSprite, 42f, customFont, 100f);

            // Row 2: Play Game, Load City
            var row2 = new GameObject("Row2").AddComponent<RectTransform>();
            row2.SetParent(menuContainer.transform, false);
            var r2Hlg = row2.gameObject.AddComponent<HorizontalLayoutGroup>();
            r2Hlg.spacing = 15f;
            r2Hlg.childControlWidth = true; r2Hlg.childControlHeight = true;
            r2Hlg.childForceExpandWidth = true; r2Hlg.childForceExpandHeight = false;
            var continueBtn = MakeButton("PlayButton", row2.transform, "PLAY GAME", BtnGreen, Color.white, btnSolidSprite, 32f, customFont, 80f);
            var dummyLoadBtn = MakeButton("LoadButton", row2.transform, "LOAD CITY", BtnBlue, Color.white, btnSolidSprite, 32f, customFont, 80f);

            // Row 3: Settings, About
            var row3 = new GameObject("Row3").AddComponent<RectTransform>();
            row3.SetParent(menuContainer.transform, false);
            var r3Hlg = row3.gameObject.AddComponent<HorizontalLayoutGroup>();
            r3Hlg.spacing = 15f;
            r3Hlg.childControlWidth = true; r3Hlg.childControlHeight = true;
            r3Hlg.childForceExpandWidth = true; r3Hlg.childForceExpandHeight = false;
            var settingsBtn = MakeButton("SettingsButton", row3.transform, "SETTINGS", BtnOrange, Color.white, btnSolidSprite, 32f, customFont, 80f);
            var quitBtn = MakeButton("AboutQuitButton", row3.transform, "ABOUT", BtnPurple, Color.white, btnSolidSprite, 32f, customFont, 80f);

            // --- 배선 ---
            UnityEventTools.AddPersistentListener(startBtn.onClick, ctrl.OnStartNewGame);
            UnityEventTools.AddPersistentListener(continueBtn.onClick, ctrl.OnPlayGameClicked);
            UnityEventTools.AddPersistentListener(settingsBtn.onClick, ctrl.OnSettings);
            UnityEventTools.AddPersistentListener(quitBtn.onClick, ctrl.OnQuit);

            var confirmPopup = MakeConfirmPopup(canvasGo.transform, Color.white, BtnBlue, TextDark, btnSolidSprite, customFont);

            var so = new SerializedObject(ctrl);
            so.FindProperty("continueButton").objectReferenceValue = continueBtn;
            so.FindProperty("confirmPopup").objectReferenceValue = confirmPopup;
            so.ApplyModifiedProperties();

            // 저장
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, "Assets/00_Scenes/TitleScene.unity");
            Debug.Log("[TitleSceneBaker] 목업 스타일 TitleScene 생성 완료!");
        }

        // ---- 헬퍼 ----

        static ConfirmPopupController MakeConfirmPopup(Transform parent, Color bg, Color btnColor, Color textCol, Sprite sprite, TMP_FontAsset font)
        {
            var panel = Panel("ConfirmPopup", parent, new Color(0, 0, 0, 0.7f), null);
            var pr = panel.rectTransform;
            pr.anchorMin = Vector2.zero; pr.anchorMax = Vector2.one;
            pr.offsetMin = pr.offsetMax = Vector2.zero;

            var card = Panel("Card", panel.transform, bg, sprite);
            var cr = card.rectTransform;
            cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0.5f);
            cr.pivot = new Vector2(0.5f, 0.5f);
            cr.sizeDelta = new Vector2(600f, 350f);

            var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(40, 40, 50, 40);
            vlg.spacing = 30f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.MiddleCenter;

            var msg = Label("Message", card.transform, "진행 내역이 삭제됩니다. 계속하시겠습니까?", 28f, textCol, FontStyles.Normal, font);
            msg.gameObject.AddComponent<LayoutElement>().preferredHeight = 120f;
            
            var btnGroup = new GameObject("ButtonGroup").AddComponent<RectTransform>();
            btnGroup.SetParent(card.transform, false);
            var hlg = btnGroup.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 30f;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = false;

            var yesBtn = MakeButton("YesButton", btnGroup.transform, "확인", BtnGreen, Color.white, sprite, 26f, font, 70f);
            var noBtn = MakeButton("NoButton", btnGroup.transform, "취소", btnColor, Color.white, sprite, 26f, font, 70f);

            var popup = panel.gameObject.AddComponent<ConfirmPopupController>();
            var so = new SerializedObject(popup);
            so.FindProperty("txtMessage").objectReferenceValue = msg;
            so.FindProperty("btnYes").objectReferenceValue = yesBtn;
            so.FindProperty("btnNo").objectReferenceValue = noBtn;
            so.ApplyModifiedProperties();

            panel.gameObject.SetActive(false);
            return popup;
        }

        static Image Panel(string name, Transform parent, Color col, Sprite sprite)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            img.color = col;
            if (sprite != null) { img.sprite = sprite; img.type = Image.Type.Sliced; }
            return img;
        }

        static void Dot(Transform parent, Sprite sprite, Color col, float size)
        {
            var img = Panel("Dot", parent, col, sprite);
            img.rectTransform.sizeDelta = new Vector2(size, size);
            var le = img.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = size; le.preferredHeight = size;
        }

        static TextMeshProUGUI Label(string name, Transform parent, string text, float size, Color col, FontStyles style, TMP_FontAsset font)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = col;
            t.fontStyle = style;
            t.alignment = TextAlignmentOptions.Center;
            t.enableWordWrapping = false;
            t.raycastTarget = false;
            if (font != null) t.font = font;
            return t;
        }

        static Button MakeButton(string name, Transform parent, string label, Color bg, Color fg, Sprite sprite, float fontSize, TMP_FontAsset font, float height)
        {
            var img = Panel(name, parent, bg, sprite);
            var le = img.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height; le.minHeight = height;
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(0.9f, 0.9f, 0.9f);
            cb.pressedColor = new Color(0.7f, 0.7f, 0.7f);
            cb.fadeDuration = 0.08f;
            btn.colors = cb;

            var t = Label("Label", img.transform, label, fontSize, fg, FontStyles.Bold, font);
            var tr = t.rectTransform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = Vector2.zero; tr.offsetMax = Vector2.zero;
            return btn;
        }

        static void SafeSet(this Object obj, Vector3 pos, float rotY)
        {
            if (obj is GameObject go)
            {
                go.transform.position = pos;
                go.transform.rotation = Quaternion.Euler(0, rotY, 0);
            }
        }
    }
}
