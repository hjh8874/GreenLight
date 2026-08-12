using CityFlow.Contracts;
using CityFlow.ViewKit;
using UnityEngine;

namespace CityFlow.View
{
    public sealed partial class MainCityView
    {
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
            VehicleFootprint standardFootprint =
                simEngine.StandardVehicleFootprint;
            float requiredCenterSpacing =
                GetRequiredVehiclePresentationHeadway(
                    RoadTrafficAgentKind.Car,
                    standardFootprint,
                    RoadTrafficAgentKind.Car,
                    standardFootprint);
            float entryClearance = ResolveRoundaboutEntryClearance(
                requiredCenterSpacing,
                vehicleLength,
                tileSize);
            // 접근 타일 중심을 하한으로 두면 arm에서 중심까지 남은 길이보다
            // 안전 간격이 큰 경우 정지선이 다시 arm 중심으로 밀려난다. 경로 시작까지
            // 후퇴할 수 있게 해야 로터리 경계와 실제 중심 간격을 확보할 수 있다.
            stopDistance = Mathf.Clamp(
                entryBoundary - entryClearance,
                0f,
                polyline.Length);
            return true;
        }

        internal static float ResolveRoundaboutEntryClearance(
            float requiredCenterSpacing,
            float vehicleLength,
            float tileSize) =>
            Mathf.Max(
                requiredCenterSpacing,
                vehicleLength * 0.5f +
                tileSize * RoundaboutStopMarginTiles);

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
            RoutePolyline.ClampTransitionSpan(roundaboutTransitionTiles);
    }
}
