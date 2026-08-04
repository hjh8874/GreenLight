using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.View
{
    public sealed class HospitalAmbulanceParkingView :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        private const float AmbulanceLengthTiles = 0.56f;

        private readonly Dictionary<Vector2Int, GameObject> visuals = new();
        private CityFlowServices services;
        private MainCityView cityView;
        private VehicleVisualCatalogSO catalog;
        private bool initialized;

        public void Initialize(CityFlowServices cityServices)
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            services = cityServices;
            cityView = GetComponent<MainCityView>();
            catalog = Resources.Load<VehicleVisualCatalogSO>(
                "CityFlow/VehicleVisualCatalog");

            if (services?.Events != null)
            {
                services.Events.Placed += HandlePlaced;
            }
            if (services?.Save != null)
            {
                services.Save.RestoreCompleted +=
                    HandleRestoreCompleted;
            }

            Rebuild();
        }

        private void OnDestroy()
        {
            if (services?.Events != null)
            {
                services.Events.Placed -= HandlePlaced;
            }
            if (services?.Save != null)
            {
                services.Save.RestoreCompleted -=
                    HandleRestoreCompleted;
            }
        }

        private void HandlePlaced(PlacedEvent placed)
        {
            if (placed.Type == TileType.Hospital ||
                placed.IsRemove)
            {
                Rebuild();
            }
        }

        private void HandleRestoreCompleted(
            RestoreCompletedEvent restore)
        {
            Rebuild();
        }

        private void Rebuild()
        {
            foreach (GameObject visual in visuals.Values)
            {
                if (visual != null)
                {
                    Destroy(visual);
                }
            }
            visuals.Clear();

            if (cityView == null ||
                services?.TileData == null ||
                catalog?.AmbulancePrefab == null)
            {
                return;
            }

            for (int y = cityView.GridOrigin.y;
                 y < cityView.GridOrigin.y + cityView.GridHeight;
                 y++)
            {
                for (int x = cityView.GridOrigin.x;
                     x < cityView.GridOrigin.x + cityView.GridWidth;
                     x++)
                {
                    Vector2Int tile = new(x, y);
                    if (services.TileData.GetTileType(tile) != TileType.Hospital ||
                        !services.TileData.IsFootprintAnchor(tile))
                    {
                        continue;
                    }

                    CreateAmbulance(tile);
                }
            }
        }

        private void CreateAmbulance(Vector2Int hospital)
        {
            GameObject instance = Instantiate(
                catalog.AmbulancePrefab,
                cityView.transform);
            instance.name =
                $"HospitalAmbulance_{hospital.x}_{hospital.y}";
            instance.transform.localScale =
                Vector3.one *
                (cityView.TileSize * AmbulanceLengthTiles);
            instance.transform.localPosition =
                cityView.GetSpecialBuildingParkingPosition(
                    hospital,
                    cityView.VehicleGroundZ);
            instance.transform.localRotation =
                cityView.GetSpecialBuildingParkingRotation(hospital);
            VehicleVisualUtility.PrepareLit(instance);
            visuals.Add(hospital, instance);
        }
    }
}
