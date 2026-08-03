using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace CityFlow.Managers
{
    public sealed class SoundManager : MonoBehaviour
    {
        public static SoundManager Instance { get; private set; }

        [Header("Audio Sources")]
        [SerializeField] private AudioSource bgmSource;
        [SerializeField] private AudioSource sfxSource;
        [SerializeField] private AudioMixerGroup bgmOutput;
        [SerializeField] private AudioMixerGroup sfxOutput;

        [Header("Catalog")]
        [SerializeField] private SoundCatalog soundCatalog;
        [SerializeField] private bool playOnStart;
        [SerializeField] private string startBgmId;

        [Header("Volume")]
        [Range(0f, 1f)]
        [SerializeField] private float bgmVolume = 0.6f;
        [Range(0f, 1f)]
        [SerializeField] private float sfxVolume = 1f;

        private readonly Dictionary<string, float> lastPlayedAt = new();
        private bool isMuted;
        private string currentBgmId;

        public float BgmVolume => bgmVolume;
        public float SfxVolume => sfxVolume;
        public bool IsMuted => isMuted;
        public bool IsConfigured => soundCatalog != null;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                if (!Instance.IsConfigured && IsConfigured)
                {
                    SoundManager previous = Instance;
                    Instance = this;
                    Destroy(previous.gameObject);
                }
                else
                {
                    Destroy(gameObject);
                    return;
                }
            }
            else
            {
                Instance = this;
            }

            if (transform.parent != null)
            {
                transform.SetParent(null);
            }

            DontDestroyOnLoad(gameObject);

            bgmSource = EnsureAudioSource(bgmSource, "BGM Source", true);
            sfxSource = EnsureAudioSource(sfxSource, "SFX Source", false);
            bgmSource.outputAudioMixerGroup = bgmOutput;
            sfxSource.outputAudioMixerGroup = sfxOutput;
            ApplyVolume();
        }

        private void Start()
        {
            if (playOnStart)
            {
                PlayBgm(startBgmId);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void PlayBgm(string soundId)
        {
            if (!TryGetSound(
                    soundId,
                    SoundType.Bgm,
                    out SoundCatalog.SoundEntry sound))
            {
                return;
            }

            PlayBgm(sound.Clip);
            currentBgmId = sound.Id;
        }

        public void PlayBgm(AudioClip clip)
        {
            if (clip == null || bgmSource == null)
            {
                return;
            }

            if (bgmSource.clip == clip && bgmSource.isPlaying)
            {
                return;
            }

            bgmSource.clip = clip;
            bgmSource.Play();
        }

        public void StopBgm()
        {
            if (bgmSource == null)
            {
                return;
            }

            bgmSource.Stop();
            bgmSource.clip = null;
        }

        public void ReleaseCurrentBgm()
        {
            StopBgm();
            currentBgmId = null;
        }

        public void PauseBgm() => bgmSource?.Pause();

        public void ResumeBgm() => bgmSource?.UnPause();

        public void PlaySfx(string soundId)
        {
            PlaySfx(soundId, 1f);
        }

        public void PlaySfx(string soundId, float volumeScale)
        {
            if (!TryGetSound(
                    soundId,
                    SoundType.Sfx,
                    out SoundCatalog.SoundEntry sound) ||
                !CanPlay(sound))
            {
                return;
            }

            PlaySfx(sound.Clip, volumeScale * sound.VolumeScale);
        }

        public void PlaySfx(AudioClip clip)
        {
            PlaySfx(clip, 1f);
        }

        public void PlaySfx(AudioClip clip, float volumeScale)
        {
            if (clip == null || sfxSource == null || isMuted)
            {
                return;
            }

            sfxSource.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
        }

        public void PlaySfxAtPoint(AudioClip clip, Vector3 position)
        {
            PlaySfxAtPoint(clip, position, 1f);
        }

        public void PlaySfxAtPoint(
            AudioClip clip,
            Vector3 position,
            float volumeScale)
        {
            if (clip == null || isMuted)
            {
                return;
            }

            AudioSource.PlayClipAtPoint(
                clip,
                position,
                sfxVolume * Mathf.Clamp01(volumeScale));
        }

        public bool TryGetSfx(
            string soundId,
            out AudioClip clip,
            out float volumeScale)
        {
            clip = null;
            volumeScale = 0f;

            if (!TryGetSound(
                    soundId,
                    SoundType.Sfx,
                    out SoundCatalog.SoundEntry sound) ||
                sound.Clip == null)
            {
                return false;
            }

            clip = sound.Clip;
            volumeScale = sound.VolumeScale;
            return true;
        }

        public void ReleaseSound(string soundId)
        {
            lastPlayedAt.Remove(soundId);
        }

        public void ReleaseAllLoadedSounds()
        {
            StopBgm();
            currentBgmId = null;
            lastPlayedAt.Clear();
        }

        public void SetBgmVolume(float volume)
        {
            bgmVolume = Mathf.Clamp01(volume);
            ApplyVolume();
        }

        public void SetSfxVolume(float volume)
        {
            sfxVolume = Mathf.Clamp01(volume);
            ApplyVolume();
        }

        public void SetMute(bool mute)
        {
            isMuted = mute;
            ApplyVolume();
        }

        public void ToggleMute()
        {
            SetMute(!isMuted);
        }

        private AudioSource EnsureAudioSource(
            AudioSource source,
            string sourceName,
            bool loop)
        {
            if (source == null)
            {
                GameObject sourceObject = new GameObject(sourceName);
                sourceObject.transform.SetParent(transform);
                source = sourceObject.AddComponent<AudioSource>();
            }

            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            return source;
        }

        private bool TryGetSound(
            string soundId,
            SoundType expectedType,
            out SoundCatalog.SoundEntry sound)
        {
            sound = null;

            return soundCatalog != null &&
                   soundCatalog.TryGetSound(soundId, out sound) &&
                   sound.Type == expectedType &&
                   sound.Clip != null;
        }

        private bool CanPlay(SoundCatalog.SoundEntry sound)
        {
            if (sound == null || sound.Clip == null)
            {
                return false;
            }

            float now = Time.unscaledTime;
            if (lastPlayedAt.TryGetValue(sound.Id, out float previous) &&
                now - previous < sound.CooldownSeconds)
            {
                return false;
            }

            lastPlayedAt[sound.Id] = now;
            return true;
        }

        private void ApplyVolume()
        {
            if (bgmSource != null)
            {
                bgmSource.volume = isMuted ? 0f : bgmVolume;
            }

            if (sfxSource != null)
            {
                sfxSource.volume = isMuted ? 0f : sfxVolume;
            }
        }

#if UNITY_EDITOR
        public void EditorConfigure(
            SoundCatalog catalog,
            AudioMixerGroup musicGroup,
            AudioMixerGroup effectsGroup)
        {
            soundCatalog = catalog;
            bgmOutput = musicGroup;
            sfxOutput = effectsGroup;
            playOnStart = false;
        }
#endif

        // Unity setup:
        // Place the baked SoundSystem prefab in a CityFlow scene.
        // The configured instance replaces legacy empty SoundManager objects.
    }
}
