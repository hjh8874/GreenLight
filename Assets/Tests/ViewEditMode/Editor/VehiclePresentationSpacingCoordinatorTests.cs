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
