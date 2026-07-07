using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.TilemapTest
{
    public sealed class TilemapTestField : MonoBehaviour, ICityFlowServiceConsumer
    {
        [Header("Field")]
        [SerializeField] private int width = GridUtil.DefaultWidth;
        [SerializeField] private int height = GridUtil.DefaultHeight;
        [SerializeField] private float tileSize = 1f;
        [SerializeField] private float tileThickness = 0.08f;
        [SerializeField] private Transform tileRoot;
        [SerializeField] private Material tileMaterial;

        [Header("Simulation")]
        [SerializeField] private bool applyBakedTilesOnStart = true;
        [SerializeField] private bool refreshCongestionColors = true;

        [Header("Vehicle Density Test")]
        [SerializeField] private Transform testVehiclePrefab;
        [SerializeField] private int maxVehiclesPerRoadTile = 10;
        [SerializeField] private float vehicleSpacing = 0.05f;

        [Header("Facility Defaults")]
        [SerializeField] private int minHousePopulation = 2;
        [SerializeField] private int maxHousePopulation = 8;
        [SerializeField] private float workerPopulationWeight = 0.45f;
        [SerializeField] private float studentPopulationWeight = 0.25f;
        [SerializeField] private float homebodyPopulationWeight = 0.3f;
        [SerializeField] private int minOfficeCapacity = 12;
        [SerializeField] private int maxOfficeCapacity = 30;
        [SerializeField] private int minSchoolCapacity = 40;
        [SerializeField] private int maxSchoolCapacity = 80;
        [SerializeField] private int minShopCapacity = 8;
        [SerializeField] private int maxShopCapacity = 20;

        [Header("Daily Demand Defaults")]
        [SerializeField] private float workerLeaveHomeHour = 8f;
        [SerializeField] private float workerReturnHomeHour = 18f;
        [SerializeField] private float studentLeaveHomeHour = 7f;
        [SerializeField] private float studentReturnHomeHour = 15f;
        [Range(0f, 1f)]
        [SerializeField] private float workerDailyShopVisitProbability = 0.25f;
        [Range(0f, 1f)]
        [SerializeField] private float studentDailyShopVisitProbability = 0.15f;
        [Range(0f, 1f)]
        [SerializeField] private float homebodyDailyShopVisitProbability = 0.35f;
        [SerializeField] private float shopDwellHours = 2f;

        [Header("Colors")]
        [SerializeField] private Color emptyColor = new Color(0.18f, 0.2f, 0.18f);
        [SerializeField] private Color roadFreeColor = new Color(0.28f, 0.32f, 0.34f);
        [SerializeField] private Color roadSlowColor = new Color(1f, 0.75f, 0.22f);
        [SerializeField] private Color roadJamColor = new Color(0.95f, 0.22f, 0.18f);
        [SerializeField] private Color houseColor = new Color(0.22f, 0.52f, 1f);
        [SerializeField] private Color officeColor = new Color(1f, 0.55f, 0.16f);
        [SerializeField] private Color schoolColor = new Color(0.62f, 0.38f, 1f);
        [SerializeField] private Color shopColor = new Color(0.2f, 0.9f, 0.65f);

        private readonly Dictionary<Vector2Int, TilemapTestTile> tiles = new Dictionary<Vector2Int, TilemapTestTile>();
        private CityFlowServices services;

        public int Width => width;
        public int Height => height;
        public float TileSize => tileSize;
        public float TileThickness => tileThickness;
        public Transform TileRoot => tileRoot;
        public Material TileMaterial => tileMaterial;
        public Transform TestVehiclePrefab => testVehiclePrefab;
        public int MaxVehiclesPerRoadTile => maxVehiclesPerRoadTile;
        public float VehicleSpacing => vehicleSpacing;
        public int MinHousePopulation => Mathf.Max(0, minHousePopulation);
        public int MaxHousePopulation => Mathf.Max(MinHousePopulation, maxHousePopulation);
        public float WorkerPopulationWeight => Mathf.Max(0f, workerPopulationWeight);
        public float StudentPopulationWeight => Mathf.Max(0f, studentPopulationWeight);
        public float HomebodyPopulationWeight => Mathf.Max(0f, homebodyPopulationWeight);
        public int MinOfficeCapacity => Mathf.Max(0, minOfficeCapacity);
        public int MaxOfficeCapacity => Mathf.Max(MinOfficeCapacity, maxOfficeCapacity);
        public int MinSchoolCapacity => Mathf.Max(0, minSchoolCapacity);
        public int MaxSchoolCapacity => Mathf.Max(MinSchoolCapacity, maxSchoolCapacity);
        public int MinShopCapacity => Mathf.Max(0, minShopCapacity);
        public int MaxShopCapacity => Mathf.Max(MinShopCapacity, maxShopCapacity);
        public float WorkerLeaveHomeHour => NormalizeHour(workerLeaveHomeHour);
        public float WorkerReturnHomeHour => NormalizeHour(workerReturnHomeHour);
        public float StudentLeaveHomeHour => NormalizeHour(studentLeaveHomeHour);
        public float StudentReturnHomeHour => NormalizeHour(studentReturnHomeHour);
        public float WorkerDailyShopVisitProbability => Mathf.Clamp01(workerDailyShopVisitProbability);
        public float StudentDailyShopVisitProbability => Mathf.Clamp01(studentDailyShopVisitProbability);
        public float HomebodyDailyShopVisitProbability => Mathf.Clamp01(homebodyDailyShopVisitProbability);
        public float ShopDwellHours => Mathf.Max(0f, shopDwellHours);

        public void SetTileRoot(Transform root)
        {
            tileRoot = root;
        }

        public void SetTileMaterial(Material material)
        {
            tileMaterial = material;
        }

        public void SetTestVehiclePrefab(Transform prefab)
        {
            testVehiclePrefab = prefab;
        }

        public void Initialize(CityFlowServices cityFlowServices)
        {
            services = cityFlowServices;
            services.Events.Placed += OnPlaced;
            RebuildCache();
        }

        public Vector3 GridToWorld(Vector2Int gridPosition)
        {
            return transform.position + new Vector3(
                gridPosition.x * tileSize,
                tileThickness * 0.5f,
                gridPosition.y * tileSize);
        }

        public bool IsInside(Vector2Int gridPosition)
        {
            return gridPosition.x >= 0 && gridPosition.x < width &&
                   gridPosition.y >= 0 && gridPosition.y < height;
        }

        public void RebuildCache()
        {
            tiles.Clear();
            TilemapTestTile[] foundTiles = GetComponentsInChildren<TilemapTestTile>(true);

            foreach (TilemapTestTile tile in foundTiles)
            {
                if (!IsInside(tile.GridPosition))
                {
                    continue;
                }

                tiles[tile.GridPosition] = tile;
                RefreshTile(tile);
            }
        }

        public void ApplyBakedTilesToSimulation()
        {
            if (services == null)
            {
                Debug.LogWarning("TilemapTestField cannot apply baked tiles because CityFlowServices is not initialized.");
                return;
            }

            RebuildCache();

            foreach (TilemapTestTile tile in tiles.Values)
            {
                if (tile.TileType == TileType.Empty)
                {
                    continue;
                }

                if (!services.Placement.Place(tile.GridPosition, tile.TileType))
                {
                    Debug.Log($"TilemapTestField skipped placement at {tile.GridPosition} as {tile.TileType}.");
                }
            }
        }

        public Color GetColor(TileType type, CongestionLevel congestion)
        {
            switch (type)
            {
                case TileType.Road:
                    return congestion switch
                    {
                        CongestionLevel.Jam => roadJamColor,
                        CongestionLevel.Slow => roadSlowColor,
                        _ => roadFreeColor
                    };
                case TileType.House:
                    return houseColor;
                case TileType.Office:
                    return officeColor;
                case TileType.School:
                    return schoolColor;
                default:
                    return emptyColor;
            }
        }

        public Color GetColor(TilemapTestTile tile, CongestionLevel congestion)
        {
            if (tile != null && tile.FacilityKind == TilemapTestFacilityKind.Shop)
            {
                return shopColor;
            }

            return GetColor(tile != null ? tile.TileType : TileType.Empty, congestion);
        }

        public void RefreshAllTileColors()
        {
            RebuildCache();
            foreach (TilemapTestTile tile in tiles.Values)
            {
                RefreshTile(tile);
            }
        }

        public void RefreshTile(TilemapTestTile tile)
        {
            if (tile == null)
            {
                return;
            }

            tile.EnsureMaterial(tileMaterial);

            CongestionLevel congestion = CongestionLevel.Free;

            if (services != null && tile.TileType == TileType.Road)
            {
                congestion = services.TileData.GetCongestion(tile.GridPosition);
            }

            tile.ApplyColor(GetColor(tile, congestion));
        }

        private void Start()
        {
            if (applyBakedTilesOnStart)
            {
                ApplyBakedTilesToSimulation();
            }
        }

        private void Update()
        {
            if (!refreshCongestionColors || services == null)
            {
                return;
            }

            foreach (TilemapTestTile tile in tiles.Values)
            {
                if (tile.TileType == TileType.Road)
                {
                    RefreshTile(tile);
                }
            }
        }

        private void OnDestroy()
        {
            if (services != null)
            {
                services.Events.Placed -= OnPlaced;
            }
        }

        private void OnPlaced(PlacedEvent placed)
        {
            RebuildCache();

            if (!tiles.TryGetValue(placed.Tile, out TilemapTestTile tile))
            {
                return;
            }

            tile.SetTileType(placed.IsRemove ? TileType.Empty : placed.Type);
            RefreshTile(tile);
        }

        private static float NormalizeHour(float hour)
        {
            return Mathf.Repeat(hour, 24f);
        }
    }
}

/*
Unity implementation:
1. Add this component to a scene GameObject named TilemapTestField.
2. Use the custom inspector buttons to bake a 3D tile hierarchy under this object.
3. Add CityBootstrap to the scene and set it to use the real SimEngine, then press Play.
*/
