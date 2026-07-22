using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;

namespace CityFlow.Sim.Tests
{
    // 턴 제한 표지판(스펙 2026-07-12): 5번째 배치 가족 — 교차로 전용이되 신호와 공존(로터리·입체와만 배타).
    // Task 1 = 배치 API + 세이브(아래). Task 2 = 라우팅 상태 확장(P턴 등) — 별도 파일/섹션.
    public class TurnRestrictTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        // OnewayTests/RoundaboutTests와 동일 기하: 직선 도로 + 곁가지 2개 → 교차로 (3,0)·(6,0).
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
            Assert.IsTrue(e.CanPlaceTurnSign(V(6, 0)));
            Assert.IsTrue(e.TryPlaceTurnSign(V(6, 0), TurnMode.RightOnly));   // 뒤 타일 먼저
            Assert.IsTrue(e.TryPlaceTurnSign(V(3, 0), TurnMode.LeftOnly));
            Assert.AreEqual(2, e.TurnSignTiles.Count);
            Assert.AreEqual(V(3, 0), e.TurnSignTiles[0]);   // flat 정렬(결정론)
            Assert.AreEqual(V(6, 0), e.TurnSignTiles[1]);
            Assert.AreEqual(TurnMode.LeftOnly, e.GetTurnMode(V(3, 0)));
            Assert.AreEqual(TurnMode.RightOnly, e.GetTurnMode(V(6, 0)));
        }

        [Test]
        public void Place_RejectsNonIntersection_Duplicate_AndAutoMode()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsFalse(e.TryPlaceTurnSign(V(1, 0), TurnMode.LeftOnly));   // 직선 도로 — 교차로 아님
            Assert.IsFalse(e.TryPlaceTurnSign(V(0, 1), TurnMode.LeftOnly));   // 도로 아님(House)
            Assert.IsTrue(e.TryPlaceTurnSign(V(3, 0), TurnMode.LeftOnly));
            Assert.IsFalse(e.TryPlaceTurnSign(V(3, 0), TurnMode.RightOnly));  // 중복(모드 변경은 철거+재배치)
            Assert.IsFalse(e.CanPlaceTurnSign(V(3, 0)));

            var auto = Build(autoDetect: true, out _);
            Assert.IsFalse(auto.CanPlaceTurnSign(V(3, 0)));   // 자동 모드 — 배치 개념 없음
            Assert.IsFalse(auto.TryPlaceTurnSign(V(3, 0), TurnMode.LeftOnly));
        }

        [Test]
        public void Place_RejectsRoundaboutAndOverpass()
        {
            var e = Build(autoDetect: false, out _);
            // 로터리 선점 → 표지판 배치 거부
            Assert.IsTrue(e.TryPlaceRoundabout(V(3, 0)));
            Assert.IsFalse(e.CanPlaceTurnSign(V(3, 0)));
            Assert.IsFalse(e.TryPlaceTurnSign(V(3, 0), TurnMode.LeftOnly));

            // 입체교차 선점 → 표지판 배치 거부
            Assert.IsTrue(e.TryPlaceOverpass(V(6, 0)));
            Assert.IsFalse(e.CanPlaceTurnSign(V(6, 0)));
            Assert.IsFalse(e.TryPlaceTurnSign(V(6, 0), TurnMode.RightOnly));

            // 철거 후엔 배치 가능(한 타일 한 장치 규약)
            Assert.IsTrue(e.TryRemoveRoundabout(V(3, 0)));
            Assert.IsTrue(e.TryPlaceTurnSign(V(3, 0), TurnMode.LeftOnly));
        }

        // 계획 정정(2026-07-12): 배타는 양방향 — 표지판 타일에 로터리/입체도 배치 불가.
        // ("3형제 무수정" 문구는 신호 공존 의도의 과확장이었음 — 신호만 공존, 로터리/입체는 상호 배타.)
        [Test]
        public void TurnSignBlocksRoundaboutAndOverpass_Bidirectional()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsTrue(e.TryPlaceTurnSign(V(3, 0), TurnMode.LeftOnly));
            Assert.IsFalse(e.CanPlaceRoundabout(V(3, 0)));
            Assert.IsFalse(e.TryPlaceRoundabout(V(3, 0)));    // 표지판 위 로터리 금지
            Assert.IsFalse(e.CanPlaceOverpass(V(3, 0)));
            Assert.IsFalse(e.TryPlaceOverpass(V(3, 0)));      // 표지판 위 입체 금지
            // 철거 후엔 배치 가능(한 타일 한 장치 — 교체는 "철거 후 배치")
            Assert.IsTrue(e.TryRemoveTurnSign(V(3, 0)));
            Assert.IsTrue(e.TryPlaceRoundabout(V(3, 0)));
        }

        // 스펙 핵심결정: 신호(시간 배분)와 표지판(방향 배분)은 직교 — 같은 교차로에 공존(양방향 핀).
        [Test]
        public void SignalAndTurnSign_Coexist_BothDirections()
        {
            var e = Build(autoDetect: false, out _);
            // 신호 먼저, 표지판 나중
            Assert.IsTrue(e.TryPlaceSignal(V(3, 0), 8));
            Assert.IsTrue(e.CanPlaceTurnSign(V(3, 0)));
            Assert.IsTrue(e.TryPlaceTurnSign(V(3, 0), TurnMode.LeftOnly));
            Assert.AreEqual(TurnMode.LeftOnly, e.GetTurnMode(V(3, 0)));

            // 표지판 먼저, 신호 나중(반대 방향)
            Assert.IsTrue(e.TryPlaceTurnSign(V(6, 0), TurnMode.RightOnly));
            Assert.IsTrue(e.CanPlaceSignal(V(6, 0)));
            Assert.IsTrue(e.TryPlaceSignal(V(6, 0), 8));
        }

        [Test]
        public void Rotation_IsRemoveThenRePlace()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsTrue(e.TryPlaceTurnSign(V(3, 0), TurnMode.LeftOnly));
            Assert.IsTrue(e.TryRemoveTurnSign(V(3, 0)));
            Assert.IsTrue(e.TryPlaceTurnSign(V(3, 0), TurnMode.RightOnly));   // 재배치 API 아님 — 철거+배치 조합
            Assert.AreEqual(TurnMode.RightOnly, e.GetTurnMode(V(3, 0)));
        }

        [Test]
        public void Remove_Works_AndRejectsAbsent()
        {
            var e = Build(autoDetect: false, out _);
            e.TryPlaceTurnSign(V(3, 0), TurnMode.LeftOnly);
            Assert.IsTrue(e.TryRemoveTurnSign(V(3, 0)));
            Assert.AreEqual(0, e.TurnSignTiles.Count);
            Assert.IsNull(e.GetTurnMode(V(3, 0)));
            Assert.IsFalse(e.TryRemoveTurnSign(V(3, 0)));    // 이미 없음
        }

        [Test]
        public void Place_OOBInput_ReturnsFalse_NoException()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsFalse(e.CanPlaceTurnSign(V(-1, 0)));
            Assert.IsFalse(e.TryPlaceTurnSign(V(-1, 0), TurnMode.LeftOnly));
            Assert.IsFalse(e.CanPlaceTurnSign(V(99, 99)));
            Assert.IsFalse(e.TryPlaceTurnSign(V(99, 99), TurnMode.LeftOnly));
        }

        [Test]
        public void PlaceAndRemove_MarkTopologyDirty()
        {
            var e = Build(autoDetect: false, out _);        // Build 끝 Tick이 dirty 소비
            Assert.IsFalse(e.TopologyDirtyForTest);
            Assert.IsTrue(e.TryPlaceTurnSign(V(3, 0), TurnMode.LeftOnly));
            Assert.IsTrue(e.TopologyDirtyForTest);          // 배치 = 재계획 강제
            e.Tick(0.25f);
            Assert.IsTrue(e.TryRemoveTurnSign(V(3, 0)));
            Assert.IsTrue(e.TopologyDirtyForTest);          // 철거도 동일
        }

        [Test]
        public void IntersectionDissolution_PrunesTurnSignNextRebuild()
        {
            var e = Build(autoDetect: false, out _);
            e.TryPlaceTurnSign(V(3, 0), TurnMode.LeftOnly);
            e.Remove(V(3, 1));                              // 곁가지 철거 → (3,0) 교차로 해제
            e.Tick(0.25f);                                   // 재구축 소비
            Assert.AreEqual(0, e.TurnSignTiles.Count);      // 자동 소멸(신호 가족과 동일 규약)
            Assert.IsNull(e.GetTurnMode(V(3, 0)));
            Assert.IsFalse(e.TryRemoveTurnSign(V(3, 0)));
        }

        [Test]
        public void SaveRoundtrip_RestoresTurnSignsWithMode()
        {
            var e = Build(autoDetect: false, out _);
            e.TryPlaceTurnSign(V(6, 0), TurnMode.RightOnly);
            e.TryPlaceTurnSign(V(3, 0), TurnMode.LeftOnly);
            var snap = e.CreateSnapshot();

            var fresh = Build(autoDetect: false, out _);
            fresh.RestoreSnapshot(snap);
            fresh.Tick(0.25f);
            Assert.AreEqual(2, fresh.TurnSignTiles.Count);
            Assert.AreEqual(V(3, 0), fresh.TurnSignTiles[0]);   // flat 정렬 복구
            Assert.AreEqual(V(6, 0), fresh.TurnSignTiles[1]);
            Assert.AreEqual(TurnMode.LeftOnly, fresh.GetTurnMode(V(3, 0)));
            Assert.AreEqual(TurnMode.RightOnly, fresh.GetTurnMode(V(6, 0)));
            Assert.IsTrue(fresh.TryRemoveTurnSign(V(3, 0)));    // 소유까지 복원
        }

        [Test]
        public void SaveRoundtrip_RejectsCorruptedEntries()
        {
            var e = Build(autoDetect: false, out _);
            e.TryPlaceTurnSign(V(3, 0), TurnMode.LeftOnly);
            e.TryPlaceRoundabout(V(6, 0));                      // 로터리 선점 좌표(세이브에 함께 포함)
            var snap = e.CreateSnapshot();
            // 손상 세이브 주입: 비교차로 좌표(1,0) + 로터리 선점 좌표(6,0) — 복원 시 배치 조건 재검증으로 버려져야 함.
            var corrupted = new List<TurnSignSaveData>(snap.TurnSigns)
            {
                new TurnSignSaveData { X = 1, Y = 0, Mode = (int)TurnMode.LeftOnly },
                new TurnSignSaveData { X = 6, Y = 0, Mode = (int)TurnMode.RightOnly },
            };
            snap.TurnSigns = corrupted.ToArray();

            var fresh = Build(autoDetect: false, out _);
            fresh.RestoreSnapshot(snap);
            fresh.Tick(0.25f);
            Assert.AreEqual(1, fresh.TurnSignTiles.Count);      // 손상 엔트리는 버림, 정상 엔트리만 채택
            Assert.AreEqual(V(3, 0), fresh.TurnSignTiles[0]);
        }

        [Test]
        public void LegacySave_WithoutTurnSigns_RestoresClean()
        {
            var e = Build(autoDetect: false, out _);
            var snap = e.CreateSnapshot();
            snap.TurnSigns = null;                               // 구세이브 = 필드 없음
            var fresh = Build(autoDetect: false, out _);
            fresh.TryPlaceTurnSign(V(3, 0), TurnMode.LeftOnly);   // 이전 세션 잔존 상태
            fresh.RestoreSnapshot(snap);
            fresh.Tick(0.25f);
            Assert.AreEqual(0, fresh.TurnSignTiles.Count);       // 복원 = 전체 교체
        }

        // ── Task 2 리뷰 위임 핀(필수 #8): 손상 세이브 Mode=99(존재하지 않는 enum 값) 거부 ──
        // RestoreSnapshot의 (mode == TurnMode.LeftOnly || mode == TurnMode.RightOnly) 재검증이
        // 이미 이 값을 걸러낸다(Task 1 구현) — 이 테스트는 그 회귀를 고정한다.
        [Test]
        public void SaveRoundtrip_RejectsCorruptedMode99()
        {
            var e = Build(autoDetect: false, out _);
            e.TryPlaceTurnSign(V(3, 0), TurnMode.LeftOnly);
            var snap = e.CreateSnapshot();
            var corrupted = new List<TurnSignSaveData>(snap.TurnSigns)
            {
                new TurnSignSaveData { X = 6, Y = 0, Mode = 99 },   // 존재하지 않는 모드값(손상)
            };
            snap.TurnSigns = corrupted.ToArray();

            var fresh = Build(autoDetect: false, out _);
            fresh.RestoreSnapshot(snap);
            fresh.Tick(0.25f);
            Assert.AreEqual(1, fresh.TurnSignTiles.Count);        // Mode=99 엔트리는 버림, 정상 엔트리만 채택
            Assert.AreEqual(V(3, 0), fresh.TurnSignTiles[0]);
            Assert.IsNull(fresh.GetTurnMode(V(6, 0)));
        }

        // ── PR#55 리뷰 지적(abicodue, SimEngine.cs:380 인근): 배치 API도 세이브 복원과 대칭으로
        // 미정의 TurnMode 값을 거부해야 한다 — TryPlaceTurnSign((TurnMode)2, ...) 직접 호출 경로.
        // SaveRoundtrip_RejectsCorruptedMode99와 대칭(복원 경로는 이미 검증, 배치 경로도 이제 동일).
        [Test]
        public void TryPlaceTurnSign_RejectsUndefinedMode()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsFalse(e.TryPlaceTurnSign(V(3, 0), (TurnMode)2));   // 존재하지 않는 모드값(손상)
            Assert.AreEqual(0, e.TurnSignTiles.Count);
            Assert.IsNull(e.GetTurnMode(V(3, 0)));
        }

        // ── Task 2 리뷰 위임 핀(필수 #9): 신호 철거/재배치 후에도 같은 타일의 표지판은 생존 ──
        // TryRemoveSignal/TryPlaceSignal이 _turnSigns를 건드리지 않는다(신호·표지판 독립 소유) — 고정.
        [Test]
        public void SignalRemoval_TurnSignSurvives()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsTrue(e.TryPlaceSignal(V(3, 0), 8));
            Assert.IsTrue(e.TryPlaceTurnSign(V(3, 0), TurnMode.LeftOnly));

            Assert.IsTrue(e.TryRemoveSignal(V(3, 0)));
            Assert.AreEqual(TurnMode.LeftOnly, e.GetTurnMode(V(3, 0)));   // 신호 철거해도 표지판 생존
            Assert.AreEqual(1, e.TurnSignTiles.Count);

            Assert.IsTrue(e.TryPlaceSignal(V(3, 0), 4));                  // 재배치(다른 초록 슬롯)
            Assert.AreEqual(TurnMode.LeftOnly, e.GetTurnMode(V(3, 0)));   // 재배치 후에도 생존
        }
    }

    // Task 2: 라우팅 상태 확장(스펙 §핵심결정 — 타일×진입방향, 표지판 0개면 레거시 무풍).
    // RoutePlanner.Search/Plan 레벨 유닛 테스트(RoutePlannerOnewayTests와 동일 스타일) —
    // SimEngine 배치 규칙(CanPlaceTurnSign 등)과는 독립. turnSigns 딕셔너리를 직접 구성해 전달.
    public class RoutePlannerTurnRestrictTests
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
            c.RoadCapacity = 10f;
            c.DemandChoicePool = 1;
            return c;
        }

        // OnewayTests.RingCity와 동일 기하(레거시 무풍·공존 핀 재사용) — 사각 순환 도로,
        // 동쪽 접점(3,1) 옆 House, 서쪽 접점(1,1) 옆 Office.
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

        // P턴 창발용 클러스터: House(1,0)→접점(1,1)[표지판,진출 무제약=시작] — 메인 표지판(2,1)
        // LeftOnly — 곁가지(2,2) — 접점(3,1)[표지판]→Office(3,0). (1,1)·(3,1)도 표지판인 이유:
        // 대각 코너컷(예: (1,1)-(2,2), (2,2)-(3,1))이 표지판 관여 금지 규칙에 걸려 회전을 우회하는
        // 지름길을 차단한다(실측: 대각 지름길은 이 엔진 규약상 항상 허용되므로, 막지 않으면
        // Dijkstra가 물리적으로 더 짧은 코너컷을 택해 P턴이 창발하지 않음 — 기하 함정 회피).
        static CityGrid PTurnCity()
        {
            var g = new CityGrid(4, 3);
            g.Place(V(1, 1), TileType.Road);
            g.Place(V(2, 1), TileType.Road);
            g.Place(V(3, 1), TileType.Road);
            g.Place(V(2, 2), TileType.Road);
            g.Place(V(1, 0), TileType.House);
            g.Place(V(3, 0), TileType.Office);
            return g;
        }

        static CityGrid PTurnDemandCity()
        {
            var g = new CityGrid(5, 4);
            g.Place(V(1, 2), TileType.Road);
            g.Place(V(2, 2), TileType.Road);
            g.Place(V(3, 2), TileType.Road);
            g.Place(V(2, 3), TileType.Road);
            g.Place(V(0, 0), TileType.House);
            g.Place(V(3, 0), TileType.Office);
            return g;
        }

        static Dictionary<Vector2Int, TurnMode> PTurnSigns(TurnMode mainMode) => new()
        {
            [V(1, 1)] = TurnMode.LeftOnly,   // 모드 무관(시작 타일 — 진출 무제약), 대각 차단용
            [V(2, 1)] = mainMode,
            [V(3, 1)] = TurnMode.LeftOnly,   // 모드 무관(도착만 확인 — 진출 안 함), 대각 차단용
        };

        static Dictionary<Vector2Int, TurnMode> PTurnDemandSigns(TurnMode mainMode) => new()
        {
            [V(1, 2)] = TurnMode.LeftOnly,
            [V(2, 2)] = mainMode,
            [V(3, 2)] = TurnMode.LeftOnly,
        };

        // 스펙 §5.2 — P턴 창발: LeftOnly 교차로에서 직진 수요가 좌회전 우회(같은 타일 재방문)를 찾음.
        // (1,1)에서 진입방향 E로 (2,1) 도착 → LeftOnly가 직진(E)·U턴(W) 거부, 좌회전(N)만 허용
        // → (2,2) 곁가지로 강제 진입 → 되돌아와 (2,1) 재방문(진입방향 S) → 이번엔 좌회전이 E(원래
        // 가려던 방향)라서 계속 진행. 경로 테이블도 같은 타일 재방문을 그대로 보존해야 한다.
        [Test]
        public void Search_LeftOnly_PTurn_RevisitsSameTile_AndPlannerPreservesPath()
        {
            var g = PTurnDemandCity();
            var planner = new RoutePlanner(g.Width, g.Height);
            var cfg = Cfg();
            var turnSigns = PTurnDemandSigns(TurnMode.LeftOnly);

            var path = planner.Search(g, V(1, 2), V(3, 2), cfg, null, turnSigns);
            CollectionAssert.AreEqual(
                new[] { V(1, 2), V(2, 2), V(2, 3), V(2, 2), V(3, 2) }, path,
                "좌회전 강제 → 곁가지로 우회 후 (2,1) 재방문 → 원래 방향으로 계속(P턴)");

            var net = new RoadNetwork(g);
            var dm = new DemandMap(cfg); dm.Reassign(g, net);
            Assert.AreEqual(1, dm.Demands.Count);

            var fullPlanner = new RoutePlanner(g.Width, g.Height);
            fullPlanner.Plan(dm, net, g, cfg, null, turnSigns);
            CollectionAssert.AreEqual(path, fullPlanner.CarRoutes[0]);
        }

        // 직교 단일 라우터에서는 표지판 유무와 관계없이 대각 연결이 없다.
        [Test]
        public void Search_DiagonalTouchingSignTile_Forbidden()
        {
            var g = Roads(3, 3, V(0, 0), V(1, 1));
            var planner = new RoutePlanner(g.Width, g.Height);
            var cfg = Cfg();

            Assert.IsNull(planner.Search(g, V(0, 0), V(1, 1), cfg));

            var turnSigns = new Dictionary<Vector2Int, TurnMode> { [V(1, 1)] = TurnMode.LeftOnly };
            Assert.IsNull(planner.Search(g, V(0, 0), V(1, 1), cfg, null, turnSigns),
                "표지판 타일로의 대각 진입 금지 — 다른 연결이 없어 미도달");
        }

        // 스펙 §5.3 — U턴·직진 금지 → 곁가지 없는 강제 회전은 미도달(무사고). LeftOnly 분기.
        [Test]
        public void Search_ForcedTurnWithNoPerpendicularExit_Unreachable_LeftOnly()
        {
            var g = Roads(3, 3, V(0, 1), V(1, 1), V(2, 1));
            var planner = new RoutePlanner(g.Width, g.Height);
            var cfg = Cfg();
            Assert.IsNotNull(planner.Search(g, V(0, 1), V(2, 1), cfg), "레거시: 직선 통과 가능");

            var turnSigns = new Dictionary<Vector2Int, TurnMode> { [V(1, 1)] = TurnMode.LeftOnly };
            List<Vector2Int> result = null;
            Assert.DoesNotThrow(() => result = planner.Search(g, V(0, 1), V(2, 1), cfg, null, turnSigns));
            Assert.IsNull(result, "좌회전 강제인데 회전할 곁가지가 없음 — 미도달, 무사고");
        }

        // 동일 시나리오 RightOnly 분기(((d+1)%4 산술 경로도 커버).
        [Test]
        public void Search_ForcedTurnWithNoPerpendicularExit_Unreachable_RightOnly()
        {
            var g = Roads(3, 3, V(0, 1), V(1, 1), V(2, 1));
            var planner = new RoutePlanner(g.Width, g.Height);
            var cfg = Cfg();

            var turnSigns = new Dictionary<Vector2Int, TurnMode> { [V(1, 1)] = TurnMode.RightOnly };
            List<Vector2Int> result = null;
            Assert.DoesNotThrow(() => result = planner.Search(g, V(0, 1), V(2, 1), cfg, null, turnSigns));
            Assert.IsNull(result, "우회전 강제인데 회전할 곁가지가 없음 — 미도달, 무사고");
        }

        // 스펙 §5.4 — 레거시 무풍: turnSigns가 null이거나 비어 있으면 기존(무수정) Search와 경로가
        // 완전히 동일해야 함(대표 기하 = OnewayTests.RingCity 재사용).
        [Test]
        public void Search_NoSigns_MatchesLegacySearch_ExactParity()
        {
            var g = RingCity();
            var planner = new RoutePlanner(g.Width, g.Height);
            var cfg = Cfg();

            var legacy = planner.Search(g, V(3, 1), V(1, 1), cfg);
            var withNullSigns = planner.Search(g, V(3, 1), V(1, 1), cfg, null, null);
            var withEmptySigns = planner.Search(g, V(3, 1), V(1, 1), cfg, null,
                new Dictionary<Vector2Int, TurnMode>());

            CollectionAssert.AreEqual(legacy, withNullSigns, "null turnSigns = 기존 Search와 완전 동일");
            CollectionAssert.AreEqual(legacy, withEmptySigns, "빈 turnSigns도 동일(Count==0 분기)");
        }

        // 스펙 §5.5 — 결정론: 같은 도시(+같은 표지판 상태)면 같은 Plan(같은 상태 확장 경로 포함).
        [Test]
        public void Plan_WithTurnSigns_Deterministic_SameCitySamePlan()
        {
            var g = PTurnDemandCity();
            var net = new RoadNetwork(g);
            var cfg = Cfg();
            var dm = new DemandMap(cfg); dm.Reassign(g, net);
            var turnSigns = PTurnDemandSigns(TurnMode.LeftOnly);

            var a = new RoutePlanner(g.Width, g.Height); a.Plan(dm, net, g, cfg, null, turnSigns);
            var b = new RoutePlanner(g.Width, g.Height); b.Plan(dm, net, g, cfg, null, turnSigns);

            Assert.AreEqual(a.Routes.Count, b.Routes.Count);
            for (int i = 0; i < a.Routes.Count; i++)
                CollectionAssert.AreEqual(a.Routes[i], b.Routes[i]);
            Assert.IsNotNull(a.Routes[0]);
            CollectionAssert.AreEqual(new[] { V(1, 2), V(2, 2), V(2, 3), V(2, 2), V(3, 2) }, a.Routes[0]);
        }

        // 스펙 §5.7 — 일방통행+표지판 공존 기하 1개: 두 필터가 같은 Search 호출에서 함께 적용됨.
        // PTurnCity 재사용 — 시작 타일(1,1)에 일방(첫 스텝 East와 호환)을 얹어 P턴 경로는 불변임을
        // 확인(양성), West로 바꾸면 규칙①이 첫 스텝부터 걸려 미도달이 됨을 확인(음성 대조 — 일방
        // 필터가 표지판 상태 확장 경로에서도 실제로 살아있다는 증거, 두 필터 상호 미간섭 확인).
        [Test]
        public void Search_OnewayAndTurnSign_Coexist_BothRulesApplied()
        {
            var g = PTurnCity();
            var planner = new RoutePlanner(g.Width, g.Height);
            var cfg = Cfg();
            var turnSigns = PTurnSigns(TurnMode.LeftOnly);

            var compatible = new Dictionary<Vector2Int, Vector2Int> { [V(1, 1)] = E };
            var path = planner.Search(g, V(1, 1), V(3, 1), cfg, compatible, turnSigns);
            CollectionAssert.AreEqual(new[] { V(1, 1), V(2, 1), V(2, 2), V(2, 1), V(3, 1) }, path,
                "일방(첫 스텝과 호환)과 표지판이 공존해도 P턴 경로는 그대로");

            var incompatible = new Dictionary<Vector2Int, Vector2Int> { [V(1, 1)] = new Vector2Int(-1, 0) };
            Assert.IsNull(planner.Search(g, V(1, 1), V(3, 1), cfg, incompatible, turnSigns),
                "일방 규칙①이 표지판 상태 확장 경로에서도 첫 스텝부터 그대로 적용됨(필터가 실제로 살아있음)");
        }
    }
}
