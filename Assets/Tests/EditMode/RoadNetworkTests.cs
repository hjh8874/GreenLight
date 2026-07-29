using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public class RoadNetworkTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static CityGrid GridWithRoads(int w, int h, params Vector2Int[] roads)
        {
            var g = new CityGrid(w, h);
            foreach (var r in roads) g.Place(r, TileType.Road);
            return g;
        }

        [Test]
        public void AccessRoad_DiagonalNeighbor_Connects()
        {
            // 8방향: 건물(0,0)의 직각 이웃엔 도로 없고 대각(1,1)만 도로 → 대각 접점으로 연결.
            var g = GridWithRoads(5, 5, V(1, 1));
            var net = new RoadNetwork(g);
            Assert.IsTrue(net.TryGetAccessRoad(V(0, 0), out var road));
            Assert.AreEqual(V(1, 1), road);
        }

        [Test]
        public void AccessRoad_PicksFirstNeighborInScanOrder()
        {
            // 건물(2,2)의 이웃 중 상(2,3)·우(3,2)가 도로. 스캔 순서(상·우·하·좌)상 상이 먼저.
            var g = GridWithRoads(5, 5, V(2, 3), V(3, 2));
            var net = new RoadNetwork(g);

            Assert.IsTrue(net.TryGetAccessRoad(V(2, 2), out var road));
            Assert.AreEqual(V(2, 3), road);
        }

        [Test]
        public void AccessRoad_NoAdjacentRoad_ReturnsFalse()
        {
            var g = GridWithRoads(5, 5, V(0, 0)); // (3,3) 주변엔 도로 없음
            var net = new RoadNetwork(g);

            Assert.IsFalse(net.TryGetAccessRoad(V(3, 3), out _));
        }

        [Test]
        public void AccessRoad_RotatedBuilding_PrefersSelectedFrontage()
        {
            var g = new CityGrid(8, 8);
            Vector2Int building = V(3, 3);
            Assert.IsTrue(g.Place(
                building,
                TileType.Office,
                PlacementDirection.East));

            Assert.IsTrue(g.Place(V(3, 2), TileType.Road));
            Assert.IsTrue(g.Place(V(5, 3), TileType.Road));
            Assert.IsTrue(g.Place(V(3, 5), TileType.Road));
            Assert.IsTrue(g.Place(V(2, 3), TileType.Road));

            var net = new RoadNetwork(g);

            Assert.IsTrue(net.TryGetAccessRoad(building, out var road));
            Assert.AreEqual(
                V(5, 3),
                road,
                "East 방향 건물은 동쪽 주차장 앞 도로를 우선 접점으로 사용해야 한다.");

            var frontages = new List<Vector2Int>();
            net.CollectAccessRoads(building, frontages);
            Assert.AreEqual(V(5, 3), frontages[0]);
        }

        [Test]
        public void AccessRoad_LegacyNorthWithoutSelectedFrontage_FallsBack()
        {
            var g = new CityGrid(8, 8);
            Vector2Int building = V(3, 3);
            Assert.IsTrue(g.Place(
                building,
                TileType.House,
                PlacementDirection.North));
            Assert.IsTrue(g.Place(V(3, 5), TileType.Road));

            var net = new RoadNetwork(g);

            Assert.IsTrue(net.TryGetAccessRoad(building, out var road));
            Assert.AreEqual(
                V(3, 5),
                road,
                "선택한 앞면에 도로가 없더라도 구버전 도시의 기존 접근 도로는 유지해야 한다.");
        }

        [Test]
        public void AccessRoad_RotatedBuildingWithoutFrontRoad_IsUnreachable()
        {
            var g = new CityGrid(8, 8);
            Vector2Int building = V(3, 3);
            Assert.IsTrue(g.Place(
                building,
                TileType.House,
                PlacementDirection.East));
            Assert.IsTrue(g.Place(V(3, 5), TileType.Road));

            var net = new RoadNetwork(g);

            Assert.IsFalse(net.TryGetAccessRoad(building, out _));

            var frontages = new List<Vector2Int>();
            net.CollectAccessRoads(building, frontages);
            Assert.That(frontages, Is.Empty);
        }
    }
}
