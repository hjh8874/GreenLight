using UnityEngine;

namespace CityFlow.Contracts
{
    public readonly struct BuildingParkingPose
    {
        public BuildingParkingPose(
            Vector3 worldPosition,
            Vector3 worldForward,
            float presentationScale = 1f)
        {
            WorldPosition = worldPosition;
            WorldForward = worldForward.sqrMagnitude > 0.0001f
                ? worldForward.normalized
                : Vector3.forward;
            PresentationScale = Mathf.Clamp(
                presentationScale,
                0.5f,
                1f);
        }

        public Vector3 WorldPosition { get; }
        public Vector3 WorldForward { get; }
        public float PresentationScale { get; }
    }

    public interface IBuildingParkingPoseProvider
    {
        int ParkingSlotCount { get; }

        bool TryGetParkingPose(
            int slotIndex,
            out BuildingParkingPose pose);
    }
}
