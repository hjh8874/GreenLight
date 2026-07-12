using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;

namespace CityFlow.Sim.Tests
{
    // 일방통행(스펙 2026-07-12): 교차로 3형제(신호·로터리·입체)와 달리 일반 도로 전용 — 자연 배타.
    // Task 1 = 배치 API + 세이브(아래). Task 2 = 라우팅 간선 필터(우회·미도달·합류 구분) — 파일 하단.
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
        public void Place_OOBInput_ReturnsFalse_NoException()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsFalse(e.CanPlaceOneway(V(-1, 0)));
            Assert.IsFalse(e.TryPlaceOneway(V(-1, 0), E));
            Assert.IsFalse(e.CanPlaceOneway(V(99, 99)));
            Assert.IsFalse(e.TryPlaceOneway(V(99, 99), E));
        }

        [Test]
        public void PlaceAndRemove_MarkTopologyDirty()
        {
            var e = Build(autoDetect: false, out _);        // Build 끝 Tick이 dirty 소비
            Assert.IsFalse(e.TopologyDirtyForTest);
            Assert.IsTrue(e.TryPlaceOneway(V(1, 0), E));
            Assert.IsTrue(e.TopologyDirtyForTest);          // 배치 = 재계획 강제
            e.Tick(0.25f);
            Assert.IsTrue(e.TryRemoveOneway(V(1, 0)));
            Assert.IsTrue(e.TopologyDirtyForTest);          // 철거도 동일
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

    // Task 2: 라우팅 간선 필터(스펙 §핵심결정 3규칙 — 상태 확장 없음). RoutePlanner.Search/Plan
    // 레벨 유닛 테스트(RoutePlannerTests와 동일 스타일) — SimEngine 배치 규칙과는 독립.
    public class RoutePlannerOnewayTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);
        static readonly Vector2Int E = new Vector2Int(1, 0);

        static CityGrid Roads(int w, int h, params Vector2Int[] tiles)
        {
            var g = new CityGrid(w, h);
            foreach (var t in tiles) g.Place(t, TileType.Road);
            return g;
        }

        static SimConfig Cfg()
        {
            var c = SimConfig.Default();
            c.DemandPerHouse = 1f;
            c.RoadCapacity = 10f;
            c.DemandChoicePool = 1;
            return c;
        }

        // 사각 순환 도로(중앙 (2,2) 비도로) — 위 가로줄 (1,1)-(2,1)-(3,1), 아래 가로줄 (1,3)-(2,3)-(3,3),
        // 좌우 측면 (1,2)·(3,2)가 둘을 잇는다. 동쪽 접점(3,1) 옆 House, 서쪽 접점(1,1) 옆 Office.
        static CityGrid RingCity()
        {
            var g = new CityGrid(5, 5);
            g.Place(V(1, 1), TileType.Road); g.Place(V(2, 1), TileType.Road); g.Place(V(3, 1), TileType.Road);
            g.Place(V(1, 2), TileType.Road); g.Place(V(3, 2), TileType.Road);
            g.Place(V(1, 3), TileType.Road); g.Place(V(2, 3), TileType.Road); g.Place(V(3, 3), TileType.Road);
            g.Place(V(4, 1), TileType.House);
            g.Place(V(0, 1), TileType.Office);
            return g;
        }

        // 스펙 §4.2 — 우회 라우팅: 위 가로줄 중앙(2,1)이 동쪽만 허용되면 서쪽행 수요는
        // 링을 반대쪽(아래 가로줄)으로 돌아야 도달(직접 관통 금지).
        [Test]
        public void Search_OnewayForcesDetour_AroundRing()
        {
            var g = RingCity();
            var planner = new RoutePlanner(g.Width, g.Height);
            var cfg = Cfg();

            var direct = planner.Search(g, V(3, 1), V(1, 1), cfg);
            Assert.AreEqual(new[] { V(3, 1), V(2, 1), V(1, 1) }, direct);   // 필터 없음 = 최단 직선

            var oneways = new Dictionary<Vector2Int, Vector2Int> { [V(2, 1)] = E };
            var detoured = planner.Search(g, V(3, 1), V(1, 1), cfg, oneways);
            Assert.IsNotNull(detoured);
            Assert.IsFalse(detoured.Contains(V(2, 1)), "일방 타일을 역방향으로 관통하면 안 됨");
            Assert.AreEqual(V(3, 1), detoured[0]);
            Assert.AreEqual(V(1, 1), detoured[detoured.Count - 1]);
            Assert.Greater(detoured.Count, direct.Count, "직선보다 길어야 진짜 우회");
            // 대각 지름길(코너 컷) 허용은 기존 Search 규약(RoutePlannerTests의 L자 지름길과 동일) —
            // 여기선 "링 반대쪽(아래 가로줄)을 지나는가"만 확인, 정확한 타일열은 핀하지 않음.
            Assert.IsTrue(
                detoured.Contains(V(1, 3)) || detoured.Contains(V(2, 3)) || detoured.Contains(V(3, 3)),
                "아래 가로줄을 지나야 함");
        }

        // 스펙 §4.3 — 미도달 무사고: 우회로 없는 외길에서 역방향 일방 → 해당 수요 미배정(예외 없음).
        [Test]
        public void Search_OnewayWithNoBypass_UnreachableReturnsNull_NoException()
        {
            var g = Roads(5, 3, V(0, 1), V(1, 1), V(2, 1));
            var planner = new RoutePlanner(g.Width, g.Height);
            var cfg = Cfg();
            var oneways = new Dictionary<Vector2Int, Vector2Int> { [V(1, 1)] = E };

            List<Vector2Int> result = null;
            Assert.DoesNotThrow(() => result = planner.Search(g, V(2, 1), V(0, 1), cfg, oneways));
            Assert.IsNull(result);                                                // 역방향 = 미도달, 무사고
            Assert.IsNotNull(planner.Search(g, V(0, 1), V(2, 1), cfg, oneways));  // 순방향은 정상
        }

        // 스펙 §4.4 — 측면 합류 허용(수직 진입 ≠ -D) / 역주행 진입 금지(-D) 구분 핀.
        [Test]
        public void Search_DistinguishesSideMergeFromReverseEntry()
        {
            var cfg = Cfg();
            var oneways = new Dictionary<Vector2Int, Vector2Int> { [V(1, 1)] = E };

            // 측면 합류: 지선(북, (1,2)) → 일방 타일 진입(수직 스텝, ≠ -D) 허용.
            var withBranch = Roads(3, 3, V(1, 1), V(1, 2));
            var mergePlanner = new RoutePlanner(withBranch.Width, withBranch.Height);
            var merged = mergePlanner.Search(withBranch, V(1, 2), V(1, 1), cfg, oneways);
            Assert.AreEqual(new[] { V(1, 2), V(1, 1) }, merged);

            // 역주행 진입: 동쪽에서 서쪽 스텝(-D)으로 진입 시도 — 우회 없는 외길에서 금지.
            var straight = Roads(3, 3, V(0, 1), V(1, 1), V(2, 1));
            var reversePlanner = new RoutePlanner(straight.Width, straight.Height);
            Assert.IsNull(reversePlanner.Search(straight, V(2, 1), V(1, 1), cfg, oneways));
        }

        // 스펙 §4.6 — 결정론: 같은 도시(+같은 일방 상태)면 같은 Plan(우회 경로 포함).
        [Test]
        public void Plan_WithOneways_Deterministic_SameCitySamePlan()
        {
            var g = RingCity();
            var net = new RoadNetwork(g);
            var cfg = Cfg();
            var dm = new DemandMap(cfg); dm.Reassign(g, net);
            Assert.AreEqual(1, dm.Demands.Count);

            var oneways = new Dictionary<Vector2Int, Vector2Int> { [V(2, 1)] = E };

            var a = new RoutePlanner(g.Width, g.Height); a.Plan(dm, net, g, cfg, oneways);
            var b = new RoutePlanner(g.Width, g.Height); b.Plan(dm, net, g, cfg, oneways);

            Assert.AreEqual(a.Routes.Count, b.Routes.Count);
            for (int i = 0; i < a.Routes.Count; i++)
                CollectionAssert.AreEqual(a.Routes[i], b.Routes[i]);
            Assert.IsNotNull(a.Routes[0]);
            Assert.IsFalse(a.Routes[0].Contains(V(2, 1)));   // 우회가 채택됨(직접 관통 없음)
        }
    }
}
