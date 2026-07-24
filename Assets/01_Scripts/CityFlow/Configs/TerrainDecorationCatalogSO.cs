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

        private void OnValidate()
        {
            scaleRange.x = Mathf.Max(0.01f, scaleRange.x);
            scaleRange.y = Mathf.Max(0.01f, scaleRange.y);
        }
    }
}
