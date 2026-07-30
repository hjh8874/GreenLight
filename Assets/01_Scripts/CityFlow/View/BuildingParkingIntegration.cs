using UnityEngine;

namespace CityFlow.View
{
    public sealed partial class MainCityView
    {
        public bool TryGetBuildingParkingPose(
            Vector2Int buildingTile,
            int slotIndex,
            out Vector3 localPosition,
            out Vector3 localForward)
        {
            localPosition = default;
            localForward = default;

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
                return false;
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

        // Unity integration: feature vehicle Views may request authored parking poses.
    }
}
