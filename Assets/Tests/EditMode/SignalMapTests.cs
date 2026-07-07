using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public class SignalMapTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        // 십자: 가로 y=2 + 세로 x=2 → 중앙(2,2)만 이웃 4
        static CityGrid CrossCity()
        {
            var g = new CityGrid(5, 5);
            for (int x = 0; x < 5; x++) g.Place(V(x, 2), TileType.Road);
            for (int y = 0; y < 5; y++) if (y != 2) g.Place(V(2, y), TileType.Road);
            return g;
        }

        [Test]
        public void Cross_CenterGetsSignal_ArmsDoNot()
        {
            var map = new SignalMap();
            map.Rebuild(CrossCity());

            Assert.IsTrue(map.TryGet(V(2, 2), out _));    // 교차점(이웃4) = 신호
            Assert.IsFalse(map.TryGet(V(1, 2), out _));   // 직선 구간(이웃2) = 없음
            Assert.IsFalse(map.TryGet(V(0, 2), out _));   // 끝(이웃1) = 없음
            Assert.AreEqual(1, map.Tiles.Count);
        }

        [Test]
        public void TJunction_GetsSignal()
        {
            // 가로 도로 + 위로 한 칸 곁가지 → (2,0)이 T자(이웃3)
            var g = new CityGrid(5, 3);
            for (int x = 0; x < 5; x++) g.Place(V(x, 0), TileType.Road);
            g.Place(V(2, 1), TileType.Road);

            var map = new SignalMap();
            map.Rebuild(g);

            Assert.IsTrue(map.TryGet(V(2, 0), out _));
            Assert.AreEqual(1, map.Tiles.Count);
        }

        [Test]
        public void Offset_SurvivesRebuild_RemovedWhenNotIntersection()
        {
            var g = CrossCity();
            var map = new SignalMap();
            map.Rebuild(g);

            map.TryGet(V(2, 2), out var sig);
            sig.OffsetSlots = 5;                          // 유저가 조율했다고 치고

            g.Place(V(4, 3), TileType.Road);              // 무관한 topology 변경
            map.Rebuild(g);
            Assert.IsTrue(map.TryGet(V(2, 2), out var again));
            Assert.AreEqual(5, again.OffsetSlots);        // 오프셋 보존

            g.Remove(V(2, 1)); g.Remove(V(2, 3));         // 십자 팔 제거 → 더는 교차로 아님
            map.Rebuild(g);
            Assert.IsFalse(map.TryGet(V(2, 2), out _));   // 신호 소멸
        }
    }
}
