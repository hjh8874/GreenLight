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
    // 타이틀 씬(그린웨이브)을 코드로 굽는 베이커. UIGenerator 패턴(정적 MenuItem + 헬퍼).
    // 수동 배선 노이즈 없이 재현 가능하게 씬+UI+버튼 onClick을 한 번에 생성한다.
    public static class TitleSceneBaker
    {
        // 팔레트 — 코지 뉴트럴 + 신호등 3색 액센트(그린웨이브 테마).
        static readonly Color Bg = new Color(0.93f, 0.90f, 0.847f);        // 따뜻한 크림 배경
        static readonly Color Band = new Color(0.87f, 0.83f, 0.74f);        // 하단 밴드(지평선 느낌)
        static readonly Color Card = new Color(0.996f, 0.988f, 0.965f, 1f); // 떠있는 카드(오프화이트)
        static readonly Color Ink = new Color(0.17f, 0.23f, 0.25f);        // 잉크 차콜
        static readonly Color Muted = new Color(0.42f, 0.48f, 0.49f);       // 뮤티드 서브텍스트
        static readonly Color SignalGreen = new Color(0.26f, 0.63f, 0.28f);
        static readonly Color SignalAmber = new Color(0.96f, 0.65f, 0.14f);
        static readonly Color SignalRed = new Color(0.90f, 0.33f, 0.24f);
        static readonly Color BtnSecondary = new Color(0.94f, 0.92f, 0.86f);

        [MenuItem("Hwan Tools/🏁 타이틀 씬 생성")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Bg;
                cam.orthographic = true;
            }

            Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            Sprite knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            // --- Canvas ---
            var canvasGo = new GameObject("TitleCanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // --- EventSystem (신규 인풋 시스템) ---
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();

            // --- 컨트롤러 ---
            var ctrlGo = new GameObject("TitleController");
            var ctrl = ctrlGo.AddComponent<TitleSceneController>();

            // --- 하단 밴드(지평선) ---
            var band = Panel("BottomBand", canvasGo.transform, Band, null);
            var br = band.rectTransform;
            br.anchorMin = new Vector2(0f, 0f);
            br.anchorMax = new Vector2(1f, 0f);
            br.pivot = new Vector2(0.5f, 0f);
            br.sizeDelta = new Vector2(0f, 240f);
            br.anchoredPosition = Vector2.zero;

            // --- 떠있는 카드 ---
            var card = Panel("Card", canvasGo.transform, Card, rounded);
            var cr = card.rectTransform;
            cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0.5f);
            cr.pivot = new Vector2(0.5f, 0.5f);
            cr.sizeDelta = new Vector2(560f, 620f);
            cr.anchoredPosition = new Vector2(0f, 24f);
            var shadow = card.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.18f);
            shadow.effectDistance = new Vector2(0f, -7f);

            var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(46, 46, 44, 44);
            vlg.spacing = 14f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperCenter;

            // 신호등 3색 점 모티프
            var dots = new GameObject("SignalDots").AddComponent<RectTransform>();
            dots.SetParent(card.transform, false);
            var dotsLe = dots.gameObject.AddComponent<LayoutElement>();
            dotsLe.preferredHeight = 26f;
            var hlg = dots.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;
            Dot(dots.transform, knob, SignalRed);
            Dot(dots.transform, knob, SignalAmber);
            Dot(dots.transform, knob, SignalGreen);

            // 타이틀 (NanumGothic fallback으로 한글 렌더)
            Label("Title", card.transform, "그린웨이브", 60f, Ink, FontStyles.Bold, 72f);
            Label("Subtitle", card.transform, "· GREEN WAVE ·", 20f, SignalGreen, FontStyles.Bold, 26f);
            Label("Tagline", card.transform, "신호등 정령 · Traffic Spirit", 17f, Muted, FontStyles.Normal, 22f);

            // 간격
            var spacer = new GameObject("Spacer").AddComponent<RectTransform>();
            spacer.SetParent(card.transform, false);
            spacer.gameObject.AddComponent<LayoutElement>().preferredHeight = 12f;

            // 버튼들
            var startBtn = MakeButton("StartButton", card.transform, "시작하기", SignalGreen, Color.white, rounded, 26f);
            var continueBtn = MakeButton("ContinueButton", card.transform, "이어하기", BtnSecondary, Ink, rounded, 24f);
            var settingsBtn = MakeButton("SettingsButton", card.transform, "설정", BtnSecondary, Ink, rounded, 24f);
            var quitBtn = MakeButton("QuitButton", card.transform, "종료", BtnSecondary, Ink, rounded, 24f);

            // 버전 / 크레딧 (카드 밖, 화면 코너)
            var ver = Label("Version", canvasGo.transform, "v0.1", 18f, Muted, FontStyles.Normal, 0f);
            var vr = ver.rectTransform;
            vr.anchorMin = vr.anchorMax = new Vector2(0f, 0f);
            vr.pivot = new Vector2(0f, 0f);
            vr.anchoredPosition = new Vector2(28f, 22f);
            vr.sizeDelta = new Vector2(200f, 30f);
            ver.alignment = TextAlignmentOptions.BottomLeft;

            var cred = Label("Credits", canvasGo.transform, "© 도시흐름팀", 18f, Muted, FontStyles.Normal, 0f);
            var cd = cred.rectTransform;
            cd.anchorMin = cd.anchorMax = new Vector2(1f, 0f);
            cd.pivot = new Vector2(1f, 0f);
            cd.anchoredPosition = new Vector2(-28f, 22f);
            cd.sizeDelta = new Vector2(240f, 30f);
            cred.alignment = TextAlignmentOptions.BottomRight;

            // --- 버튼 배선 ---
            UnityEventTools.AddPersistentListener(startBtn.onClick, ctrl.OnStartNewGame);
            UnityEventTools.AddPersistentListener(continueBtn.onClick, ctrl.OnContinue);
            UnityEventTools.AddPersistentListener(settingsBtn.onClick, ctrl.OnSettings);
            UnityEventTools.AddPersistentListener(quitBtn.onClick, ctrl.OnQuit);

            var so = new SerializedObject(ctrl);
            so.FindProperty("continueButton").objectReferenceValue = continueBtn;
            so.ApplyModifiedProperties();

            // --- 저장 ---
            System.IO.Directory.CreateDirectory(Application.dataPath + "/00_Scenes");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, "Assets/00_Scenes/TitleScene.unity");
            Debug.Log("[TitleSceneBaker] TitleScene 생성·저장 완료: Assets/00_Scenes/TitleScene.unity");
        }

        // ---- 헬퍼 ----

        static Image Panel(string name, Transform parent, Color col, Sprite sprite)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            img.color = col;
            if (sprite != null)
            {
                img.sprite = sprite;
                img.type = Image.Type.Sliced;
            }
            return img;
        }

        static void Dot(Transform parent, Sprite knob, Color col)
        {
            var img = Panel("Dot", parent, col, knob);
            img.rectTransform.sizeDelta = new Vector2(18f, 18f);
            var le = img.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 18f;
            le.preferredHeight = 18f;
        }

        static TextMeshProUGUI Label(string name, Transform parent, string text, float size,
            Color col, FontStyles style, float preferredHeight)
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
            if (preferredHeight > 0f)
            {
                go.AddComponent<LayoutElement>().preferredHeight = preferredHeight;
            }
            return t;
        }

        static Button MakeButton(string name, Transform parent, string label, Color bg, Color fg,
            Sprite sprite, float fontSize)
        {
            var img = Panel(name, parent, bg, sprite);
            var le = img.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 58f;
            le.minHeight = 58f;
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(0.93f, 0.93f, 0.93f);
            cb.pressedColor = new Color(0.85f, 0.85f, 0.85f);
            cb.fadeDuration = 0.08f;
            btn.colors = cb;

            var t = Label("Label", img.transform, label, fontSize, fg, FontStyles.Bold, 0f);
            var tr = t.rectTransform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = Vector2.zero;
            tr.offsetMax = Vector2.zero;
            return btn;
        }
    }
}
