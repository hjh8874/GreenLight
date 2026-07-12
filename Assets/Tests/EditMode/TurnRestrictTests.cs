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

        // Global Constraints("CanPlaceSignal 등 기존 3형제 API는 무수정 — 표지판 쪽만 검사")를 문자 그대로
        // 지킨 결과의 비대칭 핀: 배타 검사는 CanPlaceTurnSign 쪽에만 있다 — 로터리/입체 API는
        // 손대지 않았으므로 표지판이 이미 있는 교차로에도 로터리/입체는 (엔진 레벨에서는) 배치된다.
        // 실제 이중 배치 방지는 UI(샌드박스 배치 모드 단일 선택)의 책임 — 계획 문서에 명시된 트레이드오프.
        [Test]
        public void RoundaboutAndOverpassApis_AreUnmodified_DoNotCheckTurnSign()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsTrue(e.TryPlaceTurnSign(V(3, 0), TurnMode.LeftOnly));
            Assert.IsTrue(e.CanPlaceRoundabout(V(3, 0)));
            Assert.IsTrue(e.CanPlaceOverpass(V(3, 0)));
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
    }
}
