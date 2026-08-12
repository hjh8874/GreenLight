using CityFlow.Contracts;
using CityFlow.View;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Sim.Tests
{
    public sealed class VehiclePresentationSpacingCoordinatorTests
    {
        private static readonly VehicleFootprint Standard =
            VehicleFootprint.StandardDefault;

        private static readonly VehicleFootprint Large = new(
            VehicleSizeClass.Large,
            0.8f,
            0.28f,
            0.11f);

        [Test]
        public void RequiredHeadway_UsesBothLengthsAndLargeVehicleMargin()
        {
            var coordinator =
                new VehiclePresentationSpacingCoordinator();

            float headway = coordinator.CalculateRequiredHeadway(
                RoadTrafficAgentKind.Car,
                Standard,
                RoadTrafficAgentKind.CityBus,
                Large,
                tileSize: 1f);

            Assert.AreEqual(0.81f, headway, 0.0001f);
        }

        [Test]
        public void TryGetLeader_CanExcludePathAwarePassengerCars()
        {
            var coordinator =
                new VehiclePresentationSpacingCoordinator();
            object subject = new();
            object passengerCar = new();
            coordinator.Publish(
                passengerCar,
                RoadTrafficAgentKind.Car,
                Standard,
                new Vector3(0.7f, 0f, 0f),
                Vector3.right,
                speed: 0f,
                frame: 10);

            bool found = coordinator.TryGetLeader(
                subject,
                RoadTrafficAgentKind.Car,
                Standard,
                Vector3.zero,
                Vector3.right,
                tileSize: 1f,
                frame: 10,
                includeCarCandidates: false,
                out _);

            Assert.IsFalse(found);
        }

        [Test]
        public void LimitAdvance_StopsLargeVehicleBeforePassengerCar()
        {
            var coordinator =
                new VehiclePresentationSpacingCoordinator();
            object bus = new();
            object passengerCar = new();
            coordinator.Publish(
                passengerCar,
                RoadTrafficAgentKind.Car,
                Standard,
                new Vector3(0.7f, 0f, 0f),
                Vector3.right,
                speed: 0f,
                frame: 20);

            Assert.IsTrue(
                coordinator.TryGetLeader(
                    bus,
                    RoadTrafficAgentKind.CityBus,
                    Large,
                    Vector3.zero,
                    Vector3.right,
                    tileSize: 1f,
                    frame: 20,
                    includeCarCandidates: true,
                    out VehiclePresentationLeader leader));

            float allowed = coordinator.LimitAdvance(
                proposedAdvance: 0.2f,
                leader);

            Assert.AreEqual(0f, allowed, 0.0001f);
        }

        [Test]
        public void TryGetLeader_IgnoresOppositeTrafficFlow()
        {
            var coordinator =
                new VehiclePresentationSpacingCoordinator();
            coordinator.Publish(
                new object(),
                RoadTrafficAgentKind.SchoolBus,
                Large,
                new Vector3(0.6f, 0f, 0f),
                Vector3.left,
                speed: 0f,
                frame: 30);

            bool found = coordinator.TryGetLeader(
                new object(),
                RoadTrafficAgentKind.Car,
                Standard,
                Vector3.zero,
                Vector3.right,
                tileSize: 1f,
                frame: 30,
                includeCarCandidates: true,
                out _);

            Assert.IsFalse(found);
        }

        [Test]
        public void TryGetLeader_ConvergingCarsCannotMutuallyBlock()
        {
            var coordinator =
                new VehiclePresentationSpacingCoordinator();
            object first = new();
            object second = new();
            Vector3 firstDirection = Quaternion.Euler(0f, 0f, 20f) *
                                     Vector3.right;
            Vector3 secondDirection = Quaternion.Euler(0f, 0f, -20f) *
                                      Vector3.right;
            Vector3 firstPosition = Vector3.zero;
            Vector3 secondPosition = new(0.02f, 0.2f, 0f);

            coordinator.Publish(
                first,
                RoadTrafficAgentKind.Car,
                Standard,
                firstPosition,
                firstDirection,
                speed: 0f,
                frame: 35);
            coordinator.Publish(
                second,
                RoadTrafficAgentKind.Car,
                Standard,
                secondPosition,
                secondDirection,
                speed: 0f,
                frame: 35);

            bool firstSeesSecond = coordinator.TryGetLeader(
                first,
                RoadTrafficAgentKind.Car,
                Standard,
                firstPosition,
                firstDirection,
                tileSize: 1f,
                frame: 35,
                includeCarCandidates: true,
                useConvergingCarEnvelope: true,
                out _);
            bool secondSeesFirst = coordinator.TryGetLeader(
                second,
                RoadTrafficAgentKind.Car,
                Standard,
                secondPosition,
                secondDirection,
                tileSize: 1f,
                frame: 35,
                includeCarCandidates: true,
                useConvergingCarEnvelope: true,
                out _);

            Assert.IsTrue(firstSeesSecond);
            Assert.IsFalse(secondSeesFirst,
                "A converging car pair must not classify each other as leaders.");
        }

        [Test]
        public void TryGetLeader_ConvergingCarsUseProjectedFootprintEnvelope()
        {
            var coordinator =
                new VehiclePresentationSpacingCoordinator();
            object follower = new();
            object leader = new();
            Vector3 followerDirection = Quaternion.Euler(0f, 0f, 28f) *
                                        Vector3.right;
            Vector3 leaderDirection = Quaternion.Euler(0f, 0f, -28f) *
                                      Vector3.right;
            Vector3 followerPosition = Vector3.zero;
            Vector3 leaderPosition = new(0.05f, 0.3f, 0f);

            coordinator.Publish(
                follower,
                RoadTrafficAgentKind.Car,
                Standard,
                followerPosition,
                followerDirection,
                speed: 0f,
                frame: 36);
            coordinator.Publish(
                leader,
                RoadTrafficAgentKind.Car,
                Standard,
                leaderPosition,
                leaderDirection,
                speed: 0f,
                frame: 36);

            Assert.IsTrue(coordinator.TryGetLeader(
                follower,
                RoadTrafficAgentKind.Car,
                Standard,
                followerPosition,
                followerDirection,
                tileSize: 1f,
                frame: 36,
                includeCarCandidates: true,
                useConvergingCarEnvelope: true,
                out VehiclePresentationLeader found));
            Assert.AreSame(leader, found.Owner);
            Assert.IsFalse(coordinator.TryGetLeader(
                leader,
                RoadTrafficAgentKind.Car,
                Standard,
                leaderPosition,
                leaderDirection,
                tileSize: 1f,
                frame: 36,
                includeCarCandidates: true,
                useConvergingCarEnvelope: true,
                out _));
        }

        [Test]
        public void TryGetLeader_ParallelAdjacentLaneKeepsNormalEnvelope()
        {
            var coordinator =
                new VehiclePresentationSpacingCoordinator();
            object subject = new();
            object adjacent = new();
            coordinator.Publish(
                adjacent,
                RoadTrafficAgentKind.Car,
                Standard,
                new Vector3(0.4f, 0.23f, 0f),
                Vector3.right,
                speed: 0f,
                frame: 37);

            Assert.IsFalse(coordinator.TryGetLeader(
                subject,
                RoadTrafficAgentKind.Car,
                Standard,
                Vector3.zero,
                Vector3.right,
                tileSize: 1f,
                frame: 37,
                includeCarCandidates: true,
                useConvergingCarEnvelope: true,
                out _));
        }

        [Test]
        public void TryGetLeader_ConvergingCarsOutsideProjectedEnvelopeAreIgnored()
        {
            var coordinator =
                new VehiclePresentationSpacingCoordinator();
            object subject = new();
            object outside = new();
            Vector3 subjectDirection = Quaternion.Euler(0f, 0f, 20f) *
                                       Vector3.right;
            Vector3 outsideDirection = Quaternion.Euler(0f, 0f, -20f) *
                                       Vector3.right;
            coordinator.Publish(
                outside,
                RoadTrafficAgentKind.Car,
                Standard,
                new Vector3(0.05f, 0.38f, 0f),
                outsideDirection,
                speed: 0f,
                frame: 38);

            Assert.IsFalse(coordinator.TryGetLeader(
                subject,
                RoadTrafficAgentKind.Car,
                Standard,
                Vector3.zero,
                subjectDirection,
                tileSize: 1f,
                frame: 38,
                includeCarCandidates: true,
                useConvergingCarEnvelope: true,
                out _));
        }

        [Test]
        public void TryGetCrossFlowCarBlocker_BlocksLargeVehicleAtCorner()
        {
            var coordinator =
                new VehiclePresentationSpacingCoordinator();
            coordinator.Publish(
                new object(),
                RoadTrafficAgentKind.Car,
                Standard,
                new Vector3(0.3f, 0.1f, 0f),
                Vector3.up,
                speed: 0f,
                frame: 40);

            bool found = coordinator.TryGetCrossFlowCarBlocker(
                new object(),
                RoadTrafficAgentKind.CityBus,
                Large,
                Vector3.zero,
                Vector3.right,
                tileSize: 1f,
                frame: 40,
                out VehiclePresentationLeader blocker);

            Assert.IsTrue(found);
            Assert.AreEqual(
                RoadTrafficAgentKind.Car,
                blocker.Kind);
        }

        [Test]
        public void CalculateMaximumCatchUpAdvance_UsesConfiguredDefaultLimit()
        {
            var coordinator =
                new VehiclePresentationSpacingCoordinator();

            float advance =
                coordinator.CalculateMaximumCatchUpAdvance(
                    nominalSpeed: 2f,
                    deltaTime: 0.1f);

            Assert.AreEqual(0.23f, advance, 0.0001f);
        }
    }
}
