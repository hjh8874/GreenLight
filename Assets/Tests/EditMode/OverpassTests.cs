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

        // 십자 도시(검증된 좌표, RoundaboutTests와 동일): 시나리오 간 흐름 동일이 비교의 근거.
        static SimEngine BuildCross(int hHouses, int vHouses, float demandPerHouse,
                                    out SimEventHub hub)
        {
            var c = SimConfig.Default();
            c.TickInterval = 0.25f;
            c.GridWidth = 13; c.GridHeight = 13;
            c.DemandPerHouse = demandPerHouse;
            c.RoadCapacity = 12f;
            c.DemandChoicePool = 1;
            c.SchoolCapacity = vHouses;
            c.OfficeCapacity = 20;
            c.RushAmplitude = 0f;
            c.AutoDetectSignals = false;
            hub = new SimEventHub();
            var e = new SimEngine(c, hub);
            for (int x = 0; x <= 12; x++) e.Place(V(x, 6), TileType.Road);
            for (int y = 0; y <= 12; y++) if (y != 6) e.Place(V(6, y), TileType.Road);
            for (int i = 0; i < hHouses; i++) e.Place(V(i, 7), TileType.House);
            for (int i = 0; i < vHouses; i++) e.Place(V(5, i), TileType.House);
            e.Place(V(12, 7), TileType.Office);
            e.Place(V(5, 12), TileType.School);
            e.Tick(0.25f);
            return e;
        }

        enum Node { None, Signal, Overpass }

        static float Run(int hHouses, int vHouses, float demand, Node node, int greenSlots = 9)
        {
            var e = BuildCross(hHouses, vHouses, demand, out _);
            if (node == Node.Signal) Assert.IsTrue(e.TryPlaceSignal(V(6, 6), greenSlots));
            if (node == Node.Overpass) Assert.IsTrue(e.TryPlaceOverpass(V(6, 6)));
            e.Tick(0.25f);
            return e.DeliveredTotal;
        }

        [Test]
        public void BalancedCross_OverpassBeatsSignal_BeatsNothing()
        {
            // 실측 fH=9, fV=7.5(다목적지 수요). 입체 교차로 ratio: H 0.75·V 0.625 → 병목이
            // 간선(동쪽 1.5/세로 1.25)으로 완전 이동. 학교행: 무신호 1.75 → 신호 1.4286 → 입체 1.25 — 사슬 엄격.
            float none = Run(6, 6, 1.5f, Node.None);
            float signal = Run(6, 6, 1.5f, Node.Signal, greenSlots: 9);
            float over = Run(6, 6, 1.5f, Node.Overpass);
            Assert.Less(none, signal);
            Assert.Less(signal, over);    // 간섭 소멸 = 어떤 노드보다 강함(엔드게임 천장, 스펙 §핵심결정)
        }

        [Test]
        public void NoCrossTraffic_OverpassIsFree()
        {
            // 입체교차의 정체성 핀: 로터리와 달리 페널티가 없다 — 교차 교통 0이면 delivered가
            // 무신호와 비트 단위로 동일(fV=0: 합산 (fH+0)/C == 축독립 fH/C).
            var c = SimConfig.Default();
            c.TickInterval = 0.25f;
            c.GridWidth = 13; c.GridHeight = 13;
            c.DemandPerHouse = 14f;
            c.RoadCapacity = 12f;
            c.RushAmplitude = 0f;
            c.AutoDetectSignals = false;
            System.Func<bool, float> run = placeOverpass =>
            {
                var e = new SimEngine(c, new SimEventHub());
                for (int x = 0; x <= 12; x++) e.Place(V(x, 6), TileType.Road);
                e.Place(V(6, 5), TileType.Road);              // 더미 지선 → (6,6) 교차로 성립
                e.Place(V(6, 7), TileType.Road);
                e.Place(V(0, 7), TileType.House);
                e.Place(V(12, 7), TileType.Office);
                e.Tick(0.25f);
                if (placeOverpass) Assert.IsTrue(e.TryPlaceOverpass(V(6, 6)));
                e.Tick(0.25f);
                return e.DeliveredTotal;
            };
            Assert.AreEqual(run(false), run(true));   // 로터리는 여기서 손해였다(RoundaboutTests와 대비)
        }

        [Test]
        public void Overpass_IsDeterministic()
        {
            float a = Run(6, 6, 1.5f, Node.Overpass);
            float b = Run(6, 6, 1.5f, Node.Overpass);
            Assert.AreEqual(a, b);
        }

        [Test]
        public void SaveRoundtrip_RestoresOverpasses()
        {
            var e = Build(autoDetect: false, out _);
            e.TryPlaceOverpass(V(6, 0));
            e.TryPlaceOverpass(V(3, 0));
            var snap = e.CreateSnapshot();

            var fresh = Build(autoDetect: false, out _);
            fresh.RestoreSnapshot(snap);
            fresh.Tick(0.25f);
            Assert.AreEqual(2, fresh.OverpassTiles.Count);
            Assert.AreEqual(V(3, 0), fresh.OverpassTiles[0]);     // flat 정렬 복구
            Assert.IsTrue(fresh.TryRemoveOverpass(V(3, 0)));      // 소유까지 복원
            Assert.IsFalse(fresh.CanPlaceSignal(V(6, 0)));        // 배타도 복원
        }

        [Test]
        public void LegacySave_WithoutOverpasses_RestoresClean()
        {
            var e = Build(autoDetect: false, out _);
            var snap = e.CreateSnapshot();
            snap.Overpasses = null;                               // 구세이브 = 필드 없음
            var fresh = Build(autoDetect: false, out _);
            fresh.TryPlaceOverpass(V(3, 0));                      // 이전 세션 잔존 상태
            fresh.RestoreSnapshot(snap);
            fresh.Tick(0.25f);
            Assert.AreEqual(0, fresh.OverpassTiles.Count);        // 복원 = 전체 교체
        }
    }
}
