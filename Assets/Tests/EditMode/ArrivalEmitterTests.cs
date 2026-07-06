using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public class ArrivalEmitterTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        // 집(0,1) ─ 도로 (0,0)~(4,0) ─ 회사(4,1)
        static CityGrid StraightCity()
        {
            var g = new CityGrid(5, 2);
            for (int x = 0; x <= 4; x++) g.Place(V(x, 0), TileType.Road);
            g.Place(V(0, 1), TileType.House);
            g.Place(V(4, 1), TileType.Office);
            return g;
        }

        [Test]
        public void FractionalDelivery_CarriesAcrossTicks()
        {
            // 계획 7b: rate 0.5건/s × tick 0.25s = 틱당 0.125(정확한 float)
            // → 8틱(2초)마다 도착 정확히 1건, 소수 이월 정확.
            var cfg = SimConfig.Default();
            cfg.TickInterval = 0.25f;
            cfg.DemandPerHouse = 0.5f;
            cfg.RoadCapacity = 10f;
            cfg.CoinBase = 3f;

            var g = StraightCity();
            var dm = new DemandMap(cfg);
            dm.Reassign(g);
            var net = new RoadNetwork(g);
            var solver = new FlowSolver(g.Width, g.Height);
            solver.Assign(dm, net, cfg);
            solver.Resolve(cfg);

            var hub = new SimEventHub();
            int arrivals = 0, coins = 0;
            Vector2Int dest = default;
            hub.Arrival += e => { arrivals++; coins += e.Coins; dest = e.Destination; };
            var buffer = new SimEventBuffer(hub);
            var emitter = new ArrivalEmitter(g.Width, g.Height);

            for (int t = 0; t < 8; t++) emitter.Emit(solver, buffer, cfg);
            buffer.Drain();
            Assert.AreEqual(1, arrivals);
            Assert.AreEqual(3, coins);           // CoinBase 그대로
            Assert.AreEqual(V(4, 1), dest);      // 도착지 = 회사 '건물' 타일

            for (int t = 0; t < 8; t++) emitter.Emit(solver, buffer, cfg);
            buffer.Drain();
            Assert.AreEqual(2, arrivals);        // 이월 누적이 정확해야 딱 8틱마다 1건
        }
    }
}
