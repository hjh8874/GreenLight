using CityFlow.Contracts;
using CityFlow.ViewKit;
using UnityEngine;

namespace CityFlow.View
{
    public sealed partial class MainCityView
    {
        private const string VehicleSpacingProfileResourcePath =
            "CityFlow/VehiclePresentationSpacingProfile";

        [SerializeField]
        private VehiclePresentationSpacingProfileSO
            vehiclePresentationSpacingProfile;

        private VehiclePresentationSpacingCoordinator
            vehiclePresentationSpacingCoordinator;
        private bool vehiclePresentationSpacingProfileResolved;

        internal void PublishVehiclePresentation(
            object owner,
            RoadTrafficAgentKind kind,
            VehicleFootprint footprint,
            Vector3 localPosition,
            Vector3 localDirection,
            float speed)
        {
            ResolveVehiclePresentationSpacingCoordinator().Publish(
                owner,
                kind,
                footprint,
                localPosition,
                localDirection,
                speed,
                Time.frameCount);
        }

        internal void RemoveVehiclePresentation(object owner)
        {
            vehiclePresentationSpacingCoordinator?.Remove(owner);
        }

        internal bool TryGetVehiclePresentationLeader(
            object owner,
            RoadTrafficAgentKind kind,
            VehicleFootprint footprint,
            Vector3 localPosition,
            Vector3 localDirection,
            bool includeCarCandidates,
            out VehiclePresentationLeader leader) =>
            TryGetVehiclePresentationLeader(
                owner,
                kind,
                footprint,
                localPosition,
                localDirection,
                includeCarCandidates,
                useConvergingCarEnvelope: false,
                out leader);

        internal bool TryGetVehiclePresentationLeader(
            object owner,
            RoadTrafficAgentKind kind,
            VehicleFootprint footprint,
            Vector3 localPosition,
            Vector3 localDirection,
            bool includeCarCandidates,
            bool useConvergingCarEnvelope,
            out VehiclePresentationLeader leader) =>
            ResolveVehiclePresentationSpacingCoordinator().TryGetLeader(
                owner,
                kind,
                footprint,
                localPosition,
                localDirection,
                tileSize,
                Time.frameCount,
                includeCarCandidates,
                useConvergingCarEnvelope,
                out leader);

        internal float GetRequiredVehiclePresentationHeadway(
            RoadTrafficAgentKind subjectKind,
            VehicleFootprint subjectFootprint,
            RoadTrafficAgentKind leaderKind,
            VehicleFootprint leaderFootprint) =>
            ResolveVehiclePresentationSpacingCoordinator()
                .CalculateRequiredHeadway(
                    subjectKind,
                    subjectFootprint,
                    leaderKind,
                    leaderFootprint,
                    tileSize);

        internal float LimitVehiclePresentationCatchUp(
            float currentDistance,
            float proposedDistance,
            float nominalSpeed,
            float deltaTime)
        {
            if (proposedDistance <= currentDistance + 0.0001f)
            {
                return Mathf.Max(currentDistance, proposedDistance);
            }

            float maximumAdvance =
                ResolveVehiclePresentationSpacingCoordinator()
                    .CalculateMaximumCatchUpAdvance(
                        nominalSpeed,
                        deltaTime);
            return currentDistance + Mathf.Min(
                proposedDistance - currentDistance,
                maximumAdvance);
        }

        internal float LimitVehiclePresentationAdvance(
            object owner,
            RoadTrafficAgentKind kind,
            VehicleFootprint footprint,
            RoutePolyline polyline,
            float currentDistance,
            float proposedDistance,
            bool yieldToCrossFlowCars,
            out VehiclePresentationLeader leader)
        {
            leader = default;
            if (polyline == null ||
                proposedDistance <= currentDistance + 0.0001f)
            {
                return Mathf.Max(currentDistance, proposedDistance);
            }

            Sample sample = polyline.SampleAt(currentDistance);
            VehiclePresentationSpacingCoordinator coordinator =
                ResolveVehiclePresentationSpacingCoordinator();
            float limitedDistance = proposedDistance;
            if (coordinator.TryGetLeader(
                    owner,
                    kind,
                    footprint,
                    sample.Pos,
                    sample.Dir,
                    tileSize,
                    Time.frameCount,
                    includeCarCandidates: true,
                    out leader))
            {
                float proposedAdvance =
                    proposedDistance - currentDistance;
                float allowedAdvance = coordinator.LimitAdvance(
                    proposedAdvance,
                    leader);
                limitedDistance = currentDistance + allowedAdvance;
            }

            if (!yieldToCrossFlowCars ||
                limitedDistance <= currentDistance + 0.0001f)
            {
                return limitedDistance;
            }

            float middleDistance =
                Mathf.Lerp(
                    currentDistance,
                    limitedDistance,
                    0.5f);
            Sample middleSample =
                polyline.SampleAt(middleDistance);
            Sample proposedSample =
                polyline.SampleAt(limitedDistance);
            if (coordinator.TryGetCrossFlowCarBlocker(
                    owner,
                    kind,
                    footprint,
                    sample.Pos,
                    sample.Dir,
                    tileSize,
                    Time.frameCount,
                    out VehiclePresentationLeader blocker) ||
                coordinator.TryGetCrossFlowCarBlocker(
                    owner,
                    kind,
                    footprint,
                    middleSample.Pos,
                    middleSample.Dir,
                    tileSize,
                    Time.frameCount,
                    out blocker) ||
                coordinator.TryGetCrossFlowCarBlocker(
                    owner,
                    kind,
                    footprint,
                    proposedSample.Pos,
                    proposedSample.Dir,
                    tileSize,
                    Time.frameCount,
                    out blocker))
            {
                leader = blocker;
                return currentDistance;
            }

            return limitedDistance;
        }

        private VehiclePresentationSpacingCoordinator
            ResolveVehiclePresentationSpacingCoordinator()
        {
            if (!vehiclePresentationSpacingProfileResolved)
            {
                vehiclePresentationSpacingProfileResolved = true;
                vehiclePresentationSpacingProfile ??=
                    Resources.Load<VehiclePresentationSpacingProfileSO>(
                        VehicleSpacingProfileResourcePath);
            }

            if (vehiclePresentationSpacingCoordinator == null)
            {
                vehiclePresentationSpacingCoordinator =
                    new VehiclePresentationSpacingCoordinator(
                        vehiclePresentationSpacingProfile);
            }
            else
            {
                vehiclePresentationSpacingCoordinator.SetProfile(
                    vehiclePresentationSpacingProfile);
            }

            return vehiclePresentationSpacingCoordinator;
        }

        // Unity setup: the Resources profile is loaded automatically by MainCityView.
    }
}
