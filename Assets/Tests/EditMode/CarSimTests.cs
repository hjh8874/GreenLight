using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public class CarSimTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static SimConfig Cfg()
        {
            SimConfig cfg = SimConfig.Default();
            cfg.QueueCapacityPerTile = 4;
            cfg.QueueServicePerTick = 1;
            cfg.GridlockValveTicks = 8;
            cfg.CoinPerTrip = 10;
            cfg.CarsPerHouse = 1;
            cfg.MorningStartHour = 6f;
            cfg.MorningEndHour = 7f;
            cfg.EveningStartHour = 17f;
            cfg.EveningEndHour = 18f;
            cfg.OfficeParkingSlots = 6;
            cfg.MaxSimCars = 96;
            cfg.DemandChoicePool = 1;
            return cfg;
        }

        [Test]
        public void Morning_TwoCarsArriveAtWork_EmitsOnePaidEventPerCar()
        {
            BuildStraightCity(out CityGrid grid, out DemandMap demands, out RoutePlanner planner);
            SimConfig cfg = Cfg();
            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);
            var sim = new CarSim(cfg);
            sim.Rebuild(demands, planner, net);
            var hub = new SimEventHub();
            var events = new SimEventBuffer(hub);
            int arrivals = 0, coins = 0;
            hub.Arrival += e => { arrivals++; coins += e.Coins; };

            for (int tick = 0; tick < 8; tick++) sim.Step(7f, net, events);
            events.Drain();

            Assert.AreEqual(2, sim.CarCount);
            Assert.AreEqual(2, arrivals);
            Assert.AreEqual(2 * cfg.CoinPerTrip, coins);
            Assert.AreEqual(CarState.ParkedWork, sim.GetCar(0).State);
            Assert.AreEqual(CarState.ParkedWork, sim.GetCar(1).State);
        }

        [Test]
        public void Evening_ReturnTripArrivesHome_EmitsNoCoins()
        {
            BuildStraightCity(out CityGrid grid, out DemandMap demands, out RoutePlanner planner);
            SimConfig cfg = Cfg();
            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);
            var sim = new CarSim(cfg);
            sim.Rebuild(demands, planner, net);
            var hub = new SimEventHub();
            var events = new SimEventBuffer(hub);
            int arrivals = 0;
            hub.Arrival += _ => arrivals++;
            for (int tick = 0; tick < 8; tick++) sim.Step(7f, net, events);
            events.Drain();
            Assert.AreEqual(2, arrivals);

            for (int tick = 0; tick < 8; tick++) sim.Step(tick == 0 ? 17f : 18f, net, events);
            events.Drain();

            Assert.AreEqual(2, arrivals, "귀가 도착은 ArrivalEvent 코인 없음");
            Assert.AreEqual(CarState.ParkedHome, sim.GetCar(0).State);
            Assert.AreEqual(CarState.ParkedHome, sim.GetCar(1).State);
        }

        [Test]
        public void ReturnRoutes_AvoidReverseTravelOnOneway()
        {
            SimConfig cfg = Cfg();
            var grid = new CityGrid(5, 3);
            for (int x = 1; x <= 3; x++)
            {
                Assert.IsTrue(grid.Place(V(x, 1), TileType.Road));
                Assert.IsTrue(grid.Place(V(x, 2), TileType.Road));
            }
            Assert.IsTrue(grid.Place(V(0, 1), TileType.House));
            Assert.IsTrue(grid.Place(V(4, 1), TileType.Office));
            var road = new RoadNetwork(grid);
            var demands = new DemandMap(cfg);
            demands.Reassign(grid, road);
            var oneways = new Dictionary<Vector2Int, Vector2Int> { [V(2, 1)] = Vector2Int.right };
            var planner = new RoutePlanner(grid.Width, grid.Height);

            planner.Plan(demands, road, grid, cfg, oneways);

            CollectionAssert.AreEqual(new[] { V(1, 1), V(2, 1), V(3, 1) }, planner.CarRoutes[0]);
            CollectionAssert.AreEqual(
                new[] { V(3, 1), V(3, 2), V(2, 2), V(1, 2), V(1, 1) },
                planner.ReturnRoutes[0]);
            Assert.IsFalse(planner.ReturnRoutes[0].Contains(V(2, 1)), "일방 역주행 타일 제외");
        }

        [Test]
        public void TimeJump_RemovesMovingCarsAndConvergesToParkingPolicy()
        {
            BuildStraightCity(out CityGrid grid, out DemandMap demands, out RoutePlanner planner);
            SimConfig cfg = Cfg();
            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);
            var sim = new CarSim(cfg);
            sim.Rebuild(demands, planner, net);
            var events = new SimEventBuffer(new SimEventHub());

            sim.Step(7f, net, events);
            Assert.AreEqual(CarState.Outbound, sim.GetCar(0).State);
            sim.Step(17f, net, events); // d=10 > 1: 출근 큐 제거→회사 스냅→도래한 차는 퇴근 재개

            Assert.AreEqual(CarState.Inbound, sim.GetCar(0).State, "17시 정각 차는 스냅 후 같은 Step에 퇴근");
            Assert.AreEqual(CarState.ParkedWork, sim.GetCar(1).State, "아직 출발시각 전 차는 회사 주차");
            Assert.LessOrEqual(TotalQueued(net, grid.Width, grid.Height), 1, "점프 전 출근 큐는 전부 제거");
        }

        [Test]
        public void CarRoutes_DiagonalOnlyConnection_IsUnreachable()
        {
            SimConfig cfg = Cfg();
            var grid = new CityGrid(5, 3);
            Assert.IsTrue(grid.Place(V(1, 1), TileType.Road));
            Assert.IsTrue(grid.Place(V(2, 2), TileType.Road));
            Assert.IsTrue(grid.Place(V(0, 1), TileType.House));
            Assert.IsTrue(grid.Place(V(3, 2), TileType.Office));
            var road = new RoadNetwork(grid);
            var demands = new DemandMap(cfg);
            demands.Reassign(grid, road);
            var planner = new RoutePlanner(grid.Width, grid.Height);

            planner.Plan(demands, road, grid, cfg);

            Assert.IsNotNull(planner.Routes[0], "Task 9 전 레거시 8연결은 보존");
            Assert.IsNull(planner.CarRoutes[0], "CarSim은 대각 코너컷을 연결로 인정하지 않음");
            Assert.IsNull(planner.ReturnRoutes[0]);
        }

        private static void BuildStraightCity(
            out CityGrid grid,
            out DemandMap demands,
            out RoutePlanner planner)
        {
            SimConfig cfg = Cfg();
            grid = new CityGrid(6, 3);
            for (int x = 0; x <= 4; x++) Assert.IsTrue(grid.Place(V(x, 1), TileType.Road));
            Assert.IsTrue(grid.Place(V(0, 0), TileType.House));
            Assert.IsTrue(grid.Place(V(1, 0), TileType.House));
            Assert.IsTrue(grid.Place(V(5, 1), TileType.Office));
            var road = new RoadNetwork(grid);
            demands = new DemandMap(cfg);
            demands.Reassign(grid, road);
            planner = new RoutePlanner(grid.Width, grid.Height);
            planner.Plan(demands, road, grid, cfg);
            Assert.AreEqual(2, demands.Demands.Count);
        }

        private static int TotalQueued(RoadQueueNetwork net, int width, int height)
        {
            int count = 0;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            for (int d = 0; d < 4; d++) count += net.QueueCount(V(x, y), (Dir)d);
            return count;
        }
    }
}
