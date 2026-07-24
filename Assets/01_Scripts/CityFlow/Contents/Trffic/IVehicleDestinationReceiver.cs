using UnityEngine;

namespace CityFlow.Content.Traffic
{
    public interface IVehicleDestinationReceiver
    {
        bool HasArrivedAtDestination { get; }

        void SetDestination(
            Transform destinationPoint);
    }
}