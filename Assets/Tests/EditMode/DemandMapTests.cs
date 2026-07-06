using NUnit.Framework;
using UnityEngine;

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

        static SimConfig Caps(int company, int school)
        {
            var c = SimConfig.Default();
            c.CompanyCapacity = company;
            c.SchoolCapacity = school;
            return c;
        }

        static bool Has(DemandMap dm, Vector2Int src, Vector2Int sink)
        {
            foreach (var d in dm.Demands)
                if (d.Source == src && d.Sink == sink) return true;
            return false;
        }

        [Test]
        public void AssignsHouseToNearestCompany()
        {
            var g = MakeGrid(8, 1,
                (V(0, 0), TileType.House),
                (V(2, 0), TileType.Company),
                (V(5, 0), TileType.Company));
            var dm = new DemandMap(SimConfig.Default());
            dm.Reassign(g);

            Assert.AreEqual(1, dm.Demands.Count);        // 학교 없음 → 회사 1건만
            Assert.IsTrue(Has(dm, V(0, 0), V(2, 0)));    // 더 가까운 (2,0)
        }

        [Test]
        public void CapacityFull_OverflowsToNextNearest()
        {
            var g = MakeGrid(8, 2,
                (V(0, 0), TileType.House),
                (V(0, 1), TileType.House),
                (V(1, 0), TileType.Company),   // 두 집 모두에 가장 가까움
                (V(7, 1), TileType.Company));  // 먼 대안
            var dm = new DemandMap(Caps(company: 1, school: 10));
            dm.Reassign(g);

            Assert.IsTrue(Has(dm, V(0, 0), V(1, 0)));  // 첫 집: 가까운 회사가 받음
            Assert.IsTrue(Has(dm, V(0, 1), V(7, 1)));  // 둘째 집: 만석 → 먼 회사로
        }

        [Test]
        public void MultiDestination_CompanyAndSchool()
        {
            var g = MakeGrid(5, 5,
                (V(0, 0), TileType.House),
                (V(2, 0), TileType.Company),
                (V(0, 2), TileType.School));
            var dm = new DemandMap(SimConfig.Default());
            dm.Reassign(g);

            Assert.AreEqual(2, dm.Demands.Count);
            Assert.IsTrue(Has(dm, V(0, 0), V(2, 0)));  // 회사 통근
            Assert.IsTrue(Has(dm, V(0, 0), V(0, 2)));  // 학교 통근
        }

        [Test]
        public void NoSinkOfType_NoDemandOfType()
        {
            var g = MakeGrid(5, 5,
                (V(0, 0), TileType.House),
                (V(2, 0), TileType.Company)); // 학교 없음
            var dm = new DemandMap(SimConfig.Default());
            dm.Reassign(g);

            Assert.AreEqual(1, dm.Demands.Count);
            Assert.IsTrue(Has(dm, V(0, 0), V(2, 0)));
        }

        [Test]
        public void EquidistantSinks_PicksLowerFlatIndex()
        {
            var g = MakeGrid(5, 1,
                (V(2, 0), TileType.House),
                (V(0, 0), TileType.Company),   // flat 0, 거리 2
                (V(4, 0), TileType.Company));  // flat 4, 거리 2 (동점)
            var dm = new DemandMap(SimConfig.Default());
            dm.Reassign(g);

            Assert.IsTrue(Has(dm, V(2, 0), V(0, 0)));   // 동점 → 낮은 인덱스
            Assert.IsFalse(Has(dm, V(2, 0), V(4, 0)));
        }
    }
}
