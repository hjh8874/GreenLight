using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public class CarSimTests
    {
        // SpecialVisitRewardTests 가 하니스를 재사용한다 — internal 로 연다.
        internal static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        // 종전 전역 창(morningStart/End·eveningStart/End)을 콜백 하나로 재현한다.
        static CommuteWindow Window(
            float morningStart, float morningEnd, float eveningStart, float eveningEnd) =>
            new CommuteWindow(
                string.Empty,
                morningStart, morningEnd - morningStart,
                eveningStart, eveningEnd - eveningStart);

        // 버스 테스트가 재사용한다(#184) — internal 유지.
        internal static SimConfig Cfg()
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
                windowFor: _ => Window(6f, 7f, 17f, 18f),
                homeSlots: 2,
                maxCars: 96);

            Assert.AreEqual(2, scheduler.Cars.Count);
            Assert.AreEqual(0, scheduler.Cars[0].HomeSlot);
            Assert.AreEqual(1, scheduler.Cars[1].HomeSlot);
        }

        // [밸런스 2026-07-31] 집 1채는 회사 통근자를 CarsPerHouse명 낸다.
        // 종전(집당 1명)은 회사 좌석을 늘려도 차가 안 늘던 병목이었다.
        [Test]
        public void Demands_OfficeCommutersPerHouse_FollowCarsPerHouse()
        {
            SimConfig cfg = Cfg();
            cfg.CarsPerHouse = 2;
            cfg.OfficeCapacity = 4;
            var grid = new CityGrid(7, 4);
            for (int x = 1; x <= 5; x++) Assert.IsTrue(grid.Place(V(x, 2), TileType.Road));
            Assert.IsTrue(grid.Place(V(0, 0), TileType.House));
            Assert.IsTrue(grid.Place(V(5, 0), TileType.Office));
            var road = new RoadNetwork(grid);
            var demands = new DemandMap(cfg);

            demands.Reassign(grid, road);

            int officeDemands = 0;
            foreach (var d in demands.Demands)
                if (d.SinkType == TileType.Office) officeDemands++;
            Assert.AreEqual(2, officeDemands, "집 1채 = 회사 통근자 CarsPerHouse(2)명");
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

        // 설계 변경 2026-08-03: 교차로 원점에서도 스테이지를 부여해 정식 진입한다.
        // 옛 계약("신규 출발도 오프네트워크 대기")은 진입로가 교차로뿐인 건물의 통근차를
        // 영구 스톨시켰다. 재개(resume) 경로는 그대로 보수적이다 — RebuildResume_* 참조.
        [Test]
        public void Departure_IntersectionRouteOrigin_EntersWithStage()
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
            bool hasResume = false;

            Assert.IsTrue(CarSim.TryEnqueueRouteStart(
                routes.RouteFor(12),
                default,
                ref hasResume,
                net,
                12,
                out int start));
            Assert.AreEqual(0, start, "출발은 경로 원점에서 시작한다 — 앞 타일로 밀지 않는다");
            Assert.AreEqual(1, TotalQueued(net, grid.Width, grid.Height),
                "교차로 원점에서도 스테이지를 부여해 네트워크에 올라간다");
        }

        // D4 회귀 방지: 재개(resume)는 교차로 출발 스폰 경로를 타지 않는다.
        // CarSim.cs:1269-1276 이 안전한 이전 타일이 없으면 retryingResume 을 끄고 start=0 으로
        // 되돌리는데, 그 상태를 신규 출발로 오인하면 재개가 교차로에 앉는다(리뷰 P0-4).
        [Test]
        public void RebuildResume_IntersectionOrigin_DoesNotUseDepartureSpawn()
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
            bool hasResume = true;   // 재개 요청

            Assert.IsFalse(CarSim.TryEnqueueRouteStart(
                routes.RouteFor(12),
                V(2, 2),
                ref hasResume,
                net,
                12,
                out _));
            Assert.IsFalse(hasResume, "안전한 이전 타일이 없으면 중간 재개를 포기한다");
            Assert.AreEqual(0, TotalQueued(net, grid.Width, grid.Height),
                "재개는 교차로 스폰을 쓰지 않는다 — 출발 경로와 분리돼 있어야 한다");
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
            Assert.AreEqual(1, sim.GetCar(0).TileIndex, "전제: 차단 타일 상류까지 전진");
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

        // 리뷰 지적(2026-07-24 abicodue): preserve 리빌드가 진행 중 rescue 상태를 지우면
        // rescue 경로 위의 ResumeTile을 일반 경로에서 못 찾아 route[0] 순간이동이 재발한다.
        // L2 우회 주행 중 무관한 건물을 배치(preserve 리빌드)해도 rescue 경로·뷰 인덱스·
        // 위치가 유지되어야 한다.
        [Test]
        public void LivenessRescue_SurvivesUnrelatedBuildingPreserveRebuild()
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
            Assert.IsTrue(grid.Remove(V(2, 2)), "직선 경로를 철거해 위쪽 우회로만 남긴다");
            planner.Plan(demands, road, grid, Cfg());
            for (int tick = 0; tick < Cfg().GridlockValveTicks * 3; tick++)
                sim.Step(7f, net, events);
            Assert.AreEqual(1, sim.RescueRerouteCount, "전제: L2 rescue 발동");
            sim.Step(7f, net, events);
            sim.Step(7f, net, events);

            CarSnapshot beforeSnap = sim.GetCar(0);
            int rescueViewIndex = beforeSnap.RouteIndex;
            Assert.GreaterOrEqual(
                rescueViewIndex, planner.CarRoutes.Count, "전제: rescue 뷰 경로 사용 중");
            List<Vector2Int> rescueRoute = sim.ActiveRoutes[rescueViewIndex];
            Vector2Int beforeTile = rescueRoute[
                Mathf.Clamp(beforeSnap.TileIndex, 0, rescueRoute.Count - 1)];
            Assert.AreEqual(3, beforeTile.y, "전제: 차가 우회로(y=3) 위에 있다");

            // 무관한 건물 배치 → SimEngine이라면 preserve 리빌드를 태우는 상황.
            Assert.IsTrue(grid.Place(V(2, 0), TileType.House));
            demands.Reassign(grid, road);
            planner.Plan(demands, road, grid, Cfg());
            net.RebuildTopology(grid);
            sim.Rebuild(demands, planner, net, preserveExistingAssignments: true);
            sim.Step(7f, net, events);

            Assert.AreEqual(2, sim.CarCount, "기존 차 + 신규 짝");
            CarSnapshot afterSnap = sim.GetCar(0);
            Assert.AreEqual(
                rescueViewIndex,
                afterSnap.RouteIndex,
                "preserve 리빌드 후에도 rescue 뷰 경로 인덱스가 유지되어야 한다");
            CollectionAssert.DoesNotContain(
                sim.ActiveRoutes[afterSnap.RouteIndex],
                V(2, 2),
                "유지된 경로는 여전히 철거 타일을 피하는 rescue 우회 경로다");
            List<Vector2Int> afterRoute = sim.ActiveRoutes[afterSnap.RouteIndex];
            Vector2Int afterTile = afterRoute[
                Mathf.Clamp(afterSnap.TileIndex, 0, afterRoute.Count - 1)];
            int jump = Mathf.Abs(afterTile.x - beforeTile.x)
                + Mathf.Abs(afterTile.y - beforeTile.y);
            Assert.LessOrEqual(
                jump, 1, $"rescue 주행 위치 {afterTile}는 직전 위치 {beforeTile} 근방이어야 한다");
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
            var hub = new SimEventHub();
            events = new SimEventBuffer(hub);
            var completedTrips = new List<VehicleTripSnapshot>();
            hub.VehicleTripArrived += message =>
                completedTrips.Add(message.Trip);

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
            events.Drain();
            Assert.AreEqual(1, completedTrips.Count);
            Assert.AreEqual(V(0, 0), completedTrips[0].Destination);
            Assert.AreEqual(0, completedTrips[0].RewardCoins);
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

        [Test]
        public void Rebuild_AddedHomeDuringEvening_NewAssignmentWaitsAtHome()
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

            sim.Step(17.5f, net, events);

            Assert.AreEqual(1, sim.CarCount);
            CarSnapshot deferred = sim.GetCar(0);
            Assert.AreEqual(
                CarState.ParkedHome,
                deferred.State,
                "저녁에 생긴 신규 배정은 방문하지 않은 회사에서 퇴근하면 안 된다");
            Assert.IsTrue(deferred.AwaitingNextWave);
            Assert.AreEqual(
                0,
                TotalQueued(net, grid.Width, grid.Height),
                "대기 중인 신규 배정은 출근·퇴근 어느 큐에도 들어가면 안 된다");

            sim.Step(0f, net, events);
            Assert.IsFalse(sim.GetCar(0).AwaitingNextWave);
            Assert.AreEqual(CarState.ParkedHome, sim.GetCar(0).State);
            sim.Step(7f, net, events);
            Assert.AreEqual(
                CarState.Outbound,
                sim.GetCar(0).State,
                "다음 날 출근 전 구간을 관측한 뒤에만 신규 배정이 출근해야 한다");
        }

        [Test]
        public void Rebuild_TransientStorageDoesNotReduceRoutineCapacity()
        {
            var scheduler = new CommuteScheduler();
            var sources = new List<Vector2Int>
            {
                V(0, 0),
                V(1, 0),
                V(2, 0),
                V(3, 0)
            };
            var sinks = new List<Vector2Int>
            {
                V(0, 2),
                V(1, 2),
                V(2, 2),
                V(3, 2)
            };

            scheduler.Rebuild(
                sources,
                sinks,
                _ => 1,
                _ => Window(6f, 7f, 17f, 18f),
                homeSlots: 1,
                maxCars: 4,
                transientStorageCapacity: 2);

            Assert.AreEqual(
                4,
                scheduler.Cars.Count,
                "Unused special-trip storage must not reduce routine cars.");
            Assert.AreEqual(4, scheduler.ActiveCount);

            CommuteCar routineOwner = scheduler.Cars[0];
            CommuteCar transient = scheduler.AcquireTransient(
                routineOwner.Home,
                maxCars: 6);
            Assert.IsNotNull(transient);
            routineOwner.SetSpecialTripReservation(true);

            scheduler.Rebuild(
                sources,
                sinks,
                _ => 1,
                _ => Window(6f, 7f, 17f, 18f),
                homeSlots: 1,
                maxCars: 4,
                transientStorageCapacity: 2);

            Assert.AreEqual(
                5,
                scheduler.Cars.Count,
                "The transient object should use the storage-only capacity.");
            Assert.AreEqual(
                4,
                scheduler.ActiveCount,
                "A hidden routine owner and its transient replacement count as one active vehicle.");

            scheduler.ReleaseTransient(transient);
            routineOwner.SetSpecialTripReservation(false);
            Assert.AreEqual(4, scheduler.ActiveCount);
        }

        [Test]
        public void VehicleTrip_TransitionsThroughDrivingAndArrival()
        {
            var trip = new VehicleTrip(
                "trip-1",
                "journey-1",
                0,
                V(1, 2),
                V(7, 2),
                VehicleTripPurpose.SpecialBuildingVisit,
                "coffee-shop",
                0);

            Assert.AreEqual(VehicleTripState.Queued, trip.State);
            Assert.IsTrue(trip.TryBeginRouting());
            Assert.IsTrue(trip.TryBeginDriving());
            Assert.IsTrue(trip.TryArrive());
            Assert.IsFalse(trip.TryCancel());
            Assert.AreEqual(VehicleTripState.Arrived, trip.State);
        }

        [Test]
        public void TripScheduler_DeduplicatesAndTakesRequestsInStableOrder()
        {
            var scheduler = new TripScheduler(8, 2);
            var later = new SpecialBuildingVisitTripRequest(
                "mall",
                V(8, 1),
                3L,
                1,
                14f,
                rewardCoins: 0);
            var earlier = new SpecialBuildingVisitTripRequest(
                "coffee-shop",
                V(4, 1),
                3L,
                0,
                9f,
                rewardCoins: 0);

            Assert.IsTrue(scheduler.TryEnqueue(later));
            Assert.IsTrue(scheduler.TryEnqueue(earlier));
            Assert.IsFalse(scheduler.TryEnqueue(earlier));
            Assert.IsTrue(scheduler.TryTakeDue(3L, 10f, out var taken));
            Assert.AreEqual(earlier.BuildingId, taken.BuildingId);
            Assert.AreEqual(1, scheduler.PendingCount);

            scheduler.Requeue(taken, 100, 10);
            Assert.IsFalse(scheduler.TryTakeDue(3L, 10f, 109, out _));
            scheduler.AllowImmediateRetry();
            Assert.IsTrue(scheduler.TryTakeDue(3L, 10f, 109, out taken));
            Assert.AreEqual(earlier.BuildingId, taken.BuildingId);
        }

        [Test]
        public void CommuteScheduler_UsesConfiguredTransientStorageCapacity()
        {
            var scheduler = new CommuteScheduler();
            var homes = new List<Vector2Int>();
            var works = new List<Vector2Int>();
            for (int index = 0; index < 10; index++)
            {
                homes.Add(V(index, 0));
                works.Add(V(index, 2));
            }

            scheduler.Rebuild(
                homes,
                works,
                _ => 1,
                _ => Window(6f, 7f, 17f, 18f),
                1,
                10,
                transientStorageCapacity: 2);

            Assert.AreEqual(10, scheduler.Cars.Count);

            CommuteCar firstOwner = scheduler.Cars[0];
            CommuteCar firstTransient = scheduler.AcquireTransient(
                firstOwner.Home,
                maxCars: 12);
            Assert.NotNull(firstTransient);
            firstOwner.SetSpecialTripReservation(true);

            CommuteCar secondOwner = scheduler.Cars[1];
            CommuteCar secondTransient = scheduler.AcquireTransient(
                secondOwner.Home,
                maxCars: 12);
            Assert.NotNull(secondTransient);
            secondOwner.SetSpecialTripReservation(true);

            Assert.AreEqual(12, scheduler.Cars.Count);
            Assert.AreEqual(10, scheduler.ActiveCount);
            Assert.IsNull(scheduler.AcquireTransient(V(2, 0), 12));
        }

        [Test]
        public void SpecialVisit_DuringWorkWindow_DoesNotDelayDirectCommute()
        {
            SimConfig config = Cfg();
            config.MaxSimCars = 16;
            config.MaxPendingVehicleTrips = 16;
            config.MaxConcurrentSpecialTrips = 2;
            BuildSpecialVisitCity(
                config,
                out CityGrid grid,
                out RoadNetwork roads,
                out DemandMap demands,
                out RoutePlanner planner,
                out RoadQueueNetwork queues);
            var sim = new CarSim(config);
            sim.Rebuild(
                demands,
                planner,
                queues,
                grid: grid,
                roadNetwork: roads);
            var hub = new SimEventHub();
            var events = new SimEventBuffer(hub);
            var completed = new List<VehicleTripSnapshot>();
            int paidArrivals = 0;
            int paidCoins = 0;
            hub.VehicleTripArrived += message => completed.Add(message.Trip);
            hub.Arrival += message =>
            {
                paidArrivals++;
                paidCoins += message.Coins;
            };

            Assert.IsTrue(sim.TryScheduleSpecialBuildingVisit(
                new SpecialBuildingVisitTripRequest(
                    "coffee-shop",
                    V(6, 0),
                    1L,
                    0,
                    7f,
                    rewardCoins: 0)));

            sim.Step(1L, 7f, queues, events, null, 0);
            events.Drain();
            Assert.IsFalse(sim.AllParkedHome);

            for (int tick = 1; tick < 80; tick++)
            {
                sim.Step(1L, 7f, queues, events, null, tick);
                events.Drain();
            }

            List<VehicleTripSnapshot> specialTrips = completed.FindAll(trip =>
                trip.Purpose == VehicleTripPurpose.SpecialBuildingVisit);
            List<VehicleTripSnapshot> commuteTrips = completed.FindAll(trip =>
                trip.Purpose == VehicleTripPurpose.Commute);
            Assert.AreEqual(2, specialTrips.Count);
            Assert.AreEqual(0, specialTrips[0].LegIndex);
            Assert.AreEqual(V(6, 0), specialTrips[0].Destination);
            Assert.AreEqual(1, specialTrips[1].LegIndex);
            Assert.AreEqual(V(0, 0), specialTrips[1].Destination);
            Assert.AreEqual(0, specialTrips[0].RewardCoins);
            Assert.AreEqual(1, commuteTrips.Count);
            Assert.AreEqual(V(10, 0), commuteTrips[0].Destination);
            Assert.AreEqual(1, paidArrivals);
            Assert.AreEqual(config.CoinPerTrip, paidCoins);
            Assert.AreEqual(0, sim.PendingTripCount);
            Assert.AreEqual(0, sim.ActiveTripCount);
        }

        [Test]
        public void SpecialVisit_AfterWork_RoutesFromWorkThroughVisitToHome()
        {
            SimConfig config = Cfg();
            config.MaxSimCars = 16;
            config.MaxPendingVehicleTrips = 16;
            config.MaxConcurrentSpecialTrips = 2;
            BuildSpecialVisitCity(
                config,
                out CityGrid grid,
                out RoadNetwork roads,
                out DemandMap demands,
                out RoutePlanner planner,
                out RoadQueueNetwork queues);
            var sim = new CarSim(config);
            sim.Rebuild(
                demands,
                planner,
                queues,
                grid: grid,
                roadNetwork: roads);
            var hub = new SimEventHub();
            var events = new SimEventBuffer(hub);
            var completed = new List<VehicleTripSnapshot>();
            hub.VehicleTripArrived += message => completed.Add(message.Trip);

            for (int tick = 0; tick < 40; tick++)
            {
                sim.Step(1L, 7f, queues, events, null, tick);
                events.Drain();
            }

            Assert.AreEqual(CarState.ParkedWork, sim.GetCar(0).State);
            completed.Clear();
            Assert.IsTrue(sim.TryScheduleSpecialBuildingVisit(
                new SpecialBuildingVisitTripRequest(
                    "coffee-shop",
                    V(6, 0),
                    1L,
                    0,
                    17.5f,
                    rewardCoins: 0)));

            for (int tick = 40; tick < 120; tick++)
            {
                sim.Step(1L, 17.5f, queues, events, null, tick);
                events.Drain();
            }

            List<VehicleTripSnapshot> specialTrips = completed.FindAll(trip =>
                trip.Purpose == VehicleTripPurpose.SpecialBuildingVisit);
            Assert.AreEqual(2, specialTrips.Count);
            Assert.AreEqual(V(10, 0), specialTrips[0].Origin);
            Assert.AreEqual(V(6, 0), specialTrips[0].Destination);
            Assert.AreEqual(V(6, 0), specialTrips[1].Origin);
            Assert.AreEqual(V(0, 0), specialTrips[1].Destination);
            Assert.IsTrue(sim.AllParkedHome);
        }

        [Test]
        public void SpecialVisit_DuringWork_RoutesFromWorkThroughVisitBackToWork()
        {
            SimConfig config = Cfg();
            config.MaxSimCars = 16;
            config.MaxPendingVehicleTrips = 16;
            config.MaxConcurrentSpecialTrips = 2;
            BuildSpecialVisitCity(
                config,
                out CityGrid grid,
                out RoadNetwork roads,
                out DemandMap demands,
                out RoutePlanner planner,
                out RoadQueueNetwork queues);
            var sim = new CarSim(config);
            sim.Rebuild(demands, planner, queues, grid: grid, roadNetwork: roads);
            var hub = new SimEventHub();
            var events = new SimEventBuffer(hub);
            var completed = new List<VehicleTripSnapshot>();
            hub.VehicleTripArrived += message => completed.Add(message.Trip);

            for (int tick = 0; tick < 40; tick++)
            {
                sim.Step(1L, 7f, queues, events, null, tick);
                events.Drain();
            }

            Assert.AreEqual(CarState.ParkedWork, sim.GetCar(0).State);
            completed.Clear();

            // 7→12시를 점프 없이 증분 — 1시간 초과 점프는 SnapToHour가 차를 집으로
            // 재주차시켜 근무 중 전제가 무너진다(감독 진단).
            for (int hour = 8; hour <= 11; hour++)
            {
                sim.Step(1L, hour, queues, events, null, 40 + hour);
                events.Drain();
            }
            Assert.AreEqual(CarState.ParkedWork, sim.GetCar(0).State);

            Assert.IsTrue(sim.TryScheduleSpecialBuildingVisit(
                new SpecialBuildingVisitTripRequest(
                    "coffee-shop", V(6, 0), 1L, 0, 12f, rewardCoins: 0)));

            // 탈출 조건은 특수 방문 도착만 센다 — 통근 도착이 섞이면 조기 탈출한다(감독 진단).
            List<VehicleTripSnapshot> specialTrips = new List<VehicleTripSnapshot>();
            for (int tick = 60; tick < 200 && specialTrips.Count < 2; tick++)
            {
                sim.Step(1L, 12f, queues, events, null, tick);
                events.Drain();
                specialTrips = completed.FindAll(trip =>
                    trip.Purpose == VehicleTripPurpose.SpecialBuildingVisit);
            }

            Assert.AreEqual(2, specialTrips.Count);
            Assert.AreEqual(V(10, 0), specialTrips[0].Origin);
            Assert.AreEqual(V(6, 0), specialTrips[0].Destination);
            Assert.AreEqual(V(6, 0), specialTrips[1].Origin);
            Assert.AreEqual(V(10, 0), specialTrips[1].Destination);
            Assert.AreEqual(CarState.ParkedWork, sim.GetCar(0).State);
        }

        [Test]
        public void SpecialVisit_DuringWorkCrossingDepartureHour_RestoresWorkThenStartsLateCommute()
        {
            SimConfig config = Cfg();
            config.MaxSimCars = 16;
            config.MaxPendingVehicleTrips = 16;
            config.MaxConcurrentSpecialTrips = 2;
            BuildSpecialVisitCity(
                config,
                out CityGrid grid,
                out RoadNetwork roads,
                out DemandMap demands,
                out RoutePlanner planner,
                out RoadQueueNetwork queues);
            var sim = new CarSim(config);
            sim.Rebuild(demands, planner, queues, grid: grid, roadNetwork: roads);
            var hub = new SimEventHub();
            var events = new SimEventBuffer(hub);
            var completed = new List<VehicleTripSnapshot>();
            hub.VehicleTripArrived += message => completed.Add(message.Trip);

            for (int tick = 0; tick < 40; tick++)
            {
                sim.Step(1L, 7f, queues, events, null, tick);
                events.Drain();
            }

            completed.Clear();
            Assert.IsTrue(sim.TryScheduleSpecialBuildingVisit(
                new SpecialBuildingVisitTripRequest(
                    "coffee-shop", V(6, 0), 1L, 0, 16.9f, rewardCoins: 0)));

            int completionTick = -1;
            for (int tick = 40; tick < 220 && completed.Count < 2; tick++)
            {
                sim.Step(1L, 16.9f + (tick - 40) * 0.1f, queues, events, null, tick);
                events.Drain();
                if (completed.Count >= 2)
                {
                    completionTick = tick;
                    Assert.AreEqual(CarState.ParkedWork, sim.GetCar(0).State);
                }
            }

            Assert.GreaterOrEqual(completionTick, 0, "점심 왕복 복귀가 퇴근시각 이후에도 완료되어야 한다");
            Assert.AreEqual(V(10, 0), completed[1].Destination);

            sim.Step(
                1L,
                16.9f + (completionTick - 40 + 1) * 0.1f,
                queues,
                events,
                null,
                completionTick + 1);
            events.Drain();
            Assert.AreNotEqual(
                CarState.ParkedWork,
                sim.GetCar(0).State,
                "복귀 후 ParkedWork가 복원되면 기존 퇴근 조건으로 늦은 출발이 시작되어야 한다");
        }

        [Test]
        public void SpecialVisit_FromHome_DuringWorkWindow_RemainsUnavailable()
        {
            SimConfig config = Cfg();
            config.MaxSimCars = 16;
            config.MaxPendingVehicleTrips = 16;
            config.MaxConcurrentSpecialTrips = 2;
            BuildSpecialVisitCity(
                config,
                out CityGrid grid,
                out RoadNetwork roads,
                out DemandMap demands,
                out RoutePlanner planner,
                out RoadQueueNetwork queues);
            var sim = new CarSim(config);
            sim.Rebuild(demands, planner, queues, grid: grid, roadNetwork: roads);

            Assert.IsTrue(sim.TryScheduleSpecialBuildingVisit(
                new SpecialBuildingVisitTripRequest(
                    "coffee-shop", V(6, 0), 1L, 0, 6.5f, rewardCoins: 0)));
            sim.Step(1L, 6.5f, queues, new SimEventBuffer(new SimEventHub()), null, 0);

            Assert.AreEqual(CarState.Outbound, sim.GetCar(0).State);
            Assert.AreEqual(V(0, 0), sim.GetCar(0).Home);
            Assert.AreEqual(V(10, 0), sim.GetCar(0).Work);
        }

        [Test]
        public void SpecialVisit_SharedAccessRoad_CompletesBothLegs()
        {
            SimConfig config = Cfg();
            config.MaxSimCars = 8;
            config.MaxPendingVehicleTrips = 8;
            config.MaxConcurrentSpecialTrips = 2;
            var grid = new CityGrid(6, 3);
            Assert.IsTrue(grid.Place(V(2, 0), TileType.Road));
            Assert.IsTrue(grid.Place(V(0, 0), TileType.House));
            Assert.IsTrue(
                grid.Place(V(3, 0), TileType.SpecialBuilding));
            var roads = new RoadNetwork(grid);
            var demands = new DemandMap(config);
            demands.Reassign(grid, roads);
            var planner = new RoutePlanner(grid.Width, grid.Height);
            planner.Plan(demands, roads, grid, config);
            var queues = new RoadQueueNetwork(
                grid.Width,
                grid.Height,
                config);
            queues.RebuildTopology(grid);
            var sim = new CarSim(config);
            sim.Rebuild(
                demands,
                planner,
                queues,
                grid: grid,
                roadNetwork: roads);
            var hub = new SimEventHub();
            var events = new SimEventBuffer(hub);
            var completed = new List<VehicleTripSnapshot>();
            hub.VehicleTripArrived += message => completed.Add(message.Trip);

            Assert.IsTrue(sim.TryScheduleSpecialBuildingVisit(
                new SpecialBuildingVisitTripRequest(
                    "coffee-shop",
                    V(3, 0),
                    1L,
                    0,
                    1f,
                    rewardCoins: 0)));

            for (int tick = 0; tick < 8; tick++)
            {
                sim.Step(1L, 1f, queues, events, null, tick);
                events.Drain();
            }

            Assert.AreEqual(2, completed.Count);
            Assert.AreEqual(V(3, 0), completed[0].Destination);
            Assert.AreEqual(V(0, 0), completed[1].Destination);
            Assert.AreEqual(0, sim.PendingTripCount);
            Assert.AreEqual(0, sim.ActiveTripCount);
        }

        [Test]
        public void SpecialVisit_NoRoute_RemainsPendingUntilTopologyRebuild()
        {
            SimConfig config = Cfg();
            config.MaxSimCars = 8;
            config.MaxPendingVehicleTrips = 8;
            config.MaxConcurrentSpecialTrips = 2;
            var grid = new CityGrid(14, 4);
            for (int x = 0; x < grid.Width; x++)
            {
                if (x != 6)
                {
                    Assert.IsTrue(grid.Place(V(x, 2), TileType.Road));
                }
            }

            Assert.IsTrue(grid.Place(V(0, 0), TileType.House));
            Assert.IsTrue(grid.Place(V(10, 0), TileType.SpecialBuilding));
            Assert.IsTrue(grid.Place(V(4, 0), TileType.Office));
            var roads = new RoadNetwork(grid);
            var demands = new DemandMap(config);
            demands.Reassign(grid, roads);
            var planner = new RoutePlanner(grid.Width, grid.Height);
            planner.Plan(demands, roads, grid, config);
            var queues = new RoadQueueNetwork(
                grid.Width,
                grid.Height,
                config);
            queues.RebuildTopology(grid);
            var sim = new CarSim(config);
            sim.Rebuild(
                demands,
                planner,
                queues,
                grid: grid,
                roadNetwork: roads);
            var events = new SimEventBuffer(new SimEventHub());

            Assert.IsTrue(sim.TryScheduleSpecialBuildingVisit(
                new SpecialBuildingVisitTripRequest(
                    "cinema",
                    V(10, 0),
                    1L,
                    0,
                    1f,
                    rewardCoins: 0)));
            sim.Step(1L, 1f, queues, events, null, 0);
            Assert.AreEqual(1, sim.PendingTripCount);
            Assert.AreEqual(0, sim.ActiveTripCount);

            Assert.IsTrue(grid.Place(V(6, 2), TileType.Road));
            demands.Reassign(grid, roads);
            planner.Plan(demands, roads, grid, config);
            queues.RebuildTopology(grid);
            sim.Rebuild(
                demands,
                planner,
                queues,
                grid: grid,
                roadNetwork: roads);
            sim.Step(1L, 1f, queues, events, null, 1);

            Assert.AreEqual(0, sim.PendingTripCount);
            Assert.AreEqual(1, sim.ActiveTripCount);
        }

        internal static void BuildSpecialVisitCity(
            SimConfig config,
            out CityGrid grid,
            out RoadNetwork roads,
            out DemandMap demands,
            out RoutePlanner planner,
            out RoadQueueNetwork queues)
        {
            grid = new CityGrid(14, 4);
            for (int x = 0; x < grid.Width; x++)
            {
                Assert.IsTrue(grid.Place(V(x, 2), TileType.Road));
            }

            Assert.IsTrue(grid.Place(V(0, 0), TileType.House));
            Assert.IsTrue(grid.Place(V(6, 0), TileType.SpecialBuilding));
            Assert.IsTrue(grid.Place(V(10, 0), TileType.Office));

            roads = new RoadNetwork(grid);
            demands = new DemandMap(config);
            demands.Reassign(grid, roads);
            planner = new RoutePlanner(grid.Width, grid.Height);
            planner.Plan(demands, roads, grid, config);
            queues = new RoadQueueNetwork(
                grid.Width,
                grid.Height,
                config);
            queues.RebuildTopology(grid);

            Assert.AreEqual(1, demands.Demands.Count);
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
            // The upper detour already makes (2,2) an intersection. Keep the south
            // spur at (2,1) absent so the 2x2 house has no extra ordinary access
            // candidate beside the intended approach before the watchdog blockade.
            // The office frontages are intersection (4,2) and ordinary (5,2), so the
            // ordinary endpoint must also be the return route's safe departure origin.
            Assert.IsTrue(grid.Place(V(0, 0), TileType.House));
            Assert.IsTrue(grid.Place(V(4, 0), TileType.Office));
            Assert.IsTrue(grid.IsIntersection(V(2, 2)),
                "전제: 직선 경로 중간의 (2,2)는 교차로");
            Assert.IsTrue(grid.IsIntersection(V(3, 2)),
                "전제: 워치독 차단 타일 (3,2)는 최초 경로 위 교차로");
            Assert.IsTrue(grid.IsIntersection(V(4, 2)),
                "전제: 회사의 기존 첫 프론티지는 교차로");
            Assert.IsFalse(grid.IsIntersection(V(5, 2)),
                "전제: 회사에는 퇴근 출발도 가능한 일반 프론티지가 존재");

            road = new RoadNetwork(grid);
            demands = new DemandMap(cfg);
            demands.Reassign(grid, road);
            Assert.AreEqual(1, demands.Demands.Count);
            Assert.AreEqual(V(0, 2), demands.Demands[0].SourceRoad,
                "전제: 집의 일반 진입로와 최초 경로 prefix는 유지");
            Assert.AreEqual(V(5, 2), demands.Demands[0].SinkRoad,
                "전제: 회사는 교차로 (4,2) 대신 일반 진입로 (5,2)를 선택");
            planner = new RoutePlanner(grid.Width, grid.Height);
            planner.Plan(demands, road, grid, cfg);
            CollectionAssert.AreEqual(
                new[] { V(0, 2), V(1, 2), V(2, 2), V(3, 2), V(4, 2), V(5, 2) },
                planner.CarRoutes[0],
                "전제: 최초 경로는 차단 타일을 지나 일반 회사 진입로까지 잇는 직선 경로");
            Assert.AreEqual(V(5, 2), planner.ReturnRoutes[0][0],
                "전제: 회사의 일반 진입로가 퇴근 경로의 안전한 원점");

            net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);
            sim = new CarSim(cfg);
            sim.Rebuild(demands, planner, net);
            events = new SimEventBuffer(new SimEventHub());
        }

        // 통합 사슬 고정: DemandMap(유형별 창) → CarSim(demands.CommuteWindowAt) → 스케줄러 판정.
        // 야간조는 아침에 출근하지 않고 밤에 출근한다. 배선이 끊기면(전역 창으로 되돌아가면) 깨진다.
        [Test]
        public void NightShiftCompany_CarsDepartAtNight_NotMorning()
        {
            SimConfig cfg = Cfg();
            var grid = new CityGrid(6, 3);
            for (int x = 0; x <= 5; x++) Assert.IsTrue(grid.Place(V(x, 2), TileType.Road));
            Assert.IsTrue(grid.Place(V(0, 0), TileType.House));
            Assert.IsTrue(grid.Place(V(4, 0), TileType.Office));
            var road = new RoadNetwork(grid);
            var demands = new DemandMap(cfg);
            // 공장: 출근창 [20,24) · 퇴근창 [5,9) — 근무가 자정을 넘는다.
            // RegisterCompany 는 채용 램프가 0에서 시작해 이 틱에 차가 안 생긴다 → 로드 경로(만석)를 쓴다.
            demands.RegisterRestoredCompany(
                V(4, 0), TileType.Office,
                companyType: new CompanyTypeInfo(
                    new CommuteWindow("factory", 20f, 4f, 5f, 4f), 10));
            demands.Reassign(grid, road);
            var planner = new RoutePlanner(grid.Width, grid.Height);
            planner.Plan(demands, road, grid, cfg);

            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);
            var sim = new CarSim(cfg);
            sim.Rebuild(demands, planner, net);
            var events = new SimEventBuffer(new SimEventHub());

            Assert.AreEqual(1, sim.CarCount);

            // 정오: 야간조는 집에 있다. 전역 창(출근 [6,7) · 퇴근창 [17,18))이면 근무 시간대라
            // 여기서 Outbound 가 된다 — 배선이 끊기면 이 단정이 깨진다.
            sim.Step(12f, net, events);
            Assert.AreEqual(CarState.ParkedHome, sim.GetCar(0).State,
                "야간조는 정오에 출근하지 않는다");

            // 자정 직전: 출근창 [20,24) 안이므로 스태거 값과 무관하게 출발한다.
            sim.Step(23.99f, net, events);
            Assert.AreEqual(CarState.Outbound, sim.GetCar(0).State, "밤에 출근한다");
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
