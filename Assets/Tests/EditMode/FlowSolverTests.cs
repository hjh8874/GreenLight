using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public class FlowSolverTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        // 집(0,1) ─ 접점(0,0) ─ 도로 (0,0)~(4,0) ─ 접점(4,0) ─ 회사(4,1)
        static CityGrid StraightCity()
        {
            var g = new CityGrid(5, 2);
            for (int x = 0; x <= 4; x++) g.Place(V(x, 0), TileType.Road);
            g.Place(V(0, 1), TileType.House);
            g.Place(V(4, 1), TileType.Office);
            return g;
        }

        // 산술을 고정하기 위해 테스트가 쓰는 숫자는 전부 명시(Default 드리프트 무관).
        static SimConfig Cfg(float demand, float capacity)
        {
            var c = SimConfig.Default();
            c.DemandPerHouse = demand;
            c.RoadCapacity = capacity;
            return c;
        }

        static FlowSolver Solve(CityGrid g, in SimConfig cfg)
        {
            var dm = new DemandMap(cfg);
            dm.Reassign(g, new RoadNetwork(g));
            var net = new RoadNetwork(g);
            var solver = new FlowSolver(g.Width, g.Height);
            solver.Assign(dm, net, cfg);
            solver.Resolve(cfg);
            return solver;
        }

        [Test]
        public void StraightRoad_UnderCapacity_DeliversFullDemand()
        {
            // 수요 1 < 용량 10 → ratio 0.1 → E=1 → delivered = 수요 그대로, 도로는 Free.
            var solver = Solve(StraightCity(), Cfg(demand: 1f, capacity: 10f));

            Assert.AreEqual(1f, solver.DeliveredTotal, 1e-4f);
            Assert.AreEqual(CongestionLevel.Free, solver.GetCongestion(V(2, 0)));
        }

        [Test]
        public void OverCapacity_DecaysDelivered_JamsRoad_AccruesPendingReward()
        {
            // 수요 15 > 용량 10 → ratio 1.5 → Jam.
            // E = 1 + (1.5-1)/(2.0-1)×(0.2-1) = 0.6 → delivered = 15×0.6 = 9.
            // pending(병목 = 경로에서 최대 ratio가 처음 나오는 타일 (0,0))
            //   = 잃은 처리량 × 틱 = 15×(1-0.6)×0.1 = 0.6.
            var solver = Solve(StraightCity(), Cfg(demand: 15f, capacity: 10f));

            Assert.AreEqual(9f, solver.DeliveredTotal, 1e-3f);
            Assert.AreEqual(CongestionLevel.Jam, solver.GetCongestion(V(2, 0)));
            Assert.AreEqual(0.6f, solver.GetPendingReward(V(0, 0)), 1e-3f);
        }

        [Test]
        public void Efficiency_MonotonicallyDecreasesWithCongestion()
        {
            // 계획 2d: 자유 구간 E=1 고정, Jam~바닥 사이 단조 감소, 바닥 밑으로는 안 내려감.
            var cfg = SimConfig.Default(); // Jam 1.0 → 2.0에서 바닥 0.2

            Assert.AreEqual(1f, FlowSolver.Efficiency(0.5f, cfg), 1e-4f);
            Assert.AreEqual(1f, FlowSolver.Efficiency(1.0f, cfg), 1e-4f);
            Assert.Greater(FlowSolver.Efficiency(1.2f, cfg), FlowSolver.Efficiency(1.8f, cfg));
            Assert.AreEqual(0.2f, FlowSolver.Efficiency(2.0f, cfg), 1e-4f);
            Assert.AreEqual(0.2f, FlowSolver.Efficiency(5.0f, cfg), 1e-4f); // 바닥 유지
        }

        [Test]
        public void DisconnectedOrNoAccess_DeliversZero_NoCrash()
        {
            // 집·회사 사이 도로 없음(수요는 생김) → 흐름 0, 예외 없음. 계획 5의 solver 조각.
            var g = new CityGrid(5, 2);
            g.Place(V(0, 1), TileType.House);
            g.Place(V(4, 1), TileType.Office);

            var solver = Solve(g, Cfg(demand: 1f, capacity: 10f));

            Assert.AreEqual(0f, solver.DeliveredTotal, 1e-4f);
            Assert.AreEqual(CongestionLevel.Free, solver.GetCongestion(V(2, 0)));
        }
    }
}
