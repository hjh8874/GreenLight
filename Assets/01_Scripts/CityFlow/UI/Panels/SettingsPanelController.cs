using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;
using TMPro;

namespace CityFlow.UI
{
    public class SettingsPanelController : MonoBehaviour, ICityFlowServiceConsumer
    {
        [Header("Settings UI")]
        [SerializeField] private Toggle tglMuteAudio;
        [SerializeField] private Button btnQuitGame;
        [SerializeField] private Button btnTitleScene;
        [SerializeField] private string titleSceneName = "TitleScene";

        [Header("Audio Control UI")]
        [SerializeField] private Slider sldBgm;
        [SerializeField] private TMP_InputField inputBgm;
        [SerializeField] private Slider sldSfx;
        [SerializeField] private TMP_InputField inputSfx;

        [Header("Audio System")]
        [SerializeField] private AudioMixer audioMixer;
        [SerializeField] private string bgmParameterName = "BGMVolume";
        [SerializeField] private string sfxParameterName = "SFXVolume";

        private CityFlowServices _services;
        private bool _isBound;
        private bool _isUpdatingBgm;
        private bool _isUpdatingSfx;

        public void Configure(
            Toggle muteAudio,
            Button quitGame,
            Button titleScene = null,
            Slider bgmSlider = null,
            TMP_InputField bgmInput = null,
            Slider sfxSlider = null,
            TMP_InputField sfxInput = null,
            AudioMixer mixer = null)
        {
            tglMuteAudio = muteAudio;
            btnQuitGame = quitGame;
            btnTitleScene = titleScene;
            sldBgm = bgmSlider;
            inputBgm = bgmInput;
            sldSfx = sfxSlider;
            inputSfx = sfxInput;
            if (mixer != null)
            {
                audioMixer = mixer;
            }
            BindButtons();
        }

        public void Initialize(CityFlowServices services)
        {
            _services = services;
        }

        private void Start()
        {
            if (audioMixer == null)
            {
                audioMixer = Resources.Load<AudioMixer>("Audio/MainMixer");
                if (audioMixer == null)
                {
                    Debug.LogWarning("[SettingsPanelController] AudioMixer가 할당되지 않았으며 Resources에서도 찾을 수 없습니다. (베이킹 시 주입된 참조를 확인해주세요.)");
                }
            }

            BindButtons();

            // PlayerPrefs에서 저장된 소리 설정 불러오기
            float savedBgm = PlayerPrefs.GetFloat("Settings_BGMVolume", 0.5f);
            float savedSfx = PlayerPrefs.GetFloat("Settings_SFXVolume", 0.5f);

            // 슬라이더 값 변경 시 OnBgmSliderChanged가 호출되어 믹서와 텍스트(%) 업데이트 됨
            if (sldBgm != null) sldBgm.value = savedBgm;
            if (sldSfx != null) sldSfx.value = savedSfx;

            // 만약 슬라이더가 없더라도 믹서는 갱신되어야 함
            UpdateMixerVolume(bgmParameterName, savedBgm);
            UpdateMixerVolume(sfxParameterName, savedSfx);
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

            if (sldBgm != null)
            {
                sldBgm.onValueChanged.AddListener(OnBgmSliderChanged);
                OnBgmSliderChanged(sldBgm.value);
            }
            if (inputBgm != null)
            {
                inputBgm.onEndEdit.AddListener(OnBgmInputChanged);
            }

            if (sldSfx != null)
            {
                sldSfx.onValueChanged.AddListener(OnSfxSliderChanged);
                OnSfxSliderChanged(sldSfx.value);
            }
            if (inputSfx != null)
            {
                inputSfx.onEndEdit.AddListener(OnSfxInputChanged);
            }

            _isBound = true;
        }

        private void OnBgmSliderChanged(float value)
        {
            if (_isUpdatingBgm) return;
            _isUpdatingBgm = true;

            int percentage = Mathf.RoundToInt(value * 100f);
            if (inputBgm != null) inputBgm.text = percentage.ToString();
            UpdateMixerVolume(bgmParameterName, value);

            PlayerPrefs.SetFloat("Settings_BGMVolume", value);
            PlayerPrefs.Save();

            _isUpdatingBgm = false;
        }

        private void OnBgmInputChanged(string text)
        {
            if (_isUpdatingBgm) return;
            if (int.TryParse(text, out int percentage))
            {
                percentage = Mathf.Clamp(percentage, 0, 100);
                float value = percentage / 100f;

                _isUpdatingBgm = true;
                if (sldBgm != null) sldBgm.value = value;
                if (inputBgm != null) inputBgm.text = percentage.ToString();
                UpdateMixerVolume(bgmParameterName, value);

                PlayerPrefs.SetFloat("Settings_BGMVolume", value);
                PlayerPrefs.Save();
                _isUpdatingBgm = false;
            }
            else
            {
                if (sldBgm != null && inputBgm != null)
                {
                    inputBgm.text = Mathf.RoundToInt(sldBgm.value * 100f).ToString();
                }
            }
        }

        private void OnSfxSliderChanged(float value)
        {
            if (_isUpdatingSfx) return;
            _isUpdatingSfx = true;

            int percentage = Mathf.RoundToInt(value * 100f);
            if (inputSfx != null) inputSfx.text = percentage.ToString();
            UpdateMixerVolume(sfxParameterName, value);

            PlayerPrefs.SetFloat("Settings_SFXVolume", value);
            PlayerPrefs.Save();

            _isUpdatingSfx = false;
        }

        private void OnSfxInputChanged(string text)
        {
            if (_isUpdatingSfx) return;
            if (int.TryParse(text, out int percentage))
            {
                percentage = Mathf.Clamp(percentage, 0, 100);
                float value = percentage / 100f;

                _isUpdatingSfx = true;
                if (sldSfx != null) sldSfx.value = value;
                if (inputSfx != null) inputSfx.text = percentage.ToString();
                UpdateMixerVolume(sfxParameterName, value);

                PlayerPrefs.SetFloat("Settings_SFXVolume", value);
                PlayerPrefs.Save();
                _isUpdatingSfx = false;
            }
            else
            {
                if (sldSfx != null && inputSfx != null)
                {
                    inputSfx.text = Mathf.RoundToInt(sldSfx.value * 100f).ToString();
                }
            }
        }

        private void UpdateMixerVolume(string parameterName, float linearValue)
        {
            if (audioMixer != null)
            {
                // Convert linear (0-1) to Decibel (-80 to 0)
                float db = linearValue > 0.0001f ? Mathf.Log10(linearValue) * 20f : -80f;
                audioMixer.SetFloat(parameterName, db);
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
            if (btnTitleScene != null)
                btnTitleScene.onClick.RemoveListener(OnTitleSceneClicked);
            if (sldBgm != null)
                sldBgm.onValueChanged.RemoveListener(OnBgmSliderChanged);
            if (inputBgm != null)
                inputBgm.onEndEdit.RemoveListener(OnBgmInputChanged);
            if (sldSfx != null)
                sldSfx.onValueChanged.RemoveListener(OnSfxSliderChanged);
            if (inputSfx != null)
                inputSfx.onEndEdit.RemoveListener(OnSfxInputChanged);
        }
    }
}
