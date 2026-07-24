using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Configs;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.View
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MainCityView))]
    public sealed class TerrainDecorationView : MonoBehaviour, ICityFlowServiceConsumer
    {
        [Header("Generation")]
        [SerializeField] private TerrainDecorationCatalogSO catalog;
        [SerializeField] private float groundZ = 0.1f;

        private CityFlowServices services;
        private IReadOnlyTileData tileData;
        private MainCityView cityView;
        private bool[] clearedTiles;
        private int spawnedCount;
        private bool initialized;

        public int SpawnedCount => spawnedCount;

        public void Initialize(CityFlowServices cityServices)
        {
            if (!isActiveAndEnabled || initialized)
            {
                return;
            }

            services = cityServices;
            tileData = services?.TileData;
            cityView = GetComponent<MainCityView>();

            if (catalog == null || tileData == null || cityView == null)
            {
                Debug.LogWarning(
                    "[TerrainDecorationView] Catalog, tile data, or MainCityView is missing. " +
                    "Terrain decoration generation is disabled.",
                    this);
                return;
            }

            services.Events.Placed += OnPlaced;
            cityView.GridCellsBuilt += OnGridCellsBuilt;
            if (services.Save != null)
            {
                services.Save.RestoreCompleted += OnRestoreCompleted;
            }

            initialized = true;
            EnsureClearedTileMask();
            RebuildAll();
        }

        private void OnDestroy()
        {
            if (!initialized || services == null)
            {
                return;
            }

            services.Events.Placed -= OnPlaced;
            cityView.GridCellsBuilt -= OnGridCellsBuilt;
            if (services.Save != null)
            {
                services.Save.RestoreCompleted -= OnRestoreCompleted;
            }
        }

        private void RebuildAll()
        {
            ClearDecorations();

            for (int y = 0; y < cityView.GridHeight; y++)
            {
                for (int x = 0; x < cityView.GridWidth; x++)
                {
                    TrySpawnDecoration(new Vector2Int(x, y));
                }
            }

            Debug.Log(
                $"[TerrainDecorationView] Generated {spawnedCount} deterministic " +
                $"decorations for {cityView.GridWidth}x{cityView.GridHeight}.",
                this);
        }

        private void OnPlaced(PlacedEvent placedEvent)
        {
            if (placedEvent.IsRemove)
            {
                return;
            }

            Vector2Int footprint = tileData.GetFootprintSize(placedEvent.Type);
            footprint.x = Mathf.Max(1, footprint.x);
            footprint.y = Mathf.Max(1, footprint.y);

            for (int y = 0; y < footprint.y; y++)
            {
                for (int x = 0; x < footprint.x; x++)
                {
                    Vector2Int tile = placedEvent.Tile + new Vector2Int(x, y);
                    MarkTileCleared(tile);
                    RemoveDecoration(tile);
                }
            }
        }

        private void OnGridCellsBuilt()
        {
            RebuildAll();
        }

        private void OnRestoreCompleted(RestoreCompletedEvent _)
        {
            RebuildAll();
        }

        private void TrySpawnDecoration(Vector2Int tile)
        {
            if (!IsInsideGrid(tile) ||
                IsTileCleared(tile) ||
                !cityView.TryGetGridCell(tile, out GridCellView gridCell) ||
                gridCell.HasDecoration ||
                tileData.GetTileType(tile) != TileType.Empty ||
                !TryCreateSample(tile, out DecorationSample sample))
            {
                return;
            }

            GameObject instance = Instantiate(sample.Prefab);
            instance.name = $"{sample.Prefab.name}_{tile.x}_{tile.y}";
            gridCell.SetDecoration(instance);
            instance.transform.localPosition = new Vector3(
                sample.Offset.x,
                sample.Offset.y,
                groundZ - cityView.FieldTileZ);
            instance.transform.localRotation = Quaternion.Euler(0f, 0f, sample.RotationDegrees);
            instance.transform.localScale = Vector3.one * sample.Scale;
            spawnedCount++;
        }

        private bool TryCreateSample(Vector2Int tile, out DecorationSample sample)
        {
            sample = default;

            uint randomState = CreateTileSeed(catalog.WorldSeed, tile);
            if (Next01(ref randomState) >= catalog.SpawnChance)
            {
                return false;
            }

            int totalWeight = catalog.GetTotalWeight();
            if (totalWeight <= 0)
            {
                return false;
            }

            float selectedWeight = Next01(ref randomState) * totalWeight;
            TerrainDecorationCatalogSO.Entry selectedEntry = null;
            IReadOnlyList<TerrainDecorationCatalogSO.Entry> entries = catalog.Entries;

            for (int i = 0; i < entries.Count; i++)
            {
                TerrainDecorationCatalogSO.Entry entry = entries[i];
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

            float jitterDistance = catalog.PositionJitter * cityView.TileSize;
            Vector2 offset = new Vector2(
                Mathf.Lerp(-jitterDistance, jitterDistance, Next01(ref randomState)),
                Mathf.Lerp(-jitterDistance, jitterDistance, Next01(ref randomState)));
            float rotationDegrees = Next01(ref randomState) * 360f;
            float scale = Mathf.Lerp(
                catalog.MinimumScale,
                catalog.MaximumScale,
                Next01(ref randomState));

            sample = new DecorationSample(
                selectedEntry.Prefab,
                offset,
                rotationDegrees,
                scale);
            return true;
        }

        private void RemoveDecoration(Vector2Int tile)
        {
            if (!cityView.TryGetGridCell(tile, out GridCellView gridCell) ||
                !gridCell.HasDecoration)
            {
                return;
            }

            gridCell.RemoveDecoration();
            spawnedCount = Mathf.Max(0, spawnedCount - 1);
        }

        private void ClearDecorations()
        {
            spawnedCount = 0;

            for (int y = 0; y < cityView.GridHeight; y++)
            {
                for (int x = 0; x < cityView.GridWidth; x++)
                {
                    Vector2Int tile = new Vector2Int(x, y);
                    if (cityView.TryGetGridCell(tile, out GridCellView gridCell))
                    {
                        gridCell.RemoveDecoration();
                    }
                }
            }
        }

        private void EnsureClearedTileMask()
        {
            int requiredLength = cityView.GridWidth * cityView.GridHeight;
            if (clearedTiles == null || clearedTiles.Length != requiredLength)
            {
                clearedTiles = new bool[requiredLength];
            }
        }

        private void MarkTileCleared(Vector2Int tile)
        {
            if (!IsInsideGrid(tile))
            {
                return;
            }

            clearedTiles[ToIndex(tile)] = true;
        }

        private bool IsTileCleared(Vector2Int tile)
        {
            return clearedTiles != null &&
                   IsInsideGrid(tile) &&
                   clearedTiles[ToIndex(tile)];
        }

        private int ToIndex(Vector2Int tile)
        {
            return tile.y * cityView.GridWidth + tile.x;
        }

        private bool IsInsideGrid(Vector2Int tile)
        {
            return tile.x >= 0 &&
                   tile.x < cityView.GridWidth &&
                   tile.y >= 0 &&
                   tile.y < cityView.GridHeight;
        }

        private static uint CreateTileSeed(int worldSeed, Vector2Int tile)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = (hash ^ (uint)worldSeed) * 16777619u;
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

        private readonly struct DecorationSample
        {
            public DecorationSample(
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
}
