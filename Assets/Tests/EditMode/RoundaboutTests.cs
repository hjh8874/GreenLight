using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    // 회전교차로(스펙 2026-07-11): 신호·무신호에 이은 셋째 형제. 배치 모드 전용, 신호와 배타.
    public class RoundaboutTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        // SignalPlacementTests와 동일 기하: 직선 도로 + 곁가지 2개 → 교차로 (3,0)·(6,0).
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
            e.Place(V(0, 1), TileType.House);
            e.Place(V(9, 1), TileType.Office);
            e.Tick(0.25f);                        // 재구축 소비
            return e;
        }

        [Test]
        public void Place_OnIntersection_Works_AndListsFlatSorted()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsTrue(e.CanPlaceRoundabout(V(6, 0)));
            Assert.IsTrue(e.TryPlaceRoundabout(V(6, 0)));     // 뒤 타일 먼저
            Assert.IsTrue(e.TryPlaceRoundabout(V(3, 0)));
            Assert.AreEqual(2, e.RoundaboutTiles.Count);
            Assert.AreEqual(V(3, 0), e.RoundaboutTiles[0]);   // flat 정렬(결정론)
            Assert.AreEqual(V(6, 0), e.RoundaboutTiles[1]);
        }

        [Test]
        public void Place_RejectsNonIntersection_Duplicate_AndAutoMode()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsFalse(e.TryPlaceRoundabout(V(1, 0)));    // 직선 도로 — 교차로 아님
            Assert.IsFalse(e.TryPlaceRoundabout(V(1, 1)));    // 도로 아님
            Assert.IsTrue(e.TryPlaceRoundabout(V(3, 0)));
            Assert.IsFalse(e.TryPlaceRoundabout(V(3, 0)));    // 중복
            Assert.IsFalse(e.CanPlaceRoundabout(V(3, 0)));

            var auto = Build(autoDetect: true, out _);
            Assert.IsFalse(auto.CanPlaceRoundabout(V(3, 0))); // 자동 모드 — 배치 개념 없음
            Assert.IsFalse(auto.TryPlaceRoundabout(V(3, 0)));
        }

        [Test]
        public void SignalAndRoundabout_AreMutuallyExclusive()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsTrue(e.TryPlaceSignal(V(3, 0), 8));
            Assert.IsFalse(e.CanPlaceRoundabout(V(3, 0)));    // 신호 위에 로터리 금지
            Assert.IsFalse(e.TryPlaceRoundabout(V(3, 0)));
            Assert.IsTrue(e.TryPlaceRoundabout(V(6, 0)));
            Assert.IsFalse(e.CanPlaceSignal(V(6, 0)));        // 로터리 위에 신호 금지
            Assert.IsFalse(e.TryPlaceSignal(V(6, 0), 8));
            // 철거 후엔 교체 가능(한 타일 한 장치 — 교체는 "철거 후 배치")
            Assert.IsTrue(e.TryRemoveRoundabout(V(6, 0)));
            Assert.IsTrue(e.TryPlaceSignal(V(6, 0), 8));
        }

        [Test]
        public void Remove_Works_AndRejectsAbsent()
        {
            var e = Build(autoDetect: false, out _);
            e.TryPlaceRoundabout(V(3, 0));
            Assert.IsTrue(e.TryRemoveRoundabout(V(3, 0)));
            Assert.AreEqual(0, e.RoundaboutTiles.Count);
            Assert.IsFalse(e.TryRemoveRoundabout(V(3, 0)));   // 이미 없음
        }

        [Test]
        public void RoadRemoval_KillsRoundaboutNextRebuild()
        {
            var e = Build(autoDetect: false, out _);
            e.TryPlaceRoundabout(V(3, 0));
            e.Remove(V(3, 1));                                // 곁가지 철거 → (3,0) 교차로 해제
            e.Tick(0.25f);                                    // 재구축 소비
            Assert.AreEqual(0, e.RoundaboutTiles.Count);      // 자동 소멸(신호와 동일 규약)
            Assert.IsFalse(e.TryRemoveRoundabout(V(3, 0)));
        }
    }
}
