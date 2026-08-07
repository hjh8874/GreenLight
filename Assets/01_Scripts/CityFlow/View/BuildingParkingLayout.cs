using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.View
{
    public sealed class BuildingParkingLayout :
        MonoBehaviour,
        IBuildingParkingPoseProvider
    {
        [SerializeField]
        private Transform[] parkingSlots;

        [SerializeField]
        private Transform entrance;

        [SerializeField]
        private Transform exit;

        public int ParkingSlotCount =>
            parkingSlots?.Length ?? 0;

        public Transform Entrance => entrance;
        public Transform Exit => exit;

        public bool TryGetParkingPose(
            int slotIndex,
            out BuildingParkingPose pose)
        {
            pose = default;
            if (parkingSlots == null ||
                slotIndex < 0 ||
                slotIndex >= parkingSlots.Length ||
                parkingSlots[slotIndex] == null)
            {
                return false;
            }

            Transform slot = parkingSlots[slotIndex];
            Vector3 forward = slot.forward;
            if (forward.sqrMagnitude <= 0.0001f)
            {
                forward = transform.forward;
            }

            pose = new BuildingParkingPose(
                slot.position,
                forward);
            return true;
        }

        // Unity setup: add this component to a building visual prefab and
        // assign its authored parking slot, entrance, and exit transforms.
    }
}
