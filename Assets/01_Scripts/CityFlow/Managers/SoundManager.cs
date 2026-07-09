using System.Collections.Generic;
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

        [Header("Music Clips")]
        [Tooltip("팀원이 고른 배경음악 클립을 여기에 등록합니다.")]
        [SerializeField] private List<AudioClip> bgmClips = new();
        [SerializeField] private bool playOnStart = true;
        [SerializeField] private int startBgmIndex;

        [Header("SFX Clips")]
        [Tooltip("버튼, 알림, 효과음 클립을 여기에 등록합니다.")]
        [SerializeField] private List<AudioClip> sfxClips = new();

        [Header("Volume")]
        [Range(0f, 1f)]
        [SerializeField] private float bgmVolume = 0.6f;
        [Range(0f, 1f)]
        [SerializeField] private float sfxVolume = 1f;

        private readonly Dictionary<string, AudioClip> bgmByName = new();
        private readonly Dictionary<string, AudioClip> sfxByName = new();
        private bool isMuted;

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

            CacheClips();
            ApplyVolume();
        }

        private void Start()
        {
            if (playOnStart)
            {
                PlayBgm(startBgmIndex);
            }
        }

        public void PlayBgm(int index)
        {
            if (!TryGetClip(bgmClips, index, out AudioClip clip))
            {
                return;
            }

            PlayBgm(clip);
        }

        public void PlayBgm(string clipName)
        {
            if (string.IsNullOrWhiteSpace(clipName))
            {
                return;
            }

            if (bgmByName.TryGetValue(clipName, out AudioClip clip))
            {
                PlayBgm(clip);
            }
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

        public void PauseBgm()
        {
            bgmSource?.Pause();
        }

        public void ResumeBgm()
        {
            bgmSource?.UnPause();
        }

        public void PlaySfx(int index)
        {
            PlaySfx(index, 1f);
        }

        public void PlaySfx(int index, float volumeScale)
        {
            if (!TryGetClip(sfxClips, index, out AudioClip clip))
            {
                return;
            }

            PlaySfx(clip, volumeScale);
        }

        public void PlaySfx(string clipName)
        {
            PlaySfx(clipName, 1f);
        }

        public void PlaySfx(string clipName, float volumeScale)
        {
            if (string.IsNullOrWhiteSpace(clipName))
            {
                return;
            }

            if (sfxByName.TryGetValue(clipName, out AudioClip clip))
            {
                PlaySfx(clip, volumeScale);
            }
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

        private void CacheClips()
        {
            bgmByName.Clear();
            sfxByName.Clear();

            AddClipsToCache(bgmClips, bgmByName);
            AddClipsToCache(sfxClips, sfxByName);
        }

        private void AddClipsToCache(List<AudioClip> clips, Dictionary<string, AudioClip> cache)
        {
            for (int i = 0; i < clips.Count; i++)
            {
                AudioClip clip = clips[i];

                if (clip == null || cache.ContainsKey(clip.name))
                {
                    continue;
                }

                cache.Add(clip.name, clip);
            }
        }

        private bool TryGetClip(List<AudioClip> clips, int index, out AudioClip clip)
        {
            clip = null;

            if (index < 0 || index >= clips.Count)
            {
                return false;
            }

            clip = clips[index];
            return clip != null;
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
