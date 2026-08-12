using System.Collections.Generic;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.View
{
    public sealed partial class MainCityView
    {
        private const float ParkingOccupancyRadiusTiles = 0.12f;
        private readonly Dictionary<
            (Vector2Int BuildingTile, int SlotIndex),
            Transform> parkingReservations = new();
        private SpecialBuildingView specialBuildingParkingView;

        public bool TryGetBuildingParkingPose(
            Vector2Int buildingTile,
            int slotIndex,
            out Vector3 localPosition,
            out Vector3 localForward)
        {
            return TryGetBuildingParkingPose(
                buildingTile,
                slotIndex,
                out localPosition,
                out localForward,
                out _);
        }

        public bool TryGetBuildingParkingPose(
            Vector2Int buildingTile,
            int slotIndex,
            out Vector3 localPosition,
            out Vector3 localForward,
            out float presentationScale)
        {
            localPosition = default;
            localForward = default;
            presentationScale = 1f;

            if (tileData != null &&
                tileData.TryGetFootprintAnchor(
                    buildingTile,
                    out Vector2Int anchor))
            {
                buildingTile = anchor;
            }

            if (!tileVisuals.TryGetValue(
                    buildingTile,
                    out TileVisual visual) ||
                visual?.Object == null)
            {
                return TryGetSpecialBuildingParkingPose(
                    buildingTile,
                    slotIndex,
                    out localPosition,
                    out localForward,
                    out presentationScale);
            }

            BuildingParkingLayout layout =
                visual.Object.GetComponentInChildren<
                    BuildingParkingLayout>(true);
            if (layout != null &&
                layout.TryGetParkingPose(
                    Mathf.Max(0, slotIndex),
                    out BuildingParkingPose authoredPose))
            {
                presentationScale = authoredPose.PresentationScale;
                return TryConvertParkingPose(
                    authoredPose,
                    out localPosition,
                    out localForward);
            }

            Transform slot = visual.Object.transform.Find(
                $"ParkingSlot_{Mathf.Max(0, slotIndex)}");
            if (slot == null)
            {
                return false;
            }

            localPosition = transform.InverseTransformPoint(slot.position);
            localForward = transform.InverseTransformDirection(
                visual.Object.transform.TransformDirection(Vector3.up));
            localForward.z = 0f;
            if (localForward.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            localForward.Normalize();
            return true;
        }

        private bool TryGetSpecialBuildingParkingPose(
            Vector2Int buildingTile,
            int slotIndex,
            out Vector3 localPosition,
            out Vector3 localForward,
            out float presentationScale)
        {
            localPosition = default;
            localForward = default;
            presentationScale = 1f;
            if (specialBuildingParkingView == null)
            {
                specialBuildingParkingView =
                    FindAnyObjectByType<SpecialBuildingView>();
            }

            if (specialBuildingParkingView == null ||
                !specialBuildingParkingView.TryGetParkingPose(
                    buildingTile,
                    Mathf.Max(0, slotIndex),
                    out BuildingParkingPose pose) ||
                !TryConvertParkingPose(
                    pose,
                    out localPosition,
                    out localForward))
            {
                return false;
            }

            presentationScale = pose.PresentationScale;
            return true;
        }

        private bool TryConvertParkingPose(
            BuildingParkingPose pose,
            out Vector3 localPosition,
            out Vector3 localForward)
        {
            localPosition = transform.InverseTransformPoint(
                pose.WorldPosition);
            localForward = transform.InverseTransformDirection(
                pose.WorldForward);
            localForward.z = 0f;
            if (localForward.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            localForward.Normalize();
            return true;
        }

        public bool TryGetFirstFreeBuildingParkingPose(
            Vector2Int buildingTile,
            int slotCount,
            Transform requestingVehicle,
            out int selectedSlot,
            out Vector3 localPosition,
            out Vector3 localForward)
        {
            selectedSlot = -1;
            localPosition = default;
            localForward = default;

            for (int slotIndex = 0;
                 slotIndex < Mathf.Max(0, slotCount);
                 slotIndex++)
            {
                if (!TryGetBuildingParkingPose(
                        buildingTile,
                        slotIndex,
                        out Vector3 candidatePosition,
                        out Vector3 candidateForward) ||
                    IsParkingPoseOccupied(
                        candidatePosition,
                        requestingVehicle))
                {
                    continue;
                }

                selectedSlot = slotIndex;
                localPosition = candidatePosition;
                localForward = candidateForward;
                return true;
            }

            return false;
        }

        public bool TryReserveBuildingParkingPose(
            Vector2Int buildingTile,
            int slotIndex,
            Transform requestingVehicle,
            out Vector3 localPosition,
            out Vector3 localForward)
        {
            localPosition = default;
            localForward = default;
            if (slotIndex < 0 || requestingVehicle == null)
            {
                return false;
            }

            if (tileData != null &&
                tileData.TryGetFootprintAnchor(
                    buildingTile,
                    out Vector2Int anchor))
            {
                buildingTile = anchor;
            }

            var key = (buildingTile, slotIndex);
            if (parkingReservations.TryGetValue(
                    key,
                    out Transform reservation) &&
                reservation != null &&
                !IsSameVehicle(reservation, requestingVehicle))
            {
                return false;
            }

            if (!TryGetBuildingParkingPose(
                    buildingTile,
                    slotIndex,
                    out localPosition,
                    out localForward) ||
                IsParkingPoseOccupied(
                    localPosition,
                    requestingVehicle))
            {
                return false;
            }

            parkingReservations[key] = requestingVehicle;
            return true;
        }

        public bool TryReserveFirstFreeBuildingParkingPose(
            Vector2Int buildingTile,
            int slotCount,
            Transform requestingVehicle,
            out int selectedSlot,
            out Vector3 localPosition,
            out Vector3 localForward)
        {
            selectedSlot = -1;
            localPosition = default;
            localForward = default;
            if (requestingVehicle == null)
            {
                return false;
            }

            if (tileData != null &&
                tileData.TryGetFootprintAnchor(
                    buildingTile,
                    out Vector2Int anchor))
            {
                buildingTile = anchor;
            }

            foreach (KeyValuePair<
                         (Vector2Int BuildingTile, int SlotIndex),
                         Transform> entry in parkingReservations)
            {
                if (entry.Key.BuildingTile == buildingTile &&
                    entry.Value != null &&
                    IsSameVehicle(entry.Value, requestingVehicle) &&
                    TryGetBuildingParkingPose(
                        buildingTile,
                        entry.Key.SlotIndex,
                        out localPosition,
                        out localForward))
                {
                    selectedSlot = entry.Key.SlotIndex;
                    return true;
                }
            }

            for (int slotIndex = 0;
                 slotIndex < Mathf.Max(0, slotCount);
                 slotIndex++)
            {
                var key = (buildingTile, slotIndex);
                if (parkingReservations.TryGetValue(
                        key,
                        out Transform reservation))
                {
                    if (reservation == null)
                    {
                        parkingReservations.Remove(key);
                    }
                    else if (!IsSameVehicle(
                                 reservation,
                                 requestingVehicle))
                    {
                        continue;
                    }
                }

                if (!TryGetBuildingParkingPose(
                        buildingTile,
                        slotIndex,
                        out Vector3 candidatePosition,
                        out Vector3 candidateForward) ||
                    IsParkingPoseOccupied(
                        candidatePosition,
                        requestingVehicle))
                {
                    continue;
                }

                parkingReservations[key] = requestingVehicle;
                selectedSlot = slotIndex;
                localPosition = candidatePosition;
                localForward = candidateForward;
                return true;
            }

            return false;
        }

        public void ReleaseBuildingParkingReservation(
            Vector2Int buildingTile,
            int slotIndex,
            Transform requestingVehicle)
        {
            if (slotIndex < 0 || requestingVehicle == null)
            {
                return;
            }

            if (tileData != null &&
                tileData.TryGetFootprintAnchor(
                    buildingTile,
                    out Vector2Int anchor))
            {
                buildingTile = anchor;
            }

            var key = (buildingTile, slotIndex);
            if (parkingReservations.TryGetValue(
                    key,
                    out Transform reservation) &&
                (reservation == null ||
                 IsSameVehicle(reservation, requestingVehicle)))
            {
                parkingReservations.Remove(key);
            }
        }

        private bool IsParkingPoseOccupied(
            Vector3 localPosition,
            Transform requestingVehicle)
        {
            float occupancyRadius =
                Mathf.Max(0.01f, tileSize * ParkingOccupancyRadiusTiles);
            float occupancyRadiusSqr = occupancyRadius * occupancyRadius;
            VehicleNightLighting[] vehicleLights =
                FindObjectsByType<VehicleNightLighting>(
                    FindObjectsInactive.Exclude);

            foreach (VehicleNightLighting vehicleLight in vehicleLights)
            {
                Transform candidate = vehicleLight.transform;
                if (!candidate.gameObject.activeInHierarchy ||
                    IsSameVehicle(candidate, requestingVehicle) ||
                    !HasVisibleVehicleRenderer(candidate))
                {
                    continue;
                }

                Vector3 candidatePosition =
                    transform.InverseTransformPoint(candidate.position);
                Vector2 separation = new(
                    candidatePosition.x - localPosition.x,
                    candidatePosition.y - localPosition.y);
                if (separation.sqrMagnitude <= occupancyRadiusSqr)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasVisibleVehicleRenderer(
            Transform vehicle)
        {
            Renderer[] renderers =
                vehicle.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer != null &&
                    renderer.enabled &&
                    renderer.gameObject.activeInHierarchy &&
                    !renderer.forceRenderingOff)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSameVehicle(
            Transform candidate,
            Transform requestingVehicle)
        {
            return requestingVehicle != null &&
                   (candidate == requestingVehicle ||
                    candidate.IsChildOf(requestingVehicle) ||
                    requestingVehicle.IsChildOf(candidate));
        }

        // Unity integration: feature vehicle Views may request authored parking poses.
    }
}
