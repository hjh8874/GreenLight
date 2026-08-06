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

        private System.Collections.IEnumerator Start()
        {
            Bootstrap.CityBootstrap.IsTitlePreviewMode = true;
            previewNormalizedZoom01 = LoadPreviewZoom();

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
                if (root.name.Contains("Canvas") || root.name.Contains("UI") || root.name.Contains("Popup"))
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

        private void LateUpdate()
        {
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

        // 새 게임 — 기존 저장과 백업을 제거해 게임 씬의 자동 불러오기 대상에서 제외한다.
        // 세이브 덮어쓰기 확인 UX 연동.
        public void OnStartNewGame()
        {
            if (HasSave())
            {
                if (confirmPopup != null)
                {
                    confirmPopup.Show("새로하기를 누르시면 지금까지 진행한 내용이 모두 지워집니다. 계속하시겠습니까?", ExecuteStartNewGame);
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
            if (settingsPanel != null)
            {
                settingsPanel.SetActive(!settingsPanel.activeSelf);
            }
        }

        public void OnQuit()
        {
            if (confirmPopup != null)
            {
                confirmPopup.Show("게임을 끄시겠습니까?", ExecuteQuit);
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
