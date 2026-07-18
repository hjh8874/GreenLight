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

        // 십자 교차로 (6,6) 하나. RoundaboutTests.BuildCross/Run과 동일 기하(13×13, 간선 y=6·x=6,
        // H집 행7, V집 열5, 회사(12,7)·학교(5,12)) — 브리프 원안(Place(House/Office)가 도로 타일과
        // 겹쳐 CanPlace가 거부·delivered 0으로 RED가 무의미해짐)을 검증된 헬퍼로 교체(2026-07-13).
        // hHouses/vHouses 카운트로 편중을 흉내(AsymmetricCross_SignalBeatsRoundabout과 동일 전례).
        static SimEngine BuildCross(int hHouses, int vHouses, out SimEventHub hub)
        {
            var c = SimConfig.Default();
            c.TickInterval = 0.25f;
            c.GridWidth = 13; c.GridHeight = 13;
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

        static float RunCross(int hHouses, int vHouses, bool priorityH)
        {
            var e = BuildCross(hHouses, vHouses, out _);
            if (priorityH) Assert.IsTrue(e.TryPlacePriorityRoad(V(6, 6), Axis.Horizontal));
            e.Tick(0.25f);
            return e.DeliveredTotal;
        }



        [Test]
        public void PriorityRoad_IsDeterministic()
        {
            // 같은 도시·같은 배치를 두 번 → delivered 동일(결정론, 스펙 §8).
            float a = RunCross(5, 2, priorityH: true);
            float b = RunCross(5, 2, priorityH: true);
            Assert.AreEqual(a, b, 1e-6f);
        }

        // 신호·로터리·입체·턴제한과 동일 규약: 곁가지 철거로 교차로 해제 → 다음 재구축 틱에 소멸.
        // (SimEngine.cs RebuildSignals의 _placedPriorityRoads.RemoveAll 프루닝 블록을 핀)
        [Test]
        public void RoadRemoval_KillsPriorityRoadNextRebuild()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsTrue(e.TryPlacePriorityRoad(V(3, 0), Axis.Horizontal));
            e.Remove(V(3, 1));                                // 곁가지 철거 → (3,0)이 교차로 아님
            e.Tick(0.25f);                                    // RebuildSignals가 프루닝
            Assert.AreEqual(0, e.PriorityRoadTiles.Count);
            Assert.IsFalse(e.TryRemovePriorityRoad(V(3, 0)));
        }

        // ── 세이브 왕복(스펙 4단계): 좌표+축 보존, 구세이브(null) 마이그레이션 공짜 ──
        [Test]
        public void SaveRoundtrip_RestoresPriorityRoadsWithAxis()
        {
            var e = Build(autoDetect: false, out _);
            e.TryPlacePriorityRoad(V(3, 0), Axis.Horizontal);
            e.TryPlacePriorityRoad(V(6, 0), Axis.Vertical);
            var snap = e.CreateSnapshot();

            var e2 = Build(autoDetect: false, out _);
            e2.RestoreSnapshot(snap);
            Assert.AreEqual(2, e2.PriorityRoadTiles.Count);
            Assert.AreEqual(Axis.Horizontal, e2.GetPriorityAxis(V(3, 0)));
            Assert.AreEqual(Axis.Vertical, e2.GetPriorityAxis(V(6, 0)));
        }

        [Test]
        public void LegacySave_WithoutPriorityRoads_RestoresClean()
        {
            var e = Build(autoDetect: false, out _);
            var snap = e.CreateSnapshot();
            snap.PriorityRoads = null;   // 구세이브 시뮬
            var e2 = Build(autoDetect: false, out _);
            Assert.DoesNotThrow(() => e2.RestoreSnapshot(snap));
            Assert.AreEqual(0, e2.PriorityRoadTiles.Count);
        }
    }
}
