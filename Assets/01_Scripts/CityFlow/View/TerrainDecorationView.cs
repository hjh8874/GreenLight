using CityFlow.Bootstrap;
using CityFlow.Configs;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.View
{
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class TerrainDecorationView :
        MonoBehaviour,
        ICityFlowServiceConsumer,
        ITerrainDecorationSaveSource
    {
        [Header("Generation")]
        [SerializeField] private MainCityView cityView;
        [SerializeField] private TerrainDecorationCatalogSO catalog;
        [SerializeField] private GameObject fieldTilePrefab;
        [SerializeField] private float fieldTileZ = 0.14f;
        [SerializeField] private float groundZ = 0.1f;

        private CityFlowServices services;
        private IReadOnlyTileData tileData;
        private TerrainDecorationState decorationState;
        private int spawnedCount;
        private bool initialized;

        public int SpawnedCount => spawnedCount;
        public MainCityView CityView => cityView;
        public TerrainDecorationCatalogSO Catalog => catalog;

        private void Awake()
        {
            TryInstall();
        }

        public bool TryInstall(MainCityView target = null)
        {
            if (target != null)
            {
                cityView = target;
            }
            else if (cityView == null)
            {
                cityView = FindAnyObjectByType<MainCityView>(
                    FindObjectsInactive.Include);
            }

            if (cityView == null)
            {
                Debug.LogWarning(
                    "[TerrainDecorationView] MainCityView was not found. " +
                    "Add the terrain system prefab to a city scene.",
                    this);
                return false;
            }

            if (fieldTilePrefab != null &&
                !cityView.TryConfigureFieldTiles(fieldTilePrefab, fieldTileZ))
            {
                return false;
            }

            if (cityView.FieldTilePrefab == null)
            {
                Debug.LogWarning(
                    "[TerrainDecorationView] Field tile prefab is missing.",
                    this);
                return false;
            }

            return true;
        }

        public void Initialize(CityFlowServices cityServices)
        {
            if (!isActiveAndEnabled || initialized)
            {
                return;
            }

            services = cityServices;
            tileData = services?.TileData;

            if (!TryInstall() || catalog == null || tileData == null)
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
            EnsureDecorationState();
            services.RegisterTerrainDecorationSaveSource(this);
            RebuildAll();
        }

        public TerrainDecorationSaveData CreateSnapshot()
        {
            EnsureDecorationState();
            return decorationState.CreateSnapshot();
        }

        public void RestoreSnapshot(TerrainDecorationSaveData snapshot)
        {
            EnsureDecorationState();
            decorationState.RestoreSnapshot(snapshot);
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

            Vector2Int footprint = TileFootprint.GetRotatedSize(
                placedEvent.Type,
                placedEvent.Direction);
            footprint.x = Mathf.Max(1, footprint.x);
            footprint.y = Mathf.Max(1, footprint.y);
            EnsureDecorationState();
            decorationState.ApplyPlacement(
                placedEvent.Tile,
                footprint,
                isRemove: false);

            for (int y = 0; y < footprint.y; y++)
            {
                for (int x = 0; x < footprint.x; x++)
                {
                    Vector2Int tile = placedEvent.Tile + new Vector2Int(x, y);
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
                decorationState.IsCleared(tile) ||
                !cityView.TryGetGridCell(tile, out GridCellView gridCell) ||
                gridCell.HasDecoration ||
                tileData.GetTileType(tile) != TileType.Empty ||
                !catalog.TryCreateSample(
                    tile,
                    cityView.TileSize,
                    out TerrainDecorationSample sample))
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

        private void EnsureDecorationState()
        {
            if (decorationState == null)
            {
                decorationState = new TerrainDecorationState(
                    cityView.GridWidth,
                    cityView.GridHeight);
            }
        }

        private bool IsInsideGrid(Vector2Int tile)
        {
            return tile.x >= 0 &&
                   tile.x < cityView.GridWidth &&
                   tile.y >= 0 &&
                   tile.y < cityView.GridHeight;
        }
    }
}
