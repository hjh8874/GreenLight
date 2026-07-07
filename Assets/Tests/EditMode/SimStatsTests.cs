using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public class SimStatsTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static CityGrid StraightCity()
        {
            var g = new CityGrid(5, 2);
            for (int x = 0; x <= 4; x++) g.Place(V(x, 0), TileType.Road);
            g.Place(V(0, 1), TileType.House);
            g.Place(V(4, 1), TileType.Office);
            return g;
        }

        static SimStats StatsFor(CityGrid g, in SimConfig cfg)
        {
            var dm = new DemandMap(cfg);
            dm.Reassign(g);
            var net = new RoadNetwork(g);
            var solver = new FlowSolver(g.Width, g.Height);
            solver.Assign(dm, net, cfg);
            solver.Resolve(cfg);
            var stats = new SimStats();
            stats.Update(solver, dm, cfg);
            return stats;
        }

        static SimConfig Cfg(float demand)
        {
            var c = SimConfig.Default();
            c.DemandPerHouse = demand;
            c.RoadCapacity = 10f;
            return c;
        }

        [Test]
        public void AllFree_StabilityIsOne()
        {
            // 계획 8: 전 타일 Free → delivered = 수요 전부 → 안정도 1.0
            Assert.AreEqual(1f, StatsFor(StraightCity(), Cfg(1f)).Stability01, 1e-4f);
        }

        [Test]
        public void JamOnMainPath_StabilityDrops()
        {
            // 주요 경로 Jam: 수요 15/용량 10 → E 0.6 → delivered 9/15 → 안정도 0.6
            Assert.AreEqual(0.6f, StatsFor(StraightCity(), Cfg(15f)).Stability01, 1e-3f);
        }

        [Test]
        public void EmptyCity_StabilityIsOne()
        {
            // 수요 자체가 없으면(빈 도시) 불안정할 것도 없음 → 1.0, 0으로 나누기 없음.
            Assert.AreEqual(1f, StatsFor(new CityGrid(5, 2), Cfg(1f)).Stability01, 1e-4f);
        }
    }
}
