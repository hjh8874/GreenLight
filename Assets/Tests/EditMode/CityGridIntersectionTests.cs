using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    // CityGrid.IsIntersection: 교차로 규칙(직각 도로 이웃 ≥3)의 단일 출처.
    public class CityGridIntersectionTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        [Test]
        public void IsIntersection_CrossAndTee_DetectedArmsAndStraightNot()
        {
            var g = new CityGrid(5, 5);
            for (int x = 0; x <= 4; x++) g.Place(V(x, 2), TileType.Road);   // 가로줄
            g.Place(V(2, 3), TileType.Road);                                 // T자 가지

            Assert.IsTrue(g.IsIntersection(V(2, 2)));    // T자(이웃 3)
            Assert.IsFalse(g.IsIntersection(V(1, 2)));   // 직선(이웃 2)
            Assert.IsFalse(g.IsIntersection(V(2, 3)));   // 가지 끝(이웃 1)
            Assert.IsFalse(g.IsIntersection(V(0, 0)));   // 도로 아님
            Assert.IsFalse(g.IsIntersection(V(-1, 2)));  // OOB 무사고

            g.Place(V(2, 1), TileType.Road);             // 십자로 승격
            Assert.IsTrue(g.IsIntersection(V(2, 2)));    // 이웃 4
        }

        [Test]
        public void IsIntersection_RecomputesAfterRemove()
        {
            var g = new CityGrid(5, 5);
            for (int x = 0; x <= 4; x++) g.Place(V(x, 2), TileType.Road);
            g.Place(V(2, 3), TileType.Road);
            Assert.IsTrue(g.IsIntersection(V(2, 2)));

            g.Remove(V(2, 3));                            // 가지 철거 → TopologyVersion++
            Assert.IsFalse(g.IsIntersection(V(2, 2)));    // lazy 캐시가 버전 키로 재계산
        }
    }
}
