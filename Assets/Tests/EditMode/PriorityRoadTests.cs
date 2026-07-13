using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    // 우선도로(스펙 2026-07-13): 로터리 배치 골격 + 일방통행의 축 값 조합. 신호·로터리·입체와 4자 배타.
    public class PriorityRoadTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        // RoundaboutTests와 동일 기하: 직선 도로 + 곁가지 2개 → 교차로 (3,0)·(6,0).
        static SimEngine Build(bool autoDetect, out SimEventHub hub)
        {
            var c = SimConfig.Default();
            c.TickInterval = 0.25f;
            c.GridWidth = 10; c.GridHeight = 2;
            c.AutoDetectSignals = autoDetect;
            hub = new SimEventHub();
            var e = new SimEngine(c, hub);
            for (int x = 0; x <= 9; x++) e.Place(V(x, 0), TileType.Road);
            e.Place(V(3, 1), TileType.Road);
            e.Place(V(6, 1), TileType.Road);
            e.Tick(0.25f);                        // 재구축 소비
            return e;
        }

        [Test]
        public void Place_OnIntersection_Works_AndStoresAxis()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsTrue(e.TryPlacePriorityRoad(V(3, 0), Axis.Horizontal));
            Assert.AreEqual(1, e.PriorityRoadTiles.Count);
            Assert.AreEqual(Axis.Horizontal, e.GetPriorityAxis(V(3, 0)));
        }

        [Test]
        public void Place_RejectsNonIntersection_Duplicate_AndAutoMode()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsFalse(e.TryPlacePriorityRoad(V(1, 0), Axis.Horizontal)); // 비교차로
            Assert.IsTrue(e.TryPlacePriorityRoad(V(3, 0), Axis.Horizontal));
            Assert.IsFalse(e.TryPlacePriorityRoad(V(3, 0), Axis.Vertical));   // 중복

            var auto = Build(autoDetect: true, out _);
            Assert.IsFalse(auto.CanPlacePriorityRoad(V(3, 0)));               // 자동 모드 — 배치 개념 없음
            Assert.IsFalse(auto.TryPlacePriorityRoad(V(3, 0), Axis.Horizontal));
        }

        [Test]
        public void PriorityRoad_And_Roundabout_AreMutuallyExclusive()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsTrue(e.TryPlaceRoundabout(V(3, 0)));
            Assert.IsFalse(e.TryPlacePriorityRoad(V(3, 0), Axis.Horizontal)); // 로터리 있음 → 우선도로 거부

            Assert.IsTrue(e.TryPlacePriorityRoad(V(6, 0), Axis.Horizontal));
            Assert.IsFalse(e.TryPlaceRoundabout(V(6, 0)));                   // 우선도로 있음 → 로터리 거부(역방향)
        }

        [Test]
        public void Remove_Works_AndRejectsAbsent()
        {
            var e = Build(autoDetect: false, out _);
            e.TryPlacePriorityRoad(V(3, 0), Axis.Horizontal);
            Assert.IsTrue(e.TryRemovePriorityRoad(V(3, 0)));
            Assert.AreEqual(0, e.PriorityRoadTiles.Count);
            Assert.IsFalse(e.TryRemovePriorityRoad(V(3, 0)));
        }
    }
}
