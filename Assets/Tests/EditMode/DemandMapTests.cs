using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public class DemandMapTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static CityGrid MakeGrid(int w, int h, params (Vector2Int pos, TileType type)[] tiles)
        {
            var g = new CityGrid(w, h);
            foreach (var t in tiles) g.Place(t.pos, t.type);
            return g;
        }

        static SimConfig OfficeCap(int cap)
        {
            var c = SimConfig.Default();
            c.OfficeCapacity = cap;
            return c;
        }

        static bool Has(DemandMap dm, Vector2Int src, Vector2Int sink)
        {
            foreach (var d in dm.Demands)
                if (d.Source == src && d.Sink == sink) return true;
            return false;
        }

        [Test]
        public void AssignsHouseToNearestOffice()
        {
            var g = MakeGrid(8, 1,
                (V(0, 0), TileType.House),
                (V(2, 0), TileType.Office),
                (V(5, 0), TileType.Office));
            var dm = new DemandMap(SimConfig.Default());
            dm.Reassign(g);

            Assert.AreEqual(1, dm.Demands.Count);
            Assert.IsTrue(Has(dm, V(0, 0), V(2, 0))); // 더 가까운 (2,0)
        }

        [Test]
        public void CapacityFull_OverflowsToNextNearest()
        {
            var g = MakeGrid(8, 2,
                (V(0, 0), TileType.House),
                (V(0, 1), TileType.House),
                (V(1, 0), TileType.Office),   // 두 집 모두에 가장 가까움
                (V(7, 1), TileType.Office));  // 먼 대안
            var dm = new DemandMap(OfficeCap(1));
            dm.Reassign(g);

            Assert.IsTrue(Has(dm, V(0, 0), V(1, 0))); // 첫 집: 가까운 곳
            Assert.IsTrue(Has(dm, V(0, 1), V(7, 1))); // 둘째 집: 만석 → 먼 곳
        }

        [Test]
        public void NoOffice_NoDemand()
        {
            var g = MakeGrid(5, 5, (V(0, 0), TileType.House)); // 수요처 없음
            var dm = new DemandMap(SimConfig.Default());
            dm.Reassign(g);

            Assert.AreEqual(0, dm.Demands.Count);
        }

        [Test]
        public void EquidistantOffices_PicksLowerFlatIndex()
        {
            var g = MakeGrid(5, 1,
                (V(2, 0), TileType.House),
                (V(0, 0), TileType.Office),   // flat 0, 거리 2
                (V(4, 0), TileType.Office));  // flat 4, 거리 2 (동점)
            var dm = new DemandMap(SimConfig.Default());
            dm.Reassign(g);

            Assert.IsTrue(Has(dm, V(2, 0), V(0, 0))); // 동점 → 낮은 인덱스
            Assert.IsFalse(Has(dm, V(2, 0), V(4, 0)));
        }
    }
}
