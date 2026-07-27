using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Configs
{
    [CreateAssetMenu(
        fileName = "TerrainDecorationCatalog",
        menuName = "CityFlow/Visuals/Terrain Decoration Catalog")]
    public sealed class TerrainDecorationCatalogSO : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [SerializeField] private GameObject prefab;
            [SerializeField, Min(1)] private int weight = 1;

            public GameObject Prefab => prefab;
            public int Weight => Mathf.Max(1, weight);
        }

        [Header("Deterministic Generation")]
        [SerializeField] private int worldSeed = 20260724;
        [SerializeField, Range(0f, 1f)] private float spawnChance = 0.28f;
        [SerializeField, Range(0f, 0.45f)] private float positionJitter = 0.24f;
        [SerializeField] private Vector2 scaleRange = new Vector2(0.9f, 1.12f);

        [Header("Weighted Prefabs")]
        [SerializeField] private List<Entry> entries = new();

        public int WorldSeed => worldSeed;
        public float SpawnChance => Mathf.Clamp01(spawnChance);
        public float PositionJitter => Mathf.Clamp(positionJitter, 0f, 0.45f);
        public float MinimumScale => Mathf.Max(0.01f, Mathf.Min(scaleRange.x, scaleRange.y));
        public float MaximumScale => Mathf.Max(MinimumScale, Mathf.Max(scaleRange.x, scaleRange.y));
        public IReadOnlyList<Entry> Entries => entries;

        public int GetTotalWeight()
        {
            int totalWeight = 0;

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry?.Prefab == null)
                {
                    continue;
                }

                totalWeight += entry.Weight;
            }

            return totalWeight;
        }

        public bool TryCreateSample(
            Vector2Int tile,
            float tileSize,
            out TerrainDecorationSample sample)
        {
            sample = default;

            uint randomState = CreateTileSeed(WorldSeed, tile);
            if (Next01(ref randomState) >= SpawnChance)
            {
                return false;
            }

            int totalWeight = GetTotalWeight();
            if (totalWeight <= 0)
            {
                return false;
            }

            float selectedWeight = Next01(ref randomState) * totalWeight;
            Entry selectedEntry = null;

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry?.Prefab == null)
                {
                    continue;
                }

                selectedWeight -= entry.Weight;
                if (selectedWeight <= 0f)
                {
                    selectedEntry = entry;
                    break;
                }
            }

            if (selectedEntry == null)
            {
                return false;
            }

            float jitterDistance = PositionJitter * tileSize;
            Vector2 offset = new Vector2(
                Mathf.Lerp(-jitterDistance, jitterDistance, Next01(ref randomState)),
                Mathf.Lerp(-jitterDistance, jitterDistance, Next01(ref randomState)));
            float rotationDegrees = Next01(ref randomState) * 360f;
            float scale = Mathf.Lerp(
                MinimumScale,
                MaximumScale,
                Next01(ref randomState));

            sample = new TerrainDecorationSample(
                selectedEntry.Prefab,
                offset,
                rotationDegrees,
                scale);
            return true;
        }

        private void OnValidate()
        {
            scaleRange.x = Mathf.Max(0.01f, scaleRange.x);
            scaleRange.y = Mathf.Max(0.01f, scaleRange.y);
        }

        private static uint CreateTileSeed(int seed, Vector2Int tile)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = (hash ^ (uint)seed) * 16777619u;
                hash = (hash ^ (uint)tile.x) * 16777619u;
                hash = (hash ^ (uint)tile.y) * 16777619u;
                return hash == 0u ? 0xA341316Cu : hash;
            }
        }

        private static float Next01(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0x00FFFFFFu) / 16777216f;
        }
    }

    public readonly struct TerrainDecorationSample
    {
        public TerrainDecorationSample(
            GameObject prefab,
            Vector2 offset,
            float rotationDegrees,
            float scale)
        {
            Prefab = prefab;
            Offset = offset;
            RotationDegrees = rotationDegrees;
            Scale = scale;
        }

        public GameObject Prefab { get; }
        public Vector2 Offset { get; }
        public float RotationDegrees { get; }
        public float Scale { get; }
    }
}
