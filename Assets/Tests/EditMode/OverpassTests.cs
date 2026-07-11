using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    // 입체교차(스펙 2026-07-12): 교차로 4형제의 넷째 — 축 분리로 간섭 소멸. 3자 배타.
    public class OverpassTests
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
            e.Place(V(0, 1), TileType.House);
            e.Place(V(9, 1), TileType.Office);
            e.Tick(0.25f);
            return e;
        }

        [Test]
        public void Place_OnIntersection_Works_AndListsFlatSorted()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsTrue(e.CanPlaceOverpass(V(6, 0)));
            Assert.IsTrue(e.TryPlaceOverpass(V(6, 0)));       // 뒤 타일 먼저
            Assert.IsTrue(e.TryPlaceOverpass(V(3, 0)));
            Assert.AreEqual(2, e.OverpassTiles.Count);
            Assert.AreEqual(V(3, 0), e.OverpassTiles[0]);     // flat 정렬(결정론)
            Assert.AreEqual(V(6, 0), e.OverpassTiles[1]);
        }

        [Test]
        public void Place_RejectsNonIntersection_Duplicate_AndAutoMode()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsFalse(e.TryPlaceOverpass(V(1, 0)));      // 직선 도로 — 교차로 아님
            Assert.IsFalse(e.TryPlaceOverpass(V(1, 1)));      // 도로 아님
            Assert.IsTrue(e.TryPlaceOverpass(V(3, 0)));
            Assert.IsFalse(e.TryPlaceOverpass(V(3, 0)));      // 중복
            var auto = Build(autoDetect: true, out _);
            Assert.IsFalse(auto.CanPlaceOverpass(V(3, 0)));   // 자동 모드 — 배치 개념 없음
            Assert.IsFalse(auto.TryPlaceOverpass(V(3, 0)));
        }

        [Test]
        public void ThreeWayExclusive_OneDevicePerTile()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsTrue(e.TryPlaceSignal(V(3, 0), 8));
            Assert.IsFalse(e.TryPlaceOverpass(V(3, 0)));      // 신호 위 금지
            Assert.IsTrue(e.TryPlaceOverpass(V(6, 0)));
            Assert.IsFalse(e.TryPlaceSignal(V(6, 0), 8));     // 입체 위 신호 금지
            Assert.IsFalse(e.TryPlaceRoundabout(V(6, 0)));    // 입체 위 로터리 금지
            Assert.IsTrue(e.TryRemoveOverpass(V(6, 0)));      // 철거 후 교체 가능
            Assert.IsTrue(e.TryPlaceRoundabout(V(6, 0)));
            Assert.IsFalse(e.TryPlaceOverpass(V(6, 0)));      // 로터리 위 입체 금지
        }

        [Test]
        public void RoadRemoval_KillsOverpassNextRebuild()
        {
            var e = Build(autoDetect: false, out _);
            e.TryPlaceOverpass(V(3, 0));
            e.Remove(V(3, 1));                                // 곁가지 철거 → 교차로 해제
            e.Tick(0.25f);                                    // 재구축 소비
            Assert.AreEqual(0, e.OverpassTiles.Count);        // 자동 소멸
            Assert.IsFalse(e.TryRemoveOverpass(V(3, 0)));
        }
    }
}
