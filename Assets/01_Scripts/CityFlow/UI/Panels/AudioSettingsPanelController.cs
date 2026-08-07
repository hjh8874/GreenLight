using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

namespace CityFlow.UI
{
    public sealed class AudioSettingsPanelController : MonoBehaviour
    {
        private const string BgmVolumePreferenceKey =
            "Settings_BGMVolume";
        private const string SfxVolumePreferenceKey =
            "Settings_SFXVolume";
        private const float DefaultVolume = 0.3f;
        private const float MinimumLinearVolume = 0.0001f;
        private const float PreferenceSaveDebounceSeconds = 0.2f;

        private static readonly string[] BgmParameterNames =
        {
            "BGMVolume",
            "RadioVolume"
        };

        private static readonly string[] SfxParameterNames =
        {
            "SFXVolume",
            "UIVolume",
            "FacilityVolume",
            "AmbienceVolume",
            "CongestionVolume"
        };

        [Header("Audio Settings UI")]
        [SerializeField] private Toggle muteToggle;
        [SerializeField] private Slider bgmSlider;
        [SerializeField] private TMP_InputField bgmInput;
        [SerializeField] private Slider sfxSlider;
        [SerializeField] private TMP_InputField sfxInput;

        [Header("Audio System")]
        [SerializeField] private AudioMixer audioMixer;

        private bool isBound;
        private bool isUpdatingBgm;
        private bool isUpdatingSfx;
        private bool hasPendingPreferenceSave;
        private float savedBgmVolume = DefaultVolume;
        private float savedSfxVolume = DefaultVolume;
        private Coroutine preferenceSaveCoroutine;

        private void Awake()
        {
            ApplyInputTextReadability(bgmInput);
            ApplyInputTextReadability(sfxInput);
        }

        private static void ApplyInputTextReadability(TMP_InputField input)
        {
            if (input == null)
            {
                return;
            }

            if (input.textComponent != null)
            {
                input.textComponent.color = Color.black;
            }

            if (input.placeholder is TMP_Text placeholder)
            {
                placeholder.color = new Color(0.2f, 0.2f, 0.2f, 0.7f);
            }
        }

        public void Configure(
            Toggle mute,
            Slider bgm,
            TMP_InputField bgmPercentage,
            Slider sfx,
            TMP_InputField sfxPercentage,
            AudioMixer mixer)
        {
            UnbindControls();
            muteToggle = mute;
            bgmSlider = bgm;
            bgmInput = bgmPercentage;
            sfxSlider = sfx;
            sfxInput = sfxPercentage;
            audioMixer = mixer;
            ApplyInputTextReadability(bgmInput);
            ApplyInputTextReadability(sfxInput);

            if (Application.isPlaying)
            {
                LoadPreferencesAndApplyAudioState();
                BindControls();
            }
        }

        public void ConfigureMuteOnly(Toggle mute)
        {
            UnbindControls();
            muteToggle = mute;
            bgmSlider = null;
            bgmInput = null;
            sfxSlider = null;
            sfxInput = null;
            audioMixer = null;

            if (Application.isPlaying)
            {
                ApplyMuteState(muteToggle != null && muteToggle.isOn);
                BindControls();
            }
        }

        private void Start()
        {
            if (audioMixer == null && HasVolumeControls())
            {
                Debug.LogError(
                    "[AudioSettingsPanelController] UI_AudioSettings " +
                    "Prefab에 CityAudioMixer 참조가 없습니다.",
                    this);
            }

            LoadPreferencesAndApplyAudioState();
            BindControls();
        }

        private bool HasVolumeControls()
        {
            return bgmSlider != null ||
                bgmInput != null ||
                sfxSlider != null ||
                sfxInput != null;
        }

        private void LoadPreferencesAndApplyAudioState()
        {
            savedBgmVolume = LoadVolumePreference(
                BgmVolumePreferenceKey);
            savedSfxVolume = LoadVolumePreference(
                SfxVolumePreferenceKey);

            bgmSlider?.SetValueWithoutNotify(savedBgmVolume);
            sfxSlider?.SetValueWithoutNotify(savedSfxVolume);
            bgmInput?.SetTextWithoutNotify(
                ToPercentage(savedBgmVolume).ToString());
            sfxInput?.SetTextWithoutNotify(
                ToPercentage(savedSfxVolume).ToString());

            ApplyMixerVolume(BgmParameterNames, savedBgmVolume);
            ApplyMixerVolume(SfxParameterNames, savedSfxVolume);
            ApplyMuteState(muteToggle != null && muteToggle.isOn);
        }

        internal static float LoadVolumePreference(string key)
        {
            return Mathf.Clamp01(PlayerPrefs.GetFloat(
                key,
                DefaultVolume));
        }

        private void BindControls()
        {
            if (isBound)
            {
                return;
            }

            muteToggle?.onValueChanged.AddListener(OnMuteChanged);
            bgmSlider?.onValueChanged.AddListener(OnBgmSliderChanged);
            bgmInput?.onEndEdit.AddListener(OnBgmInputChanged);
            sfxSlider?.onValueChanged.AddListener(OnSfxSliderChanged);
            sfxInput?.onEndEdit.AddListener(OnSfxInputChanged);
            isBound = true;
        }

        private void UnbindControls()
        {
            if (!isBound)
            {
                return;
            }

            muteToggle?.onValueChanged.RemoveListener(OnMuteChanged);
            bgmSlider?.onValueChanged.RemoveListener(OnBgmSliderChanged);
            bgmInput?.onEndEdit.RemoveListener(OnBgmInputChanged);
            sfxSlider?.onValueChanged.RemoveListener(OnSfxSliderChanged);
            sfxInput?.onEndEdit.RemoveListener(OnSfxInputChanged);
            isBound = false;
        }

        private void OnMuteChanged(bool isMuted)
        {
            ApplyMuteState(isMuted);
        }

        private static void ApplyMuteState(bool isMuted)
        {
            AudioListener.volume = isMuted ? 0f : 1f;
        }

        private void OnBgmSliderChanged(float value)
        {
            if (isUpdatingBgm)
            {
                return;
            }

            isUpdatingBgm = true;
            savedBgmVolume = Mathf.Clamp01(value);
            bgmInput?.SetTextWithoutNotify(
                ToPercentage(savedBgmVolume).ToString());
            ApplyMixerVolume(BgmParameterNames, savedBgmVolume);
            QueuePreferenceSave(
                BgmVolumePreferenceKey,
                savedBgmVolume);
            isUpdatingBgm = false;
        }

        private void OnBgmInputChanged(string text)
        {
            ApplyPercentageInput(
                text,
                ref isUpdatingBgm,
                ref savedBgmVolume,
                bgmSlider,
                bgmInput,
                BgmParameterNames,
                BgmVolumePreferenceKey);
        }

        private void OnSfxSliderChanged(float value)
        {
            if (isUpdatingSfx)
            {
                return;
            }

            isUpdatingSfx = true;
            savedSfxVolume = Mathf.Clamp01(value);
            sfxInput?.SetTextWithoutNotify(
                ToPercentage(savedSfxVolume).ToString());
            ApplyMixerVolume(SfxParameterNames, savedSfxVolume);
            QueuePreferenceSave(
                SfxVolumePreferenceKey,
                savedSfxVolume);
            isUpdatingSfx = false;
        }

        private void OnSfxInputChanged(string text)
        {
            ApplyPercentageInput(
                text,
                ref isUpdatingSfx,
                ref savedSfxVolume,
                sfxSlider,
                sfxInput,
                SfxParameterNames,
                SfxVolumePreferenceKey);
        }

        private void ApplyPercentageInput(
            string text,
            ref bool isUpdating,
            ref float savedVolume,
            Slider slider,
            TMP_InputField input,
            string[] parameterNames,
            string preferenceKey)
        {
            if (isUpdating)
            {
                return;
            }

            if (!int.TryParse(text, out int percentage))
            {
                input?.SetTextWithoutNotify(
                    ToPercentage(savedVolume).ToString());
                return;
            }

            isUpdating = true;
            percentage = Mathf.Clamp(percentage, 0, 100);
            savedVolume = percentage / 100f;
            slider?.SetValueWithoutNotify(savedVolume);
            input?.SetTextWithoutNotify(percentage.ToString());
            ApplyMixerVolume(parameterNames, savedVolume);
            QueuePreferenceSave(preferenceKey, savedVolume);
            isUpdating = false;
        }

        private void ApplyMixerVolume(
            string[] parameterNames,
            float linearValue)
        {
            if (audioMixer == null)
            {
                return;
            }

            float clampedValue = Mathf.Clamp01(linearValue);
            float db = clampedValue > MinimumLinearVolume
                ? Mathf.Log10(clampedValue) * 20f
                : -80f;

            for (int index = 0; index < parameterNames.Length; index++)
            {
                audioMixer.SetFloat(parameterNames[index], db);
            }
        }

        private void QueuePreferenceSave(string key, float value)
        {
            PlayerPrefs.SetFloat(key, Mathf.Clamp01(value));
            hasPendingPreferenceSave = true;

            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                FlushPendingPreferenceSave();
                return;
            }

            if (preferenceSaveCoroutine != null)
            {
                StopCoroutine(preferenceSaveCoroutine);
            }

            preferenceSaveCoroutine = StartCoroutine(
                SavePreferencesAfterDelay());
        }

        private IEnumerator SavePreferencesAfterDelay()
        {
            yield return new WaitForSecondsRealtime(
                PreferenceSaveDebounceSeconds);
            preferenceSaveCoroutine = null;
            SavePendingPreferences();
        }

        private void FlushPendingPreferenceSave()
        {
            if (preferenceSaveCoroutine != null)
            {
                StopCoroutine(preferenceSaveCoroutine);
                preferenceSaveCoroutine = null;
            }

            SavePendingPreferences();
        }

        private void SavePendingPreferences()
        {
            if (!hasPendingPreferenceSave)
            {
                return;
            }

            PlayerPrefs.Save();
            hasPendingPreferenceSave = false;
        }

        private static int ToPercentage(float value)
        {
            return Mathf.RoundToInt(Mathf.Clamp01(value) * 100f);
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (isPaused)
            {
                FlushPendingPreferenceSave();
            }
        }

        private void OnApplicationQuit()
        {
            FlushPendingPreferenceSave();
        }

        private void OnDisable()
        {
            FlushPendingPreferenceSave();
        }

        private void OnDestroy()
        {
            UnbindControls();
            FlushPendingPreferenceSave();
        }
    }
}
