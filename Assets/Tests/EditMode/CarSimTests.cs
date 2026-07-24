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
            cfg.MaxSimCars = 96;
            cfg.DemandChoicePool = 1;
            return cfg;
        }

        [Test]
        public void HomeParking_TwoSlotsLimitEachHouseToTwoCars()
        {
            Vector2Int home = V(1, 1);
            Vector2Int work = V(4, 1);
            var homes = new List<Vector2Int> { home, home, home };
            var works = new List<Vector2Int> { work, work, work };
            var scheduler = new CommuteScheduler();

            scheduler.Rebuild(
                homes,
                works,
                workCapacityFor: _ => 6,
                homeSlots: 2,
                maxCars: 96,
                morningStart: 6f,
                morningEnd: 7f,
                eveningStart: 17f,
                eveningEnd: 18f);

            Assert.AreEqual(2, scheduler.Cars.Count);
            Assert.AreEqual(0, scheduler.Cars[0].HomeSlot);
            Assert.AreEqual(1, scheduler.Cars[1].HomeSlot);
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
            Assert.IsFalse(sim.AllParkedHome);

            var occupied = new HashSet<(Vector2Int work, int slot)>();
            for (int i = 0; i < sim.CarCount; i++)
            {
                CarSnapshot snapshot = sim.GetCar(i);
                Assert.IsTrue(occupied.Add((snapshot.Work, snapshot.WorkSlot)),
                    "뷰가 실제 회사 주차 슬롯을 받으려면 스냅샷 슬롯이 유일해야 한다");
                Assert.AreEqual(0, snapshot.HomeSlot, "CarsPerHouse=1이면 집 슬롯은 0");
            }
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
            Assert.IsTrue(sim.AllParkedHome);
        }

        [Test]
        public void ReturnRoutes_AvoidReverseTravelOnOneway()
        {
            SimConfig cfg = Cfg();
            var grid = new CityGrid(7, 4);
            for (int x = 1; x <= 5; x++)
            {
                Assert.IsTrue(grid.Place(V(x, 2), TileType.Road));
                Assert.IsTrue(grid.Place(V(x, 3), TileType.Road));
            }
            Assert.IsTrue(grid.Place(V(0, 0), TileType.House));
            Assert.IsTrue(grid.Place(V(5, 0), TileType.Office));
            var road = new RoadNetwork(grid);
            var demands = new DemandMap(cfg);
            demands.Reassign(grid, road);
            var oneways = new Dictionary<Vector2Int, Vector2Int> { [V(3, 2)] = Vector2Int.right };
            var planner = new RoutePlanner(grid.Width, grid.Height);

            planner.Plan(demands, road, grid, cfg, oneways);

            CollectionAssert.AreEqual(new[] { V(1, 2), V(2, 2), V(3, 2), V(4, 2), V(5, 2) }, planner.CarRoutes[0]);
            CollectionAssert.AreEqual(
                new[] { V(5, 2), V(4, 2), V(4, 3), V(3, 3), V(2, 3), V(2, 2), V(1, 2) },
                planner.ReturnRoutes[0]);
            Assert.IsFalse(planner.ReturnRoutes[0].Contains(V(3, 2)), "일방 역주행 타일 제외");
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

            int inbound = 0;
            int parkedWork = 0;
            for (int i = 0; i < sim.CarCount; i++)
            {
                if (sim.GetCar(i).State == CarState.Inbound) inbound++;
                if (sim.GetCar(i).State == CarState.ParkedWork) parkedWork++;
            }
            Assert.AreEqual(1, inbound, "17시 정각에는 첫 퇴근 차량만 출발");
            Assert.AreEqual(1, parkedWork, "아직 출발 시각 전인 차량은 회사에 주차");
            Assert.LessOrEqual(TotalQueued(net, grid.Width, grid.Height), 1, "점프 전 출근 큐는 전부 제거");
        }

        [Test]
        public void CarRoutes_DiagonalOnlyConnection_IsUnreachable()
        {
            SimConfig cfg = Cfg();
            var grid = new CityGrid(6, 4);
            Assert.IsTrue(grid.Place(V(2, 1), TileType.Road));
            Assert.IsTrue(grid.Place(V(3, 2), TileType.Road));
            Assert.IsTrue(grid.Place(V(0, 0), TileType.House));
            Assert.IsTrue(grid.Place(V(4, 2), TileType.Office));
            var road = new RoadNetwork(grid);
            var demands = new DemandMap(cfg);
            demands.Reassign(grid, road);
            var planner = new RoutePlanner(grid.Width, grid.Height);

            planner.Plan(demands, road, grid, cfg);

            Assert.IsNull(planner.Routes[0], "직교 단일 라우터는 대각 코너컷을 연결로 인정하지 않음");
            Assert.IsNull(planner.CarRoutes[0]);
            Assert.IsNull(planner.ReturnRoutes[0]);
        }

        // 건설(=Rebuild)이 주행 중인 차를 집으로 되돌리면 안 된다.
        // 라이브 계측(2026-07-20): 도로 1개 배치에 4대가 한 프레임에 최대 5.90타일 순간이동.
        [Test]
        public void Rebuild_MidRoute_DoesNotResetSurvivorToHome()
        {
            BuildStraightCity(out CityGrid grid, out DemandMap demands, out RoutePlanner planner);
            SimConfig cfg = Cfg();
            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);
            var sim = new CarSim(cfg);
            sim.Rebuild(demands, planner, net);
            var hub = new SimEventHub();
            var events = new SimEventBuffer(hub);

            for (int t = 0; t < 3; t++) sim.Step(7f, net, events);
            CarSnapshot before = sim.GetCar(0);
            Assert.AreEqual(CarState.Outbound, before.State, "전제: 차가 주행 중이어야 한다");
            Assert.Greater(before.TileIndex, 0, "전제: 차가 집 타일을 벗어나 있어야 한다");

            // 토폴로지 변경(건설)과 같은 경로 — 경로 자체는 그대로다.
            sim.Rebuild(demands, planner, net);
            sim.Step(7f, net, events);

            CarSnapshot after = sim.GetCar(0);
            Assert.AreEqual(CarState.Outbound, after.State,
                "건설이 주행 중인 차를 주차 상태로 되돌리면 안 된다");
            // TileIndex>0만 보면 약하다 — 집에서 재출발해 한 칸 전진해도 통과한다.
            // 진행도가 뒤로 가지 않았는지를 봐야 순간이동을 잡는다.
            Assert.GreaterOrEqual(after.TileIndex, before.TileIndex,
                $"건설이 주행 차의 진행도를 되돌렸다 ({before.TileIndex} -> {after.TileIndex})"
                + " — 뷰에서 집으로 순간이동하는 것으로 보인다");
        }

        [Test]
        public void RebuildResume_IntersectionTile_TwoCrossingCarsBacktrackAndExit()
        {
            SimConfig cfg = Cfg();
            var grid = new CityGrid(5, 5);
            Assert.IsTrue(grid.Place(V(2, 2), TileType.Road));
            Assert.IsTrue(grid.Place(V(1, 2), TileType.Road));
            Assert.IsTrue(grid.Place(V(3, 2), TileType.Road));
            Assert.IsTrue(grid.Place(V(2, 1), TileType.Road));
            Assert.IsTrue(grid.Place(V(2, 3), TileType.Road));
            Assert.IsTrue(grid.IsIntersection(V(2, 2)));
            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);

            var routes = new ResumeRouteProvider();
            routes.Add(10, V(1, 2), V(2, 2), V(3, 2));
            routes.Add(11, V(2, 1), V(2, 2), V(2, 3));
            int eastboundStart = CarSim.FindResumeStart(
                routes.RouteFor(10),
                V(2, 2),
                net);
            int northboundStart = CarSim.FindResumeStart(
                routes.RouteFor(11),
                V(2, 2),
                net);

            Assert.AreEqual(0, eastboundStart);
            Assert.AreEqual(0, northboundStart);
            Assert.IsTrue(net.TryEnqueue(routes.RouteFor(10)[eastboundStart], Dir.E, 10));
            Assert.IsTrue(net.TryEnqueue(routes.RouteFor(11)[northboundStart], Dir.N, 11));

            var arrived = new HashSet<int>();
            for (int tick = 0; tick < 8 && arrived.Count < 2; tick++)
            {
                net.Step(routes);
                for (int i = 0; i < net.ArrivalCount; i++)
                {
                    arrived.Add(net.GetArrival(i).CarId);
                }
            }

            CollectionAssert.AreEquivalent(
                new[] { 10, 11 },
                arrived,
                "교차 방향 재개 차량도 일반 타일에서 중재를 거쳐 유한 틱 안에 빠져나가야 한다");
        }

        [Test]
        public void RebuildResume_AdjacentIntersection_FullOnlyOrdinaryTileRetriesAndProgresses()
        {
            SimConfig cfg = Cfg();
            var grid = new CityGrid(5, 5);
            Assert.IsTrue(grid.Place(V(2, 2), TileType.Road));
            Assert.IsTrue(grid.Place(V(1, 2), TileType.Road));
            Assert.IsTrue(grid.Place(V(3, 2), TileType.Road));
            Assert.IsTrue(grid.Place(V(2, 1), TileType.Road));
            Assert.IsTrue(grid.Place(V(2, 3), TileType.Road));
            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);

            var routes = new ResumeRouteProvider();
            routes.Add(10, V(1, 2), V(2, 2));
            Assert.IsTrue(net.TryEnqueue(V(1, 2), Dir.E, 20));
            Assert.IsTrue(net.TryEnqueue(V(1, 2), Dir.E, 21));
            bool hasResume = true;

            Assert.IsFalse(CarSim.TryEnqueueRouteStart(
                routes.RouteFor(10),
                V(2, 2),
                ref hasResume,
                net,
                10,
                out int start));
            Assert.IsTrue(hasResume,
                "유일한 일반 재개 타일이 잠시 만석이면 다음 틱에도 같은 재개를 재시도해야 한다");
            Assert.AreEqual(0, start);

            net.RemoveAllCars();
            Assert.IsTrue(CarSim.TryEnqueueRouteStart(
                routes.RouteFor(10),
                V(2, 2),
                ref hasResume,
                net,
                10,
                out start));
            Assert.IsFalse(hasResume);

            bool arrived = false;
            for (int tick = 0; tick < 4 && !arrived; tick++)
            {
                net.Step(routes);
                for (int i = 0; i < net.ArrivalCount; i++)
                {
                    if (net.GetArrival(i).CarId == 10) arrived = true;
                }
            }
            Assert.IsTrue(arrived, "재개 큐가 비면 정상 교차로 중재로 진입해 유한 틱 안에 도착한다");
        }

        [Test]
        public void Departure_SpecialRouteOrigin_StaysOffNetwork()
        {
            SimConfig cfg = Cfg();
            var grid = new CityGrid(5, 5);
            Assert.IsTrue(grid.Place(V(2, 2), TileType.Road));
            Assert.IsTrue(grid.Place(V(1, 2), TileType.Road));
            Assert.IsTrue(grid.Place(V(3, 2), TileType.Road));
            Assert.IsTrue(grid.Place(V(2, 1), TileType.Road));
            Assert.IsTrue(grid.Place(V(2, 3), TileType.Road));
            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);

            var routes = new ResumeRouteProvider();
            routes.Add(12, V(2, 2), V(3, 2));
            bool hasResume = true;

            Assert.IsFalse(CarSim.TryEnqueueRouteStart(
                routes.RouteFor(12),
                V(2, 2),
                ref hasResume,
                net,
                12,
                out int start));
            Assert.IsFalse(hasResume, "안전한 이전 타일이 없으면 중간 재개를 명시적으로 포기한다");
            Assert.AreEqual(0, start);

            Assert.IsFalse(CarSim.TryEnqueueRouteStart(
                routes.RouteFor(12),
                default,
                ref hasResume,
                net,
                12,
                out start));
            Assert.AreEqual(0, TotalQueued(net, grid.Width, grid.Height),
                "신규 출발도 특수 route[0]에 stage 없는 차를 앉히지 않고 오프네트워크 대기한다");
        }

        [Test]
        public void Departure_WhenAccessRoadIsRamp_EntersHighway()
        {
            SimConfig cfg = Cfg();
            var grid = new CityGrid(11, 4);
            for (int x = 2; x <= 8; x++) Assert.IsTrue(grid.Place(V(x, 2), TileType.Road));
            Assert.IsTrue(grid.Place(V(0, 0), TileType.House));
            Assert.IsTrue(grid.Place(V(9, 1), TileType.Office));
            var road = new RoadNetwork(grid);
            var demands = new DemandMap(cfg);
            demands.Reassign(grid, road);
            var links = new List<HighwayLink> { new HighwayLink(V(2, 2), V(7, 2)) };
            var planner = new RoutePlanner(grid.Width, grid.Height);
            planner.Plan(demands, road, grid, cfg, null, null, links);
            CollectionAssert.AreEqual(
                new[] { V(2, 2), V(7, 2), V(8, 2) },
                planner.CarRoutes[0],
                "test setup must begin with a highway jump");

            var devices = new FakeDeviceState();
            devices.AddHighway(V(2, 2), V(7, 2));
            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid, devices);
            var sim = new CarSim(cfg);
            sim.Rebuild(demands, planner, net);

            sim.Step(7f, net, new SimEventBuffer(new SimEventHub()));

            Assert.AreEqual(CarState.Outbound, sim.GetCar(0).State);
            Assert.AreEqual(0, sim.GetCar(0).QueueSlot,
                "a car whose first route step is a highway jump must be enqueued on the link");
        }

        [Test]
        public void LivenessWatchdog_BlockRemovedWithDetour_ReroutesAtL2AndProgresses()
        {
            BuildWatchdogCity(
                out CityGrid grid,
                out RoadNetwork road,
                out DemandMap demands,
                out RoutePlanner planner,
                out RoadQueueNetwork net,
                out CarSim sim,
                out SimEventBuffer events);

            sim.Step(7f, net, events);
            Assert.AreEqual(1, sim.GetCar(0).TileIndex, "전제: 교차로 직전 타일까지 전진");
            Assert.IsTrue(net.TryEnqueue(V(3, 2), Dir.E, 90));
            Assert.IsTrue(net.TryEnqueue(V(3, 2), Dir.E, 91));

            Assert.IsTrue(grid.Remove(V(2, 2)), "직선 경로를 철거해 위쪽 우회로만 남긴다");
            planner.Plan(demands, road, grid, Cfg());

            for (int tick = 0; tick < Cfg().GridlockValveTicks * 3 - 1; tick++)
                sim.Step(7f, net, events);
            Assert.AreEqual(0, sim.RescueRerouteCount, "L2 임계 전 조기 구제 금지");
            sim.Step(7f, net, events);

            Assert.AreEqual(1, sim.RescueRerouteCount);
            Assert.AreEqual(0, sim.RescueRestartCount);
            Assert.AreEqual(0, sim.LastRescueCarId);
            Assert.AreEqual(V(1, 2), sim.LastRescueTile);
            int rescueRouteIndex = sim.GetCar(0).RouteIndex;
            Assert.GreaterOrEqual(rescueRouteIndex, planner.CarRoutes.Count);
            CollectionAssert.DoesNotContain(
                sim.ActiveRoutes[rescueRouteIndex],
                V(2, 2),
                "View가 읽는 route index도 차량별 L2 우회 경로를 가리켜야 한다");

            for (int tick = 0;
                 tick < 8 && sim.GetCar(0).State == CarState.Outbound;
                 tick++)
            {
                sim.Step(7f, net, events);
            }
            Assert.AreEqual(
                CarState.ParkedWork,
                sim.GetCar(0).State,
                "L2 경로 교체 뒤 철거 타일을 피해 유한 틱 안에 도착해야 한다");
        }

        [Test]
        public void LivenessWatchdog_NoDetour_RestartsAtL3AndCountsRescue()
        {
            BuildWatchdogCity(
                out CityGrid grid,
                out RoadNetwork road,
                out DemandMap demands,
                out RoutePlanner planner,
                out RoadQueueNetwork net,
                out CarSim sim,
                out SimEventBuffer events);

            sim.Step(7f, net, events);
            Assert.IsTrue(net.TryEnqueue(V(3, 2), Dir.E, 90));
            Assert.IsTrue(net.TryEnqueue(V(3, 2), Dir.E, 91));

            Assert.IsTrue(grid.Remove(V(2, 2)));
            Assert.IsTrue(grid.Remove(V(1, 3)), "우회로도 끊어 L2 재탐색을 실패시킨다");
            planner.Plan(demands, road, grid, Cfg());

            for (int tick = 0; tick < Cfg().GridlockValveTicks * 6 - 1; tick++)
                sim.Step(7f, net, events);
            Assert.AreEqual(0, sim.RescueRestartCount, "L3 임계 전 조기 재출발 금지");
            sim.Step(7f, net, events);

            Assert.AreEqual(1, sim.RescueRerouteCount, "L2 재탐색 시도도 발동 누계에 포함");
            Assert.AreEqual(1, sim.RescueRestartCount);
            Assert.AreEqual(0, sim.LastRescueCarId);
            Assert.AreEqual(V(1, 2), sim.LastRescueTile);
            Assert.AreEqual(
                0,
                sim.GetCar(0).TileIndex,
                "L3는 막힌 중간 큐를 버리고 원래 출발 타일에서 트립을 재시작한다");
        }

        [Test]
        public void LivenessWatchdog_TemporaryBlockBelowL2_DoesNotActivate()
        {
            BuildWatchdogCity(
                out _,
                out _,
                out _,
                out _,
                out RoadQueueNetwork net,
                out CarSim sim,
                out SimEventBuffer events);

            sim.Step(7f, net, events);
            Assert.IsTrue(net.TryEnqueue(V(3, 2), Dir.E, 90));
            Assert.IsTrue(net.TryEnqueue(V(3, 2), Dir.E, 91));

            for (int tick = 0; tick < Cfg().GridlockValveTicks * 3 - 1; tick++)
                sim.Step(7f, net, events);
            Assert.AreEqual(0, sim.RescueRerouteCount);
            Assert.AreEqual(0, sim.RescueRestartCount);

            Assert.IsTrue(net.TryRemoveCarForRescue(90));
            Assert.IsTrue(net.TryRemoveCarForRescue(91));
            for (int tick = 0;
                 tick < 8 && sim.GetCar(0).State == CarState.Outbound;
                 tick++)
            {
                sim.Step(7f, net, events);
            }

            Assert.AreEqual(CarState.ParkedWork, sim.GetCar(0).State);
            Assert.AreEqual(0, sim.RescueRerouteCount);
            Assert.AreEqual(0, sim.RescueRestartCount);
        }

        [Test]
        public void Rebuild_RemovedHome_CarFinishesCurrentTripBeforeRetiring()
        {
            BuildRetirementCity(
                out CityGrid grid,
                out RoadNetwork road,
                out DemandMap demands,
                out RoutePlanner planner,
                out RoadQueueNetwork net,
                out CarSim sim,
                out SimEventBuffer events);

            sim.Step(7f, net, events);
            Assert.AreEqual(CarState.Outbound, sim.GetCar(0).State);

            Assert.IsTrue(grid.Remove(V(0, 0)));
            RebuildCarTopology(grid, road, demands, planner, net, sim);

            Assert.AreEqual(1, sim.CarCount, "집 철거 리빌드가 주행 차를 즉시 소멸시키면 안 된다");
            for (int tick = 0;
                 tick < 8 && sim.GetCar(0).State == CarState.Outbound;
                 tick++)
            {
                sim.Step(7f, net, events);
            }
            Assert.AreEqual(
                CarState.ParkedWork,
                sim.GetCar(0).State,
                "HomeLost 차는 철거 전 출근 경로와 보상 의미를 유지해 현재 트립을 완주한다");

            RebuildCarTopology(grid, road, demands, planner, net, sim);
            Assert.AreEqual(0, sim.CarCount, "안전한 주차 경계 다음 리빌드에서만 은퇴한다");
        }

        [Test]
        public void Rebuild_RemovedWork_OutboundCarTurnsBackAndReturnsHome()
        {
            BuildRetirementCity(
                out CityGrid grid,
                out RoadNetwork road,
                out DemandMap demands,
                out RoutePlanner planner,
                out RoadQueueNetwork net,
                out CarSim sim,
                out SimEventBuffer events);

            sim.Step(7f, net, events);
            Assert.AreEqual(CarState.Outbound, sim.GetCar(0).State);

            Assert.IsTrue(grid.Remove(V(4, 0)));
            RebuildCarTopology(grid, road, demands, planner, net, sim);

            Assert.AreEqual(1, sim.CarCount);
            Assert.AreEqual(
                CarState.Inbound,
                sim.GetCar(0).State,
                "WorkLost 출근 차는 보상 가능한 출근을 계속하지 않고 즉시 포기 귀가로 전환한다");
            for (int tick = 0;
                 tick < 8 && sim.GetCar(0).State == CarState.Inbound;
                 tick++)
            {
                sim.Step(7f, net, events);
            }
            Assert.AreEqual(CarState.ParkedHome, sim.GetCar(0).State);
        }

        [Test]
        public void Rebuild_AddedHome_NewAssignmentWaitsUntilNextMorningWave()
        {
            SimConfig cfg = Cfg();
            var grid = new CityGrid(6, 3);
            for (int x = 0; x <= 5; x++)
                Assert.IsTrue(grid.Place(V(x, 2), TileType.Road));
            Assert.IsTrue(grid.Place(V(4, 0), TileType.Office));
            var road = new RoadNetwork(grid);
            var demands = new DemandMap(cfg);
            demands.Reassign(grid, road);
            var planner = new RoutePlanner(grid.Width, grid.Height);
            planner.Plan(demands, road, grid, cfg);
            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);
            var sim = new CarSim(cfg);
            sim.Rebuild(demands, planner, net);
            var events = new SimEventBuffer(new SimEventHub());

            Assert.AreEqual(0, sim.CarCount);
            Assert.IsTrue(grid.Place(V(0, 0), TileType.House));
            RebuildCarTopology(grid, road, demands, planner, net, sim);

            sim.Step(7f, net, events);
            Assert.AreEqual(1, sim.CarCount);
            Assert.AreEqual(
                CarState.ParkedHome,
                sim.GetCar(0).State,
                "이미 시작된 아침 웨이브에 신규 배정을 소급 출발시키면 안 된다");
            sim.Step(7f, net, events);
            Assert.AreEqual(CarState.ParkedHome, sim.GetCar(0).State);

            sim.Step(0f, net, events);
            sim.Step(7f, net, events);
            Assert.AreEqual(
                CarState.Outbound,
                sim.GetCar(0).State,
                "다음 날 출발 시각 경계를 지난 뒤 신규 배정이 활성화되어야 한다");
        }

        private static void BuildRetirementCity(
            out CityGrid grid,
            out RoadNetwork road,
            out DemandMap demands,
            out RoutePlanner planner,
            out RoadQueueNetwork net,
            out CarSim sim,
            out SimEventBuffer events)
        {
            SimConfig cfg = Cfg();
            grid = new CityGrid(6, 3);
            for (int x = 0; x <= 5; x++)
                Assert.IsTrue(grid.Place(V(x, 2), TileType.Road));
            Assert.IsTrue(grid.Place(V(0, 0), TileType.House));
            Assert.IsTrue(grid.Place(V(4, 0), TileType.Office));
            road = new RoadNetwork(grid);
            demands = new DemandMap(cfg);
            demands.Reassign(grid, road);
            planner = new RoutePlanner(grid.Width, grid.Height);
            planner.Plan(demands, road, grid, cfg);
            net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);
            sim = new CarSim(cfg);
            sim.Rebuild(demands, planner, net);
            events = new SimEventBuffer(new SimEventHub());
        }

        private static void RebuildCarTopology(
            CityGrid grid,
            RoadNetwork road,
            DemandMap demands,
            RoutePlanner planner,
            RoadQueueNetwork net,
            CarSim sim)
        {
            demands.Reassign(grid, road);
            planner.Plan(demands, road, grid, Cfg());
            net.RebuildTopology(grid);
            sim.Rebuild(demands, planner, net);
        }

        private static void BuildWatchdogCity(
            out CityGrid grid,
            out RoadNetwork road,
            out DemandMap demands,
            out RoutePlanner planner,
            out RoadQueueNetwork net,
            out CarSim sim,
            out SimEventBuffer events)
        {
            SimConfig cfg = Cfg();
            grid = new CityGrid(6, 4);
            for (int x = 0; x <= 5; x++)
                Assert.IsTrue(grid.Place(V(x, 2), TileType.Road));
            for (int x = 1; x <= 4; x++)
                Assert.IsTrue(grid.Place(V(x, 3), TileType.Road));
            // The upper detour already makes (2,2) an intersection. A south spur at
            // (2,1) would also become the 2x2 house's first frontage and bypass the
            // intended ordinary approach tiles before the watchdog blockade.
            Assert.IsTrue(grid.Place(V(0, 0), TileType.House));
            Assert.IsTrue(grid.Place(V(4, 0), TileType.Office));
            Assert.IsTrue(grid.IsIntersection(V(2, 2)));

            road = new RoadNetwork(grid);
            demands = new DemandMap(cfg);
            demands.Reassign(grid, road);
            planner = new RoutePlanner(grid.Width, grid.Height);
            planner.Plan(demands, road, grid, cfg);
            CollectionAssert.AreEqual(
                new[] { V(0, 2), V(1, 2), V(2, 2), V(3, 2), V(4, 2) },
                planner.CarRoutes[0],
                "전제: 최초 경로는 짧은 직선 교차로 경로");

            net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);
            sim = new CarSim(cfg);
            sim.Rebuild(demands, planner, net);
            events = new SimEventBuffer(new SimEventHub());
        }

        private static void BuildStraightCity(
            out CityGrid grid,
            out DemandMap demands,
            out RoutePlanner planner)
        {
            SimConfig cfg = Cfg();
            grid = new CityGrid(6, 3);
            for (int x = 0; x <= 5; x++) Assert.IsTrue(grid.Place(V(x, 2), TileType.Road));
            Assert.IsTrue(grid.Place(V(0, 0), TileType.House));
            Assert.IsTrue(grid.Place(V(2, 0), TileType.House));
            Assert.IsTrue(grid.Place(V(4, 0), TileType.Office));
            var road = new RoadNetwork(grid);
            demands = new DemandMap(cfg);
            demands.Reassign(grid, road);
            planner = new RoutePlanner(grid.Width, grid.Height);
            planner.Plan(demands, road, grid, cfg);
            Assert.AreEqual(2, demands.Demands.Count);
        }

        private sealed class ResumeRouteProvider : ICarRouteProvider
        {
            private readonly Dictionary<int, List<Vector2Int>> _routes = new();

            public void Add(int carId, params Vector2Int[] route) =>
                _routes.Add(carId, new List<Vector2Int>(route));

            public IReadOnlyList<Vector2Int> RouteFor(int carId) => _routes[carId];

            public bool TryGetNextTile(
                int carId,
                Vector2Int current,
                out Vector2Int next,
                out Dir entryDirAtNext)
            {
                List<Vector2Int> route = _routes[carId];
                for (int i = 0; i < route.Count - 1; i++)
                {
                    if (route[i] != current) continue;
                    next = route[i + 1];
                    Vector2Int delta = next - current;
                    entryDirAtNext = delta == Vector2Int.right
                        ? Dir.E
                        : delta == Vector2Int.left
                            ? Dir.W
                            : delta == Vector2Int.up
                                ? Dir.N
                                : Dir.S;
                    return true;
                }
                next = default;
                entryDirAtNext = default;
                return false;
            }

            public bool IsDestination(int carId, Vector2Int tile)
            {
                List<Vector2Int> route = _routes[carId];
                return route[route.Count - 1] == tile;
            }
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
