using System.IO;
using CityFlow.Contracts.Save;
using CityFlow.Save;
using CityFlow.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CityFlow.UI
{
    // 타이틀 화면 컨트롤러 — 시작/이어하기/설정/종료.
    // 엔진·경제 로직 무연결. 세이브는 파일 존재 여부만 읽는다(이어하기 활성 판정).
    public sealed class TitleSceneController : MonoBehaviour
    {
        private const string EnhancementsResourcePath =
            "CityFlow/UI/UI_TitleSceneEnhancements";
        private const string FloatingTitleBarCanvasName =
            "FloatingWindowTitleBarCanvas";
        private const string TitleContentRootName =
            "FloatingWindowTitleContentRoot";

        [Header("Scene Flow")]
        [Tooltip("시작하기가 로드할 게임 씬 이름. Build Settings에 등록돼 있어야 로드된다. (주석님 메인씬)")]
        [SerializeField] private string gameSceneName = "CityFlowIntegrated_cmt";

        [Header("Refs")]
        [Tooltip("세이브가 없으면 비활성화할 이어하기 버튼.")]
        [SerializeField] private Button continueButton;
        [Tooltip("설정 토글로 켜고 끌 패널(옵션).")]
        [SerializeField] private GameObject settingsPanel;
        [Tooltip("새 게임 시 띄울 경고 팝업(옵션).")]
        [SerializeField] private ConfirmPopupController confirmPopup;
        [Tooltip("타이틀 보조 UI를 설치할 Canvas. 비어 있으면 기존 UI 참조의 부모 Canvas를 사용합니다.")]
        [SerializeField] private Canvas titleCanvas;
        [Tooltip("비활성화할 언어 버튼. 비어 있으면 OnLanguageClicked 이벤트 연결로 찾습니다.")]
        [SerializeField] private Button languageButton;
        [Tooltip("로고와 유사한 색상의 배경에서도 글자가 보이도록 " +
                 "로고 뒤 명암 패널을 표시합니다.")]
        [SerializeField] private bool showLogoBackdrop = true;

        private TitleSceneEnhancementsView enhancementsView;

        private System.Collections.IEnumerator Start()
        {
            Bootstrap.CityBootstrap.IsTitlePreviewMode = true;
            previewNormalizedZoom01 = LoadPreviewZoom();
            InstallTitleEnhancements();
            DisableLanguageButton();

            // 백그라운드 씬(실제 게임) 로드 및 설정 (UI 숨김, 자동저장 방지)
            yield return StartCoroutine(LoadBackgroundSceneRoutine());

            if (continueButton != null)
            {
                continueButton.interactable = HasSave();
            }

            if (settingsPanel != null)
            {
                settingsPanel.SetActive(false);
            }
        }

        private System.Collections.IEnumerator LoadBackgroundSceneRoutine()
        {
            if (string.IsNullOrEmpty(gameSceneName) || !Application.CanStreamedLevelBeLoaded(gameSceneName))
                yield break;

            // 이미 로드되어 있는지 확인
            var scene = SceneManager.GetSceneByName(gameSceneName);
            if (!scene.isLoaded)
            {
                var op = SceneManager.LoadSceneAsync(gameSceneName, LoadSceneMode.Additive);
                yield return op;
                scene = SceneManager.GetSceneByName(gameSceneName);
            }

            // 게임 씬의 UI 캔버스 숨기기 (타이틀 씬 UI만 보이도록)
            // GameObject 전체를 비활성화(SetActive(false))하면 런타임 UI 등
            // UI에 붙은 스크립트들이 StartCoroutine을 실행하지 못하고 에러가 발생합니다.
            // 따라서 렌더링(Canvas)과 터치(GraphicRaycaster) 기능만 꺼줍니다.
            foreach (var root in scene.GetRootGameObjects())
            {
                bool isFloatingTitleBar =
                    root.name == FloatingTitleBarCanvasName;
                if (!isFloatingTitleBar
                    && (root.name.Contains("Canvas")
                        || root.name.Contains("UI")
                        || root.name.Contains("Popup")))
                {
                    var canvases = root.GetComponentsInChildren<Canvas>(true);
                    foreach (var c in canvases) c.enabled = false;

                    var raycasters = root.GetComponentsInChildren<UnityEngine.UI.GraphicRaycaster>(true);
                    foreach (var r in raycasters) r.enabled = false;
                }

                // 백그라운드 씬의 EventSystem 비활성화 (TitleScene과 충돌 방지)
                var eventSystems = root.GetComponentsInChildren<UnityEngine.EventSystems.EventSystem>(true);
                foreach (var es in eventSystems)
                {
                    es.enabled = false;
                    var inputModule = es.GetComponent<UnityEngine.EventSystems.BaseInputModule>();
                    if (inputModule != null) inputModule.enabled = false;
                }
            }

            ConfigureFloatingTitleContent();

            // 게임이 라이브로 돌아가면서 자동 저장되는 것을 방지하기 위해 AutoSaveService 파괴
            var autoSave = FindAnyObjectByType<CityFlow.Gameplay.Save.AutoSaveService>(FindObjectsInactive.Include);
            if (autoSave != null)
            {
                Destroy(autoSave);
            }

            // TitleScene에 기존 카메라가 남아있다면 충돌 방지를 위해 제거합니다.
            // [리뷰 반영] 다른 씬의 모든 카메라를 파괴하지 않고 카메라 컴포넌트만 끕니다.
            foreach (var cam in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (cam.gameObject.scene != scene && cam.name != "UI Camera" && cam.name != "UICamera")
                {
                    cam.enabled = false;
                    var audioListener = cam.GetComponent<AudioListener>();
                    if (audioListener != null) audioListener.enabled = false;
                }
            }

            // TitleScene에서는 MainCityView의 카메라 초기화가 지연되거나 무시될 수 있으므로,
            // 매 프레임 강제로 카메라 각도를 고정하는 컴포넌트를 부착합니다.
            StartCoroutine(ForceQuarterViewRoutine(scene));
        }

        private System.Collections.IEnumerator ForceQuarterViewRoutine(Scene loadedScene)
        {
            Camera mainCam = null;
            MainCityView cityView = null;
            // 로드된 씬에서 카메라 찾기
            float timeout = 5f;
            while (mainCam == null && timeout > 0)
            {
                if (loadedScene.isLoaded)
                {
                    foreach (var root in loadedScene.GetRootGameObjects())
                    {
                        mainCam = root.GetComponentInChildren<Camera>(true);
                        cityView ??= root.GetComponentInChildren<MainCityView>(true);
                        if (mainCam != null) break;
                    }
                }
                
                timeout -= Time.deltaTime;
                yield return null;
            }

            if (mainCam == null) yield break;

            // 카메라 각도 고정을 위한 컴포넌트 추가
            // 카메라 각도 고정을 위해 Update 루프에서 사용할 참조 저장
            forcedCamera = mainCam;
            forcedCityView = cityView != null
                ? cityView
                : FindAnyObjectByType<MainCityView>(FindObjectsInactive.Include);
        }

        private Camera forcedCamera;
        private MainCityView forcedCityView;
        private float previewNormalizedZoom01;
        private RectTransform titleContentRoot;
        private FloatingWindowService floatingWindowService;

        private void LateUpdate()
        {
            ApplyFloatingTitleInset();

            if (forcedCamera == null) return;

            // [리뷰 반영] MainCityView의 위치/회전 제어와 경합하지 않도록 줌인(orthographicSize)만 덮어씁니다.
            forcedCamera.orthographic = true;
            if (forcedCityView != null)
            {
                forcedCamera.orthographicSize =
                    forcedCityView.GetOrthographicSize(
                        previewNormalizedZoom01);
            }
        }

        private void ConfigureFloatingTitleContent()
        {
            titleCanvas = ResolveTitleCanvas();
            if (titleCanvas == null)
            {
                return;
            }

            titleContentRoot = titleCanvas.transform.Find(TitleContentRootName)
                as RectTransform;
            if (titleContentRoot == null)
            {
                GameObject contentObject = new GameObject(
                    TitleContentRootName,
                    typeof(RectTransform));
                titleContentRoot = contentObject.GetComponent<RectTransform>();
                titleContentRoot.SetParent(titleCanvas.transform, false);

                Transform[] children = new Transform[
                    titleCanvas.transform.childCount - 1];
                int childIndex = 0;
                for (int index = 0;
                    index < titleCanvas.transform.childCount;
                    index++)
                {
                    Transform child = titleCanvas.transform.GetChild(index);
                    if (child != titleContentRoot)
                    {
                        children[childIndex++] = child;
                    }
                }

                for (int index = 0; index < childIndex; index++)
                {
                    children[index].SetParent(titleContentRoot, false);
                }

                titleContentRoot.SetAsFirstSibling();
            }

            floatingWindowService = FindAnyObjectByType<FloatingWindowService>(
                FindObjectsInactive.Include);
            ApplyFloatingTitleInset();
        }

        private void ApplyFloatingTitleInset()
        {
            if (titleCanvas == null || titleContentRoot == null)
            {
                return;
            }

            if (floatingWindowService == null)
            {
                floatingWindowService = FindAnyObjectByType<FloatingWindowService>(
                    FindObjectsInactive.Include);
            }

            float reservedHeight = floatingWindowService != null
                && floatingWindowService.IsFloating
                && !floatingWindowService.IsMaximized
                    ? FloatingWindowTitleBarController.TitleBarHeight
                    : 0f;
            FloatingWindowTitleBarController.ApplyTopInset(
                titleContentRoot,
                titleCanvas,
                reservedHeight);
        }

        // 새 게임 — 기존 저장과 백업을 제거해 게임 씬의 자동 불러오기 대상에서 제외한다.
        // 세이브 덮어쓰기 확인 UX 연동.
        public void OnStartNewGame()
        {
            if (HasSave())
            {
                if (confirmPopup != null)
                {
                    ShowConfirmation(
                        "새로하기를 누르시면 지금까지 진행한 내용이 " +
                        "모두 지워집니다. 계속하시겠습니까?",
                        ExecuteStartNewGame);
                }
                else
                {
                    Debug.LogWarning("[TitleScene] confirmPopup이 연결되어 있지 않습니다. 기존 저장을 덮어쓰고 새 게임을 즉시 시작합니다.");
                    ExecuteStartNewGame();
                }
            }
            else
            {
                ExecuteStartNewGame();
            }
        }

        private void ExecuteStartNewGame()
        {
            try
            {
                new JsonSaveRepository().DeleteSave();
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"[TitleScene] 새 게임을 시작하기 전에 기존 저장을 삭제하지 못했습니다.\n{exception.Message}");
                return;
            }

            LoadGame();
        }

        public void OnLanguageClicked()
        {
            Debug.Log("[TitleScene] 언어 설정 클릭 (미구현)");
        }

        public void OnPlayGameClicked()
        {
            if (HasSave())
            {
                LoadGame();
            }
            else
            {
                ExecuteStartNewGame();
            }
        }

        public void OnSettings()
        {
            InstallTitleEnhancements();
            if (enhancementsView != null)
            {
                bool visible = !enhancementsView.IsSettingsVisible;
                if (visible)
                {
                    HideConfirmationImmediately();
                }

                enhancementsView.SetSettingsVisible(visible);
                return;
            }

            if (settingsPanel != null)
            {
                bool visible = !settingsPanel.activeSelf;
                if (visible)
                {
                    HideConfirmationImmediately();
                }

                settingsPanel.SetActive(visible);
            }
        }

        private void CloseSettings()
        {
            enhancementsView?.SetSettingsVisible(false);
            if (settingsPanel != null)
            {
                settingsPanel.SetActive(false);
            }
        }

        private void HideConfirmationImmediately()
        {
            if (confirmPopup != null &&
                confirmPopup.gameObject.activeSelf)
            {
                confirmPopup.HideImmediate();
            }
        }

        private void ShowConfirmation(
            string message,
            System.Action onConfirm)
        {
            CloseSettings();
            confirmPopup.Show(message, onConfirm);
        }

        private void InstallTitleEnhancements()
        {
            if (enhancementsView != null)
            {
                return;
            }

            Canvas resolvedCanvas = ResolveTitleCanvas();
            GameObject prefab = Resources.Load<GameObject>(
                EnhancementsResourcePath);
            if (resolvedCanvas == null || prefab == null)
            {
                Debug.LogWarning(
                    "[TitleScene] 타이틀 보조 UI를 설치할 수 없습니다. " +
                    $"Canvas={resolvedCanvas != null}, " +
                    $"Prefab={prefab != null}.");
                return;
            }

            GameObject instance = Instantiate(
                prefab,
                resolvedCanvas.transform,
                false);
            instance.name = "UI_TitleSceneEnhancements";
            enhancementsView =
                instance.GetComponent<TitleSceneEnhancementsView>();
            enhancementsView?.Initialize(showLogoBackdrop);
        }

        private void DisableLanguageButton()
        {
            Button button = ResolveLanguageButton();
            if (button != null)
            {
                button.interactable = false;
            }
        }

        private Canvas ResolveTitleCanvas()
        {
            if (titleCanvas != null)
            {
                return titleCanvas;
            }

            titleCanvas = GetComponentInParent<Canvas>();
            titleCanvas ??= confirmPopup != null
                ? confirmPopup.GetComponentInParent<Canvas>(true)
                : null;
            titleCanvas ??= continueButton != null
                ? continueButton.GetComponentInParent<Canvas>(true)
                : null;
            titleCanvas ??= settingsPanel != null
                ? settingsPanel.GetComponentInParent<Canvas>(true)
                : null;
            return titleCanvas;
        }

        private Button ResolveLanguageButton()
        {
            if (languageButton != null)
            {
                return languageButton;
            }

            Canvas canvas = ResolveTitleCanvas();
            if (canvas == null)
            {
                return null;
            }

            Button[] buttons = canvas.GetComponentsInChildren<Button>(true);
            for (int buttonIndex = 0;
                 buttonIndex < buttons.Length;
                 buttonIndex++)
            {
                Button candidate = buttons[buttonIndex];
                int listenerCount =
                    candidate.onClick.GetPersistentEventCount();
                for (int listenerIndex = 0;
                     listenerIndex < listenerCount;
                     listenerIndex++)
                {
                    if (candidate.onClick.GetPersistentTarget(listenerIndex) ==
                            this &&
                        candidate.onClick.GetPersistentMethodName(
                            listenerIndex) == nameof(OnLanguageClicked))
                    {
                        languageButton = candidate;
                        return languageButton;
                    }
                }
            }

            return null;
        }

        public void OnQuit()
        {
            if (confirmPopup != null)
            {
                ShowConfirmation("게임을 끄시겠습니까?", ExecuteQuit);
            }
            else
            {
                ExecuteQuit();
            }
        }

        private void ExecuteQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
        private void LoadGame()
        {
            Bootstrap.CityBootstrap.IsTitlePreviewMode = false;

            if (string.IsNullOrEmpty(gameSceneName))
            {
                Debug.LogWarning("[TitleScene] gameSceneName이 비어 있어 로드할 수 없습니다.");
                return;
            }

            // Build Settings 미등록 씬은 LoadScene이 예외를 던지므로 먼저 가드.
            if (Application.CanStreamedLevelBeLoaded(gameSceneName))
            {
                SceneManager.LoadScene(gameSceneName);
            }
            else
            {
                Debug.LogWarning(
                    $"[TitleScene] '{gameSceneName}' 씬이 Build Settings에 없어 로드할 수 없습니다. " +
                    "File > Build Settings에 추가하세요.");
            }
        }

        // save_v1.json(또는 백업) 존재 = 이어하기 가능. 경로 진실원 = SaveFilePathProvider.
        private static bool HasSave()
        {
            return File.Exists(SaveFilePathProvider.GetDefaultSavePath())
                || File.Exists(SaveFilePathProvider.GetDefaultBackupSavePath());
        }

        private static float LoadPreviewZoom()
        {
            var repository = new JsonSaveRepository();
            if (!repository.TryLoad(out GameSaveData saveData) ||
                saveData?.CameraView == null ||
                !saveData.CameraView.HasZoom)
            {
                return 0f;
            }

            return Mathf.Clamp01(saveData.CameraView.NormalizedZoom01);
        }
    }
}

