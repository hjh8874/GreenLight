using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    // SimConfig 런타임 재주입 seam(스펙 2026-07-12): 정책 테크 트리(진우)가 밸런스 값을
    // 틱 사이에 갈아 끼우는 창구. 구조 필드(GridWidth/GridHeight/AutoDetectSignals)는 보존.
    public class SimConfigSeamTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        // 단일 직선 경로(House→Office, 교차로 없음) — E(효율)를 손계산으로 검증하기 쉬운 최소 기하.
        // SignalPlacementTests/RoundaboutTests의 Build와 동일 패턴(도로 행 + 집/회사는 곁줄 프론티지).
        // House/Office는 도로 타일과 겹칠 수 없다(Place는 Empty에만 성공) — 곁가지 없이 y=1에 배치.
        static SimEngine Build(SimConfig c, out SimEventHub hub)
        {
            hub = new SimEventHub();
            var e = new SimEngine(c, hub);
            for (int x = 0; x <= 9; x++) e.Place(V(x, 0), TileType.Road);
            e.Place(V(0, 1), TileType.House);
            e.Place(V(9, 1), TileType.Office);
            e.Tick(c.TickInterval);   // 재구축 소비(TopologyDirty 클리어)
            return e;
        }

        static SimConfig BaseConfig()
        {
            var c = SimConfig.Default();
            c.TickInterval = 0.25f;
            c.GridWidth = 10; c.GridHeight = 2;
            c.AutoDetectSignals = false;   // 배치 모드 — 신호 없음(SignalFactor=1로 단순화)
            c.RushAmplitude = 0f;
            return c;
        }

        // 교차로가 있는 기하(RoundaboutTests/SignalPlacementTests의 Build와 동일): 직선 도로 +
        // 곁가지 2개 → 교차로 (3,0)·(6,0). 배치 신호 생존 검증용.
        static SimEngine BuildWithIntersection(SimConfig c, out SimEventHub hub)
        {
            hub = new SimEventHub();
            var e = new SimEngine(c, hub);
            for (int x = 0; x <= 9; x++) e.Place(V(x, 0), TileType.Road);
            e.Place(V(3, 1), TileType.Road);
            e.Place(V(6, 1), TileType.Road);
            e.Place(V(0, 1), TileType.House);
            e.Place(V(9, 1), TileType.Office);
            e.Tick(c.TickInterval);   // 재구축 소비
            return e;
        }

        [Test]
        public void ApplyConfig_DemandDelta_TakesEffectNextTick()
        {
            var c = BaseConfig();
            c.RoadCapacity = 20f;      // 여유 용량 — Free flow(E=1)로 delivered = DemandRate 그대로
            c.DemandPerHouse = 10f;
            var e = Build(c, out _);

            e.Tick(c.TickInterval);
            float before = e.DeliveredTotal;
            Assert.AreEqual(10f, before, 0.001f);   // Free flow → delivered == DemandPerHouse

            // 정책: 재택근무 수요 -15%(스펙 예시 시나리오 그대로).
            var next = c;
            next.DemandPerHouse = c.DemandPerHouse * 0.85f;
            e.ApplyConfig(next);

            e.Tick(c.TickInterval);
            float after = e.DeliveredTotal;
            Assert.AreEqual(before * 0.85f, after, 0.001f);   // 비례 감소
        }

        [Test]
        public void ApplyConfig_CapacityUp_RelievesJam()
        {
            var c = BaseConfig();
            c.RoadCapacity = 5f;
            c.DemandPerHouse = 8f;     // ratio = 8/5 = 1.6 > JamRatio(1.0) → 정체
            var e = Build(c, out _);

            e.Tick(c.TickInterval);
            Assert.AreEqual(CongestionLevel.Jam, e.GetCongestion(V(5, 0)));
            float jammedDelivered = e.DeliveredTotal;
            Assert.Less(jammedDelivered, c.DemandPerHouse);   // 병목 손실 존재

            // 정책: 차선 확장(RoadCapacity 상향) → 같은 타일이 Free로 전환.
            var next = c;
            next.RoadCapacity = 20f;   // ratio = 8/20 = 0.4 < SlowRatio(0.7) → Free
            e.ApplyConfig(next);

            e.Tick(c.TickInterval);
            Assert.AreEqual(CongestionLevel.Free, e.GetCongestion(V(5, 0)));
            Assert.Greater(e.DeliveredTotal, jammedDelivered);       // 혼잡 완화
            Assert.AreEqual(c.DemandPerHouse, e.DeliveredTotal, 0.001f);   // 완전 해소(E=1)
        }

        [Test]
        public void ApplyConfig_PreservesStructuralFields()
        {
            var c = BaseConfig();
            c.GridWidth = 10; c.GridHeight = 2;
            var e = BuildWithIntersection(c, out _);
            Assert.IsTrue(e.TryPlaceSignal(V(3, 0), 8));   // 교차로 (3,0)에 신호 배치(배치 모드)
            Assert.AreEqual(1, e.SignalTiles.Count);

            // 구조 필드를 다르게 넣어도 무시되어야 함: 그리드 크기·자동감지 스위치.
            var next = c;
            next.GridWidth = 999;
            next.GridHeight = 999;
            next.AutoDetectSignals = true;   // 배치→자동 전환 시도
            next.DemandPerHouse = 12f;       // 이 값은 반영되어야(비구조 필드)
            e.ApplyConfig(next);
            e.Tick(c.TickInterval);          // 재계획 강제(MarkTopologyDirty) 소비

            // 엔진의 실제 config를 직접 핀(CurrentConfig 관찰 seam) — e.GridWidth는 _grid.Width라
            // 보존 코드를 지워도 안 깨지는 공허한 관찰이었다(최종 리뷰).
            Assert.AreEqual(10, e.CurrentConfig.GridWidth);      // 보존 — 정책이 그리드 리사이즈 못 함
            Assert.AreEqual(2, e.CurrentConfig.GridHeight);
            Assert.IsFalse(e.CurrentConfig.AutoDetectSignals);   // 부트 스위치 보존
            Assert.AreEqual(12f, e.CurrentConfig.DemandPerHouse);   // 비구조 필드는 반영
            Assert.AreEqual(1, e.SignalTiles.Count);      // 배치 신호 생존(자동감지로 안 튐)
            Assert.AreEqual(V(3, 0), e.SignalTiles[0]);
            // AutoDetectSignals가 실제로 true였다면 CanPlaceSignal은 항상 false(배치 개념 없음) —
            // 보존됐다면 여전히 배치 모드 규약(교차로 + 미점유)이 살아있어야 true.
            Assert.IsTrue(e.CanPlaceSignal(V(6, 0)));
            Assert.IsTrue(e.TryPlaceSignal(V(6, 0), 8));  // 그리드 정상 동작(교차로 판정 살아있음)
        }

        [Test]
        public void ApplyConfig_IsDeterministic()
        {
            System.Func<float> run = () =>
            {
                var c = BaseConfig();
                c.RoadCapacity = 5f;
                c.DemandPerHouse = 8f;
                var e = Build(c, out _);
                e.Tick(c.TickInterval);

                var next = c;
                next.RoadCapacity = 20f;
                e.ApplyConfig(next);
                e.Tick(c.TickInterval);
                return e.DeliveredTotal;
            };

            float a = run();
            float b = run();
            Assert.AreEqual(a, b);
        }
    }
}
