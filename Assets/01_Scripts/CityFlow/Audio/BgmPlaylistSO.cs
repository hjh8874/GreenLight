using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Audio
{
    [CreateAssetMenu(
        fileName = "BgmPlaylist",
        menuName = "CityFlow/Audio/BGM Playlist")]
    public sealed class BgmPlaylistSO : ScriptableObject
    {
        [SerializeField] private AudioClip[] tracks = System.Array.Empty<AudioClip>();
        [SerializeField] private bool shuffle = true;
        [Min(0f)]
        [SerializeField] private float crossfadeSeconds = 2f;
        [Range(0f, 1f)]
        [SerializeField] private float musicVolume = 0.5f;
        [SerializeField] private AnimationCurve musicByZoom =
            new(
                new Keyframe(0f, 1f),
                new Keyframe(0.5f, 0.8f),
                new Keyframe(1f, 0f));

        public IReadOnlyList<AudioClip> Tracks => tracks;
        public bool Shuffle => shuffle;
        public float CrossfadeSeconds => Mathf.Max(0f, crossfadeSeconds);
        public float MusicVolume => musicVolume;
        public AnimationCurve MusicByZoom => musicByZoom;

#if UNITY_EDITOR
        public void EditorConfigure(AudioClip[] clips)
        {
            tracks = clips ?? System.Array.Empty<AudioClip>();
            shuffle = true;
            crossfadeSeconds = 2f;
            musicVolume = 0.5f;
            musicByZoom = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.5f, 0.8f),
                new Keyframe(1f, 0f));
        }
#endif
    }
}
