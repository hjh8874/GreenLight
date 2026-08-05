using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Audio;

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

        public void Configure(Toggle muteAudio, Button quitGame, Button titleScene, 
                              Slider bgmSlider = null, TMP_InputField bgmInput = null, 
                              Slider sfxSlider = null, TMP_InputField sfxInput = null,
                              AudioMixer mixer = null)
        {
            tglMuteAudio = muteAudio;
            btnQuitGame = quitGame;
            btnTitleScene = titleScene;
            sldBgm = bgmSlider;
            inputBgm = bgmInput;
            sldSfx = sfxSlider;
            inputSfx = sfxInput;
            if (mixer != null) audioMixer = mixer;
            
            BindButtons();
        }

        public void Initialize(CityFlowServices services)
        {
            _services = services;
        }

        private void Start()
        {
            BindButtons();
        }

        private void BindButtons()
        {
            if (_isBound)
                return;

            if (tglMuteAudio != null)
                tglMuteAudio.onValueChanged.AddListener(OnMuteToggleChanged);

            if (btnQuitGame != null)
                btnQuitGame.onClick.AddListener(OnQuitClicked);

            if (btnTitleScene != null)
                btnTitleScene.onClick.AddListener(OnTitleSceneClicked);

            // BGM Binding
            if (sldBgm != null)
            {
                sldBgm.onValueChanged.AddListener(OnBgmSliderChanged);
                // Initialize default
                OnBgmSliderChanged(sldBgm.value);
            }
            if (inputBgm != null)
                inputBgm.onEndEdit.AddListener(OnBgmInputChanged);

            // SFX Binding
            if (sldSfx != null)
            {
                sldSfx.onValueChanged.AddListener(OnSfxSliderChanged);
                // Initialize default
                OnSfxSliderChanged(sldSfx.value);
            }
            if (inputSfx != null)
                inputSfx.onEndEdit.AddListener(OnSfxInputChanged);

            _isBound = true;
        }

        private void OnBgmSliderChanged(float value)
        {
            if (_isUpdatingBgm) return;
            _isUpdatingBgm = true;

            int percentage = Mathf.RoundToInt(value * 100f);
            if (inputBgm != null) inputBgm.text = percentage.ToString();
            UpdateMixerVolume(bgmParameterName, value);

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
                _isUpdatingBgm = false;
            }
        }

        private void OnSfxSliderChanged(float value)
        {
            if (_isUpdatingSfx) return;
            _isUpdatingSfx = true;

            int percentage = Mathf.RoundToInt(value * 100f);
            if (inputSfx != null) inputSfx.text = percentage.ToString();
            UpdateMixerVolume(sfxParameterName, value);

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
                _isUpdatingSfx = false;
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
            AudioListener.volume = isMuted ? 0f : 1f;
        }

        private void OnQuitClicked()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnTitleSceneClicked()
        {
            if (!Application.CanStreamedLevelBeLoaded(titleSceneName))
                return;

            if (_services != null && _services.Save != null && !_services.Save.Save())
                return;

            UnityEngine.SceneManagement.SceneManager.LoadScene(titleSceneName);
        }

        private void OnDestroy()
        {
            if (tglMuteAudio != null) tglMuteAudio.onValueChanged.RemoveListener(OnMuteToggleChanged);
            if (btnQuitGame != null) btnQuitGame.onClick.RemoveListener(OnQuitClicked);
            if (btnTitleScene != null) btnTitleScene.onClick.RemoveListener(OnTitleSceneClicked);
            if (sldBgm != null) sldBgm.onValueChanged.RemoveListener(OnBgmSliderChanged);
            if (inputBgm != null) inputBgm.onEndEdit.RemoveListener(OnBgmInputChanged);
            if (sldSfx != null) sldSfx.onValueChanged.RemoveListener(OnSfxSliderChanged);
            if (inputSfx != null) inputSfx.onEndEdit.RemoveListener(OnSfxInputChanged);
        }
    }
}
