using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public class BurstDetectorTests
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

        // 한 틱: 수요를 바꿔가며 배정→혼잡→감지까지 돌리고 발행.
        static void Step(float demand, ref SimConfig cfg, CityGrid g, DemandMap dm,
            RoadNetwork net, FlowSolver solver, BurstDetector detector, SimEventBuffer buffer)
        {
            cfg.DemandPerHouse = demand;
            solver.Assign(dm, net, cfg);
            solver.Resolve(cfg);
            detector.Scan(solver, buffer, cfg);
            buffer.Drain();
        }

        static SimConfig BurstCfg()
        {
            var c = SimConfig.Default();      // tick 0.1, Jam 진입 1.0, Free 복귀 0.6
            c.RoadCapacity = 10f;
            c.BurstRewardThreshold = 0.5f;
            c.BurstRewardMultiplier = 5f;
            return c;
        }

        [Test]
        public void JamThenRelief_FiresSingleBurst_AtBottleneck()
        {
            // 계획 3: 과수요로 Jam(pending 0.6 적립) → 해소 → 전이에서 Burst 딱 1회,
            // 금액 = round(0.6×5) = 3, 위치 = 병목 타일.
            var g = StraightCity();
            var cfg = BurstCfg();
            var dm = new DemandMap(cfg); dm.Reassign(g, new RoadNetwork(g));
            var net = new RoadNetwork(g);
            var solver = new FlowSolver(g.Width, g.Height);
            var detector = new BurstDetector(g.Width, g.Height);

            var hub = new SimEventHub();
            int bursts = 0, reward = 0; Vector2Int tile = default;
            hub.FlowBurst += e => { bursts++; reward += e.Reward; tile = e.Tile; };
            var buffer = new SimEventBuffer(hub);

            Step(15f, ref cfg, g, dm, net, solver, detector, buffer); // ratio 1.5 → Jam 진입
            Assert.AreEqual(0, bursts);                               // 해소 전엔 없음

            Step(1f, ref cfg, g, dm, net, solver, detector, buffer);  // ratio 0.1 → Free 복귀 = 전이
            Assert.AreEqual(1, bursts);
            Assert.AreEqual(3, reward);                               // round(0.6 × 5)
            Assert.AreEqual(V(0, 0), tile);                           // 병목(경로 첫 최대 ratio 타일)

            Step(1f, ref cfg, g, dm, net, solver, detector, buffer);  // 계속 Free → 추가 발행 없음
            Assert.AreEqual(1, bursts);
        }

        [Test]
        public void BoundaryOscillation_NoBurstSpam()
        {
            // 계획 4: ratio 0.9↔1.1 진동. 1.1은 Jam 진입이지만 0.9는 복귀선(0.6) '위'
            // → 히스테리시스가 전이를 막아 Burst 0회.
            var g = StraightCity();
            var cfg = BurstCfg();
            var dm = new DemandMap(cfg); dm.Reassign(g, new RoadNetwork(g));
            var net = new RoadNetwork(g);
            var solver = new FlowSolver(g.Width, g.Height);
            var detector = new BurstDetector(g.Width, g.Height);

            var hub = new SimEventHub();
            int bursts = 0;
            hub.FlowBurst += e => bursts++;
            var buffer = new SimEventBuffer(hub);

            for (int i = 0; i < 3; i++)
            {
                Step(11f, ref cfg, g, dm, net, solver, detector, buffer); // ratio 1.1
                Step(9f, ref cfg, g, dm, net, solver, detector, buffer);  // ratio 0.9
            }

            Assert.AreEqual(0, bursts);
        }
    }
}
