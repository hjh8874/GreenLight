using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI
{
    public class SettingsPanelController : MonoBehaviour
    {
        [Header("Settings UI")]
        [SerializeField] private Toggle tglMuteAudio;
        [SerializeField] private Button btnQuitGame;

        private void Start()
        {
            // 뮤트 톳글 이벤트 바인딩
            if (tglMuteAudio != null)
            {
                tglMuteAudio.onValueChanged.AddListener(OnMuteToggleChanged);
            }

            // 종료 버튼 이벤트 바인딩
            if (btnQuitGame != null)
            {
                btnQuitGame.onClick.AddListener(OnQuitClicked);
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
    }
}
