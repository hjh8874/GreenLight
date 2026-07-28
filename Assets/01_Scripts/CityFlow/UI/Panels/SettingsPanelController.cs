using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI
{
    public class SettingsPanelController : MonoBehaviour, ICityFlowServiceConsumer
    {
        [Header("Settings UI")]
        [SerializeField] private Toggle tglMuteAudio;
        [SerializeField] private Toggle tglCongestionView;
        [SerializeField] private Button btnQuitGame;
        [SerializeField] private Button btnTitleScene;
        [SerializeField] private string titleSceneName = "TitleScene";

        private CityFlowServices _services;
        private bool _isBound;

        public void Configure(Toggle muteAudio, Button quitGame, Toggle congestionView = null, Button titleScene = null)
        {
            tglMuteAudio = muteAudio;
            btnQuitGame = quitGame;
            tglCongestionView = congestionView;
            btnTitleScene = titleScene;
            BindButtons();
        }

        public void Initialize(CityFlowServices services)
        {
            _services = services;
            if (tglCongestionView != null && _services?.Events != null)
            {
                tglCongestionView.SetIsOnWithoutNotify(_services.Events.IsCongestionViewEnabled);
            }
        }

        private void Start()
        {
            BindButtons();
        }

        private void BindButtons()
        {
            if (_isBound)
            {
                return;
            }



            // 뮤트 토글 이벤트 바인딩
            if (tglMuteAudio != null)
            {
                tglMuteAudio.onValueChanged.AddListener(OnMuteToggleChanged);
            }

            // 종료 버튼 이벤트 바인딩
            if (btnQuitGame != null)
            {
                btnQuitGame.onClick.AddListener(OnQuitClicked);
            }

            // 타이틀 이동 버튼 이벤트 바인딩
            if (btnTitleScene != null)
            {
                btnTitleScene.onClick.AddListener(OnTitleSceneClicked);
            }

            // 혼잡도 오버레이 토글 이벤트 바인딩
            if (tglCongestionView != null)
            {
                tglCongestionView.onValueChanged.AddListener(OnCongestionToggleChanged);
            }

            _isBound = true;
        }

        private void OnCongestionToggleChanged(bool isOn)
        {
            if (_services != null && _services.Events != null)
            {
                _services.Events.PublishCongestionViewToggled(isOn);
                Debug.Log($"[Settings] 혼잡도 뷰 토글: {isOn}");
            }
        }

        private void OnMuteToggleChanged(bool isMuted)
        {
            // 유니티 전체 사운드 리스너 볼륨 조절 (0 = 뮤트, 1 = 정상)
            AudioListener.volume = isMuted ? 0f : 1f;
            Debug.Log($"[Settings] 전체 사운드 뮤트: {isMuted}");
        }

        private void OnQuitClicked()
        {
            Debug.Log("[Settings] 게임 종료 버튼 클릭됨.");
            
#if UNITY_EDITOR
            // 에디터에서는 플레이 모드를 종료합니다.
            UnityEditor.EditorApplication.isPlaying = false;
#else
            // 실제 빌드에서는 프로그램을 종료합니다.
            Application.Quit();
#endif
        }

        private void OnTitleSceneClicked()
        {
            if (!Application.CanStreamedLevelBeLoaded(titleSceneName))
            {
                Debug.LogError($"[Settings] '{titleSceneName}' 씬을 로드할 수 없습니다. Build Settings에 추가되어 있는지 확인하세요.");
                return;
            }

            if (_services == null)
            {
                Debug.LogWarning("[Settings] CityFlowServices가 초기화되지 않아 저장을 건너뜁니다.");
            }
            else
            {
                Debug.Log("[Settings] 게임 상태를 저장하고 타이틀 화면으로 이동합니다.");
                if (_services.Save != null && !_services.Save.Save())
                {
                    Debug.LogError("[Settings] 게임 상태 저장에 실패했습니다. 진행 손실을 막기 위해 타이틀 화면으로 이동하지 않습니다.");
                    return;
                }
            }
            
            UnityEngine.SceneManagement.SceneManager.LoadScene(titleSceneName);
        }

        private void OnDestroy()
        {
            if (tglMuteAudio != null)
                tglMuteAudio.onValueChanged.RemoveListener(OnMuteToggleChanged);
            if (btnQuitGame != null)
                btnQuitGame.onClick.RemoveListener(OnQuitClicked);
            if (tglCongestionView != null)
                tglCongestionView.onValueChanged.RemoveListener(OnCongestionToggleChanged);
            if (btnTitleScene != null)
                btnTitleScene.onClick.RemoveListener(OnTitleSceneClicked);
        }
        }
    }
}
