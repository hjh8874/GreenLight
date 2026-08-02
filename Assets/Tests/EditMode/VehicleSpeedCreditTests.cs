using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    // M1 속도 크레딧 RED 테스트 (계획: greenlight-vehicle-speed-plan.md)
    // Task 1: ICarRouteProvider.TryConsumeAdvanceCredit 게이트 (RoadQueueNetwork 단위)
    // Task 2: SimConfig.TruckCommuterRatio 배정 (통근 해시 + 방문 transient)
    // Task 3: CarSnapshot.WaitingForSpeedCredit 뷰 플래그
    public class VehicleSpeedCreditTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static SimConfig Cfg() // RoadQueueNetworkTests.Cfg 미러
        {
            var c = SimConfig.Default();
            c.QueueCapacityPerTile = 4;
            c.QueueServicePerTick = 1;
            return c;
        }

        static SimConfig CommuteCfg() // CarSimTests.Cfg 미러
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

        // RoadQueueNetworkTests.FakeRouteProvider 미러(계획 Task 1 지시).
        // 이 페이크가 신규 인터페이스 멤버 추가 후에도 무수정 컴파일되는 것 자체가
        // TryConsumeAdvanceCredit 기본 구현의 검증이다.
        private sealed class FakeRouteProvider : ICarRouteProvider
        {
            private sealed class Route
            {
                public Vector2Int[] Tiles;
                public bool DestinationAtEnd;
            }

            private readonly Dictionary<int, Route> _routes = new();

            public void AddRoute(
                int carId,
                bool destinationAtEnd,
                params Vector2Int[] tiles)
            {
                _routes.Add(carId, new Route
                {
                    Tiles = tiles,
                    DestinationAtEnd = destinationAtEnd
                });
            }

            public bool TryGetNextTile(
                int carId,
                Vector2Int current,
                out Vector2Int next,
                out Dir entryDirAtNext)
            {
                next = default;
                entryDirAtNext = default;
                if (!_routes.TryGetValue(carId, out Route route))
                {
                    return false;
                }

                for (int i = 0; i < route.Tiles.Length - 1; i++)
                {
                    if (route.Tiles[i] != current)
                    {
                        continue;
                    }

                    next = route.Tiles[i + 1];
                    entryDirAtNext = DirectionOf(next - current);
                    return true;
                }

                return false;
            }

            public bool IsDestination(int carId, Vector2Int tile)
            {
                return _routes.TryGetValue(carId, out Route route)
                    && route.DestinationAtEnd
                    && route.Tiles.Length > 0
                    && route.Tiles[route.Tiles.Length - 1] == tile;
            }

            private static Dir DirectionOf(Vector2Int delta)
            {
                if (delta == Vector2Int.up) return Dir.N;
                if (delta == Vector2Int.right) return Dir.E;
                if (delta == Vector2Int.down) return Dir.S;
                if (delta == Vector2Int.left) return Dir.W;
                Assert.Fail($"직교 인접 스텝이 아님: {delta}");
                return default;
            }
        }

        // 크레딧 게이트를 켠 페이크: car 0만 저속(numerator/60), 나머지 통과.
        // 정수 크레딧 규약(계획 Q1): 틱당 분자 적립, 60 도달 시 허가 후 차감, 캡 120.
        sealed class TruckZeroProvider : ICarRouteProvider
        {
            private readonly ICarRouteProvider inner;
            private readonly int numerator;
            private int credit;
            private int lastTick = int.MinValue;

            public TruckZeroProvider(ICarRouteProvider inner, int numerator = 40)
            {
                this.inner = inner;
                this.numerator = numerator;
            }

            public bool TryGetNextTile(int carId, Vector2Int c, out Vector2Int n, out Dir d)
                => inner.TryGetNextTile(carId, c, out n, out d);

            public bool IsDestination(int carId, Vector2Int t)
                => inner.IsDestination(carId, t);

            public bool TryConsumeAdvanceCredit(int carId, int tick)
            {
                if (carId != 0) return true;
                if (tick != lastTick)
                {
                    credit = Mathf.Min(120, credit + numerator);
                    lastTick = tick;
                }
                if (credit < 60) return false;
                credit -= 60;
                return true;
            }
        }

        // ── Task 1: 심 크레딧 게이트 ─────────────────────────────

        [Test]
        public void Truck_AdvancesTwoOfThreeTicks_OnStraightRoad()
        {
            // 직선 13타일: Step_StraightFiveTiles 셋업 미러를 늘려 12틱을 관찰한다.
            var q = new RoadQueueNetwork(13, 1, Cfg());
            var inner = new FakeRouteProvider();
            inner.AddRoute(
                0,
                destinationAtEnd: true,
                V(0, 0), V(1, 0), V(2, 0), V(3, 0), V(4, 0), V(5, 0), V(6, 0),
                V(7, 0), V(8, 0), V(9, 0), V(10, 0), V(11, 0), V(12, 0));
            var routes = new TruckZeroProvider(inner);
            Assert.IsTrue(q.TryEnqueue(V(0, 0), Dir.E, 0));

            for (int tick = 1; tick <= 12; tick++)
            {
                q.Step(routes, null, tick);
            }

            // 40/60 적립: 대기·전진·전진 반복 → 12틱 중 8틱 전진.
            Assert.IsTrue(q.TryLocateCar(0, out Vector2Int tile, out _, out _),
                "트럭은 아직 주행 중이어야 한다(게이트 없이는 12틱에 완주해 버린다)");
            Assert.AreEqual(V(8, 0), tile, "12틱 * 2/3 = 8타일 전진");
        }

        [Test]
        public void Truck_BehindStandardCars_DelaysFollowers()
        {
            // 같은 직선로 호송: car0(트럭) 뒤 car1·car2(표준).
            // 전원 표준 대조군보다 후행 차 도착 틱이 늦어야 한다(역압 실증).
            Dictionary<int, int> control = RunConvoy(truckInFront: false);
            Dictionary<int, int> withTruck = RunConvoy(truckInFront: true);

            Assert.AreEqual(3, control.Count, "대조군 전원 도착");
            Assert.AreEqual(3, withTruck.Count, "트럭 혼합군 전원 도착");
            Assert.Greater(withTruck[1], control[1], "car1은 트럭 뒤에서 늦게 도착");
            Assert.Greater(withTruck[2], control[2], "car2도 역압으로 늦게 도착");
        }

        static Dictionary<int, int> RunConvoy(bool truckInFront)
        {
            var q = new RoadQueueNetwork(8, 1, Cfg());
            var inner = new FakeRouteProvider();
            inner.AddRoute(
                0, destinationAtEnd: true,
                V(1, 0), V(2, 0), V(3, 0), V(4, 0), V(5, 0), V(6, 0), V(7, 0));
            inner.AddRoute(
                1, destinationAtEnd: true,
                V(1, 0), V(2, 0), V(3, 0), V(4, 0), V(5, 0), V(6, 0), V(7, 0));
            inner.AddRoute(
                2, destinationAtEnd: true,
                V(0, 0), V(1, 0), V(2, 0), V(3, 0), V(4, 0), V(5, 0), V(6, 0), V(7, 0));
            ICarRouteProvider routes = truckInFront
                ? new TruckZeroProvider(inner)
                : inner;
            Assert.IsTrue(q.TryEnqueue(V(1, 0), Dir.E, 0));
            Assert.IsTrue(q.TryEnqueue(V(1, 0), Dir.E, 1));
            Assert.IsTrue(q.TryEnqueue(V(0, 0), Dir.E, 2));

            var arrivalTicks = new Dictionary<int, int>();
            for (int tick = 1; tick <= 40 && arrivalTicks.Count < 3; tick++)
            {
                q.Step(routes, null, tick);
                for (int i = 0; i < q.ArrivalCount; i++)
                {
                    arrivalTicks[q.GetArrival(i).CarId] = tick;
                }
            }

            return arrivalTicks;
        }

        [Test]
        public void CreditWait_DoesNotIncrementBlockedTicks_NoValveActivation()
        {
            // 분자 10 = 전진 사이 대기 5틱 연속. 밸브 임계 4보다 길어서,
            // credit 대기가 _blockedTicks를 올리는 잘못된 구현이면 밸브가 오발한다.
            SimConfig cfg = Cfg();
            cfg.GridlockValveTicks = 4;
            var q = new RoadQueueNetwork(13, 1, cfg);
            var inner = new FakeRouteProvider();
            inner.AddRoute(
                0,
                destinationAtEnd: true,
                V(0, 0), V(1, 0), V(2, 0), V(3, 0), V(4, 0), V(5, 0), V(6, 0),
                V(7, 0), V(8, 0), V(9, 0), V(10, 0), V(11, 0), V(12, 0));
            var routes = new TruckZeroProvider(inner, numerator: 10);
            Assert.IsTrue(q.TryEnqueue(V(0, 0), Dir.E, 0));

            int valveTotal = 0;
            for (int tick = 1; tick <= 18; tick++)
            {
                valveTotal += q.Step(routes, null, tick).ValveActivations;
            }

            Assert.AreEqual(0, valveTotal,
                "credit 대기는 자발적 페이스 — GridlockValve와 무관해야 한다");
            Assert.IsTrue(q.TryLocateCar(0, out Vector2Int tile, out _, out _));
            Assert.AreEqual(V(3, 0), tile, "18틱 * 10/60 = 3타일 전진(밸브 강제 전진 없음)");
        }

        [Test]
        public void DefaultProvider_AllowsEveryTick()
        {
            // 기본 구현: 어떤 carId·tick이든 true → 기존 경로(페이크·RoadTrafficCoordinator) 무수정 호환.
            ICarRouteProvider routes = new FakeRouteProvider();

            Assert.IsTrue(routes.TryConsumeAdvanceCredit(0, 1));
            Assert.IsTrue(routes.TryConsumeAdvanceCredit(0, 1), "소비 개념 없음 — 같은 틱 재질의도 true");
            Assert.IsTrue(routes.TryConsumeAdvanceCredit(1234567, 99), "버스 오프셋 등 범위 밖 id도 true");
        }

        // ── Task 2: 트럭 배정 + 에셋 키 ─────────────────────────────

        // 집 8·회사 2 직선 도시(집당 1대 = 8대). BuildStraightCity 미러 확장.
        static CarSim BuildEightCarCity(SimConfig cfg)
        {
            var grid = new CityGrid(20, 3);
            for (int x = 0; x <= 19; x++)
            {
                Assert.IsTrue(grid.Place(V(x, 2), TileType.Road));
            }
            int[] houseColumns = { 0, 2, 6, 8, 12, 14, 16, 18 };
            foreach (int x in houseColumns)
            {
                Assert.IsTrue(grid.Place(V(x, 0), TileType.House));
            }
            Assert.IsTrue(grid.Place(V(4, 0), TileType.Office));
            Assert.IsTrue(grid.Place(V(10, 0), TileType.Office));
            var road = new RoadNetwork(grid);
            var demands = new DemandMap(cfg);
            demands.Reassign(grid, road);
            Assert.AreEqual(8, demands.Demands.Count, "전제: 8가구 전원 취업");
            var planner = new RoutePlanner(grid.Width, grid.Height);
            planner.Plan(demands, road, grid, cfg);
            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);
            var sim = new CarSim(cfg);
            sim.Rebuild(demands, planner, net);
            Assert.AreEqual(8, sim.CarCount, "전제: 집당 1대 = 8대");
            return sim;
        }

        [Test]
        public void TruckRatioZero_Default_AllCommutersStandard()
        {
            // TruckCommuterRatio 기본 0f = 꺼짐 → 전원 표준(60) = 기존과 비트 동일.
            CarSim sim = BuildEightCarCity(CommuteCfg());

            for (int i = 0; i < sim.CarCount; i++)
            {
                Assert.AreEqual(60, sim.GetCar(i).SpeedFactorNumerator, $"car {i}");
            }
        }

        [Test]
        public void TruckRatioOne_AllCommutersBecomeTrucks()
        {
            SimConfig cfg = CommuteCfg();
            cfg.TruckCommuterRatio = 1f;
            CarSim sim = BuildEightCarCity(cfg);

            for (int i = 0; i < sim.CarCount; i++)
            {
                Assert.AreEqual(40, sim.GetCar(i).SpeedFactorNumerator, $"car {i}");
            }
        }

        [Test]
        public void TruckRatioHalf_MixedClasses_DeterministicAcrossRebuilds()
        {
            SimConfig cfg = CommuteCfg();
            cfg.TruckCommuterRatio = 0.5f;
            CarSim first = BuildEightCarCity(cfg);
            CarSim second = BuildEightCarCity(cfg);

            int trucks = 0;
            for (int i = 0; i < first.CarCount; i++)
            {
                int numerator = first.GetCar(i).SpeedFactorNumerator;
                Assert.IsTrue(numerator == 40 || numerator == 60,
                    $"car {i}: 차급은 40 또는 60만 존재해야 한다 (was {numerator})");
                if (numerator == 40) trucks++;
                Assert.AreEqual(numerator, second.GetCar(i).SpeedFactorNumerator,
                    $"car {i}: 같은 도시·같은 설정이면 배정이 결정론이어야 한다");
            }

            Assert.Greater(trucks, 0, "ratio 0.5 · 8대면 트럭이 존재해야 한다");
            Assert.Less(trucks, first.CarCount, "전원 트럭이어도 안 된다");
        }

        [Test]
        public void VisitTransient_TruckByJourneyHash_RestoredOnRelease()
        {
            // 회사 없는 도시 → 방문은 transient 차량으로만 수행된다
            // (SpecialVisit_SharedAccessRoad 미러 + 도로 연장).
            // ratio 1이면 여정 해시 결과와 무관하게 트럭 — 결정론 단정 가능.
            SimConfig config = CommuteCfg();
            config.MaxSimCars = 8;
            config.MaxPendingVehicleTrips = 8;
            config.MaxConcurrentSpecialTrips = 2;
            config.TruckCommuterRatio = 1f;
            var grid = new CityGrid(10, 3);
            for (int x = 2; x <= 7; x++)
            {
                Assert.IsTrue(grid.Place(V(x, 0), TileType.Road));
            }
            Assert.IsTrue(grid.Place(V(0, 0), TileType.House));
            Assert.IsTrue(grid.Place(V(8, 0), TileType.SpecialBuilding));
            var roads = new RoadNetwork(grid);
            var demands = new DemandMap(config);
            demands.Reassign(grid, roads);
            var planner = new RoutePlanner(grid.Width, grid.Height);
            planner.Plan(demands, roads, grid, config);
            var queues = new RoadQueueNetwork(grid.Width, grid.Height, config);
            queues.RebuildTopology(grid);
            var sim = new CarSim(config);
            sim.Rebuild(demands, planner, queues, grid: grid, roadNetwork: roads);
            Assert.AreEqual(0, sim.CarCount, "전제: 통근차 없음 → 방문은 transient");
            var hub = new SimEventHub();
            var events = new SimEventBuffer(hub);
            var completed = new List<VehicleTripSnapshot>();
            hub.VehicleTripArrived += message => completed.Add(message.Trip);

            Assert.IsTrue(sim.TryScheduleSpecialBuildingVisit(
                new SpecialBuildingVisitTripRequest(
                    "coffee-shop",
                    V(8, 0),
                    1L,
                    0,
                    1f,
                    rewardCoins: 0)));

            bool sawTruckOnJourney = false;
            for (int tick = 0; tick < 60; tick++)
            {
                sim.Step(1L, 1f, queues, events, null, tick);
                events.Drain();
                if (sim.CarCount > 0 && sim.ActiveTripCount > 0)
                {
                    CarSnapshot snapshot = sim.GetCar(0);
                    if (snapshot.Purpose == VehicleTripPurpose.SpecialBuildingVisit)
                    {
                        sawTruckOnJourney |= snapshot.SpeedFactorNumerator == 40;
                    }
                }
            }

            Assert.AreEqual(2, completed.Count, "전제: 방문·귀가 2레그 완주");
            Assert.AreEqual(0, sim.ActiveTripCount);
            Assert.IsTrue(sawTruckOnJourney,
                "ratio 1이면 여정 중 transient는 트럭(40)이어야 한다");
            Assert.AreEqual(1, sim.CarCount, "transient 보관차는 리빌드 전까지 잔존");
            Assert.AreEqual(60, sim.GetCar(0).SpeedFactorNumerator,
                "ReleaseTransient가 표준(60)으로 복원해야 다음 여정에 차급이 새지 않는다");
        }

        // ── Task 3: 스냅샷 플래그 ─────────────────────────────

        // BuildStraightCity 미러(집 2·회사 1) + net/sim 구성.
        static CarSim BuildTwoCarCommuteCity(SimConfig cfg, out RoadQueueNetwork net)
        {
            var grid = new CityGrid(6, 3);
            for (int x = 0; x <= 5; x++)
            {
                Assert.IsTrue(grid.Place(V(x, 2), TileType.Road));
            }
            Assert.IsTrue(grid.Place(V(0, 0), TileType.House));
            Assert.IsTrue(grid.Place(V(2, 0), TileType.House));
            Assert.IsTrue(grid.Place(V(4, 0), TileType.Office));
            var road = new RoadNetwork(grid);
            var demands = new DemandMap(cfg);
            demands.Reassign(grid, road);
            var planner = new RoutePlanner(grid.Width, grid.Height);
            planner.Plan(demands, road, grid, cfg);
            net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);
            var sim = new CarSim(cfg);
            sim.Rebuild(demands, planner, net);
            Assert.AreEqual(2, sim.CarCount);
            return sim;
        }

        [Test]
        public void TruckCommuter_CreditWaitTick_SetsSnapshotFlagAndHoldsTile()
        {
            SimConfig cfg = CommuteCfg();
            cfg.TruckCommuterRatio = 1f;
            CarSim sim = BuildTwoCarCommuteCity(cfg, out RoadQueueNetwork net);
            var events = new SimEventBuffer(new SimEventHub());

            int waitTicks = 0;
            int advanceTicks = 0;
            for (int tick = 0;
                 tick < 30 && sim.GetCar(0).State != CarState.ParkedWork;
                 tick++)
            {
                CarSnapshot before = sim.GetCar(0);
                sim.Step(7f, net, events);
                CarSnapshot after = sim.GetCar(0);
                if (before.State != CarState.Outbound ||
                    after.State != CarState.Outbound)
                {
                    continue;
                }

                if (after.WaitingForSpeedCredit)
                {
                    waitTicks++;
                    Assert.AreEqual(before.TileIndex, after.TileIndex,
                        "credit 대기 틱에는 전진하면 안 된다");
                }
                else if (after.TileIndex > before.TileIndex)
                {
                    advanceTicks++;
                }
            }

            Assert.Greater(waitTicks, 0, "트럭(40/60)은 credit 대기 틱을 겪어야 한다");
            Assert.Greater(advanceTicks, 0, "대기만 하면 안 된다 — 2/3 페이스 전진");
            Assert.AreEqual(CarState.ParkedWork, sim.GetCar(0).State,
                "트럭도 유한 틱 안에 도착해야 한다");
        }

        [Test]
        public void StandardCommuter_NeverWaitsForSpeedCredit()
        {
            // ratio 기본 0 → 전원 표준. 플래그는 항상 false = 기존 동작 그대로.
            CarSim sim = BuildTwoCarCommuteCity(CommuteCfg(), out RoadQueueNetwork net);
            var events = new SimEventBuffer(new SimEventHub());

            for (int tick = 0; tick < 12; tick++)
            {
                sim.Step(7f, net, events);
                for (int i = 0; i < sim.CarCount; i++)
                {
                    Assert.IsFalse(sim.GetCar(i).WaitingForSpeedCredit,
                        $"tick {tick} car {i}: 표준차는 credit 대기 없음");
                }
            }
            Assert.AreEqual(CarState.ParkedWork, sim.GetCar(0).State, "전제: 정상 완주");
        }

        [Test]
        public void TruckCommuter_ServicePerTickTwo_StillAccruesOncePerTick()
        {
            // 알려진 리스크(계획): _servicePerTick > 1이면 라운드당 재질의로 이중 적립 위험.
            // 서비스 2 구성에서도 트럭은 여전히 대기 틱을 겪어야 한다(적립은 틱당 1회).
            SimConfig cfg = CommuteCfg();
            cfg.QueueServicePerTick = 2;
            cfg.TruckCommuterRatio = 1f;
            CarSim sim = BuildTwoCarCommuteCity(cfg, out RoadQueueNetwork net);
            var events = new SimEventBuffer(new SimEventHub());

            int waitTicks = 0;
            for (int tick = 0;
                 tick < 30 && sim.GetCar(0).State != CarState.ParkedWork;
                 tick++)
            {
                sim.Step(7f, net, events);
                if (sim.GetCar(0).State == CarState.Outbound &&
                    sim.GetCar(0).WaitingForSpeedCredit)
                {
                    waitTicks++;
                }
            }

            Assert.Greater(waitTicks, 0,
                "서비스 2에서 이중 적립되면 트럭이 매 틱 전진해 대기 틱이 사라진다");
        }
    }
}
