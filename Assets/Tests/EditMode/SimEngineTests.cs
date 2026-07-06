using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public class SimEngineTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        // TickInterval을 0.25f로: float로 정확히 표현되는 1/4이라
        // 0.1f의 반올림 잡음 없이 누산기 '로직'만 순수하게 검증한다.
        static SimConfig Cfg(float tick, int cap = 5)
        {
            var c = SimConfig.Default();
            c.TickInterval = tick;
            c.MaxStepsPerFrame = cap;
            return c;
        }

        static SimEngine Engine(SimConfig c) => new SimEngine(c, new SimEventHub());

        [Test]
        public void Accumulator_FourEighthTicks_ProduceTwoSteps()
        {
            var e = Engine(Cfg(0.25f));
            for (int i = 0; i < 4; i++) e.Tick(0.125f); // 0.125 ×4 = 0.5 = 2 틱
            Assert.AreEqual(2, e.StepCount);
        }

        [Test]
        public void Accumulator_CarriesRemainderAcrossCalls()
        {
            var e = Engine(Cfg(0.25f));
            e.Tick(0.875f);                  // 0.875 / 0.25 = 3.5 → 3 스텝, 잔여 0.125
            Assert.AreEqual(3, e.StepCount);
            e.Tick(0.125f);                  // 잔여 0.125 + 0.125 = 0.25 → +1 스텝
            Assert.AreEqual(4, e.StepCount);
        }

        [Test]
        public void Accumulator_CapsStepsPerFrame()
        {
            var e = Engine(Cfg(0.25f, cap: 5));
            e.Tick(100f);                    // 원래 400 스텝이지만 캡에 걸려 5
            Assert.AreEqual(5, e.StepCount);
        }

        [Test]
        public void EndToEnd_PlacedCity_FlowsAfterOneTick()
        {
            // 파사드 관통: Place(IPlacementService)로 도시 배치 → Tick 1스텝 →
            // IReadOnlyTileData로 조회. 수요 1/용량 10 → ratio 0.1 → Free, density 0.1.
            var c = Cfg(0.25f);
            c.GridWidth = 5; c.GridHeight = 2;
            c.DemandPerHouse = 1f; c.RoadCapacity = 10f;
            var e = Engine(c);

            for (int x = 0; x <= 4; x++) Assert.IsTrue(e.Place(V(x, 0), TileType.Road));
            Assert.IsTrue(e.Place(V(0, 1), TileType.House));
            Assert.IsTrue(e.Place(V(4, 1), TileType.Office));

            e.Tick(0.25f); // 정확히 1스텝

            Assert.AreEqual(TileType.House, e.GetTileType(V(0, 1)));
            Assert.AreEqual(CongestionLevel.Free, e.GetCongestion(V(2, 0)));
            Assert.AreEqual(0.1f, e.GetDensity01(V(2, 0)), 1e-4f);
        }

        [Test]
        public void EndToEnd_ArrivalPublishedThroughHub()
        {
            // 파이프라인 완주: rate 0.5/s × 8틱(0.25s) = 2초 시뮬 → 도착 정확히 1건이 hub로.
            var c = Cfg(0.25f);
            c.GridWidth = 5; c.GridHeight = 2;
            c.DemandPerHouse = 0.5f; c.RoadCapacity = 10f; c.CoinBase = 1f;

            var hub = new SimEventHub();
            int arrivals = 0, coins = 0;
            hub.Arrival += ev => { arrivals++; coins += ev.Coins; };
            var e = new SimEngine(c, hub);

            for (int x = 0; x <= 4; x++) e.Place(V(x, 0), TileType.Road);
            e.Place(V(0, 1), TileType.House);
            e.Place(V(4, 1), TileType.Office);

            for (int i = 0; i < 8; i++) e.Tick(0.25f);

            Assert.AreEqual(1, arrivals);
            Assert.AreEqual(1, coins);
        }

        [Test]
        public void EndToEnd_JamCity_StabilityDrops()
        {
            // 계획 8을 파사드로: 수요 15/용량 10 → E 0.6 → Stability01 = 9/15 = 0.6
            var c = Cfg(0.25f);
            c.GridWidth = 5; c.GridHeight = 2;
            c.DemandPerHouse = 15f; c.RoadCapacity = 10f;
            var e = new SimEngine(c, new SimEventHub());

            for (int x = 0; x <= 4; x++) e.Place(V(x, 0), TileType.Road);
            e.Place(V(0, 1), TileType.House);
            e.Place(V(4, 1), TileType.Office);

            e.Tick(0.25f);

            Assert.AreEqual(0.6f, e.Stability01, 1e-3f);
        }
    }
}
