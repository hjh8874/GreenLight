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
        public void StraightLine_ReturnsPathInOrder()
        {
            var g = GridWithRoads(5, 5, V(0, 0), V(1, 0), V(2, 0));
            var net = new RoadNetwork(g);
            Assert.AreEqual(new[] { V(0, 0), V(1, 0), V(2, 0) }, net.FindPath(V(0, 0), V(2, 0)));
        }

        [Test]
        public void LShaped_TurnsCorner()
        {
            var g = GridWithRoads(5, 5, V(0, 0), V(0, 1), V(0, 2), V(1, 2), V(2, 2));
            var net = new RoadNetwork(g);
            Assert.AreEqual(
                new[] { V(0, 0), V(0, 1), V(0, 2), V(1, 2), V(2, 2) },
                net.FindPath(V(0, 0), V(2, 2)));
        }

        [Test]
        public void Disconnected_ReturnsNull()
        {
            var g = GridWithRoads(6, 5, V(0, 0), V(1, 0), V(4, 0), V(5, 0)); // (2,0),(3,0) 없음
            var net = new RoadNetwork(g);
            Assert.IsNull(net.FindPath(V(0, 0), V(5, 0)));
        }

        [Test]
        public void NonRoadEndpoint_ReturnsNull()
        {
            var g = GridWithRoads(5, 5, V(0, 0), V(1, 0));
            var net = new RoadNetwork(g);
            Assert.IsNull(net.FindPath(V(0, 0), V(2, 0))); // (2,0)은 Empty
        }

        [Test]
        public void SameTile_ReturnsSingle()
        {
            var g = GridWithRoads(5, 5, V(2, 2));
            var net = new RoadNetwork(g);
            Assert.AreEqual(new[] { V(2, 2) }, net.FindPath(V(2, 2), V(2, 2)));
        }

        [Test]
        public void TopologyChange_InvalidatesCache()
        {
            var g = GridWithRoads(5, 5, V(0, 0), V(1, 0), V(3, 0), V(4, 0)); // (2,0) 빔
            var net = new RoadNetwork(g);
            Assert.IsNull(net.FindPath(V(0, 0), V(4, 0))); // 미연결 → null (캐시됨)

            g.Place(V(2, 0), TileType.Road);               // topology 변경 → Version++
            Assert.AreEqual(                               // 자동 무효화 후 재탐색
                new[] { V(0, 0), V(1, 0), V(2, 0), V(3, 0), V(4, 0) },
                net.FindPath(V(0, 0), V(4, 0)));
        }
    }
}
