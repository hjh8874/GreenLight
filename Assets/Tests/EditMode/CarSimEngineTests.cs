using System.Collections.Generic;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Sim.Tests
{
    public class CarSimEngineTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static SimConfig Cfg()
        {
            SimConfig cfg = SimConfig.Default();
            cfg.GridWidth = 6;
            cfg.GridHeight = 3;
            cfg.TickInterval = 0.25f;
            cfg.MaxStepsPerFrame = 20;
            cfg.QueueCapacityPerTile = 4;
            cfg.QueueServicePerTick = 1;
            cfg.QueueSlowRatio = 0.5f;
            cfg.QueueJamRatio = 0.99f;
            cfg.CoinPerTrip = 10;
            cfg.CarsPerHouse = 1;
            cfg.MorningStartHour = 6f;
            cfg.MorningEndHour = 7f;
            cfg.EveningStartHour = 17f;
            cfg.EveningEndHour = 18f;
            cfg.CompanyHiringSlotsPerGameHour = 100f;
            cfg.MaxSimCars = 96;
            cfg.DayLengthSeconds = 24f;
            cfg.DemandChoicePool = 1;
            return cfg;
        }

        [Test]
        public void QueueOccupancy_TranslatesToThreeCongestionLevels()
        {
            SimConfig cfg = Cfg();
            Assert.AreEqual(CongestionLevel.Free, SimEngine.CongestionForOccupancy(0.49f, cfg));
            Assert.AreEqual(CongestionLevel.Slow, SimEngine.CongestionForOccupancy(0.5f, cfg));
            Assert.AreEqual(CongestionLevel.Jam, SimEngine.CongestionForOccupancy(0.99f, cfg));
        }

        [Test]
        public void QueueCount_MatchesRoadQueueNetwork_AndReturnsZeroOutOfBounds()
        {
            SimConfig cfg = Cfg();
            var engine = new SimEngine(cfg, new SimEventHub());
            RoadQueueNetwork queues = engine.RoadQueuesForTest;
            Vector2Int tile = V(2, 1);

            Assert.IsTrue(queues.TryEnqueue(tile, Dir.N, 10));
            Assert.IsTrue(queues.TryEnqueue(tile, Dir.E, 11));
            Assert.IsTrue(queues.TryEnqueue(tile, Dir.E, 12));

            for (int direction = 0; direction < 4; direction++)
            {
                var entryDir = (Dir)direction;
                Assert.AreEqual(
                    queues.QueueCount(tile, entryDir),
                    engine.GetQueueCount(tile, entryDir),
                    $"{entryDir} 방향 큐");
            }

            Assert.AreEqual(0, engine.GetQueueCount(V(-1, 1), Dir.E));
            Assert.AreEqual(0, engine.GetQueueCount(V(cfg.GridWidth, 1), Dir.E));
        }

        [Test]
        public void MiniCityArrivesAndPaysPerCar()
        {
            var hub = new SimEventHub();
            int arrivals = 0, coins = 0;
            hub.Arrival += e => { arrivals++; coins += e.Coins; };
            SimEngine engine = BuildStraightCity(Cfg(), hub);
            engine.SetGameHour(7f);

            for (int i = 0; i < 8; i++) engine.Tick(0.25f);

            Assert.AreEqual(2, engine.ActiveVehicleCount);
            Assert.AreEqual(2, arrivals);
            Assert.AreEqual(20, coins);
            Assert.AreEqual(CarState.ParkedWork, engine.GetCarSnapshot(0).State);
        }

        [Test]
        public void StraightSingleCommute_SnapshotIndicesMatchActiveRouteTable()
        {
            SimConfig cfg = Cfg();
            var engine = new SimEngine(cfg, new SimEventHub());
            for (int x = 0; x <= 5; x++) Assert.IsTrue(engine.Place(V(x, 2), TileType.Road));
            Assert.IsTrue(engine.Place(V(0, 0), TileType.House));
            Assert.IsTrue(engine.Place(V(4, 0), TileType.Office));
            engine.SetGameHour(7f);

            for (int tick = 0; tick < 6; tick++)
            {
                engine.Tick(0.25f);
                Assert.AreEqual(1, engine.ActiveVehicleCount);
                CarSnapshot snapshot = engine.GetCarSnapshot(0);
                Assert.That(snapshot.RouteIndex, Is.InRange(0, engine.ActiveRoutes.Count - 1));
                IReadOnlyList<Vector2Int> route = snapshot.State == CarState.Inbound
                    ? engine.ActiveReturnRoutes[snapshot.RouteIndex]
                    : engine.ActiveRoutes[snapshot.RouteIndex];
                Assert.That(snapshot.TileIndex, Is.InRange(0, route.Count - 1));
                Vector2Int tile = route[snapshot.TileIndex];
                TestContext.WriteLine(
                    $"tick={tick} route={snapshot.RouteIndex} tileIndex={snapshot.TileIndex} tile={tile} state={snapshot.State}");
                Assert.AreEqual(2, tile.y, "직선 도시 스냅샷은 동일한 직교 도로 행을 가리켜야 한다");
            }
        }

        [Test]
        public void NewOffice_ReassignsOnlyAfterAllCarsReturnHome()
        {
            SimConfig cfg = Cfg();
            cfg.GridWidth = 12;
            cfg.GridHeight = 5;
            var engine = new SimEngine(cfg, new SimEventHub());
            for (int x = 2; x <= 9; x++) Assert.IsTrue(engine.Place(V(x, 2), TileType.Road));
            Assert.IsTrue(engine.Place(V(0, 0), TileType.House));
            Vector2Int oldOffice = V(10, 1);
            Vector2Int newOffice = V(4, 3);
            Assert.IsTrue(engine.Place(oldOffice, TileType.Office));
            engine.SetGameHour(7f);
            for (int i = 0; i < 12; i++) engine.Tick(0.25f);
            Assert.AreEqual(CarState.ParkedWork, engine.GetCarSnapshot(0).State);
            Assert.AreEqual(oldOffice, engine.GetCarSnapshot(0).Work);

            Assert.IsTrue(engine.Place(newOffice, TileType.Office));
            engine.Tick(0.25f);
            Assert.AreEqual(oldOffice, engine.GetCarSnapshot(0).Work,
                "회사에 있거나 이동 중인 차의 목적지는 즉시 바뀌면 안 된다");

            engine.SetGameHour(17f);
            engine.Tick(0.25f);
            engine.SetGameHour(18f);
            bool returnedHome = false;
            for (int i = 0; i < 12; i++)
            {
                engine.Tick(0.25f);
                if (engine.GetCarSnapshot(0).State != CarState.ParkedHome) continue;
                returnedHome = true;
                break;
            }
            Assert.IsTrue(returnedHome, "기존 회사에서 귀가 완료");
            Assert.AreEqual(CarState.ParkedHome, engine.GetCarSnapshot(0).State);
            Assert.AreEqual(oldOffice, engine.GetCarSnapshot(0).Work,
                "귀가를 완료한 틱까지는 기존 왕복 짝을 유지한다");

            engine.Tick(0.25f);

            Assert.AreEqual(CarState.ParkedHome, engine.GetCarSnapshot(0).State);
            Assert.AreEqual(newOffice, engine.GetCarSnapshot(0).Work,
                "전 차량 귀가 다음 틱에 새 회사로 일괄 재배정");
        }

        [Test]
        public void UnrelatedBuildingsAdded_PreserveExistingVehicleIdentityAndRoute()
        {
            SimConfig cfg = Cfg();
            cfg.GridWidth = 8;
            cfg.GridHeight = 5;
            var engine = new SimEngine(cfg, new SimEventHub());
            for (int x = 0; x <= 7; x++)
                Assert.IsTrue(engine.Place(V(x, 2), TileType.Road));
            Assert.IsTrue(engine.Place(V(0, 0), TileType.House));
            Assert.IsTrue(engine.Place(V(2, 0), TileType.House));
            Assert.IsTrue(engine.Place(V(6, 0), TileType.Office));
            engine.SetGameHour(7f);
            engine.Tick(0.25f);

            Assert.AreEqual(2, engine.ActiveVehicleCount);
            var before = new CarSnapshot[engine.ActiveVehicleCount];
            var routeRefs = new List<Vector2Int>[engine.ActiveVehicleCount];
            for (int i = 0; i < before.Length; i++)
            {
                before[i] = engine.GetCarSnapshot(i);
                routeRefs[i] = engine.ActiveRoutes[before[i].RouteIndex];
            }

            Assert.IsTrue(engine.Place(V(4, 3), TileType.House));
            Assert.IsTrue(engine.Place(V(6, 3), TileType.Office));
            engine.EnsureCarTopologyCurrent();

            Assert.AreEqual(3, engine.ActiveVehicleCount, "신규 짝만 기존 차량 뒤에 추가한다");
            for (int i = 0; i < before.Length; i++)
            {
                CarSnapshot after = engine.GetCarSnapshot(i);
                Assert.AreEqual(before[i].Home, after.Home, $"car[{i}] Home");
                Assert.AreEqual(before[i].Work, after.Work, $"car[{i}] Work");
                Assert.AreEqual(before[i].RouteIndex, after.RouteIndex, $"car[{i}] RouteIndex");
                Assert.AreEqual(before[i].TileIndex, after.TileIndex, $"car[{i}] TileIndex");
                Assert.AreEqual(before[i].State, after.State, $"car[{i}] State");
                Assert.AreSame(
                    routeRefs[i],
                    engine.ActiveRoutes[after.RouteIndex],
                    $"car[{i}] 구 경로 List 참조");
            }
        }

        // 주차·주행 혼재 회귀 가드: (3,0)은 stagger로 ~6.82에, (5,0)은 ~6.05에 출발한다.
        // 6.5시에는 앞 인덱스 차가 주차, 뒤 인덱스 차가 주행 — 캡처를 on-road 우선으로
        // 하면 preserve 리빌드에서 인덱스가 뒤집혀 View 미러 전체 새로고침이 재발한다.
        [Test]
        public void UnrelatedBuildingsAdded_ParkedBeforeDrivingCar_KeepsIndexOrder()
        {
            SimConfig cfg = Cfg();
            cfg.GridWidth = 8;
            cfg.GridHeight = 5;
            var engine = new SimEngine(cfg, new SimEventHub());
            for (int x = 0; x <= 7; x++)
                Assert.IsTrue(engine.Place(V(x, 2), TileType.Road));
            Assert.IsTrue(engine.Place(V(3, 0), TileType.House));
            Assert.IsTrue(engine.Place(V(5, 0), TileType.House));
            Assert.IsTrue(engine.Place(V(0, 0), TileType.Office));
            engine.SetGameHour(6.5f);
            engine.Tick(0.25f);

            Assert.AreEqual(2, engine.ActiveVehicleCount);
            Assert.AreEqual(CarState.ParkedHome, engine.GetCarSnapshot(0).State, "전제: car[0] 주차");
            Assert.AreEqual(CarState.Outbound, engine.GetCarSnapshot(1).State, "전제: car[1] 주행");
            var before = new CarSnapshot[engine.ActiveVehicleCount];
            var routeRefs = new List<Vector2Int>[engine.ActiveVehicleCount];
            for (int i = 0; i < before.Length; i++)
            {
                before[i] = engine.GetCarSnapshot(i);
                routeRefs[i] = engine.ActiveRoutes[before[i].RouteIndex];
            }

            Assert.IsTrue(engine.Place(V(4, 3), TileType.House));
            Assert.IsTrue(engine.Place(V(6, 3), TileType.Office));
            engine.EnsureCarTopologyCurrent();

            Assert.AreEqual(3, engine.ActiveVehicleCount, "신규 짝만 기존 차량 뒤에 추가한다");
            for (int i = 0; i < before.Length; i++)
            {
                CarSnapshot after = engine.GetCarSnapshot(i);
                Assert.AreEqual(before[i].Home, after.Home, $"car[{i}] Home");
                Assert.AreEqual(before[i].Work, after.Work, $"car[{i}] Work");
                Assert.AreEqual(before[i].RouteIndex, after.RouteIndex, $"car[{i}] RouteIndex");
                Assert.AreEqual(before[i].TileIndex, after.TileIndex, $"car[{i}] TileIndex");
                Assert.AreEqual(before[i].State, after.State, $"car[{i}] State");
                Assert.AreSame(
                    routeRefs[i],
                    engine.ActiveRoutes[after.RouteIndex],
                    $"car[{i}] 구 경로 List 참조");
            }
        }

        // 라우팅 장치 변경이 건물 변경과 같은 틱 윈도우에 겹치면 preserve 리빌드를 포기하고
        // 전체 재계획해야 한다 — 구 경로가 새 일방통행 규칙을 위반한 채 유지되면 안 된다.
        // 링 도로라 일방통행을 역행하는 구 경로에는 항상 합법 우회로가 존재한다.
        [Test]
        public void BuildingAndOnewayInSameWindow_ReplansInsteadOfPreserving()
        {
            SimConfig cfg = Cfg();
            cfg.GridWidth = 8;
            cfg.GridHeight = 7;
            cfg.AutoDetectSignals = false;
            var engine = new SimEngine(cfg, new SimEventHub());
            for (int x = 0; x <= 7; x++)
            {
                Assert.IsTrue(engine.Place(V(x, 2), TileType.Road));
                Assert.IsTrue(engine.Place(V(x, 4), TileType.Road));
            }
            Assert.IsTrue(engine.Place(V(0, 3), TileType.Road));
            Assert.IsTrue(engine.Place(V(7, 3), TileType.Road));
            Assert.IsTrue(engine.Place(V(2, 0), TileType.House));
            Assert.IsTrue(engine.Place(V(5, 5), TileType.Office));
            engine.SetGameHour(7f);
            engine.Tick(0.25f);
            Assert.AreEqual(1, engine.ActiveVehicleCount);
            List<Vector2Int> oldRoute = engine.ActiveRoutes[engine.GetCarSnapshot(0).RouteIndex];
            var oldCopy = new List<Vector2Int>(oldRoute);

            // 구 경로 중간의 비교차로 타일 하나를 골라, 그 통과 방향을 거스르는 일방통행을
            // 건물 배치(preserve 후보)와 같은 윈도우에 놓는다.
            bool placedOneway = false;
            for (int p = 1; p < oldCopy.Count - 1 && !placedOneway; p++)
            {
                Vector2Int tile = oldCopy[p];
                if (tile.y != 2 || tile.x < 1 || tile.x > 6) continue;
                Vector2Int travel = tile - oldCopy[p - 1];
                placedOneway = engine.TryPlaceOneway(tile, -travel);
            }
            Assert.IsTrue(placedOneway, "전제: 구 경로 위 일방통행 배치 성공");
            Assert.IsTrue(engine.Place(V(2, 5), TileType.House));
            engine.EnsureCarTopologyCurrent();

            Assert.IsTrue(engine.ActiveVehicleCount >= 1);
            List<Vector2Int> newRoute = engine.ActiveRoutes[engine.GetCarSnapshot(0).RouteIndex];
            CollectionAssert.AreNotEqual(
                oldCopy,
                newRoute,
                "일방통행이 겹치면 구 경로 carry-over 대신 재계획해야 한다");
        }

        // 리뷰 지적(2026-07-24 hjh8874): 회사 철거 시 ResumeTile은 아웃바운드 경로에서
        // 캡처되는데 재큐잉은 인바운드 경로에서 찾는다. 일방통행 두 개로 출근=서쪽 링,
        // 귀가=동쪽 링을 강제해 왕복 타일 집합을 갈라 놓으면, 미수정 코드는 start=0
        // 폴백으로 철거된 회사 쪽 타일에 순간이동한다. 정차 지점 (0,4)는 인바운드
        // 경로의 어느 타일과도 맨해튼 거리 4 이상이라, 최근접 스냅 폴백으로도 통과할
        // 수 없다 — 제자리 재계획(포기 귀가)만 jump<=1을 만족한다.
        [Test]
        public void RemovedWork_DivergedReturnLoop_ResumesNearCurrentTile()
        {
            SimConfig cfg = Cfg();
            cfg.GridWidth = 8;
            cfg.GridHeight = 9;
            cfg.AutoDetectSignals = false;
            var engine = new SimEngine(cfg, new SimEventHub());
            for (int x = 0; x <= 7; x++)
            {
                Assert.IsTrue(engine.Place(V(x, 2), TileType.Road));
                Assert.IsTrue(engine.Place(V(x, 6), TileType.Road));
            }
            for (int y = 3; y <= 5; y++)
            {
                Assert.IsTrue(engine.Place(V(0, y), TileType.Road));
                Assert.IsTrue(engine.Place(V(7, y), TileType.Road));
            }
            // 서쪽 기둥은 북행 전용(출근), 동쪽 기둥은 남행 전용(귀가) → 왕복 경로 분리.
            Assert.IsTrue(engine.TryPlaceOneway(V(0, 4), V(0, 1)));
            Assert.IsTrue(engine.TryPlaceOneway(V(7, 4), V(0, -1)));
            Assert.IsTrue(engine.Place(V(2, 0), TileType.House));
            Assert.IsTrue(engine.Place(V(2, 7), TileType.Office));
            engine.SetGameHour(7f);

            // 출근 차가 서쪽 기둥 중간 (0,4)에 도달할 때까지 진행.
            Vector2Int carTile = default;
            bool reached = false;
            for (int tick = 0; tick < 20 && !reached; tick++)
            {
                engine.Tick(0.25f);
                Assert.AreEqual(1, engine.ActiveVehicleCount);
                CarSnapshot s = engine.GetCarSnapshot(0);
                if (s.State != CarState.Outbound) continue;
                carTile = engine.ActiveRoutes[s.RouteIndex][s.TileIndex];
                reached = carTile == V(0, 4);
            }
            Assert.IsTrue(reached, "전제: 출근 차가 서쪽 기둥 중간 (0,4) 도달");

            List<Vector2Int> returnRoute =
                engine.ActiveReturnRoutes[engine.GetCarSnapshot(0).RouteIndex];
            CollectionAssert.DoesNotContain(
                returnRoute, carTile, "전제: 왕복 경로가 실제로 갈라져 있어야 한다");
            Vector2Int officeSideStart = returnRoute[0];
            Assert.IsTrue(engine.Remove(V(2, 7)));
            engine.EnsureCarTopologyCurrent();
            engine.Tick(0.25f);

            // (0,4)에서 인바운드 어느 타일도 거리 4 이상 → 한 틱 만의 귀가 완료는 불가.
            // 제자리 재계획이면 재큐잉 후 한 틱 이동까지 포함해도 jump <= 1이어야 한다.
            CarSnapshot after = engine.GetCarSnapshot(0);
            Assert.AreEqual(CarState.Inbound, after.State, "포기 귀가 전환");
            Vector2Int resumed =
                engine.ActiveReturnRoutes[after.RouteIndex][after.TileIndex];
            Assert.AreNotEqual(
                officeSideStart,
                resumed,
                "철거된 회사 쪽 귀가 시작 타일로 순간이동하면 안 된다");
            int jump = Mathf.Abs(resumed.x - carTile.x)
                + Mathf.Abs(resumed.y - carTile.y);
            Assert.LessOrEqual(
                jump, 1, $"재큐잉 타일 {resumed}은 직전 위치 {carTile} 제자리여야 한다");
        }

        // 리뷰 지적(2026-07-24 abicodue): 건물 철거 + 라우팅 변경이 같은 리빌드에 겹치면
        // (non-preserve) 은퇴 carry-over 차량도 구 경로 대신 최신 규칙으로 재계획해야 한다.
        // HomeLost+Outbound가 대표 케이스 — 구 출근 경로를 거스르는 일방통행을 집 철거와
        // 같은 윈도우에 넣는다. WorkLost+Inbound/ParkedWork도 같은 ReplanLeg 경로를 탄다.
        [Test]
        public void RemovedHomeAndOnewayInSameWindow_RetireeReplansOutbound()
        {
            SimConfig cfg = Cfg();
            cfg.GridWidth = 8;
            cfg.GridHeight = 7;
            cfg.AutoDetectSignals = false;
            var engine = new SimEngine(cfg, new SimEventHub());
            for (int x = 0; x <= 7; x++)
            {
                Assert.IsTrue(engine.Place(V(x, 2), TileType.Road));
                Assert.IsTrue(engine.Place(V(x, 4), TileType.Road));
            }
            Assert.IsTrue(engine.Place(V(0, 3), TileType.Road));
            Assert.IsTrue(engine.Place(V(7, 3), TileType.Road));
            Assert.IsTrue(engine.Place(V(2, 0), TileType.House));
            Assert.IsTrue(engine.Place(V(5, 5), TileType.Office));
            engine.SetGameHour(7f);
            engine.Tick(0.25f);
            Assert.AreEqual(1, engine.ActiveVehicleCount);
            CarSnapshot before = engine.GetCarSnapshot(0);
            Assert.AreEqual(CarState.Outbound, before.State);
            var oldCopy = new List<Vector2Int>(engine.ActiveRoutes[before.RouteIndex]);
            Vector2Int beforeTile = oldCopy[Mathf.Clamp(before.TileIndex, 0, oldCopy.Count - 1)];

            // 차 진행 방향 앞쪽(y=4 구간) 비교차로 타일에 역방향 일방통행 배치.
            Vector2Int onewayTile = default;
            Vector2Int onewayDir = default;
            bool placedOneway = false;
            for (int p = 1; p < oldCopy.Count - 1 && !placedOneway; p++)
            {
                Vector2Int tile = oldCopy[p];
                if (tile.y != 4 || tile.x < 1 || tile.x > 6) continue;
                onewayDir = -(tile - oldCopy[p - 1]);
                placedOneway = engine.TryPlaceOneway(tile, onewayDir);
                onewayTile = tile;
            }
            Assert.IsTrue(placedOneway, "전제: 구 출근 경로 위 일방통행 배치 성공");
            Assert.IsTrue(engine.Remove(V(2, 0)), "전제: 집 철거");
            engine.EnsureCarTopologyCurrent();
            engine.Tick(0.25f);

            Assert.AreEqual(1, engine.ActiveVehicleCount, "HomeLost 주행 차는 트립 완주까지 생존");
            CarSnapshot after = engine.GetCarSnapshot(0);
            Assert.AreEqual(CarState.Outbound, after.State);
            List<Vector2Int> newRoute = engine.ActiveRoutes[after.RouteIndex];
            CollectionAssert.AreNotEqual(
                oldCopy, newRoute, "은퇴 차도 라우팅 변경 시 구 경로 대신 재계획해야 한다");
            int onewayIdx = newRoute.IndexOf(onewayTile);
            if (onewayIdx > 0)
            {
                Assert.AreEqual(
                    onewayDir,
                    newRoute[onewayIdx] - newRoute[onewayIdx - 1],
                    "새 경로는 새 일방통행 규칙을 준수해야 한다");
            }
            Vector2Int afterTile = newRoute[Mathf.Clamp(after.TileIndex, 0, newRoute.Count - 1)];
            int jump = Mathf.Abs(afterTile.x - beforeTile.x) + Mathf.Abs(afterTile.y - beforeTile.y);
            Assert.LessOrEqual(jump, 2, $"재계획 후 위치 {afterTile}는 직전 위치 {beforeTile} 근방이어야 한다");
        }

        // 리뷰 지적(2026-07-24 abicodue): 스케줄러의 MaxSimCars 확정 루프는 꼬리를 자른다.
        // 상한 포화 상태에서 건물 교체가 non-preserve 리빌드(라우팅 변경 동반과 동일 경로)에
        // 들어올 때, 주행 중 은퇴 차가 신규 배정에 밀려 즉시 소멸하면 안 된다("트립 완주 후
        // 은퇴" 계약). CarSim 레벨인 이유: 엔진 경유는 회사 고용 램프·sticky가 슬롯 경합으로
        // 결함을 가릴 수 있어, append 순서 vs 상한이라는 결함 지점을 직접 검증한다.
        [Test]
        public void MaxCarsSaturated_DrivingRetireeOutranksNewAssignment()
        {
            SimConfig cfg = Cfg();
            cfg.GridWidth = 8;
            cfg.GridHeight = 5;
            cfg.MaxSimCars = 1;
            var grid = new CityGrid(8, 5);
            for (int x = 0; x <= 7; x++)
                Assert.IsTrue(grid.Place(V(x, 2), TileType.Road));
            Assert.IsTrue(grid.Place(V(0, 0), TileType.House));
            Assert.IsTrue(grid.Place(V(6, 0), TileType.Office));
            var road = new RoadNetwork(grid);
            var demands = new DemandMap(cfg);
            demands.Reassign(grid, road);
            var planner = new RoutePlanner(8, 5);
            planner.Plan(demands, road, grid, cfg);
            var net = new RoadQueueNetwork(8, 5, cfg);
            net.RebuildTopology(grid);
            var sim = new CarSim(cfg);
            sim.Rebuild(demands, planner, net);
            var events = new SimEventBuffer(new SimEventHub());
            sim.Step(7f, net, events);
            Assert.AreEqual(1, sim.CarCount);
            Assert.AreEqual(CarState.Outbound, sim.GetCar(0).State);

            // 같은 리빌드 윈도우: 기존 집 철거 + 새 집 배치 → 신규 짝 1개가 유일한 자리를
            // 노린다. 주행 중 HomeLost 차가 자리를 지켜야 한다.
            Assert.IsTrue(grid.Remove(V(0, 0)));
            Assert.IsTrue(grid.Place(V(2, 0), TileType.House));
            demands.Reassign(grid, road);
            planner.Plan(demands, road, grid, cfg);
            net.RebuildTopology(grid);
            sim.Rebuild(demands, planner, net);

            Assert.AreEqual(1, sim.CarCount);
            Assert.AreEqual(
                V(0, 0),
                sim.GetCar(0).Home,
                "상한 포화 시 주행 중 은퇴 차가 신규 배정보다 우선해야 한다");
            Assert.AreEqual(CarState.Outbound, sim.GetCar(0).State);
        }

        [Test]
        public void CompletedDay_BlendsSuccessByHalf_AndPersistsAcrossSave()
        {
            SimEngine engine = BuildStraightCity(Cfg(), new SimEventHub());
            engine.SetGameHour(23.9f);
            engine.Tick(0.25f); // 하루 도착 0
            engine.SetGameHour(0.1f);
            engine.Tick(0.25f); // dayRate=0, EMA 1→0.5

            Assert.AreEqual(0.5f, engine.TripSuccessRateForTest, 1e-4f);
            SimSaveData save = engine.CreateSnapshot();
            var restored = new SimEngine(Cfg(), new SimEventHub());
            restored.RestoreSnapshot(save);

            Assert.AreEqual(0.5f, restored.TripSuccessRateForTest, 1e-4f);
            Assert.AreEqual(0.5f, save.CarTripSuccessRate, 1e-4f);
            Assert.IsTrue(save.HasCarSimStats);
        }

        [Test]
        public void LegacySave_UsesInnocentSuccessRate()
        {
            var engine = new SimEngine(Cfg(), new SimEventHub());
            engine.RestoreSnapshot(new SimSaveData());
            Assert.AreEqual(1f, engine.TripSuccessRateForTest);
        }

        [Test]
        public void Snapshot_RestoresRotatedBuildingDirection()
        {
            var engine = new SimEngine(Cfg(), new SimEventHub());
            var coord = new Vector2Int(1, 1);
            engine.Place(coord, TileType.School, PlacementDirection.East);

            SimSaveData save = engine.CreateSnapshot();
            var restored = new SimEngine(Cfg(), new SimEventHub());
            restored.RestoreSnapshot(save);

            var readOnlyData = (IReadOnlyTileData)restored;
            Assert.AreEqual(TileType.School, readOnlyData.GetTileType(coord));
            Assert.AreEqual(PlacementDirection.East, readOnlyData.GetDirection(coord));
        }

        [Test]
        public void JumpedDay_IsExcludedAtNextMidnightWrap()
        {
            SimEngine engine = BuildStraightCity(Cfg(), new SimEventHub());
            engine.SetGameHour(7f);
            engine.Tick(0.25f);
            engine.SetGameHour(17f); // 점프: 당일 집계 폐기, 이 날은 skip
            engine.Tick(0.25f);
            for (int hour = 18; hour <= 23; hour++)
            {
                engine.SetGameHour(hour);
                engine.Tick(0.25f);
            }
            engine.SetGameHour(23.9f);
            engine.Tick(0.25f);
            engine.SetGameHour(0.1f);
            engine.Tick(0.25f);

            Assert.AreEqual(1f, engine.TripSuccessRateForTest, "점프가 낀 날은 EMA 미산출");
        }

        private static SimEngine BuildStraightCity(SimConfig cfg, SimEventHub hub)
        {
            var engine = new SimEngine(cfg, hub);
            for (int x = 0; x <= 5; x++) Assert.IsTrue(engine.Place(V(x, 2), TileType.Road));
            Assert.IsTrue(engine.Place(V(0, 0), TileType.House));
            Assert.IsTrue(engine.Place(V(2, 0), TileType.House));
            Assert.IsTrue(engine.Place(V(4, 0), TileType.Office));
            return engine;
        }
    }
}
