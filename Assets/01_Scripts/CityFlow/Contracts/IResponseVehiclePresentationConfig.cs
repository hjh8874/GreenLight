using UnityEngine;

namespace CityFlow.Contracts
{
    public interface IResponseVehiclePresentationConfig
    {
        string VehicleDisplayName { get; }
        GameObject VehicleVisualPrefab { get; }
        float VisualScale { get; }
        float VisualDepth { get; }
        VehicleFootprint VehicleFootprint { get; }
        float TravelSecondsPerTile { get; }
    }
}
