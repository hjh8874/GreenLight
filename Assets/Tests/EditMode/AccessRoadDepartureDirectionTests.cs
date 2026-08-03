using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public class AccessRoadDepartureDirectionTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        [Test]
        public void DepartureEntryDir_OrthogonalFrontage_PointsFromBuildingToRoad()
        {
            var grid = new CityGrid(8, 8);
            // 집 (2,2) 2x2 → (2,2)(3,2)(2,3)(3,3) 점유. 그 위 y=4 가 도로.
            Assert.IsTrue(grid.Place(V(2, 4), TileType.Road));
            Assert.IsTrue(grid.Place(V(3, 4), TileType.Road));
            Assert.IsTrue(grid.Place(V(2, 2), TileType.House));
            var net = new RoadNetwork(grid);

            Assert.IsTrue(net.TryGetDepartureEntryDir(V(2, 2), V(2, 4), out Dir entry),
                "집 셀 (2,3) 이 도로 (2,4) 와 직교 인접하므로 방향이 나와야 한다");
            Assert.AreEqual(Dir.N, entry, "건물에서 도로로 향하는 진행 방향은 북쪽이다");
        }

        [Test]
        public void DepartureEntryDir_DiagonalOnlyFrontage_ReturnsFalse()
        {
            var grid = new CityGrid(8, 8);
            // 집 (2,2) 2x2 점유는 x=2..3, y=2..3. (4,4) 는 (3,3) 과 대각으로만 닿는다.
            Assert.IsTrue(grid.Place(V(4, 4), TileType.Road));
            Assert.IsTrue(grid.Place(V(2, 2), TileType.House));
            var net = new RoadNetwork(grid);

            Assert.IsFalse(net.TryGetDepartureEntryDir(V(2, 2), V(4, 4), out _),
                "대각으로만 닿은 진입로는 진출 방향을 정의할 수 없다 — 호출자가 exit 폴백을 쓴다");
        }
    }
}
