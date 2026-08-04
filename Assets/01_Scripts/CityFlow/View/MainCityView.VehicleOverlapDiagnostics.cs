using System.Collections.Generic;
using CityFlow.Contracts;
using CityFlow.Sim;
using CityFlow.ViewKit;
using UnityEngine;

namespace CityFlow.View
{
    public sealed partial class MainCityView
    {
        private float nextCommuteOverlapCheckTime;
        private float nextCommuteOverlapWarningTime;
        private readonly List<CommuteOverlapCandidate>
            commuteOverlapCandidates = new();

        private readonly struct CommuteOverlapCandidate
        {
            public CommuteOverlapCandidate(
                int index,
                RouteVehicle vehicle,
                CarSnapshot snapshot,
                Bounds bounds,
                Vector3 direction,
                Vector2Int tile,
                float visualLengthTiles)
            {
                Index = index;
                Vehicle = vehicle;
                Snapshot = snapshot;
                Bounds = bounds;
                Direction = direction;
                Tile = tile;
                VisualLengthTiles = visualLengthTiles;
            }

            public int Index { get; }
            public RouteVehicle Vehicle { get; }
            public CarSnapshot Snapshot { get; }
            public Bounds Bounds { get; }
            public Vector3 Direction { get; }
            public Vector2Int Tile { get; }
            public float VisualLengthTiles { get; }
        }

        private void UpdateCommuteVehicleOverlapDiagnostics()
        {
            if (simEngine == null ||
                Time.unscaledTime < nextCommuteOverlapCheckTime)
            {
                return;
            }

            nextCommuteOverlapCheckTime =
                Time.unscaledTime + 0.2f;
            if (Time.unscaledTime < nextCommuteOverlapWarningTime ||
                !TryBuildCommuteVehiclePairOverlapDiagnostic(
                    out string diagnostic))
            {
                return;
            }

            nextCommuteOverlapWarningTime =
                Time.unscaledTime + 1.5f;
            Debug.LogWarning(
                $"[VehicleOverlap] {diagnostic}",
                this);
        }

        private bool TryBuildCommuteVehiclePairOverlapDiagnostic(
            out string diagnostic)
        {
            diagnostic = string.Empty;
            float overlapTolerance =
                Mathf.Max(0.005f, tileSize * 0.01f);
            float safeTileSize =
                Mathf.Max(0.0001f, tileSize);
            VehicleFootprint footprint =
                simEngine.StandardVehicleFootprint;
            float requiredCenterSpacingTiles =
                GetRequiredVehiclePresentationHeadway(
                    RoadTrafficAgentKind.Car,
                    footprint,
                    RoadTrafficAgentKind.Car,
                    footprint) /
                safeTileSize;

            commuteOverlapCandidates.Clear();
            for (int index = 0;
                 index < carSimMirrors.Count;
                 index++)
            {
                CommuteCar car = carSimMirrors[index];
                CarSnapshot snapshot =
                    simEngine.GetCarSnapshot(index);
                if (!snapshot.IsVisible ||
                    !TryGetDiagnosticVehicle(
                        car,
                        out RouteVehicle vehicle,
                        out Bounds bounds))
                {
                    continue;
                }

                Vector3 direction =
                    ResolveDiagnosticDirection(vehicle);
                Vector2Int tile = ResolveCarSimulationTile(
                    car,
                    snapshot,
                    vehicle.CurrentTile);
                float visualLengthTiles =
                    ProjectRendererLength(
                        vehicle.Object,
                        transform,
                        direction) /
                    safeTileSize;
                commuteOverlapCandidates.Add(
                    new CommuteOverlapCandidate(
                        index,
                        vehicle,
                        snapshot,
                        bounds,
                        direction,
                        tile,
                        visualLengthTiles));
            }

            for (int firstCandidateIndex = 0;
                 firstCandidateIndex <
                     commuteOverlapCandidates.Count;
                 firstCandidateIndex++)
            {
                CommuteOverlapCandidate first =
                    commuteOverlapCandidates[
                        firstCandidateIndex];
                for (int secondCandidateIndex =
                         firstCandidateIndex + 1;
                     secondCandidateIndex <
                         commuteOverlapCandidates.Count;
                     secondCandidateIndex++)
                {
                    CommuteOverlapCandidate second =
                        commuteOverlapCandidates[
                            secondCandidateIndex];

                    float overlapX = Mathf.Min(
                        first.Bounds.max.x,
                        second.Bounds.max.x) - Mathf.Max(
                        first.Bounds.min.x,
                        second.Bounds.min.x);
                    float overlapY = Mathf.Min(
                        first.Bounds.max.y,
                        second.Bounds.max.y) - Mathf.Max(
                        first.Bounds.min.y,
                        second.Bounds.min.y);
                    if (overlapX <= overlapTolerance ||
                        overlapY <= overlapTolerance)
                    {
                        continue;
                    }

                    bool sameFlow =
                        VehicleSpacingMath.IsSameFlowDirection(
                            first.Direction,
                            second.Direction);
                    bool hasQueueSlots =
                        first.Snapshot.QueueSlot >= 0 &&
                        second.Snapshot.QueueSlot >= 0;
                    bool sameQueue =
                        hasQueueSlots &&
                        first.Tile == second.Tile &&
                        sameFlow;
                    float queueCenterSpacingTiles =
                        hasQueueSlots
                            ? Mathf.Abs(
                                first.Snapshot.QueueOffsetTiles -
                                second.Snapshot.QueueOffsetTiles)
                            : 0f;
                    float viewCenterSpacingTiles =
                        Mathf.Abs(Vector3.Dot(
                            second.Bounds.center -
                            first.Bounds.center,
                            first.Direction)) /
                        safeTileSize;
                    bool parkingTransition =
                        first.Vehicle.Settling ||
                        second.Vehicle.Settling ||
                        !hasQueueSlots;
                    bool bothParked =
                        (first.Snapshot.State is
                            CarState.ParkedHome or
                            CarState.ParkedWork) &&
                        (second.Snapshot.State is
                            CarState.ParkedHome or
                            CarState.ParkedWork);

                    string cause;
                    if (bothParked &&
                        (first.Vehicle.Settling ||
                         second.Vehicle.Settling))
                    {
                        cause = "parking settlement overlaps an already parked vehicle";
                    }
                    else if (bothParked)
                    {
                        cause = "building parking positions overlap";
                    }
                    else if (sameQueue &&
                        queueCenterSpacingTiles +
                            overlapTolerance / safeTileSize <
                        requiredCenterSpacingTiles)
                    {
                        cause = "simulation queue spacing is below the configured footprint";
                    }
                    else if (sameFlow && parkingTransition)
                    {
                        cause = "parking or queue-release transition overlaps an active vehicle";
                    }
                    else if (sameFlow &&
                             viewCenterSpacingTiles +
                                 overlapTolerance / safeTileSize <
                             requiredCenterSpacingTiles)
                    {
                        cause = "view spacing is below the configured footprint across a queue boundary";
                    }
                    else if (first.VisualLengthTiles >
                                 footprint.LengthTiles * 1.15f ||
                             second.VisualLengthTiles >
                                 footprint.LengthTiles * 1.15f)
                    {
                        cause = "rendered vehicle length exceeds its configured footprint";
                    }
                    else if (sameQueue)
                    {
                        cause = "view interpolation crossed a simulation-safe queue gap";
                    }
                    else if (first.Tile == second.Tile)
                    {
                        cause = "opposing or crossing lane visuals overlap on the same tile";
                    }
                    else
                    {
                        cause = "adjacent tile or turn presentation paths overlap";
                    }

                    diagnostic =
                        $"cause={cause}, vehicleAKind=Car, " +
                        $"vehicleAIndex={first.Index}, " +
                        $"vehicleAState={first.Snapshot.State}, " +
                        $"vehicleATile={first.Tile}, " +
                        $"vehicleASlot={first.Snapshot.QueueSlot}, " +
                        $"vehicleAOffset={first.Snapshot.QueueOffsetTiles:F3}, " +
                        $"vehicleASettling={first.Vehicle.Settling}, " +
                        $"vehicleAVisualLength={first.VisualLengthTiles:F2}, " +
                        $"vehicleBKind=Car, " +
                        $"vehicleBIndex={second.Index}, " +
                        $"vehicleBState={second.Snapshot.State}, " +
                        $"vehicleBTile={second.Tile}, " +
                        $"vehicleBSlot={second.Snapshot.QueueSlot}, " +
                        $"vehicleBOffset={second.Snapshot.QueueOffsetTiles:F3}, " +
                        $"vehicleBSettling={second.Vehicle.Settling}, " +
                        $"vehicleBVisualLength={second.VisualLengthTiles:F2}, " +
                        $"footprint={footprint.LengthTiles:F2}, " +
                        $"sameFlow={sameFlow}, sameQueue={sameQueue}, " +
                        $"requiredCenterSpacing={requiredCenterSpacingTiles:F3}, " +
                        $"queueCenterSpacing={queueCenterSpacingTiles:F3}, " +
                        $"viewCenterSpacing={viewCenterSpacingTiles:F3}, " +
                        $"planarOverlap=({overlapX / safeTileSize:F3}," +
                        $"{overlapY / safeTileSize:F3})";
                    return true;
                }
            }

            return false;
        }

        private bool TryGetDiagnosticVehicle(
            CommuteCar car,
            out RouteVehicle vehicle,
            out Bounds bounds)
        {
            bounds = default;
            return carVehicles.TryGetValue(car, out vehicle) &&
                   vehicle?.Object != null &&
                   vehicle.Object.activeInHierarchy &&
                   TryGetRendererBounds(
                       vehicle.Object,
                       transform,
                       out bounds);
        }

        private Vector3 ResolveDiagnosticDirection(
            RouteVehicle vehicle)
        {
            Vector3 direction = vehicle.Dir;
            direction.z = 0f;
            if (direction.sqrMagnitude > 0.0001f)
            {
                return direction.normalized;
            }

            direction = transform.InverseTransformDirection(
                vehicle.Object.transform.right);
            direction.z = 0f;
            return direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : Vector3.right;
        }

        internal bool TryBuildCommuteVehicleOverlapDiagnostic(
            Transform externalVisual,
            RoadTrafficSnapshot externalSnapshot,
            VehicleFootprint presentationFootprint,
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
                    GetRequiredVehiclePresentationHeadway(
                        externalSnapshot.Kind,
                        presentationFootprint,
                        RoadTrafficAgentKind.Car,
                        carFootprint) /
                    Mathf.Max(0.0001f, tileSize);
                float queueCenterSpacingTiles = Mathf.Abs(
                    externalSnapshot.QueueOffsetTiles -
                    carSnapshot.QueueOffsetTiles);
                float externalVisualLength = ProjectRendererLength(
                    externalVisual.gameObject,
                    transform,
                    externalDirection);
                float carVisualLength = ProjectRendererLength(
                    vehicle.Object,
                    transform,
                    carDirection);
                float externalConfiguredLength =
                    presentationFootprint.LengthTiles * tileSize;
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
                    $"busFootprint={presentationFootprint.LengthTiles:F2}, " +
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

        private static float ProjectRendererLength(
            GameObject target,
            Transform reference,
            Vector3 direction)
        {
            if (target == null || reference == null)
            {
                return 0f;
            }

            Vector3 normalized = direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : Vector3.right;
            float minimum = float.PositiveInfinity;
            float maximum = float.NegativeInfinity;
            Renderer[] renderers =
                target.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                Renderer targetRenderer = renderers[rendererIndex];
                Bounds bounds = targetRenderer.localBounds;
                for (int x = -1; x <= 1; x += 2)
                {
                    for (int y = -1; y <= 1; y += 2)
                    {
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 corner = bounds.center +
                                Vector3.Scale(
                                    bounds.extents,
                                    new Vector3(x, y, z));
                            Vector3 referencePoint =
                                reference.InverseTransformPoint(
                                    targetRenderer.transform.TransformPoint(
                                        corner));
                            float projection =
                                Vector3.Dot(referencePoint, normalized);
                            minimum = Mathf.Min(minimum, projection);
                            maximum = Mathf.Max(maximum, projection);
                        }
                    }
                }
            }

            return float.IsInfinity(minimum)
                ? 0f
                : Mathf.Max(0f, maximum - minimum);
        }

        // Unity setup: no component is required; BusWorldView invokes this diagnostic automatically.
    }
}
