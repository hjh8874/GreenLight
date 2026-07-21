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
            cfg.OfficeParkingSlots = 6;
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
            for (int x = 0; x <= 4; x++) Assert.IsTrue(engine.Place(V(x, 1), TileType.Road));
            Assert.IsTrue(engine.Place(V(0, 0), TileType.House));
            Assert.IsTrue(engine.Place(V(5, 1), TileType.Office));
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
                Assert.AreEqual(1, tile.y, "직선 도시 스냅샷은 동일한 직교 도로 행을 가리켜야 한다");
            }
        }

        [Test]
        public void NewOffice_ReassignsOnlyAfterAllCarsReturnHome()
        {
            SimConfig cfg = Cfg();
            cfg.GridWidth = 9;
            var engine = new SimEngine(cfg, new SimEventHub());
            for (int x = 1; x <= 7; x++) Assert.IsTrue(engine.Place(V(x, 0), TileType.Road));
            Assert.IsTrue(engine.Place(V(0, 0), TileType.House));
            Vector2Int oldOffice = V(8, 0);
            Vector2Int newOffice = V(4, 1);
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
            for (int x = 0; x <= 4; x++) Assert.IsTrue(engine.Place(V(x, 1), TileType.Road));
            Assert.IsTrue(engine.Place(V(0, 0), TileType.House));
            Assert.IsTrue(engine.Place(V(1, 0), TileType.House));
            Assert.IsTrue(engine.Place(V(5, 1), TileType.Office));
            return engine;
        }
    }
}
