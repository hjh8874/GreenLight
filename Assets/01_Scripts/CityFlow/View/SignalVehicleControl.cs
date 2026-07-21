using CityFlow.ViewKit;
using UnityEngine;

namespace CityFlow.View
{
    public sealed partial class MainCityView
    {
        private const float BaseVehicleLengthTiles = 0.38f;
        private const float SignalStopMarginTiles = 0.04f;

        private bool TryGetSignalEntryStopDistance(
            RoutePolyline polyline,
            int currentTileIndex,
            RouteVehicle vehicle,
            out float stopDistance)
        {
            stopDistance = 0f;
            if (signalControl == null || polyline == null)
            {
                return false;
            }

            int current = Mathf.Clamp(currentTileIndex, 0, polyline.TileCount - 1);
            int next = current + 1;
            if (next >= polyline.TileCount)
            {
                return false;
            }

            Vector2Int nextTile = polyline.TileAt(next);
            if (signalControl.GetSignalCycleSlots(nextTile) <= 0)
            {
                return false;
            }

            float vehicleLength = tileSize
                * BaseVehicleLengthTiles
                * Mathf.Max(0.1f, vehicle.Style.LengthScale);
            stopDistance = SignalStopLineMath.CalculateStopDistance(
                polyline.DistanceAtTile(current),
                polyline.DistanceAtTile(next),
                vehicleLength,
                tileSize * SignalStopMarginTiles);
            return true;
        }
    }
}
