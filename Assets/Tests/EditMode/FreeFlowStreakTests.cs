using NUnit.Framework;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public sealed class FreeFlowStreakTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static CarSim BuildCommuteCity(
            SimConfig cfg,
            bool intersections,
            out RoadQueueNetwork queues,
            out List<Vector2Int> intersectionRouteTiles)
        {
            int width = 10;
            var grid = new CityGrid(width, intersections ? 5 : 3);
            for (int x = 0; x < width; x++)
                Assert.IsTrue(grid.Place(V(x, 2), TileType.Road), $"main road x={x}");

            if (intersections)
            {
                foreach (int x in new[] { 2, 4, 6 })
                {
                    Assert.IsTrue(grid.Place(V(x, 1), TileType.Road), $"branch up x={x}");
                    Assert.IsTrue(grid.Place(V(x, 3), TileType.Road), $"branch down x={x}");
                    Assert.IsTrue(grid.IsIntersection(V(x, 2)), $"intersection x={x}");
                }
            }

            Assert.IsTrue(grid.Place(V(0, 0), TileType.House), "house");
            Assert.IsTrue(grid.Place(V(8, 0), TileType.Office), "office");
            var roads = new RoadNetwork(grid);
            var demands = new DemandMap(cfg);
            demands.Reassign(grid, roads);
            var planner = new RoutePlanner(grid.Width, grid.Height);
            planner.Plan(demands, roads, grid, cfg);
            queues = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            queues.RebuildTopology(grid);

            Assert.AreEqual(1, demands.Demands.Count, "one commute demand");
            intersectionRouteTiles = new List<Vector2Int>();
            if (intersections)
            {
                int routeIntersections = 0;
                foreach (Vector2Int tile in planner.CarRoutes[0])
                    if (grid.IsIntersection(tile))
                    {
                        routeIntersections++;
                        intersectionRouteTiles.Add(tile);
                    }
                Assert.GreaterOrEqual(routeIntersections, 3);
            }

            var sim = new CarSim(cfg);
            sim.Rebuild(demands, planner, queues, grid: grid, roadNetwork: roads);
            return sim;
        }

        static void Step(CarSim sim, RoadQueueNetwork queues, int count, float hour = 7f)
        {
            var events = new SimEventBuffer(new SimEventHub());
            for (int tick = 0; tick < count; tick++)
            {
                sim.Step(1L, hour, queues, events, null, tick);
                events.Drain();
            }
        }

        private sealed class BlockingSignalGate : ISignalGate
        {
            private readonly Vector2Int _tile;
            public bool Closed { get; set; }

            public BlockingSignalGate(Vector2Int tile) => _tile = tile;

            public bool IsServiceOpen(Vector2Int tile, Dir entryDir, int tick) =>
                !Closed || tile != _tile;
        }

        static void RunRedLightScenario(
            out int maxAtReset,
            out int streakAfterResume)
        {
            maxAtReset = 0;
            streakAfterResume = 0;
            CarSim sim = BuildCommuteCity(
                CarSimTests.Cfg(),
                true,
                out RoadQueueNetwork queues,
                out List<Vector2Int> intersections);
            var gate = new BlockingSignalGate(intersections[1]);
            var events = new SimEventBuffer(new SimEventHub());
            int tick = 0;
            for (; tick < 100; tick++)
            {
                sim.Step(1L, 7f, queues, events, gate, tick);
                events.Drain();
                if (sim.GetCar(0).FreeFlowStreak == 1) break;
            }
            Assert.AreEqual(1, sim.GetCar(0).FreeFlowStreak, "first intersection was not passed");

            gate.Closed = true;
            bool resetObserved = false;
            for (int i = 0; i < 40; i++, tick++)
            {
                sim.Step(1L, 7f, queues, events, gate, tick);
                events.Drain();
                CarSnapshot snapshot = sim.GetCar(0);
                if (!snapshot.WaitingForSpeedCredit && snapshot.FreeFlowStreak == 0)
                {
                    maxAtReset = snapshot.FreeFlowStreakMax;
                    resetObserved = true;
                    break;
                }
            }
            Assert.IsTrue(resetObserved, "red-light stop did not reset the current streak");

            gate.Closed = false;
            streakAfterResume = 0;
            for (int i = 0; i < 100; i++, tick++)
            {
                sim.Step(1L, 7f, queues, events, gate, tick);
                events.Drain();
                streakAfterResume = sim.GetCar(0).FreeFlowStreak;
                if (streakAfterResume > 0) break;
            }
            Assert.Greater(streakAfterResume, 0, "vehicle did not pass the second intersection after green");
        }

        [Test]
        public void FreeFlowStreak_IncrementsToOneAfterOneIntersection()
        {
            CarSim sim = BuildCommuteCity(CarSimTests.Cfg(), true, out RoadQueueNetwork queues, out _);
            for (int tick = 0; tick < 80; tick++)
            {
                Step(sim, queues, 1);
                int streak = sim.GetCar(0).FreeFlowStreak;
                if (streak > 0)
                {
                    Assert.AreEqual(1, streak);
                    return;
                }
            }
            Assert.Fail("교차로 무정차 통과가 관측되지 않았다");
        }

        [Test]
        public void FreeFlowStreak_ReachesTwoAcrossTwoIntersections()
        {
            CarSim sim = BuildCommuteCity(CarSimTests.Cfg(), true, out RoadQueueNetwork queues, out _);
            for (int tick = 0; tick < 100; tick++)
            {
                Step(sim, queues, 1);
                if (sim.GetCar(0).FreeFlowStreak >= 2)
                {
                    Assert.AreEqual(2, sim.GetCar(0).FreeFlowStreak);
                    return;
                }
            }
            Assert.Fail("두 교차로 연속 무정차 통과가 관측되지 않았다");
        }

        [Test]
        public void FreeFlowStreak_CapsAtThree()
        {
            CarSim sim = BuildCommuteCity(CarSimTests.Cfg(), true, out RoadQueueNetwork queues, out _);
            Step(sim, queues, 120);
            Assert.AreEqual(CarSim.FreeFlowStreakCap, sim.GetCar(0).FreeFlowStreakMax);
        }

        [Test]
        public void FreeFlowStreak_StopsResetCurrentButPreservesMax()
        {
            RunRedLightScenario(out int maxAtReset, out int resumed);
            Assert.AreEqual(1, resumed);
            Assert.Greater(maxAtReset, 0);
        }

        [Test]
        public void FreeFlowStreak_StraightRoadWithoutIntersectionsStaysZero()
        {
            CarSim sim = BuildCommuteCity(CarSimTests.Cfg(), false, out RoadQueueNetwork queues, out _);
            Step(sim, queues, 120);
            Assert.AreEqual(0, sim.GetCar(0).FreeFlowStreak);
            Assert.AreEqual(0, sim.GetCar(0).FreeFlowStreakMax);
        }

        [Test]
        public void FreeFlowStreakMax_PreservesHighestStageAfterReset()
        {
            RunRedLightScenario(out int maxAtReset, out int resumed);
            Assert.AreEqual(1, resumed);
            Assert.AreEqual(maxAtReset, 1);
        }

        [Test]
        public void FreeFlowStreak_SpecialVisitSnapshotCarriesBothFields()
        {
            SimConfig cfg = CarSimTests.Cfg();
            cfg.MaxSimCars = 8;
            cfg.MaxPendingVehicleTrips = 8;
            cfg.MaxConcurrentSpecialTrips = 2;
            var grid = new CityGrid(10, 5);
            for (int x = 0; x < 9; x++)
                Assert.IsTrue(grid.Place(V(x, 2), TileType.Road));
            foreach (int x in new[] { 2, 4, 6 })
            {
                Assert.IsTrue(grid.Place(V(x, 1), TileType.Road));
                Assert.IsTrue(grid.Place(V(x, 3), TileType.Road));
            }
            Assert.IsTrue(grid.Place(V(0, 0), TileType.House));
            Assert.IsTrue(grid.Place(V(8, 0), TileType.SpecialBuilding));
            var roads = new RoadNetwork(grid);
            var demands = new DemandMap(cfg);
            demands.Reassign(grid, roads);
            var planner = new RoutePlanner(grid.Width, grid.Height);
            planner.Plan(demands, roads, grid, cfg);
            var queues = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            queues.RebuildTopology(grid);
            var sim = new CarSim(cfg);
            sim.Rebuild(demands, planner, queues, grid: grid, roadNetwork: roads);
            var events = new SimEventBuffer(new SimEventHub());
            Assert.IsTrue(sim.TryScheduleSpecialBuildingVisit(
                new SpecialBuildingVisitTripRequest(
                    "coffee-shop", V(8, 0), 1L, 0, 7f, rewardCoins: 0)));

            for (int tick = 0; tick < 80; tick++)
            {
                sim.Step(1L, 7f, queues, events, null, tick);
                events.Drain();
                for (int i = 0; i < sim.CarCount; i++)
                {
                    CarSnapshot snapshot = sim.GetCar(i);
                    if (snapshot.Purpose != VehicleTripPurpose.SpecialBuildingVisit) continue;
                    if (snapshot.FreeFlowStreakMax > 0)
                    {
                        Assert.That(snapshot.FreeFlowStreak, Is.InRange(0, CarSim.FreeFlowStreakCap));
                        Assert.That(snapshot.FreeFlowStreakMax, Is.InRange(1, CarSim.FreeFlowStreakCap));
                        return;
                    }
                }
            }
            Assert.Fail("특수 통행 스냅샷을 관측하지 못했다");
        }

        [Test]
        public void RoadQueueNodeReverseIndex_RemainsConsistentAfterRoundaboutTraffic()
        {
            SimConfig cfg = CarSimTests.Cfg();
            cfg.GridWidth = 10;
            cfg.GridHeight = 5;
            cfg.MaxSimCars = 8;
            cfg.CarsPerHouse = 2;
            cfg.OfficeCapacity = 8;
            cfg.TickInterval = 0.25f;
            cfg.AutoDetectSignals = false;
            var engine = new SimEngine(cfg, new SimEventHub());
            for (int x = 0; x <= 9; x++)
                Assert.IsTrue(engine.Place(V(x, 2), TileType.Road), $"road x={x}");
            foreach (int x in new[] { 2, 4, 6 })
            {
                Assert.IsTrue(engine.Place(V(x, 1), TileType.Road), $"branch up x={x}");
                Assert.IsTrue(engine.Place(V(x, 3), TileType.Road), $"branch down x={x}");
            }
            Assert.IsTrue(engine.Place(V(0, 0), TileType.House), "house");
            Assert.IsTrue(engine.Place(V(8, 0), TileType.Office), "office");
            engine.Tick(0.25f);
            Assert.IsTrue(engine.TryPlaceRoundabout(V(6, 2)), "roundabout");

            for (int tick = 0; tick < 80; tick++)
                engine.Tick(0.25f);

            RoadQueueNetwork queues = engine.RoadQueuesForTest;
            FieldInfo reverseField = typeof(RoadQueueNetwork).GetField(
                "_nodeByCarId",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo carsField = typeof(RoadQueueNetwork).GetField(
                "_cars",
                BindingFlags.Instance | BindingFlags.NonPublic);
            int[] nodeByCarId = (int[])reverseField.GetValue(queues);
            int[] cars = (int[])carsField.GetValue(queues);
            for (int carId = 0; carId < nodeByCarId.Length; carId++)
            {
                int node = nodeByCarId[carId];
                if (node == -1) continue;
                Assert.That(node, Is.InRange(0, cars.Length - 1), $"carId={carId}");
                Assert.AreEqual(carId, cars[node], $"carId={carId}, node={node}");
            }
        }

        [Test]
        public void FreeFlowReward_StageOneUsesOneTimesBaseReward()
        {
            Assert.AreEqual(10, CarSim.CalculateFreeFlowReward(10, 1));
        }

        [Test]
        public void FreeFlowReward_StageTwoUsesTwoTimesBaseReward()
        {
            Assert.AreEqual(20, CarSim.CalculateFreeFlowReward(10, 2));
        }

        [Test]
        public void FreeFlowReward_StageThreeUsesFourTimesBaseReward()
        {
            Assert.AreEqual(40, CarSim.CalculateFreeFlowReward(10, 3));
        }

        [Test]
        public void FreeFlowReward_ArrivalReadsMaxWhenCurrentWasReset()
        {
            CarSim sim = BuildCommuteCity(
                CarSimTests.Cfg(),
                true,
                out RoadQueueNetwork queues,
                out _);
            var hub = new SimEventHub();
            var events = new SimEventBuffer(hub);
            int paidCoins = 0;
            hub.Arrival += message => paidCoins = message.Coins;

            FieldInfo maxField = typeof(CarSim).GetField(
                "_freeFlowStreakMax",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo currentField = typeof(CarSim).GetField(
                "_freeFlowStreak",
                BindingFlags.Instance | BindingFlags.NonPublic);
            int[] max = (int[])maxField.GetValue(sim);
            int[] current = (int[])currentField.GetValue(sim);
            max[0] = 3;
            current[0] = 0;

            for (int tick = 0; tick < 120 && paidCoins == 0; tick++)
            {
                sim.Step(1L, 7f, queues, events, null, tick);
                events.Drain();
            }

            Assert.AreEqual(40, paidCoins,
                "도착 보상은 현재 연결이 아니라 통근 최대 연결을 읽어야 한다");
        }
    }
}
