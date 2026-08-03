using CityFlow.View;
using UnityEngine;
using UnityEngine.Audio;

namespace CityFlow.Audio
{
    public sealed class BgmPlaylistController : MonoBehaviour
    {
        [SerializeField] private BgmPlaylistSO playlist;
        [SerializeField] private AudioMixerGroup musicOutput;

        private AudioSource currentSource;
        private AudioSource nextSource;
        private MainCityView cityView;
        private int currentTrackIndex = -1;
        private float transitionElapsed;
        private bool isTransitioning;
        private float nextResolveTime;

        private void Awake()
        {
            currentSource = CreateSource("BGM A");
            nextSource = CreateSource("BGM B");
        }

        private void Start()
        {
            StartNextTrack(immediate: true);
        }

        private void Update()
        {
            if (playlist == null || playlist.Tracks.Count == 0)
            {
                return;
            }

            ResolveCityView();
            float zoom = cityView != null ? cityView.NormalizedZoom01 : 0f;
            float targetVolume = Mathf.Clamp01(
                playlist.MusicVolume * playlist.MusicByZoom.Evaluate(zoom));

            if (currentSource.clip == null)
            {
                StartNextTrack(immediate: true);
            }

            float fadeDuration = Mathf.Max(0.01f, playlist.CrossfadeSeconds);
            if (!isTransitioning && currentSource.clip != null)
            {
                float remaining = currentSource.clip.length - currentSource.time;
                if (!currentSource.isPlaying || remaining <= fadeDuration)
                {
                    StartNextTrack(immediate: false);
                }
            }

            if (!isTransitioning)
            {
                currentSource.volume = targetVolume;
                nextSource.volume = 0f;
                return;
            }

            transitionElapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(transitionElapsed / fadeDuration);
            currentSource.volume = targetVolume * (1f - t);
            nextSource.volume = targetVolume * t;

            if (t < 1f)
            {
                return;
            }

            currentSource.Stop();
            (currentSource, nextSource) = (nextSource, currentSource);
            nextSource.clip = null;
            nextSource.volume = 0f;
            isTransitioning = false;
        }

        private void ResolveCityView()
        {
            if (cityView != null || Time.unscaledTime < nextResolveTime)
            {
                return;
            }

            cityView = FindAnyObjectByType<MainCityView>();
            nextResolveTime = Time.unscaledTime + 1f;
        }

        private void StartNextTrack(bool immediate)
        {
            if (playlist == null || playlist.Tracks.Count == 0)
            {
                return;
            }

            int nextIndex = SelectNextTrackIndex();
            AudioClip clip = playlist.Tracks[nextIndex];
            if (clip == null)
            {
                currentTrackIndex = nextIndex;
                return;
            }

            currentTrackIndex = nextIndex;
            if (immediate || currentSource.clip == null)
            {
                currentSource.clip = clip;
                currentSource.volume = 0f;
                currentSource.Play();
                return;
            }

            nextSource.clip = clip;
            nextSource.volume = 0f;
            nextSource.Play();
            transitionElapsed = 0f;
            isTransitioning = true;
        }

        private int SelectNextTrackIndex()
        {
            int count = playlist.Tracks.Count;
            if (count <= 1)
            {
                return 0;
            }

            if (!playlist.Shuffle)
            {
                return (currentTrackIndex + 1) % count;
            }

            if (currentTrackIndex < 0)
            {
                return Random.Range(0, count);
            }

            int candidate = Random.Range(0, count - 1);
            return candidate >= currentTrackIndex ? candidate + 1 : candidate;
        }

        private AudioSource CreateSource(string sourceName)
        {
            GameObject child = new GameObject(sourceName);
            child.transform.SetParent(transform, false);
            AudioSource source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.outputAudioMixerGroup = musicOutput;
            return source;
        }

#if UNITY_EDITOR
        public void EditorConfigure(
            BgmPlaylistSO playlistAsset,
            AudioMixerGroup output)
        {
            playlist = playlistAsset;
            musicOutput = output;
        }
#endif

        // Unity setup:
        // The Sound System baker assigns the playlist and mixer group.
    }
}
