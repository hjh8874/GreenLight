using CityFlow.Contracts;
using CityFlow.Sim;
using CityFlow.ViewKit;
using UnityEngine;

namespace CityFlow.View
{
    public sealed partial class MainCityView
    {
        internal bool TryBuildCommuteVehicleOverlapDiagnostic(
            Transform externalVisual,
            RoadTrafficSnapshot externalSnapshot,
            out string diagnostic)
        {
            diagnostic = string.Empty;
            if (externalVisual == null ||
                simEngine == null ||
                !externalVisual.gameObject.activeInHierarchy ||
                !TryGetRendererBounds(
                    externalVisual.gameObject,
                    transform,
                    out Bounds externalBounds))
            {
                return false;
            }

            float overlapTolerance = Mathf.Max(0.005f, tileSize * 0.01f);
            Vector3 externalDirection = transform.InverseTransformDirection(
                externalVisual.right);
            externalDirection.z = 0f;
            externalDirection = externalDirection.sqrMagnitude > 0.0001f
                ? externalDirection.normalized
                : Vector3.right;

            for (int index = 0; index < carSimMirrors.Count; index++)
            {
                CommuteCar car = carSimMirrors[index];
                if (!carVehicles.TryGetValue(car, out RouteVehicle vehicle) ||
                    vehicle?.Object == null ||
                    !vehicle.Object.activeInHierarchy ||
                    !TryGetRendererBounds(
                        vehicle.Object,
                        transform,
                        out Bounds carBounds))
                {
                    continue;
                }

                float overlapX = Mathf.Min(
                    externalBounds.max.x,
                    carBounds.max.x) - Mathf.Max(
                    externalBounds.min.x,
                    carBounds.min.x);
                float overlapY = Mathf.Min(
                    externalBounds.max.y,
                    carBounds.max.y) - Mathf.Max(
                    externalBounds.min.y,
                    carBounds.min.y);
                if (overlapX <= overlapTolerance ||
                    overlapY <= overlapTolerance)
                {
                    continue;
                }

                CarSnapshot carSnapshot = simEngine.GetCarSnapshot(index);
                Vector2Int carSimulationTile = ResolveCarSimulationTile(
                    car,
                    carSnapshot,
                    vehicle.CurrentTile);
                Vector3 carDirection = vehicle.Dir;
                carDirection.z = 0f;
                bool sameFlow = VehicleSpacingMath.IsSameFlowDirection(
                    externalDirection,
                    carDirection);
                bool sameQueue =
                    externalSnapshot.CurrentTile == carSimulationTile &&
                    sameFlow;

                VehicleFootprint carFootprint =
                    simEngine.StandardVehicleFootprint;
                float requiredCenterSpacingTiles =
                    externalSnapshot.Footprint.LengthTiles * 0.5f +
                    Mathf.Max(
                        externalSnapshot.Footprint.MinimumGapTiles,
                        carFootprint.MinimumGapTiles) +
                    carFootprint.LengthTiles * 0.5f;
                float queueCenterSpacingTiles = Mathf.Abs(
                    externalSnapshot.QueueOffsetTiles -
                    carSnapshot.QueueOffsetTiles);
                float externalVisualLength = ProjectBoundsLength(
                    externalBounds,
                    externalDirection);
                float carVisualLength = ProjectBoundsLength(
                    carBounds,
                    carDirection);
                float externalConfiguredLength =
                    externalSnapshot.Footprint.LengthTiles * tileSize;
                float carConfiguredLength =
                    carFootprint.LengthTiles * tileSize;
                float viewCenterSpacing = Mathf.Abs(Vector3.Dot(
                    carBounds.center - externalBounds.center,
                    externalDirection));
                bool viewSpacingViolation =
                    sameFlow &&
                    viewCenterSpacing / tileSize +
                    overlapTolerance / tileSize <
                    requiredCenterSpacingTiles;

                string cause;
                if (sameQueue &&
                    queueCenterSpacingTiles + overlapTolerance <
                    requiredCenterSpacingTiles)
                {
                    cause = "simulation queue spacing is below the configured footprint";
                }
                else if (viewSpacingViolation)
                {
                    cause = "view spacing is below the configured footprint across a queue boundary";
                }
                else if (externalVisualLength >
                         externalConfiguredLength * 1.15f ||
                         carVisualLength > carConfiguredLength * 1.15f)
                {
                    cause = "rendered vehicle length exceeds its configured footprint";
                }
                else if (sameQueue)
                {
                    cause = "view interpolation crossed a simulation-safe queue gap";
                }
                else if (externalSnapshot.CurrentTile == carSimulationTile)
                {
                    cause = "opposing or crossing lane visuals overlap on the same tile";
                }
                else
                {
                    cause = "adjacent tile or turn presentation paths overlap";
                }

                diagnostic =
                    $"cause={cause}, busKind={externalSnapshot.Kind}, " +
                    $"busTile={externalSnapshot.CurrentTile}, " +
                    $"busSlot={externalSnapshot.QueueSlot}, " +
                    $"busOffset={externalSnapshot.QueueOffsetTiles:F3}, " +
                    $"busFootprint={externalSnapshot.Footprint.LengthTiles:F2}, " +
                    $"busVisualLength={externalVisualLength / tileSize:F2}, " +
                    $"carIndex={index}, carState={carSnapshot.State}, " +
                    $"carTile={carSimulationTile}, " +
                    $"carSlot={carSnapshot.QueueSlot}, " +
                    $"carOffset={carSnapshot.QueueOffsetTiles:F3}, " +
                    $"carFootprint={carFootprint.LengthTiles:F2}, " +
                    $"carVisualLength={carVisualLength / tileSize:F2}, " +
                    $"sameFlow={sameFlow}, sameQueue={sameQueue}, " +
                    $"requiredCenterSpacing={requiredCenterSpacingTiles:F3}, " +
                    $"queueCenterSpacing={queueCenterSpacingTiles:F3}, " +
                    $"viewCenterSpacing={viewCenterSpacing / tileSize:F3}, " +
                    $"planarOverlap=({overlapX / tileSize:F3}," +
                    $"{overlapY / tileSize:F3})";
                return true;
            }

            return false;
        }

        private Vector2Int ResolveCarSimulationTile(
            CommuteCar car,
            CarSnapshot snapshot,
            Vector2Int fallback)
        {
            if (!bakedRoutes.TryGetValue(
                    car.RouteIndex,
                    out BakedRoutePair pair))
            {
                return fallback;
            }

            bool inbound = snapshot.State == CarState.Inbound;
            RoutePolyline polyline = inbound
                ? pair.Inbound
                : pair.Outbound;
            if (polyline == null || polyline.TileCount == 0)
            {
                return fallback;
            }

            return polyline.TileAt(Mathf.Clamp(
                snapshot.TileIndex,
                0,
                polyline.TileCount - 1));
        }

        private static float ProjectBoundsLength(
            Bounds bounds,
            Vector3 direction)
        {
            Vector3 normalized = direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : Vector3.right;
            return 2f * (
                Mathf.Abs(normalized.x) * bounds.extents.x +
                Mathf.Abs(normalized.y) * bounds.extents.y);
        }

        // Unity setup: no component is required; BusWorldView invokes this diagnostic automatically.
    }
}
