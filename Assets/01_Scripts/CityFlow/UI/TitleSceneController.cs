using System.IO;
using CityFlow.Save;
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

        private void Start()
        {
            if (continueButton != null)
            {
                continueButton.interactable = HasSave();
            }

            if (settingsPanel != null)
            {
                settingsPanel.SetActive(false);
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
                    Debug.LogError("[TitleScene] confirmPopup이 연결되어 있지 않아 기존 저장을 안전하게 삭제할 수 없습니다. 팝업이 연결될 때까지 새 게임을 시작할 수 없습니다.");
                    return;
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

        public void OnContinue()
        {
            if (HasSave())
            {
                LoadGame();
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
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void LoadGame()
        {
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
    }
}
