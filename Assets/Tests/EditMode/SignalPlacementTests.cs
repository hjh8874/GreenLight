using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    // 신호 배치형 전환(스펙 2026-07-11): AutoDetectSignals=false면 배치된 곳에만 신호가 존재.
    // 기본 true = 현행 자동 감지 — 기존 스위트 전체가 그 회귀 게이트.
    public class SignalPlacementTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        // 직선 도로 + 곁가지 2개 → 교차로 (3,0)·(6,0). 집/회사로 흐름도 형성 가능.
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
        public void PlacedMode_StartsWithNoSignals_EvenOnIntersections()
        {
            var e = Build(autoDetect: false, out _);
            Assert.AreEqual(0, e.SignalTiles.Count);   // 교차로 2개 있어도 신호 0 — 자동 생성 죽음
        }

        [Test]
        public void PlacedMode_PlaceOnIntersection_AppliesGreenSlots()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsTrue(e.CanPlaceSignal(V(3, 0)));
            Assert.IsTrue(e.TryPlaceSignal(V(3, 0), greenSlots: 12));   // 가로 우선 듀티로 구매
            Assert.AreEqual(1, e.SignalTiles.Count);
            Assert.AreEqual(V(3, 0), e.SignalTiles[0]);
            Assert.AreEqual(12, e.GetSignalGreenSlots(V(3, 0)));        // 방향+초가 즉시 반영
        }

        [Test]
        public void PlacedMode_RejectsNonIntersection_Duplicate_AndAutoMode()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsFalse(e.TryPlaceSignal(V(1, 0), 8));    // 직선 도로 — 교차로 아님
            Assert.IsFalse(e.TryPlaceSignal(V(1, 1), 8));    // 도로 아님
            Assert.IsTrue(e.TryPlaceSignal(V(3, 0), 8));
            Assert.IsFalse(e.TryPlaceSignal(V(3, 0), 8));    // 중복
            Assert.IsFalse(e.CanPlaceSignal(V(3, 0)));

            var auto = Build(autoDetect: true, out _);
            Assert.IsFalse(auto.CanPlaceSignal(V(3, 0)));    // 자동 모드 — 배치 개념 없음
            Assert.IsFalse(auto.TryPlaceSignal(V(3, 0), 8));
            Assert.AreEqual(2, auto.SignalTiles.Count);       // 자동은 현행대로 전 교차로
        }

        [Test]
        public void PlacedMode_RemoveSignal_Works()
        {
            var e = Build(autoDetect: false, out _);
            e.TryPlaceSignal(V(3, 0), 8);
            e.TryPlaceSignal(V(6, 0), 8);
            Assert.IsTrue(e.TryRemoveSignal(V(3, 0)));
            Assert.AreEqual(1, e.SignalTiles.Count);
            Assert.AreEqual(V(6, 0), e.SignalTiles[0]);
            Assert.IsFalse(e.TryRemoveSignal(V(3, 0)));      // 이미 없음
        }

        [Test]
        public void PlacedMode_PlacedListStaysFlatSorted()
        {
            var e = Build(autoDetect: false, out _);
            e.TryPlaceSignal(V(6, 0), 8);                     // 뒤 타일 먼저 배치
            e.TryPlaceSignal(V(3, 0), 8);
            Assert.AreEqual(V(3, 0), e.SignalTiles[0]);       // 순회 순서는 flat 정렬(결정론)
            Assert.AreEqual(V(6, 0), e.SignalTiles[1]);
        }

        [Test]
        public void PlacedMode_RoadRemoval_KillsSignalNextRebuild()
        {
            var e = Build(autoDetect: false, out _);
            e.TryPlaceSignal(V(3, 0), 8);
            e.Remove(V(3, 1));                                // 곁가지 철거 → (3,0) 교차로 해제
            Assert.AreEqual(1, e.SignalTiles.Count);          // 소멸은 다음 재구축 틱까지 지연(즉시 아님)
            e.Tick(0.25f);                                    // 재구축 소비
            Assert.AreEqual(0, e.SignalTiles.Count);          // 신호 자동 소멸
            Assert.IsFalse(e.TryRemoveSignal(V(3, 0)));       // 배치 목록에서도 사라짐
        }

        [Test]
        public void PlacedMode_GreenSlotsClampStillApplies()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsTrue(e.TryPlaceSignal(V(3, 0), 999));    // 과대값
            Assert.AreEqual(15, e.GetSignalGreenSlots(V(3, 0)));   // [1, 주기-1] 클램프(기존 규약)
        }

        [Test]
        public void PlacedMode_SaveRoundtrip_RestoresPlacementAndLevers()
        {
            var e = Build(autoDetect: false, out _);
            e.TryPlaceSignal(V(3, 0), 12);
            e.TryPlaceSignal(V(6, 0), 4);
            e.TrySetSignalOffsetSlots(V(6, 0), 5);
            var snap = e.CreateSnapshot();

            var fresh = Build(autoDetect: false, out _);
            Assert.AreEqual(0, fresh.SignalTiles.Count);
            fresh.RestoreSnapshot(snap);
            fresh.Tick(0.25f);
            Assert.AreEqual(2, fresh.SignalTiles.Count);              // 배치가 세이브에서 복원됨
            Assert.AreEqual(12, fresh.GetSignalGreenSlots(V(3, 0)));
            Assert.AreEqual(4, fresh.GetSignalGreenSlots(V(6, 0)));
            Assert.AreEqual(5, fresh.GetSignalOffsetSlots(V(6, 0)));
            Assert.IsTrue(fresh.TryRemoveSignal(V(3, 0)));            // 복원된 것도 배치 소유로 관리됨
        }

        [Test]
        public void LegacyAutoSave_RestoredInPlacedMode_PlacesAllSavedSignals()
        {
            // 자동 시절 세이브(전 교차로 신호) → 배치 모드로 열면 그 신호들이 전부 배치된 걸로.
            var auto = Build(autoDetect: true, out _);
            var snap = auto.CreateSnapshot();

            var placed = Build(autoDetect: false, out _);
            placed.RestoreSnapshot(snap);
            placed.Tick(0.25f);
            Assert.AreEqual(2, placed.SignalTiles.Count);             // 마이그레이션 공짜
            Assert.IsTrue(placed.TryRemoveSignal(V(3, 0)));           // 소유까지 이전 — 배치로 관리됨
        }

        [Test]
        public void PlacedMode_CorridorOverride_CollectsPlacedLine()
        {
            // 배치된 신호 3개 라인에서 코리도어가 그대로 작동(SignalMap 경유라 자동 정합).
            var c = SimConfig.Default();
            c.TickInterval = 0.25f;
            c.GridWidth = 12; c.GridHeight = 5;
            c.AutoDetectSignals = false;
            c.OverrideDurationSeconds = 0.5f;
            c.OverrideCooldownSeconds = 1f;
            var e = new SimEngine(c, new SimEventHub());
            for (int x = 0; x <= 10; x++) e.Place(V(x, 2), TileType.Road);
            e.Place(V(2, 3), TileType.Road);
            e.Place(V(5, 3), TileType.Road);
            e.Place(V(8, 3), TileType.Road);
            e.Tick(0.25f);
            e.TryPlaceSignal(V(2, 2), 8);
            e.TryPlaceSignal(V(5, 2), 8);
            e.TryPlaceSignal(V(8, 2), 8);

            Assert.IsTrue(e.TryOverrideSignal(V(5, 2), horizontal: true));
            Assert.Greater(e.GetOverrideSecondsLeft(V(2, 2)), 0f);
            Assert.Greater(e.GetOverrideSecondsLeft(V(8, 2)), 0f);
        }


    }
}
