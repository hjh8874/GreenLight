using System.Collections.Generic;
using CityFlow.Contracts;
using CityFlow.ViewKit;
using UnityEngine;

namespace CityFlow.View
{
    public readonly struct VehiclePresentationLeader
    {
        public VehiclePresentationLeader(
            object owner,
            RoadTrafficAgentKind kind,
            float headway,
            float requiredHeadway,
            float speed)
        {
            Owner = owner;
            Kind = kind;
            Headway = Mathf.Max(0f, headway);
            RequiredHeadway = Mathf.Max(0f, requiredHeadway);
            Speed = Mathf.Max(0f, speed);
        }

        public object Owner { get; }
        public RoadTrafficAgentKind Kind { get; }
        public float Headway { get; }
        public float RequiredHeadway { get; }
        public float Speed { get; }
    }

    public sealed class VehiclePresentationSpacingCoordinator
    {
        private sealed class Entry
        {
            public object Owner { get; set; }
            public RoadTrafficAgentKind Kind { get; set; }
            public VehicleFootprint Footprint { get; set; }
            public Vector3 Position { get; set; }
            public Vector3 Direction { get; set; }
            public float Speed { get; set; }
            public int UpdatedFrame { get; set; }
        }

        private readonly Dictionary<object, Entry> entries = new();
        private readonly List<object> staleOwners = new();
        private VehiclePresentationSpacingProfileSO profile;

        public VehiclePresentationSpacingCoordinator(
            VehiclePresentationSpacingProfileSO profile = null)
        {
            this.profile = profile;
        }

        public int RegisteredVehicleCount => entries.Count;

        public void SetProfile(
            VehiclePresentationSpacingProfileSO nextProfile)
        {
            profile = nextProfile;
        }

        public void Publish(
            object owner,
            RoadTrafficAgentKind kind,
            VehicleFootprint footprint,
            Vector3 position,
            Vector3 direction,
            float speed,
            int frame)
        {
            if (IsOwnerMissing(owner) ||
                direction.sqrMagnitude <= 0.0001f)
            {
                Remove(owner);
                return;
            }

            if (!entries.TryGetValue(owner, out Entry entry))
            {
                entry = new Entry { Owner = owner };
                entries.Add(owner, entry);
            }

            direction.z = 0f;
            entry.Kind = kind;
            entry.Footprint = footprint;
            entry.Position = position;
            entry.Direction = direction.normalized;
            entry.Speed = Mathf.Max(0f, speed);
            entry.UpdatedFrame = frame;
        }

        public void Remove(object owner)
        {
            if (owner != null)
            {
                entries.Remove(owner);
            }
        }

        public bool TryGetLeader(
            object subjectOwner,
            RoadTrafficAgentKind subjectKind,
            VehicleFootprint subjectFootprint,
            Vector3 subjectPosition,
            Vector3 subjectDirection,
            float tileSize,
            int frame,
            bool includeCarCandidates,
            out VehiclePresentationLeader leader)
        {
            return TryGetLeader(
                subjectOwner,
                subjectKind,
                subjectFootprint,
                subjectPosition,
                subjectDirection,
                tileSize,
                frame,
                includeCarCandidates,
                useConvergingCarEnvelope: false,
                out leader);
        }

        public bool TryGetLeader(
            object subjectOwner,
            RoadTrafficAgentKind subjectKind,
            VehicleFootprint subjectFootprint,
            Vector3 subjectPosition,
            Vector3 subjectDirection,
            float tileSize,
            int frame,
            bool includeCarCandidates,
            bool useConvergingCarEnvelope,
            out VehiclePresentationLeader leader)
        {
            leader = default;
            PurgeStale(frame);

            subjectDirection.z = 0f;
            if (subjectDirection.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            float safeTileSize = Mathf.Max(0.0001f, tileSize);
            Vector3 forward = subjectDirection.normalized;
            float laneTolerance = ResolveLaneToleranceTiles() *
                                  safeTileSize;
            float heightTolerance = ResolveHeightToleranceTiles() *
                                    safeTileSize;
            float nearestHeadway = float.PositiveInfinity;
            Entry nearest = null;
            float nearestRequiredHeadway = 0f;

            foreach (Entry candidate in entries.Values)
            {
                bool convergingCarPair =
                    useConvergingCarEnvelope &&
                    subjectKind == RoadTrafficAgentKind.Car &&
                    candidate.Kind == RoadTrafficAgentKind.Car;
                float minimumDirectionDot = convergingCarPair
                    ? Mathf.Min(ResolveMinimumDirectionDot(), 0.5f)
                    : ResolveMinimumDirectionDot();
                if (ReferenceEquals(candidate.Owner, subjectOwner) ||
                    (!includeCarCandidates &&
                     candidate.Kind == RoadTrafficAgentKind.Car) ||
                    !VehicleSpacingMath.IsSameFlowDirection(
                        forward,
                        candidate.Direction,
                        minimumDirectionDot))
                {
                    continue;
                }

                Vector3 comparisonForward = forward;
                if (convergingCarPair)
                {
                    // 합류 곡선에서는 각 차량의 접선으로 상대를 투영하면 두 차가 서로를
                    // 동시에 앞차로 볼 수 있다. 승용차 쌍은 대칭인 평균 진행축을 공유해
                    // 한 쌍에서 오직 뒤쪽 차량만 양보하게 한다.
                    Vector3 sharedFlowAxis =
                        forward + candidate.Direction;
                    if (sharedFlowAxis.sqrMagnitude > 0.0001f)
                    {
                        comparisonForward = sharedFlowAxis.normalized;
                    }
                }

                Vector3 separation = candidate.Position - subjectPosition;
                float headway = Vector3.Dot(
                    separation,
                    comparisonForward);
                if (headway <= 0f ||
                    Mathf.Abs(separation.z) > heightTolerance)
                {
                    continue;
                }

                Vector3 lateralAxis = new(
                    -comparisonForward.y,
                    comparisonForward.x,
                    0f);
                float lateralDistance = Mathf.Abs(Vector3.Dot(
                    separation,
                    lateralAxis));
                float laneHalfWidth = convergingCarPair
                    ? ProjectFootprintHalfExtent(
                          subjectFootprint,
                          forward,
                          lateralAxis,
                          safeTileSize) +
                      ProjectFootprintHalfExtent(
                          candidate.Footprint,
                          candidate.Direction,
                          lateralAxis,
                          safeTileSize) +
                      laneTolerance
                    : (subjectFootprint.WidthTiles +
                       candidate.Footprint.WidthTiles) * 0.5f *
                      safeTileSize + laneTolerance;
                if (lateralDistance > laneHalfWidth ||
                    headway >= nearestHeadway)
                {
                    continue;
                }

                nearestHeadway = headway;
                nearest = candidate;
                nearestRequiredHeadway = CalculateRequiredHeadway(
                    subjectKind,
                    subjectFootprint,
                    candidate.Kind,
                    candidate.Footprint,
                    safeTileSize);
            }

            if (nearest == null)
            {
                return false;
            }

            leader = new VehiclePresentationLeader(
                nearest.Owner,
                nearest.Kind,
                nearestHeadway,
                nearestRequiredHeadway,
                nearest.Speed);
            return true;
        }

        private static float ProjectFootprintHalfExtent(
            VehicleFootprint footprint,
            Vector3 vehicleForward,
            Vector3 projectionAxis,
            float tileSize)
        {
            Vector3 forward = vehicleForward.normalized;
            Vector3 lateral = new(-forward.y, forward.x, 0f);
            return 0.5f * tileSize * (
                footprint.LengthTiles *
                Mathf.Abs(Vector3.Dot(forward, projectionAxis)) +
                footprint.WidthTiles *
                Mathf.Abs(Vector3.Dot(lateral, projectionAxis)));
        }

        public float LimitAdvance(
            float proposedAdvance,
            in VehiclePresentationLeader leader) =>
            VehicleSpacingMath.LimitAdvance(
                proposedAdvance,
                leader.Headway,
                leader.RequiredHeadway);

        public float CalculateMaximumCatchUpAdvance(
            float nominalSpeed,
            float deltaTime) =>
            Mathf.Max(0f, nominalSpeed) *
            Mathf.Max(0f, deltaTime) *
            ResolveMaximumCatchUpSpeedMultiplier();

        public bool TryGetCrossFlowCarBlocker(
            object subjectOwner,
            RoadTrafficAgentKind subjectKind,
            VehicleFootprint subjectFootprint,
            Vector3 subjectPosition,
            Vector3 subjectDirection,
            float tileSize,
            int frame,
            out VehiclePresentationLeader blocker)
        {
            blocker = default;
            PurgeStale(frame);

            subjectDirection.z = 0f;
            if (subjectDirection.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            float safeTileSize = Mathf.Max(0.0001f, tileSize);
            Vector3 forward = subjectDirection.normalized;
            Vector3 lateralAxis = new(-forward.y, forward.x, 0f);
            float lateralTolerance = ResolveLaneToleranceTiles() *
                                     safeTileSize;
            float nearestDistanceSquared = float.PositiveInfinity;
            Entry nearest = null;
            float nearestLongitudinalDistance = 0f;
            float nearestRequiredHeadway = 0f;

            foreach (Entry candidate in entries.Values)
            {
                if (ReferenceEquals(candidate.Owner, subjectOwner) ||
                    candidate.Kind != RoadTrafficAgentKind.Car ||
                    VehicleSpacingMath.IsSameFlowDirection(
                        forward,
                        candidate.Direction,
                        ResolveMinimumDirectionDot()))
                {
                    continue;
                }

                Vector3 separation =
                    candidate.Position - subjectPosition;
                if (Mathf.Abs(separation.z) >
                    ResolveHeightToleranceTiles() * safeTileSize)
                {
                    continue;
                }

                float longitudinalDistance =
                    Mathf.Abs(Vector3.Dot(separation, forward));
                float lateralDistance =
                    Mathf.Abs(Vector3.Dot(separation, lateralAxis));
                float requiredLongitudinalDistance =
                    CalculateRequiredHeadway(
                        subjectKind,
                        subjectFootprint,
                        candidate.Kind,
                        candidate.Footprint,
                        safeTileSize);
                float requiredLateralDistance =
                    (subjectFootprint.WidthTiles +
                     candidate.Footprint.WidthTiles) * 0.5f *
                    safeTileSize + lateralTolerance;
                if (longitudinalDistance >=
                        requiredLongitudinalDistance ||
                    lateralDistance >= requiredLateralDistance)
                {
                    continue;
                }

                float distanceSquared = separation.sqrMagnitude;
                if (distanceSquared >= nearestDistanceSquared)
                {
                    continue;
                }

                nearestDistanceSquared = distanceSquared;
                nearest = candidate;
                nearestLongitudinalDistance =
                    longitudinalDistance;
                nearestRequiredHeadway =
                    requiredLongitudinalDistance;
            }

            if (nearest == null)
            {
                return false;
            }

            blocker = new VehiclePresentationLeader(
                nearest.Owner,
                nearest.Kind,
                nearestLongitudinalDistance,
                nearestRequiredHeadway,
                nearest.Speed);
            return true;
        }

        public float CalculateRequiredHeadway(
            RoadTrafficAgentKind subjectKind,
            VehicleFootprint subjectFootprint,
            RoadTrafficAgentKind leaderKind,
            VehicleFootprint leaderFootprint,
            float tileSize)
        {
            float extraGapTiles = Mathf.Max(
                ResolveExtraGapTiles(subjectKind, subjectFootprint),
                ResolveExtraGapTiles(leaderKind, leaderFootprint));
            return (
                subjectFootprint.LengthTiles * 0.5f +
                Mathf.Max(
                    subjectFootprint.MinimumGapTiles,
                    leaderFootprint.MinimumGapTiles) +
                extraGapTiles +
                leaderFootprint.LengthTiles * 0.5f) *
                Mathf.Max(0.0001f, tileSize);
        }

        private void PurgeStale(int frame)
        {
            staleOwners.Clear();
            int oldestFrame = frame - ResolveStaleFrameTolerance();
            foreach (KeyValuePair<object, Entry> pair in entries)
            {
                if (IsOwnerMissing(pair.Key) ||
                    pair.Value.UpdatedFrame < oldestFrame)
                {
                    staleOwners.Add(pair.Key);
                }
            }

            for (int index = 0; index < staleOwners.Count; index++)
            {
                entries.Remove(staleOwners[index]);
            }
        }

        private float ResolveExtraGapTiles(
            RoadTrafficAgentKind kind,
            VehicleFootprint footprint) =>
            profile != null
                ? profile.GetExtraGapTiles(kind, footprint.SizeClass)
                : kind == RoadTrafficAgentKind.FeatureVehicle
                    ? VehiclePresentationSpacingProfileSO
                        .DefaultEmergencyExtraGapTiles
                    : kind == RoadTrafficAgentKind.CityBus ||
                      kind == RoadTrafficAgentKind.SchoolBus ||
                      footprint.SizeClass == VehicleSizeClass.Large
                        ? VehiclePresentationSpacingProfileSO
                            .DefaultLargeExtraGapTiles
                        : VehiclePresentationSpacingProfileSO
                            .DefaultStandardExtraGapTiles;

        private float ResolveLaneToleranceTiles() =>
            profile != null
                ? profile.LaneToleranceTiles
                : VehiclePresentationSpacingProfileSO
                    .DefaultLaneToleranceTiles;

        private float ResolveHeightToleranceTiles() =>
            profile != null
                ? profile.HeightToleranceTiles
                : VehiclePresentationSpacingProfileSO
                    .DefaultHeightToleranceTiles;

        private float ResolveMinimumDirectionDot() =>
            profile != null
                ? profile.MinimumDirectionDot
                : VehiclePresentationSpacingProfileSO
                    .DefaultMinimumDirectionDot;

        private float ResolveMaximumCatchUpSpeedMultiplier() =>
            profile != null
                ? profile.MaximumCatchUpSpeedMultiplier
                : VehiclePresentationSpacingProfileSO
                    .DefaultMaximumCatchUpSpeedMultiplier;

        private int ResolveStaleFrameTolerance() =>
            profile != null
                ? profile.StaleFrameTolerance
                : VehiclePresentationSpacingProfileSO
                    .DefaultStaleFrameTolerance;

        private static bool IsOwnerMissing(object owner) =>
            owner == null ||
            owner is Object unityOwner && unityOwner == null;

        // Unity setup: MainCityView owns this coordinator; no scene component is required.
    }
}
