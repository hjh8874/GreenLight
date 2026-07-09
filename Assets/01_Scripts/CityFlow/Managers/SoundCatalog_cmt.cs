using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace CityFlow.Managers
{
    [CreateAssetMenu(fileName = "SoundCatalog_cmt", menuName = "CityFlow/Sound Catalog cmt")]
    public sealed class SoundCatalog_cmt : ScriptableObject
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
            [SerializeField] private AssetReferenceT<AudioClip> clip;
            [Range(0f, 1f)]
            [SerializeField] private float volumeScale = 1f;
            [SerializeField] private bool preload;

            public string Id => id;
            public SoundType Type => type;
            public AssetReferenceT<AudioClip> Clip => clip;
            public float VolumeScale => volumeScale;
            public bool Preload => preload;
        }
    }

    public enum SoundType
    {
        Bgm,
        Sfx
    }
}
