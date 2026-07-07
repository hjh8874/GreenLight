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
            var dm = new DemandMap(cfg); dm.Reassign(g);
            var net = new RoadNetwork(g);
            var signals = new SignalMap();
            signals.Rebuild(g);
            signals.TryGet(V(6, 0), out var second);
            second.OffsetSlots = offsetAtSecond;

            var solver = new FlowSolver(g.Width, g.Height);
            solver.Assign(dm, net, cfg);
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
            // 둘 다 오프셋 0 = 이동시간 4만큼 어긋남 → eff = 1 - (4/6)×0.5 = 0.6667
            var (solver, _) = Solve(TwoSignalCity(), Cfg(), offsetAtSecond: 0);
            Assert.AreEqual(0.6667f, solver.DeliveredTotal, 1e-3f);
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
            var dm = new DemandMap(cfg); dm.Reassign(g);
            var net = new RoadNetwork(g);
            var signals = new SignalMap(); signals.Rebuild(g);
            var solver = new FlowSolver(g.Width, g.Height);
            solver.Assign(dm, net, cfg);
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
            Assert.AreEqual(0.6667f, e.Stability01, 1e-3f);       // 기본 오프셋 0 = 어긋남

            Assert.IsTrue(e.TrySetSignalOffsetSlots(V(6, 0), 4)); // 그린웨이브 조율(이동시간 4)
            e.Tick(0.25f);
            Assert.AreEqual(1f, e.Stability01, 1e-3f);            // 처리량 회복 — 레버가 살아있음
        }
    }
}
