using System.Threading.Tasks;
using UnityEngine;

namespace CityFlow.Managers
{
    public sealed class SoundManager : MonoBehaviour
    {
        public static SoundManager Instance { get; private set; }

        [Header("Audio Sources")]
        [Tooltip("BGM 재생용 AudioSource. 비워두면 자동 생성됩니다.")]
        [SerializeField] private AudioSource bgmSource;
        [Tooltip("SFX 재생용 AudioSource. 비워두면 자동 생성됩니다.")]
        [SerializeField] private AudioSource sfxSource;

        [Header("Catalog")]
        [SerializeField] private SoundCatalog soundCatalog;
        [SerializeField] private bool playOnStart = true;
        [SerializeField] private string startBgmId;

        [Header("Volume")]
        [Range(0f, 1f)]
        [SerializeField] private float bgmVolume = 0.6f;
        [Range(0f, 1f)]
        [SerializeField] private float sfxVolume = 1f;

        private readonly SoundHandleCache handleCache = new();
        private bool isMuted;
        private string currentBgmId;

        public float BgmVolume => bgmVolume;
        public float SfxVolume => sfxVolume;
        public bool IsMuted => isMuted;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (transform.parent != null)
            {
                transform.SetParent(null);
            }

            DontDestroyOnLoad(gameObject);

            bgmSource = EnsureAudioSource(bgmSource, "BGM Source", true);
            sfxSource = EnsureAudioSource(sfxSource, "SFX Source", false);

            ApplyVolume();
        }

        private void Start()
        {
            PreloadMarkedSounds();

            if (playOnStart)
            {
                PlayBgm(startBgmId);
            }
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            handleCache.ReleaseAll();
            Instance = null;
        }

        public async void PlayBgm(string soundId)
        {
            if (!TryGetSound(soundId, SoundType.Bgm, out SoundCatalog.SoundEntry sound))
            {
                return;
            }

            AudioClip clip = await LoadClipAsync(sound);

            if (clip == null)
            {
                return;
            }

            PlayBgm(clip);
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
            handleCache.Release(currentBgmId);
            currentBgmId = null;
        }

        public void PauseBgm()
        {
            bgmSource?.Pause();
        }

        public void ResumeBgm()
        {
            bgmSource?.UnPause();
        }

        public void PlaySfx(string soundId)
        {
            PlaySfx(soundId, 1f);
        }

        public async void PlaySfx(string soundId, float volumeScale)
        {
            if (!TryGetSound(soundId, SoundType.Sfx, out SoundCatalog.SoundEntry sound))
            {
                return;
            }

            AudioClip clip = await LoadClipAsync(sound);

            if (clip == null)
            {
                return;
            }

            PlaySfx(clip, volumeScale * sound.VolumeScale);
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

        public void PlaySfxAtPoint(AudioClip clip, Vector3 position, float volumeScale)
        {
            if (clip == null || isMuted)
            {
                return;
            }

            AudioSource.PlayClipAtPoint(clip, position, sfxVolume * Mathf.Clamp01(volumeScale));
        }

        public void ReleaseSound(string soundId)
        {
            handleCache.Release(soundId);
        }

        public void ReleaseAllLoadedSounds()
        {
            StopBgm();
            currentBgmId = null;
            handleCache.ReleaseAll();
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

        private AudioSource EnsureAudioSource(AudioSource source, string sourceName, bool loop)
        {
            if (source == null)
            {
                GameObject sourceObject = new GameObject(sourceName);
                sourceObject.transform.SetParent(transform);
                source = sourceObject.AddComponent<AudioSource>();
            }

            source.playOnAwake = false;
            source.loop = loop;
            return source;
        }

        private async void PreloadMarkedSounds()
        {
            if (soundCatalog == null)
            {
                return;
            }

            for (int i = 0; i < soundCatalog.Sounds.Count; i++)
            {
                SoundCatalog.SoundEntry sound = soundCatalog.Sounds[i];

                if (sound == null || !sound.Preload)
                {
                    continue;
                }

                await LoadClipAsync(sound);
            }
        }

        private bool TryGetSound(string soundId, SoundType expectedType, out SoundCatalog.SoundEntry sound)
        {
            sound = null;

            return soundCatalog != null
                && soundCatalog.TryGetSound(soundId, out sound)
                && sound.Type == expectedType;
        }

        private Task<AudioClip> LoadClipAsync(SoundCatalog.SoundEntry sound)
        {
            return handleCache.LoadAsync(sound);
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
    }
}
