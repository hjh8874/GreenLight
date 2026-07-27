using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.DebugTools
{
    /// <summary>
    /// PR #151 replacement prototype data.
    /// Recreates the integrated Debug scene's showcase block using real
    /// placement services so validation never reads or writes a player save.
    /// </summary>
    public sealed class ContentFeaturePrototypeScenario :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        [SerializeField] private bool logResult = true;
        [SerializeField] private CityBusService cityBus;

        private static bool seeded;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            seeded = false;
        }

        public void Initialize(CityFlowServices services)
        {
            if (!isActiveAndEnabled || seeded ||
                services?.Placement == null)
            {
                return;
            }

            int placed = BuildPrototypeCity(
                services.Placement);
            seeded = true;
            cityBus ??= GetComponent<CityBusService>();
            cityBus?.StartService();

            if (logResult)
            {
                Debug.Log(
                    $"[ContentPrototype] Seeded {placed} tiles. " +
                    "City bus and emergency flow are ready.",
                    this);
            }
        }

        public static int BuildPrototypeCity(
            IPlacementService placement)
        {
            int placed = 0;

            for (int x = 6; x <= 12; x++)
            {
                placed += Place(
                    placement,
                    new Vector2Int(x, 9),
                    TileType.Road);
                placed += Place(
                    placement,
                    new Vector2Int(x, 15),
                    TileType.Road);
            }

            for (int y = 10; y <= 14; y++)
            {
                placed += Place(
                    placement,
                    new Vector2Int(6, y),
                    TileType.Road);
                placed += Place(
                    placement,
                    new Vector2Int(12, y),
                    TileType.Road);
            }

            placed += PlaceBuilding(
                placement,
                new Vector2Int(4, 12),
                TileType.House);
            placed += PlaceBuilding(
                placement,
                new Vector2Int(13, 12),
                TileType.Office);
            placed += PlaceBuilding(
                placement,
                new Vector2Int(9, 16),
                TileType.School);
            placed += PlaceBuilding(
                placement,
                new Vector2Int(13, 14),
                TileType.Hospital);

            return placed;
        }

        private static int PlaceBuilding(
            IPlacementService placement,
            Vector2Int tile,
            TileType type)
        {
            return Place(placement, tile, type);
        }

        private static int Place(
            IPlacementService placement,
            Vector2Int tile,
            TileType type)
        {
            return placement.Place(tile, type)
                ? 1
                : 0;
        }
    }
}
