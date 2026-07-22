using CityFlow.ViewKit;
using UnityEngine;

namespace CityFlow.View
{
    public sealed partial class MainCityView
    {
        private const float MinimumRoundaboutTransitionTiles = 0.66f;
        private const float RoundaboutStopMarginTiles = 0.04f;

        private bool TryGetRoundaboutEntryStopDistance(
            RoutePolyline polyline,
            int currentTileIndex,
            RouteVehicle vehicle,
            out float stopDistance)
        {
            stopDistance = 0f;
            if (polyline == null) return false;

            int current = Mathf.Clamp(currentTileIndex, 0, polyline.TileCount - 1);
            int next = current + 1;
            if (next >= polyline.TileCount)
            {
                return false;
            }

            bool enteringRing = IsRoundaboutTile(polyline.TileAt(next));
            int afterNext = next + 1;
            bool enteringArm = !enteringRing
                && afterNext < polyline.TileCount
                && IsRoundaboutTile(polyline.TileAt(afterNext));
            if (!enteringRing && !enteringArm) return false;

            float vehicleLength = tileSize
                * BaseVehicleLengthTiles
                * Mathf.Max(0.1f, vehicle.Style.LengthScale);
            float entryBoundary = enteringRing
                ? polyline.DistanceAtPhase(next - RoundaboutTransitionSpan())
                : polyline.DistanceAtPhase(next - 0.5f);
            stopDistance = Mathf.Max(
                polyline.DistanceAtTile(current),
                entryBoundary - vehicleLength * 0.5f - tileSize * RoundaboutStopMarginTiles);
            return true;
        }

        private float GetRoundaboutAuthorizedDistance(
            RoutePolyline polyline,
            int roundaboutTileIndex,
            float progress01)
        {
            float transitionSpan = RoundaboutTransitionSpan();
            float startPhase = roundaboutTileIndex - transitionSpan;
            float endPhase = roundaboutTileIndex + transitionSpan;
            return polyline.DistanceAtPhase(Mathf.Lerp(startPhase, endPhase, Mathf.Clamp01(progress01)));
        }

        private float RoundaboutTransitionSpan() =>
            Mathf.Clamp(roundaboutTransitionTiles, MinimumRoundaboutTransitionTiles, 0.95f);
    }
}
