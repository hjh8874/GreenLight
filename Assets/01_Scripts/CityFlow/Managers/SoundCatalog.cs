using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Managers
{
    [CreateAssetMenu(fileName = "SoundCatalog", menuName = "CityFlow/Sound Catalog")]
    public sealed class SoundCatalog : ScriptableObject
    {
        [SerializeField] private List<SoundEntry> sounds = new();

        public IReadOnlyList<SoundEntry> Sounds => sounds;

        public bool TryGetSound(string id, out SoundEntry sound)
        {
            sound = null;

            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            for (int i = 0; i < sounds.Count; i++)
            {
                SoundEntry entry = sounds[i];

                if (entry == null || entry.Id != id)
                {
                    continue;
                }

                sound = entry;
                return true;
            }

            return false;
        }

        [Serializable]
        public sealed class SoundEntry
        {
            [SerializeField] private string id;
            [SerializeField] private SoundType type = SoundType.Sfx;
            [SerializeField] private AudioClip clip;
            [Range(0f, 1f)]
            [SerializeField] private float volumeScale = 1f;
            [Min(0f)]
            [SerializeField] private float cooldownSeconds;
            [SerializeField] private bool preload;

            public string Id => id;
            public SoundType Type => type;
            public AudioClip Clip => clip;
            public float VolumeScale => volumeScale;
            public float CooldownSeconds => Mathf.Max(0f, cooldownSeconds);
            public bool Preload => preload;

#if UNITY_EDITOR
            public SoundEntry(
                string id,
                SoundType type,
                AudioClip clip,
                float volumeScale,
                float cooldownSeconds,
                bool preload)
            {
                this.id = id;
                this.type = type;
                this.clip = clip;
                this.volumeScale = Mathf.Clamp01(volumeScale);
                this.cooldownSeconds = Mathf.Max(0f, cooldownSeconds);
                this.preload = preload;
            }
#endif
        }

#if UNITY_EDITOR
        public void EditorSetSounds(IEnumerable<SoundEntry> entries)
        {
            sounds.Clear();
            if (entries != null)
            {
                sounds.AddRange(entries);
            }
        }
#endif
    }

    public enum SoundType
    {
        Bgm,
        Sfx
    }
}
