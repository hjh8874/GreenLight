using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Audio
{
    [CreateAssetMenu(
        fileName = "CityAmbienceProfile",
        menuName = "CityFlow/Audio/City Ambience Profile")]
    public sealed class CityAmbienceProfileSO : ScriptableObject
    {
        [Header("Day And Night")]
        [SerializeField] private AudioClip[] dayClips = System.Array.Empty<AudioClip>();
        [SerializeField] private AudioClip[] nightClips = System.Array.Empty<AudioClip>();
        [Range(0, 23)]
        [SerializeField] private int dayStartHour = 6;
        [Range(0, 23)]
        [SerializeField] private int nightStartHour = 22;

        [Header("Zoom Mix")]
        [SerializeField] private AnimationCurve ambienceByZoom =
            new(
                new Keyframe(0f, 0f),
                new Keyframe(0.5f, 0.2f),
                new Keyframe(1f, 1f));
        [Range(0f, 1f)]
        [SerializeField] private float ambienceVolume = 1f;
        [Min(0.01f)]
        [SerializeField] private float fadeSeconds = 1.5f;

        [Header("Drive View")]
        [SerializeField] private AudioClip driveRoomTone;
        [Range(0f, 1f)]
        [SerializeField] private float driveRoomToneVolume = 0.5f;

        [Header("Congestion")]
        [SerializeField] private AudioClip congestionClip;
        [Range(0f, 1f)]
        [SerializeField] private float congestionStartZoom = 0.7f;
        [Min(1)]
        [SerializeField] private int jamTilesForMinimumVolume = 3;
        [Min(1)]
        [SerializeField] private int jamTilesForFullVolume = 12;
        [Range(0f, 1f)]
        [SerializeField] private float congestionVolume = 0.7f;

        public IReadOnlyList<AudioClip> DayClips => dayClips;
        public IReadOnlyList<AudioClip> NightClips => nightClips;
        public int DayStartHour => dayStartHour;
        public int NightStartHour => nightStartHour;
        public AnimationCurve AmbienceByZoom => ambienceByZoom;
        public float AmbienceVolume => ambienceVolume;
        public float FadeSeconds => Mathf.Max(0.01f, fadeSeconds);
        public AudioClip DriveRoomTone => driveRoomTone;
        public float DriveRoomToneVolume => driveRoomToneVolume;
        public AudioClip CongestionClip => congestionClip;
        public float CongestionStartZoom => congestionStartZoom;
        public int JamTilesForMinimumVolume =>
            Mathf.Max(1, jamTilesForMinimumVolume);
        public int JamTilesForFullVolume =>
            Mathf.Max(JamTilesForMinimumVolume, jamTilesForFullVolume);
        public float CongestionVolume => congestionVolume;

        public bool IsDayHour(int hour)
        {
            int normalized = ((hour % 24) + 24) % 24;
            if (dayStartHour < nightStartHour)
            {
                return normalized >= dayStartHour && normalized < nightStartHour;
            }

            return normalized >= dayStartHour || normalized < nightStartHour;
        }

#if UNITY_EDITOR
        public void EditorConfigure(
            AudioClip[] day,
            AudioClip[] night,
            AudioClip roomTone,
            AudioClip congestion)
        {
            dayClips = day ?? System.Array.Empty<AudioClip>();
            nightClips = night ?? System.Array.Empty<AudioClip>();
            driveRoomTone = roomTone;
            congestionClip = congestion;
            dayStartHour = 6;
            nightStartHour = 22;
            ambienceByZoom = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.5f, 0.2f),
                new Keyframe(1f, 1f));
            ambienceVolume = 1f;
            fadeSeconds = 1.5f;
            driveRoomToneVolume = 0.5f;
            congestionStartZoom = 0.7f;
            jamTilesForMinimumVolume = 3;
            jamTilesForFullVolume = 12;
            congestionVolume = 0.7f;
        }
#endif
    }
}
