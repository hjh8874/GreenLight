using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    // 신호 → 흐름 통합(설계 §2·§4): 경로 위 인접 신호쌍의 그린웨이브 효율(min)이 delivered에 곱해진다.
    public class SignalFlowTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        // 집(0,1)─도로 y=0 (x=0..8)─회사(8,1). 곁가지 (2,1)·(6,1)로 (2,0)·(6,0)이 교차로(신호).
        // 두 신호 사이 타일 거리 = 4 (경로 인덱스 2→6).
        static CityGrid TwoSignalCity()
        {
            var g = new CityGrid(9, 2);
            for (int x = 0; x <= 8; x++) g.Place(V(x, 0), TileType.Road);
            g.Place(V(2, 1), TileType.Road);
            g.Place(V(6, 1), TileType.Road);
            g.Place(V(0, 1), TileType.House);
            g.Place(V(8, 1), TileType.Office);
            return g;
        }

        static (FlowSolver solver, SignalMap signals) Solve(CityGrid g, in SimConfig cfg, int offsetAtSecond)
        {
            var dm = new DemandMap(cfg); dm.Reassign(g, new RoadNetwork(g));
            var net = new RoadNetwork(g);
            var planner = new RoutePlanner(g.Width, g.Height);
            planner.Plan(dm, net, g, cfg);
            var signals = new SignalMap();
            signals.Rebuild(g);
            signals.TryGet(V(6, 0), out var second);
            second.OffsetSlots = offsetAtSecond;

            var solver = new FlowSolver(g.Width, g.Height);
            solver.Assign(dm, planner, cfg);
            solver.Resolve(cfg, signals);
            return (solver, signals);
        }

        static SimConfig Cfg()
        {
            var c = SimConfig.Default();      // GreenWaveFloor 0.5, 주기 12슬롯
            c.DemandPerHouse = 1f;
            c.RoadCapacity = 10f;             // 혼잡 없음 → E=1, 신호 factor만 남음
            return c;
        }

        [Test]
        public void AlignedSignals_NoThroughputLoss()
        {
            // 두 번째 신호 오프셋 = 이동시간(4슬롯) → 그린웨이브 완성 → delivered = 수요 그대로
            var (solver, _) = Solve(TwoSignalCity(), Cfg(), offsetAtSecond: 4);
            Assert.AreEqual(1f, solver.DeliveredTotal, 1e-3f);
        }

        [Test]
        public void MisalignedSignals_ReduceThroughput()
        {
            // 하류 오프셋 8 = 도착이 하류 초록창 반대편 → 효율 저하 → delivered 뚜렷이 감소
            var (solver, _) = Solve(TwoSignalCity(), Cfg(), offsetAtSecond: 8);
            Assert.Less(solver.DeliveredTotal, 0.8f);   // 정렬(1.0) 대비 감소 — 주기 튜닝에 견고한 관계 단언
        }

        [Test]
        public void SingleSignal_NoEffect()
        {
            // 신호 1개면 조율할 짝이 없음 → factor 1
            var g = new CityGrid(9, 2);
            for (int x = 0; x <= 8; x++) g.Place(V(x, 0), TileType.Road);
            g.Place(V(4, 1), TileType.Road);          // 교차로 하나만
            g.Place(V(0, 1), TileType.House);
            g.Place(V(8, 1), TileType.Office);

            var cfg = Cfg();
            var dm = new DemandMap(cfg); dm.Reassign(g, new RoadNetwork(g));
            var net = new RoadNetwork(g);
            var planner = new RoutePlanner(g.Width, g.Height);
            planner.Plan(dm, net, g, cfg);
            var signals = new SignalMap(); signals.Rebuild(g);
            var solver = new FlowSolver(g.Width, g.Height);
            solver.Assign(dm, planner, cfg);
            solver.Resolve(cfg, signals);

            Assert.AreEqual(1f, solver.DeliveredTotal, 1e-3f);
        }

        [Test]
        public void EndToEnd_OffsetLever_ChangesThroughput()
        {
            // 유저 레버 관통: 어긋난 신호 → 안정도 하락 → 오프셋 조율 → 1.0 회복.
            var c = SimConfig.Default();
            c.TickInterval = 0.25f;
            c.GridWidth = 9; c.GridHeight = 2;
            c.DemandPerHouse = 1f; c.RoadCapacity = 10f;
            var e = new SimEngine(c, new SimEventHub());

            for (int x = 0; x <= 8; x++) e.Place(V(x, 0), TileType.Road);
            e.Place(V(2, 1), TileType.Road);
            e.Place(V(6, 1), TileType.Road);
            e.Place(V(0, 1), TileType.House);
            e.Place(V(8, 1), TileType.Office);

            e.Tick(0.25f);
            Assert.AreEqual(2, e.SignalTiles.Count);              // 교차로 2개 자동 감지

            Assert.IsTrue(e.TrySetSignalOffsetSlots(V(6, 0), 8)); // 일부러 어긋나게(반대편 착지)
            e.Tick(0.25f);
            Assert.Less(e.Stability01, 0.8f);                     // 처리량 저하(정렬 대비)

            Assert.IsTrue(e.TrySetSignalOffsetSlots(V(6, 0), 4)); // 그린웨이브 조율(이동시간 4)
            e.Tick(0.25f);
            Assert.AreEqual(1f, e.Stability01, 1e-3f);            // 회복 — 레버가 살아있음
        }

        // ── GreenRatio(초록 길이) 레버: 신호 타일 유효 용량 = RoadCapacity × 듀티 ──

        // 직선 도로 + 곁가지 (4,1) → (4,0) 교차로 하나. 신호 1개 = 그린웨이브 factor 1 보장.
        static CityGrid OneSignalCity()
        {
            var g = new CityGrid(9, 2);
            for (int x = 0; x <= 8; x++) g.Place(V(x, 0), TileType.Road);
            g.Place(V(4, 1), TileType.Road);
            g.Place(V(0, 1), TileType.House);
            g.Place(V(8, 1), TileType.Office);
            return g;
        }

        static (FlowSolver solver, SignalMap signals) SolveCity(
            CityGrid g, in SimConfig cfg, System.Action<SignalMap> tune = null)
        {
            var dm = new DemandMap(cfg); dm.Reassign(g, new RoadNetwork(g));
            var net = new RoadNetwork(g);
            var planner = new RoutePlanner(g.Width, g.Height);
            planner.Plan(dm, net, g, cfg);
            var signals = new SignalMap(); signals.Rebuild(g);
            tune?.Invoke(signals);
            var solver = new FlowSolver(g.Width, g.Height);
            solver.Assign(dm, planner, cfg);
            solver.Resolve(cfg, signals);
            return (solver, signals);
        }

        [Test]
        public void SignalTile_JamsAtEffectiveCapacity_NormalRoadStaysFree()
        {
            // 수요 6/용량 10: 일반 도로 ratio 0.6(Free), 신호 타일은 유효 용량 5(듀티 0.5) → 1.2(Jam).
            // 빨간불 동안 큐가 쌓이는 걸 용량 감소로 근사 — 신호가 '먼저' 병목이 된다.
            var cfg = Cfg(); cfg.DemandPerHouse = 6f;
            var (solver, _) = SolveCity(OneSignalCity(), cfg);

            Assert.AreEqual(CongestionLevel.Free, solver.GetCongestion(V(2, 0)));
            Assert.AreEqual(CongestionLevel.Jam, solver.GetCongestion(V(4, 0)));
        }

        [Test]
        public void SignalBottleneck_ReducesDelivered()
        {
            // 신호 타일 ratio 1.2 → E = 1 - 0.2×0.8 = 0.84 → delivered = 6 × 0.84 = 5.04
            var cfg = Cfg(); cfg.DemandPerHouse = 6f;
            var (solver, _) = SolveCity(OneSignalCity(), cfg);

            Assert.AreEqual(5.04f, solver.DeliveredTotal, 1e-3f);
        }

        [Test]
        public void GreenSlots_IsAThroughputLever()
        {
            // 같은 도시에서 초록만 늘리면(듀티 0.5 → 1.0) 처리량이 회복돼야 레버다.
            var cfg = Cfg(); cfg.DemandPerHouse = 6f;
            var (shortGreen, _) = SolveCity(OneSignalCity(), cfg);
            var (fullGreen, _) = SolveCity(OneSignalCity(), cfg, sm =>
            {
                sm.TryGet(V(4, 0), out var s);
                s.GreenSlots = s.CycleSlots;   // 듀티 1.0 → 유효 용량 = RoadCapacity
            });

            Assert.Less(shortGreen.DeliveredTotal, fullGreen.DeliveredTotal);
            Assert.AreEqual(6f, fullGreen.DeliveredTotal, 1e-3f);   // ratio 0.6 Free → 수요 그대로
        }
    }
}
