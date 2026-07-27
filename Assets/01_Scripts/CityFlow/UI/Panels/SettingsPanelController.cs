using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI
{
    public class SettingsPanelController : MonoBehaviour
    {
        [Header("Settings UI")]
        [SerializeField] private Toggle tglMuteAudio;
        [SerializeField] private Button btnQuitGame;
        [SerializeField] private Button btnTitleScene;

        private bool _isBound;

        public void Configure(Toggle muteAudio, Button quitGame, Button titleScene = null)
        {
            tglMuteAudio = muteAudio;
            btnQuitGame = quitGame;
            btnTitleScene = titleScene;
            BindButtons();
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

            _isBound = true;
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
            Debug.Log("[Settings] 타이틀 화면으로 이동합니다.");
            UnityEngine.SceneManagement.SceneManager.LoadScene("TitleScene");
        }
    }
}
