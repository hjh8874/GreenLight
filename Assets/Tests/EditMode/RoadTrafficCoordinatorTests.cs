using CityFlow.Contracts;
using CityFlow.Sim;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Tests.EditMode
{
    public sealed class RoadTrafficCoordinatorTests
    {
        private sealed class EmptyRouteProvider : ICarRouteProvider
        {
            public bool TryGetNextTile(
                int carId,
                Vector2Int current,
                out Vector2Int next,
                out Dir entryDirAtNext)
            {
                next = default;
                entryDirAtNext = default;
                return false;
            }

            public bool IsDestination(int carId, Vector2Int tile) =>
                false;
        }

        [Test]
        public void FootprintProfiles_MapToExpectedQueueUnits()
        {
            var standard = new VehicleFootprint(
                VehicleSizeClass.Standard,
                0.44f,
                0.2f,
                0.11f);
            var large = new VehicleFootprint(
                VehicleSizeClass.Large,
                0.8f,
                0.24f,
                0.11f);

            Assert.AreEqual(
                1,
                RoadQueueNetwork.CalculateOccupancyUnits(standard));
            Assert.AreEqual(
                2,
                RoadQueueNetwork.CalculateOccupancyUnits(large));
        }

        [Test]
        public void StandardQueue_UsesPhysicalCenterDistance()
        {
            SimConfig config = SimConfig.Default();
            config.GridWidth = 1;
            config.GridHeight = 1;
            config.QueueCapacityPerTile = 4;
            var network = new RoadQueueNetwork(
                1,
                1,
                config,
                VehicleFootprint.StandardDefault);

            Assert.IsTrue(network.TryEnqueue(
                Vector2Int.zero,
                Dir.E,
                10));
            Assert.IsTrue(network.TryEnqueue(
                Vector2Int.zero,
                Dir.E,
                11));
            Assert.IsTrue(network.TryLocateCar(
                11,
                out _,
                out _,
                out int queueSlot,
                out _,
                out _,
                out _,
                out float queueOffsetTiles));

            Assert.AreEqual(1, queueSlot);
            Assert.AreEqual(
                VehicleFootprint.StandardDefault.HeadwayTiles,
                queueOffsetTiles,
                0.0001f);
        }

        [Test]
        public void MixedQueue_UsesBothLengthsAndLargestSafetyGap()
        {
            SimConfig config = SimConfig.Default();
            config.GridWidth = 1;
            config.GridHeight = 1;
            config.QueueCapacityPerTile = 4;
            var compact = new VehicleFootprint(
                VehicleSizeClass.Standard,
                0.2f,
                0.1f,
                0.05f);
            var longVehicle = new VehicleFootprint(
                VehicleSizeClass.Large,
                0.3f,
                0.12f,
                0.1f);
            var network = new RoadQueueNetwork(
                1,
                1,
                config,
                compact);

            Assert.IsTrue(network.TryEnqueue(
                Vector2Int.zero,
                Dir.E,
                20,
                compact));
            Assert.IsTrue(network.TryEnqueue(
                Vector2Int.zero,
                Dir.E,
                21,
                longVehicle));
            Assert.IsTrue(network.TryLocateCar(
                21,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _,
                out float queueOffsetTiles));

            Assert.AreEqual(0.35f, queueOffsetTiles, 0.0001f);
        }

        [Test]
        public void LargeVehicleFootprint_ConsumesBothDirectionalQueueUnits()
        {
            SimConfig config = SimConfig.Default();
            config.GridWidth = 3;
            config.GridHeight = 1;
            config.QueueCapacityPerTile = 4;
            var network = new RoadQueueNetwork(3, 1, config);
            var coordinator = new RoadTrafficCoordinator(network);

            Assert.IsTrue(coordinator.TryRegisterAgent(
                new RoadTrafficAgentRegistration(
                    RoadTrafficAgentKind.CityBus,
                    new VehicleFootprint(
                        VehicleSizeClass.Large,
                        0.8f,
                        0.24f,
                        0.11f)),
                out RoadTrafficAgentId busId));
            Assert.IsTrue(coordinator.TryAssignRoute(
                new RoadTrafficRouteRequest(
                    busId,
                    new RoadRoutePlan(new[]
                    {
                        new Vector2Int(0, 0),
                        new Vector2Int(1, 0)
                    }),
                    false)));
            Assert.IsTrue(coordinator.TryStartAgent(busId));

            coordinator.PrepareStep(new EmptyRouteProvider());
            coordinator.SynchronizeSnapshots();

            Assert.AreEqual(1, network.QueueCount(
                new Vector2Int(0, 0),
                Dir.E));
            Assert.AreEqual(2, network.QueueOccupancyUnits(
                new Vector2Int(0, 0),
                Dir.E));
            Assert.IsTrue(coordinator.TryGetSnapshot(
                busId,
                out RoadTrafficSnapshot snapshot));
            Assert.AreEqual(
                VehicleSizeClass.Large,
                snapshot.Footprint.SizeClass);
            Assert.AreEqual(0.8f, snapshot.Footprint.LengthTiles);
            Assert.AreEqual(0f, snapshot.QueueOffsetTiles);
            Assert.AreEqual(-1f, snapshot.IntersectionProgress01);
            Assert.AreEqual(-1f, snapshot.RoundaboutProgress01);
            Assert.IsFalse(network.TryEnqueue(
                new Vector2Int(0, 0),
                Dir.E,
                42));
        }

        [Test]
        public void PauseOnEntry_ReservesSchoolBusFootprintUntilReleased()
        {
            SimConfig config = SimConfig.Default();
            config.GridWidth = 3;
            config.GridHeight = 1;
            config.QueueCapacityPerTile = 4;
            var network = new RoadQueueNetwork(3, 1, config);
            var coordinator = new RoadTrafficCoordinator(network);

            Assert.IsTrue(coordinator.TryRegisterAgent(
                new RoadTrafficAgentRegistration(
                    RoadTrafficAgentKind.SchoolBus,
                    new VehicleFootprint(
                        VehicleSizeClass.Large,
                        0.8f,
                        0.24f,
                        0.11f)),
                out RoadTrafficAgentId busId));
            Assert.IsTrue(coordinator.TryAssignRoute(
                new RoadTrafficRouteRequest(
                    busId,
                    new RoadRoutePlan(new[]
                    {
                        new Vector2Int(0, 0),
                        new Vector2Int(1, 0),
                        new Vector2Int(2, 0)
                    }),
                    false,
                    RoadTrafficArrivalPolicy.HoldAtDestination,
                    true)));
            Assert.IsTrue(coordinator.TryStartAgent(busId));

            coordinator.PrepareStep(new EmptyRouteProvider());
            coordinator.SynchronizeSnapshots();

            Assert.IsTrue(coordinator.TryGetSnapshot(
                busId,
                out RoadTrafficSnapshot reserved));
            Assert.AreEqual(
                RoadTrafficAgentState.Paused,
                reserved.State);
            Assert.IsTrue(reserved.IsVisible);
            Assert.AreEqual(Vector2Int.zero, reserved.CurrentTile);
            Assert.AreEqual(2, network.QueueOccupancyUnits(
                Vector2Int.zero,
                Dir.E));

            network.Step(coordinator, null, 0);
            coordinator.ProcessArrivals();
            coordinator.SynchronizeSnapshots();
            Assert.IsTrue(coordinator.TryGetSnapshot(
                busId,
                out RoadTrafficSnapshot stillReserved));
            Assert.AreEqual(
                Vector2Int.zero,
                stillReserved.CurrentTile);
            Assert.AreEqual(
                RoadTrafficAgentState.Paused,
                stillReserved.State);

            Assert.IsTrue(coordinator.TrySetAgentPaused(
                busId,
                false));
            network.Step(coordinator, null, 1);
            coordinator.ProcessArrivals();
            coordinator.SynchronizeSnapshots();
            Assert.IsTrue(coordinator.TryGetSnapshot(
                busId,
                out RoadTrafficSnapshot released));
            Assert.AreNotEqual(
                RoadTrafficAgentState.Paused,
                released.State);
        }

        [Test]
        public void RegisteredBus_UsesRoadQueueAndPublishesArrival()
        {
            SimConfig config = SimConfig.Default();
            config.GridWidth = 3;
            config.GridHeight = 1;
            config.QueueServicePerTick = 1;
            var network = new RoadQueueNetwork(3, 1, config);
            var coordinator = new RoadTrafficCoordinator(network);
            var coreRoutes = new EmptyRouteProvider();
            int arrivalCount = 0;
            coordinator.AgentArrived += _ => arrivalCount++;

            coordinator.TryRegisterAgent(
                new RoadTrafficAgentRegistration(
                    RoadTrafficAgentKind.SchoolBus,
                    new VehicleFootprint(
                        VehicleSizeClass.Large,
                        0.8f,
                        0.24f,
                        0.11f)),
                out RoadTrafficAgentId busId);
            coordinator.TryAssignRoute(new RoadTrafficRouteRequest(
                busId,
                new RoadRoutePlan(new[]
                {
                    new Vector2Int(0, 0),
                    new Vector2Int(1, 0),
                    new Vector2Int(2, 0)
                }),
                false));
            coordinator.TryStartAgent(busId);

            for (int tick = 0; tick < 4; tick++)
            {
                coordinator.PrepareStep(coreRoutes);
                network.Step(coordinator, null, tick);
                coordinator.ProcessArrivals();
                coordinator.SynchronizeSnapshots();
            }

            Assert.AreEqual(1, arrivalCount);
            Assert.IsTrue(coordinator.TryGetSnapshot(
                busId,
                out RoadTrafficSnapshot snapshot));
            Assert.AreEqual(
                RoadTrafficAgentState.Arrived,
                snapshot.State);
            Assert.AreEqual(new Vector2Int(2, 0), snapshot.CurrentTile);
            Assert.AreEqual(0, network.QueueCount(
                new Vector2Int(2, 0),
                Dir.E));
        }

        [Test]
        public void RouteStartingInRoundabout_EntersAtFirstSafeRoadTile()
        {
            SimConfig config = SimConfig.Default();
            config.GridWidth = 7;
            config.GridHeight = 3;
            config.AutoDetectSignals = false;
            var engine = new SimEngine(config, new SimEventHub());

            for (int x = 0; x < config.GridWidth; x++)
            {
                Assert.IsTrue(engine.Place(
                    new Vector2Int(x, 1),
                    TileType.Road));
            }

            Assert.IsTrue(engine.Place(
                new Vector2Int(3, 0),
                TileType.Road));
            Assert.IsTrue(engine.Place(
                new Vector2Int(3, 2),
                TileType.Road));
            Assert.IsTrue(engine.TryPlaceRoundabout(
                new Vector2Int(3, 1)));

            IRoadTrafficService traffic = engine.RoadTraffic;
            Assert.IsTrue(traffic.TryRegisterAgent(
                new RoadTrafficAgentRegistration(
                    RoadTrafficAgentKind.CityBus,
                    new VehicleFootprint(
                        VehicleSizeClass.Large,
                        0.8f,
                        0.24f,
                        0.11f)),
                out RoadTrafficAgentId busId));
            Assert.IsTrue(traffic.TryAssignRoute(
                new RoadTrafficRouteRequest(
                    busId,
                    new RoadRoutePlan(new[]
                    {
                        new Vector2Int(3, 1),
                        new Vector2Int(4, 1),
                        new Vector2Int(5, 1),
                        new Vector2Int(6, 1)
                    }),
                    false)));
            Assert.IsTrue(traffic.TryStartAgent(busId));

            engine.Tick(config.TickInterval);

            Assert.IsTrue(traffic.TryGetSnapshot(
                busId,
                out RoadTrafficSnapshot snapshot));
            Assert.AreNotEqual(
                RoadTrafficAgentState.RouteUnavailable,
                snapshot.State);
            Assert.IsTrue(snapshot.IsVisible);
            Assert.GreaterOrEqual(snapshot.RouteTileIndex, 2);
        }

        [Test]
        public void HoldingBus_PreservesQueueFootprintAcrossRouteChange()
        {
            SimConfig config = SimConfig.Default();
            config.GridWidth = 4;
            config.GridHeight = 1;
            config.QueueServicePerTick = 1;
            var network = new RoadQueueNetwork(4, 1, config);
            var coordinator = new RoadTrafficCoordinator(network);
            var coreRoutes = new EmptyRouteProvider();

            coordinator.TryRegisterAgent(
                new RoadTrafficAgentRegistration(
                    RoadTrafficAgentKind.SchoolBus,
                    new VehicleFootprint(
                        VehicleSizeClass.Large,
                        0.8f,
                        0.24f,
                        0.11f)),
                out RoadTrafficAgentId busId);
            coordinator.TryAssignRoute(new RoadTrafficRouteRequest(
                busId,
                new RoadRoutePlan(new[]
                {
                    new Vector2Int(0, 0),
                    new Vector2Int(1, 0),
                    new Vector2Int(2, 0)
                }),
                false,
                RoadTrafficArrivalPolicy.HoldAtDestination));
            coordinator.TryStartAgent(busId);

            for (int tick = 0; tick < 4; tick++)
            {
                coordinator.PrepareStep(coreRoutes);
                network.Step(coordinator, null, tick);
                coordinator.ProcessArrivals();
                coordinator.SynchronizeSnapshots();
            }

            Assert.IsTrue(coordinator.TryGetSnapshot(
                busId,
                out RoadTrafficSnapshot holding));
            Assert.AreEqual(
                RoadTrafficAgentState.HoldingAtDestination,
                holding.State);
            Assert.AreEqual(1, network.QueueCount(
                new Vector2Int(2, 0),
                Dir.E));
            Assert.AreEqual(2, network.QueueOccupancyUnits(
                new Vector2Int(2, 0),
                Dir.E));

            Assert.IsTrue(coordinator.TryAssignRoute(
                new RoadTrafficRouteRequest(
                    busId,
                    new RoadRoutePlan(new[]
                    {
                        new Vector2Int(2, 0),
                        new Vector2Int(3, 0)
                    }),
                    false,
                    RoadTrafficArrivalPolicy.HoldAtDestination)));
            Assert.AreEqual(1, network.QueueCount(
                new Vector2Int(2, 0),
                Dir.E));
            Assert.IsTrue(coordinator.TryStartAgent(busId));

            for (int tick = 4; tick < 7; tick++)
            {
                coordinator.PrepareStep(coreRoutes);
                network.Step(coordinator, null, tick);
                coordinator.ProcessArrivals();
                coordinator.SynchronizeSnapshots();
            }

            Assert.IsTrue(coordinator.TryGetSnapshot(
                busId,
                out RoadTrafficSnapshot resumed));
            Assert.AreEqual(
                RoadTrafficAgentState.HoldingAtDestination,
                resumed.State);
            Assert.AreEqual(new Vector2Int(3, 0), resumed.CurrentTile);
        }
    }
}
