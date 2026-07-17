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
            cfg.UseCarSim = true;
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
            cfg.OfflineCapHours = 8f;
            cfg.DemandChoicePool = 1;
            return cfg;
        }

        [Test]
        public void Default_SwitchIsOff()
        {
            Assert.IsFalse(SimConfig.Default().UseCarSim);
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
        public void SwitchOn_MiniCityArrivesAndPaysPerCar()
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

        [Test]
        public void Offline_UsesDaysPopulationCoinAndPureSuccessRate()
        {
            SimConfig cfg = Cfg();
            var settlements = new List<SettlementEvent>();
            var hub = new SimEventHub();
            hub.SettlementComputed += settlements.Add;
            SimEngine engine = BuildStraightCity(cfg, hub);
            engine.SetGameHour(7f);
            engine.Tick(0.25f); // topology + CarCount=2

            engine.SettleOffline(12.0); // DayLengthSeconds=24 → 0.5일

            Assert.AreEqual(1, settlements.Count);
            Assert.AreEqual(10L, settlements[0].Coins); // 0.5 × 2 × 10 × 초기 success 1
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
