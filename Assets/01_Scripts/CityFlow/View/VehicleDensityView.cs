using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.View
{
    public sealed class VehicleDensityView : MonoBehaviour, ICityFlowServiceConsumer
    {
        [SerializeField] private Vector2Int tile;
        [SerializeField] private Transform vehiclePrefab;
        [SerializeField] private int maxVehicles = 5;
        [SerializeField] private float spacing = 0.15f;

        private readonly List<Transform> vehicles = new List<Transform>();
        private IReadOnlyTileData tileData;

        public void Initialize(CityFlowServices services)
        {
            tileData = services.TileData;
            Refresh();
        }

        private void Update()
        {
            Refresh();
        }

        private void Refresh()
        {
            if (tileData == null || vehiclePrefab == null)
            {
                return;
            }

            int visibleCount = Mathf.RoundToInt(tileData.GetDensity01(tile) * maxVehicles);
            EnsureVehicleCount(visibleCount);

            for (int i = 0; i < vehicles.Count; i++)
            {
                bool isVisible = i < visibleCount;
                Transform vehicle = vehicles[i];
                vehicle.gameObject.SetActive(isVisible);

                if (isVisible)
                {
                    float offset = (i - ((visibleCount - 1) * 0.5f)) * spacing;
                    vehicle.localPosition = new Vector3(offset, 0f, 0f);
                }
            }
        }

        private void EnsureVehicleCount(int targetCount)
        {
            while (vehicles.Count < targetCount)
            {
                Transform vehicle = Instantiate(vehiclePrefab, transform);
                vehicle.gameObject.SetActive(false);
                vehicles.Add(vehicle);
            }
        }
    }
}
