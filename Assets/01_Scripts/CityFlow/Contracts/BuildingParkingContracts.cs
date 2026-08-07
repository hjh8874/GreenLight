using UnityEngine;

namespace CityFlow.Contracts
{
    public readonly struct BuildingParkingPose
    {
        public BuildingParkingPose(
            Vector3 worldPosition,
            Vector3 worldForward)
        {
            WorldPosition = worldPosition;
            WorldForward = worldForward.sqrMagnitude > 0.0001f
                ? worldForward.normalized
                : Vector3.forward;
        }

        public Vector3 WorldPosition { get; }
        public Vector3 WorldForward { get; }
    }

    public interface IBuildingParkingPoseProvider
    {
        int ParkingSlotCount { get; }

        bool TryGetParkingPose(
            int slotIndex,
            out BuildingParkingPose pose);
    }
}
