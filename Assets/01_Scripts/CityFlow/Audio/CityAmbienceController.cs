using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.View;
using UnityEngine;
using UnityEngine.Audio;

namespace CityFlow.Audio
{
    public sealed class CityAmbienceController :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        [SerializeField] private CityAmbienceProfileSO profile;
        [SerializeField] private AudioMixerGroup ambienceOutput;
        [SerializeField] private AudioMixerGroup congestionOutput;

        private readonly HashSet<Vector2Int> jamTiles = new();
        private readonly List<AudioSource> daySources = new();
        private readonly List<AudioSource> nightSources = new();

        private CityFlowServices services;
        private IGameCalendarService calendar;
        private MainCityView cityView;
        private AudioSource roomToneSource;
        private AudioSource congestionSource;
        private float nextResolveTime;

        public void Initialize(CityFlowServices cityFlowServices)
        {
            if (ReferenceEquals(services, cityFlowServices))
            {
                return;
            }

            Unbind();
            services = cityFlowServices;
            if (services == null)
            {
                return;
            }

            services.Events.CongestionChanged += OnCongestionChanged;
            services.GameCalendarRegistered += BindCalendar;
            BindCalendar(services.GameCalendar);
        }

        private void Awake()
        {
            BuildSources();
        }

        private void Update()
        {
            if (profile == null)
            {
                return;
            }

            ResolveCityView();
            float zoom = cityView != null ? cityView.NormalizedZoom01 : 0f;
            bool driveView = cityView != null && cityView.IsDriveViewActive;
            bool isDay = profile.IsDayHour(calendar?.Hour ?? 12);
            float ambience = Mathf.Clamp01(
                profile.AmbienceVolume * profile.AmbienceByZoom.Evaluate(zoom));
            float step = Time.unscaledDeltaTime / profile.FadeSeconds;

            SetLayerVolume(daySources, isDay ? ambience : 0f, step);
            SetLayerVolume(nightSources, isDay ? 0f : ambience, step);
            SetSourceVolume(
                roomToneSource,
                driveView ? profile.DriveRoomToneVolume : 0f,
                step);
            SetSourceVolume(
                congestionSource,
                CalculateCongestionVolume(zoom),
                step);
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private void BuildSources()
        {
            if (profile == null)
            {
                return;
            }

            CreateLayerSources(
                "Day Ambience",
                profile.DayClips,
                ambienceOutput,
                daySources);
            CreateLayerSources(
                "Night Ambience",
                profile.NightClips,
                ambienceOutput,
                nightSources);
            roomToneSource = CreateLoopSource(
                "Drive Room Tone",
                profile.DriveRoomTone,
                ambienceOutput);
            congestionSource = CreateLoopSource(
                "Congestion Ambience",
                profile.CongestionClip,
                congestionOutput);
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

        private void BindCalendar(IGameCalendarService service)
        {
            calendar = service;
        }

        private void OnCongestionChanged(CongestionEvent congestion)
        {
            if (congestion.Level == CongestionLevel.Jam)
            {
                jamTiles.Add(congestion.Tile);
            }
            else
            {
                jamTiles.Remove(congestion.Tile);
            }
        }

        private float CalculateCongestionVolume(float zoom)
        {
            if (jamTiles.Count < profile.JamTilesForMinimumVolume ||
                zoom < profile.CongestionStartZoom)
            {
                return 0f;
            }

            float zoomGain = Mathf.InverseLerp(
                profile.CongestionStartZoom,
                1f,
                zoom);
            float jamGain = Mathf.InverseLerp(
                profile.JamTilesForMinimumVolume,
                profile.JamTilesForFullVolume,
                jamTiles.Count);
            return Mathf.Clamp01(
                profile.CongestionVolume * zoomGain * jamGain);
        }

        private void CreateLayerSources(
            string layerName,
            IReadOnlyList<AudioClip> clips,
            AudioMixerGroup output,
            ICollection<AudioSource> destination)
        {
            if (clips == null)
            {
                return;
            }

            for (int index = 0; index < clips.Count; index++)
            {
                AudioClip clip = clips[index];
                if (clip == null)
                {
                    continue;
                }

                GameObject child = new GameObject($"{layerName} {index + 1}");
                AudioSource source = child.AddComponent<AudioSource>();
                child.transform.SetParent(transform, false);
                source.clip = clip;
                source.playOnAwake = false;
                source.loop = true;
                source.spatialBlend = 0f;
                source.volume = 0f;
                source.outputAudioMixerGroup = output;
                destination.Add(source);
            }
        }

        private AudioSource CreateLoopSource(
            string sourceName,
            AudioClip clip,
            AudioMixerGroup output)
        {
            GameObject child = new GameObject(sourceName);
            child.transform.SetParent(transform, false);
            AudioSource source = child.AddComponent<AudioSource>();
            source.clip = clip;
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.volume = 0f;
            source.outputAudioMixerGroup = output;
            return source;
        }

        private static void SetLayerVolume(
            IReadOnlyList<AudioSource> sources,
            float totalVolume,
            float step)
        {
            float perSource = sources.Count > 0
                ? Mathf.Clamp01(totalVolume) / sources.Count
                : 0f;

            for (int index = 0; index < sources.Count; index++)
            {
                SetSourceVolume(sources[index], perSource, step);
            }
        }

        private static void SetSourceVolume(
            AudioSource source,
            float target,
            float step)
        {
            if (source == null || source.clip == null)
            {
                return;
            }

            float clamped = Mathf.Clamp01(target);
            if (clamped > 0.001f && !source.isPlaying)
            {
                source.Play();
            }

            source.volume = Mathf.MoveTowards(source.volume, clamped, step);
            if (clamped <= 0.001f && source.volume <= 0.001f && source.isPlaying)
            {
                source.Stop();
            }
        }

        private void Unbind()
        {
            if (services != null)
            {
                services.Events.CongestionChanged -= OnCongestionChanged;
                services.GameCalendarRegistered -= BindCalendar;
            }

            services = null;
            calendar = null;
            jamTiles.Clear();
        }

#if UNITY_EDITOR
        public void EditorConfigure(
            CityAmbienceProfileSO profileAsset,
            AudioMixerGroup ambienceGroup,
            AudioMixerGroup congestionGroup)
        {
            profile = profileAsset;
            ambienceOutput = ambienceGroup;
            congestionOutput = congestionGroup;
        }
#endif

        // Unity setup:
        // The Sound System baker assigns the profile and mixer groups.
    }
}
