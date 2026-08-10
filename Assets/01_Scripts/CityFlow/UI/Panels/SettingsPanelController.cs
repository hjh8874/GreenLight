using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI
{
    public class SettingsPanelController : MonoBehaviour,
        ICityFlowServiceConsumer
    {
        private const string RedundantCongestionToggleName =
            "CongestionViewToggle";

        [Header("Settings UI")]
        [SerializeField] private Button btnQuitGame;
        [SerializeField] private Button btnTitleScene;
        [SerializeField] private string titleSceneName = "TitleScene";

        private CityFlowServices services;
        private bool isBound;

        private void Awake()
        {
            RemoveRedundantCongestionToggle();
        }

        private void RemoveRedundantCongestionToggle()
        {
            Transform redundantToggle =
                transform.Find(RedundantCongestionToggleName);
            if (redundantToggle == null)
            {
                return;
            }

            redundantToggle.gameObject.SetActive(false);
            if (Application.isPlaying)
            {
                Destroy(redundantToggle.gameObject);
            }
        }

        public void Configure(
            Button quitGame,
            Button titleScene = null)
        {
            UnbindButtons();
            btnQuitGame = quitGame;
            btnTitleScene = titleScene;
            BindButtons();
        }

        public void Configure(
            Toggle runtimeMute,
            Button quitGame,
            Button titleScene)
        {
            AudioSettingsPanelController audioSettings =
                GetComponent<AudioSettingsPanelController>();
            if (audioSettings == null)
            {
                audioSettings =
                    gameObject.AddComponent<AudioSettingsPanelController>();
            }

            audioSettings.ConfigureMuteOnly(runtimeMute);
            Configure(quitGame, titleScene);
        }

        public void Initialize(CityFlowServices cityFlowServices)
        {
            services = cityFlowServices;
        }

        private void Start()
        {
            BindButtons();
        }

        private void BindButtons()
        {
            if (isBound)
            {
                return;
            }

            btnQuitGame?.onClick.AddListener(OnQuitClicked);
            btnTitleScene?.onClick.AddListener(OnTitleSceneClicked);
            isBound = true;
        }

        private void UnbindButtons()
        {
            if (!isBound)
            {
                return;
            }

            btnQuitGame?.onClick.RemoveListener(OnQuitClicked);
            btnTitleScene?.onClick.RemoveListener(OnTitleSceneClicked);
            isBound = false;
        }

        private void OnQuitClicked()
        {
            Debug.Log("[Settings] 게임 종료 버튼 클릭됨.");

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnTitleSceneClicked()
        {
            if (!Application.CanStreamedLevelBeLoaded(titleSceneName))
            {
                Debug.LogError(
                    $"[Settings] '{titleSceneName}' 씬을 로드할 수 없습니다. " +
                    "Build Settings에 추가되어 있는지 확인하세요.");
                return;
            }

            if (services == null)
            {
                Debug.LogWarning(
                    "[Settings] CityFlowServices가 초기화되지 않아 " +
                    "저장을 건너뜁니다.");
            }
            else
            {
                Debug.Log(
                    "[Settings] 게임 상태를 저장하고 타이틀 화면으로 " +
                    "이동합니다.");
                if (services.Save != null && !services.Save.Save())
                {
                    Debug.LogError(
                        "[Settings] 게임 상태 저장에 실패했습니다. " +
                        "진행 손실을 막기 위해 타이틀 화면으로 " +
                        "이동하지 않습니다.");
                    return;
                }
            }

            UnityEngine.SceneManagement.SceneManager.LoadScene(
                titleSceneName);
        }

        private void OnDestroy()
        {
            UnbindButtons();
        }
    }
}
