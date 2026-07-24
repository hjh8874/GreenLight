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

        // 채용 게이지 오버레이(CompanyHiringGaugeOverlay)의 데이터 계약 고정:
        // staffing 조회는 회사 "앵커" 타일에서만 응답해야 라벨이 회사당 1개가 되고,
        // Filled는 채용 램프를 따라 배정 인원까지 차오른다.
        [Test]
        public void CompanyStaffing_RespondsOnlyAtAnchor_AndFillsOverHiringRamp()
        {
            SimConfig cfg = Cfg();
            var engine = BuildStraightCity(cfg, new SimEventHub());
            Vector2Int anchor = V(4, 0);
            engine.SetGameHour(7f);
            engine.Tick(0.25f);

            Assert.IsTrue(
                engine.TryGetCompanyStaffing(anchor, out CompanyStaffing staffing),
                "앵커 타일은 staffing을 보고한다");
            Assert.AreEqual(cfg.OfficeCapacity, staffing.Capacity, "게이지 분모 = 총 정원");
            foreach (Vector2Int offset in new[] { V(1, 0), V(0, 1), V(1, 1) })
            {
                Assert.IsFalse(
                    engine.TryGetCompanyStaffing(anchor + offset, out _),
                    $"풋프린트 비앵커 타일 {anchor + offset}은 응답하지 않는다(라벨 중복 방지)");
            }

            for (int tick = 0; tick < 16 && staffing.Filled < 2; tick++)
            {
                engine.Tick(0.25f);
                Assert.IsTrue(engine.TryGetCompanyStaffing(anchor, out staffing));
            }
            Assert.AreEqual(2, staffing.Filled, "채용 램프를 따라 배정 인원까지 차오른다");
            Assert.LessOrEqual(staffing.Filled, staffing.Capacity);
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
