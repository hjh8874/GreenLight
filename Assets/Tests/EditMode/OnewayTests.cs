using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;

namespace CityFlow.Sim.Tests
{
    // 일방통행(스펙 2026-07-12): 교차로 3형제(신호·로터리·입체)와 달리 일반 도로 전용 — 자연 배타.
    // Task 1 = 배치 API + 세이브. 라우팅 필터(우회·미도달·합류 구분)는 Task 2(RoutePlannerTests 쪽) 몫.
    public class OnewayTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);
        static readonly Vector2Int E = new Vector2Int(1, 0);
        static readonly Vector2Int N = new Vector2Int(0, 1);
        static readonly Vector2Int S = new Vector2Int(0, -1);

        // RoundaboutTests.Build와 동일 기하: 직선 도로 + 곁가지 2개 → 교차로 (3,0)·(6,0)(거부 핀).
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
        public void Place_OnStraightRoad_Works_AndListsFlatSorted()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsTrue(e.CanPlaceOneway(V(5, 0)));
            Assert.IsTrue(e.TryPlaceOneway(V(5, 0), E));   // 뒤 타일 먼저
            Assert.IsTrue(e.TryPlaceOneway(V(1, 0), E));
            Assert.AreEqual(2, e.OnewayTiles.Count);
            Assert.AreEqual(V(1, 0), e.OnewayTiles[0]);    // flat 정렬(결정론)
            Assert.AreEqual(V(5, 0), e.OnewayTiles[1]);
            Assert.AreEqual(E, e.GetOnewayDir(V(1, 0)));
        }

        [Test]
        public void Place_RejectsIntersection_NonRoad_Duplicate_AndAutoMode()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsFalse(e.CanPlaceOneway(V(3, 0)));
            Assert.IsFalse(e.TryPlaceOneway(V(3, 0), E));   // 교차로 — 거부 핀
            Assert.IsFalse(e.TryPlaceOneway(V(6, 0), E));   // 교차로 — 거부 핀
            Assert.IsFalse(e.TryPlaceOneway(V(0, 1), E));   // 도로 아님(House)
            Assert.IsTrue(e.TryPlaceOneway(V(1, 0), E));
            Assert.IsFalse(e.TryPlaceOneway(V(1, 0), N));   // 중복(방향 회전은 철거+재배치로 — 재배치 API 아님)
            Assert.IsFalse(e.CanPlaceOneway(V(1, 0)));

            var auto = Build(autoDetect: true, out _);
            Assert.IsFalse(auto.CanPlaceOneway(V(1, 0)));   // 자동 모드 — 배치 개념 없음
            Assert.IsFalse(auto.TryPlaceOneway(V(1, 0), E));
        }

        [Test]
        public void Place_RejectsDiagonal_AndZeroDir_AndNonUnit()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsFalse(e.TryPlaceOneway(V(1, 0), new Vector2Int(1, 1)));   // 대각
            Assert.IsFalse(e.TryPlaceOneway(V(1, 0), Vector2Int.zero));        // zero
            Assert.IsFalse(e.TryPlaceOneway(V(1, 0), new Vector2Int(2, 0)));   // 비단위
            Assert.AreEqual(0, e.OnewayTiles.Count);
            Assert.AreEqual(Vector2Int.zero, e.GetOnewayDir(V(1, 0)));         // 없으면 zero
        }

        [Test]
        public void Rotation_IsRemoveThenRePlace()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsTrue(e.TryPlaceOneway(V(1, 0), E));
            Assert.IsTrue(e.TryRemoveOneway(V(1, 0)));
            Assert.IsTrue(e.TryPlaceOneway(V(1, 0), S));   // 재배치 API 아님 — 철거+배치 조합으로 회전
            Assert.AreEqual(S, e.GetOnewayDir(V(1, 0)));
        }

        [Test]
        public void Remove_Works_AndRejectsAbsent()
        {
            var e = Build(autoDetect: false, out _);
            e.TryPlaceOneway(V(1, 0), E);
            Assert.IsTrue(e.TryRemoveOneway(V(1, 0)));
            Assert.AreEqual(0, e.OnewayTiles.Count);
            Assert.AreEqual(Vector2Int.zero, e.GetOnewayDir(V(1, 0)));
            Assert.IsFalse(e.TryRemoveOneway(V(1, 0)));    // 이미 없음
        }

        [Test]
        public void RoadRemoval_PrunesOnewayNextRebuild()
        {
            var e = Build(autoDetect: false, out _);
            e.TryPlaceOneway(V(1, 0), E);
            e.Remove(V(1, 0));                             // 도로 자체 철거
            e.Tick(0.25f);                                 // 재구축 소비
            Assert.AreEqual(0, e.OnewayTiles.Count);
            Assert.IsFalse(e.TryRemoveOneway(V(1, 0)));
        }

        [Test]
        public void IntersectionFormation_PrunesOnewayNextRebuild()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsTrue(e.TryPlaceOneway(V(4, 0), E));
            e.Place(V(4, 1), TileType.Road);               // 곁가지 추가 → (4,0) 교차로화(배치 조건 위반)
            e.Tick(0.25f);                                 // 재구축 소비
            Assert.AreEqual(0, e.OnewayTiles.Count);       // 신호 가족과 동일 규약(조건은 반대)
        }

        [Test]
        public void SaveRoundtrip_RestoresOnewaysWithDirection()
        {
            var e = Build(autoDetect: false, out _);
            e.TryPlaceOneway(V(5, 0), E);
            e.TryPlaceOneway(V(1, 0), N);
            var snap = e.CreateSnapshot();

            var fresh = Build(autoDetect: false, out _);
            fresh.RestoreSnapshot(snap);
            fresh.Tick(0.25f);
            Assert.AreEqual(2, fresh.OnewayTiles.Count);
            Assert.AreEqual(V(1, 0), fresh.OnewayTiles[0]);   // flat 정렬 복구
            Assert.AreEqual(V(5, 0), fresh.OnewayTiles[1]);
            Assert.AreEqual(N, fresh.GetOnewayDir(V(1, 0)));
            Assert.AreEqual(E, fresh.GetOnewayDir(V(5, 0)));
            Assert.IsTrue(fresh.TryRemoveOneway(V(1, 0)));    // 소유까지 복원
        }

        [Test]
        public void SaveRoundtrip_RejectsCorruptedIntersectionEntry()
        {
            var e = Build(autoDetect: false, out _);
            e.TryPlaceOneway(V(1, 0), E);
            var snap = e.CreateSnapshot();
            // 손상 세이브 주입: 교차로 좌표(3,0)에 얹힌 엔트리 — 복원 시 배치 조건 재검증으로 버려져야 함.
            var corrupted = new System.Collections.Generic.List<OnewaySaveData>(snap.Oneways)
            {
                new OnewaySaveData { X = 3, Y = 0, DirX = 1, DirY = 0 }
            };
            snap.Oneways = corrupted.ToArray();

            var fresh = Build(autoDetect: false, out _);
            fresh.RestoreSnapshot(snap);
            fresh.Tick(0.25f);
            Assert.AreEqual(1, fresh.OnewayTiles.Count);      // 교차로 엔트리는 버림, 정상 엔트리만 채택
            Assert.AreEqual(V(1, 0), fresh.OnewayTiles[0]);
        }

        [Test]
        public void LegacySave_WithoutOneways_RestoresClean()
        {
            var e = Build(autoDetect: false, out _);
            var snap = e.CreateSnapshot();
            snap.Oneways = null;                               // 구세이브 = 필드 없음
            var fresh = Build(autoDetect: false, out _);
            fresh.TryPlaceOneway(V(1, 0), E);                  // 이전 세션 잔존 상태
            fresh.RestoreSnapshot(snap);
            fresh.Tick(0.25f);
            Assert.AreEqual(0, fresh.OnewayTiles.Count);       // 복원 = 전체 교체
        }
    }
}
