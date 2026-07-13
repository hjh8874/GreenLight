using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    // 축분리 + 무신호 간섭(스펙 2026-07-11): 교차로에서 가로/세로 흐름이 분리되어
    // 신호 듀티·간섭·오버라이드가 축별로 정직하게 작동한다.
    public class AxisFlowTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        // 십자 도시: 가로 간선 y=6(x=0..12) + 세로 간선 x=6(y=0..12), 교차로 (6,6) 하나.
        // 가로 흐름 = 서쪽 집들(y=7, 접점 하(x,6)) → 동단 Office(12,7). 직진 관통.
        // 세로 흐름 = 북쪽 집들(x=5, 접점 우(6,y)) → 남단 School(5,12). 직진 관통.
        // ⚠️ 세로 집들의 Office 수요는 꺾는 경로 = 대각 코너컷으로 (6,6)을 스킵(엔진 성질) →
        //    교차로 축 흐름을 오염시키지 않음. 단 간선에 흐름은 추가되므로 RoadCapacity로 여유 확보.
        static CityGrid CrossCity(int hHouses, int vHouses)
        {
            var g = new CityGrid(13, 13);
            for (int x = 0; x <= 12; x++) g.Place(V(x, 6), TileType.Road);
            for (int y = 0; y <= 12; y++) if (y != 6) g.Place(V(6, y), TileType.Road);

            // 가로 집은 전부 서쪽(x<6) — 세로 간선(x=6)과 충돌 금지 + 전원 (6,6) 직진 관통.
            // y=7 행 6채, 넘치면 y=5 행(최대 12채). ((6,7)에 놓으면 Place가 도로라 거부되고
            // 옆집이 세로 도로로 코너컷해 교차로를 스킵 — 기하 검증에서 잡은 함정.)
            for (int i = 0; i < hHouses; i++)
                g.Place(i < 6 ? V(i, 7) : V(i - 6, 5), TileType.House);              // 접점 (x,6)
            for (int i = 0; i < vHouses; i++) g.Place(V(5, i), TileType.House);     // 접점 (6,i)
            g.Place(V(12, 7), TileType.Office);                                      // 접점 (12,6)
            g.Place(V(5, 12), TileType.School);                                      // 접점 (6,12)
            return g;
        }

        // SchoolCapacity = vHouses: 세로 집들(flat 순서상 y가 작아 먼저)이 School 슬롯을 전부 차지
        // → 가로 집들은 School 수요 없음(만석 스킵). Office는 넉넉히 → 전원 배정.
        static SimConfig CrossCfg(int vHouses, float capacity)
        {
            var c = SimConfig.Default();
            c.GridWidth = 13; c.GridHeight = 13;
            c.DemandPerHouse = 1f;
            c.RoadCapacity = capacity;
            c.DemandChoicePool = 1;          // 항상 최근접(결정론 단순화)
            c.SchoolCapacity = vHouses;
            c.OfficeCapacity = 20;
            return c;
        }

        static (FlowSolver solver, SignalMap signals, CityGrid grid) Solve(
            CityGrid g, in SimConfig cfg, bool withSignals, System.Action<SignalMap> tune = null)
        {
            var dm = new DemandMap(cfg); dm.Reassign(g, new RoadNetwork(g));
            var net = new RoadNetwork(g);
            var planner = new RoutePlanner(g.Width, g.Height);
            planner.Plan(dm, net, g, cfg);
            SignalMap signals = null;
            if (withSignals) { signals = new SignalMap(); signals.Rebuild(g); tune?.Invoke(signals); }
            var solver = new FlowSolver(g.Width, g.Height);
            solver.Assign(dm, planner, cfg);
            solver.Resolve(cfg, signals, g);
            return (solver, signals, g);
        }

        [Test]
        public void S1_QuietUnsignaledIntersection_NoLoss()
        {
            // 한산(가로3/세로1, C=10): 간섭 있어도 ratio<0.7 → 전 수요 통과. 무신호 = 무해.
            var cfg = CrossCfg(vHouses: 1, capacity: 10f);
            var (solver, _, _) = Solve(CrossCity(3, 1), cfg, withSignals: false);
            // 수요 = 가로 Office 3 + 세로 School 1 + 세로집 Office 1(코너컷 경로) = 5
            Assert.AreEqual(5f, solver.DeliveredTotal, 1e-3f);
        }

        [Test]
        public void S2_BusyUnsignaledIntersection_SignalBeatsIt()
        {
            // 붐빔(6/6, C=12): 무신호 교차로 ratioH=(6+1.5×6)/12=1.25 Jam → 신호(듀티0.5: 6/6=1.0)가 이김.
            var cfg = CrossCfg(vHouses: 6, capacity: 12f);
            var (unsig, _, _) = Solve(CrossCity(6, 6), cfg, withSignals: false);
            var (sig, _, _) = Solve(CrossCity(6, 6), cfg, withSignals: true);
            Assert.Less(unsig.DeliveredTotal, sig.DeliveredTotal);   // 신호를 살 이유
        }

        [Test]
        public void S3_AsymmetricFlows_DutyTuningIsARealDecision()
        {
            // 비대칭(가로9/세로1, C=10): d=0.9(바쁜 축에 몰기) > 기본 d=0.5 — "방향+초"가 진짜 결정.
            var cfg = CrossCfg(vHouses: 1, capacity: 10f);
            var (half, _, _) = Solve(CrossCity(9, 1), cfg, withSignals: true);   // 기본 듀티 8/16=0.5
            var (tuned, _, _) = Solve(CrossCity(9, 1), cfg, withSignals: true, tune: sm =>
            {
                sm.TryGet(V(6, 6), out var s);
                s.GreenSlots = 14;                       // d=14/16=0.875 — 가로에 몰기
            });
            // d=0.5: ratioH=9/5=1.8 → E=0.36 vs d=0.875: 9/8.75≈1.03 → E≈0.98
            Assert.Less(half.DeliveredTotal, tuned.DeliveredTotal);
            // 잘못 산 신호(세로에 몰기)는 더 나쁨 — 오설정 리스크가 실재
            var (wrong, _, _) = Solve(CrossCity(9, 1), cfg, withSignals: true, tune: sm =>
            {
                sm.TryGet(V(6, 6), out var s);
                s.GreenSlots = 2;                        // d=0.125 — 바쁜 가로가 질식
            });
            Assert.Less(wrong.DeliveredTotal, half.DeliveredTotal);
        }

        [Test]
        public void DiagonalStep_SplitsHalfHalf()
        {
            // 대각으로만 이어진 두 도로: 경로의 모든 스텝이 대각 → 양축 0.5/0.5 적립.
            var g = new CityGrid(4, 4);
            g.Place(V(1, 1), TileType.Road);
            g.Place(V(2, 2), TileType.Road);
            g.Place(V(0, 1), TileType.House);    // 접점 우(1,1)
            g.Place(V(3, 2), TileType.Office);   // 접점 좌(2,2)
            var cfg = SimConfig.Default();
            cfg.GridWidth = 4; cfg.GridHeight = 4; cfg.DemandPerHouse = 1f;

            var (solver, _, _) = Solve(g, cfg, withSignals: false);
            int i11 = 1 * 4 + 1, i22 = 2 * 4 + 2;
            Assert.AreEqual(0.5f, solver.GetFlowHForTest(i11), 1e-4f);
            Assert.AreEqual(0.5f, solver.GetFlowVForTest(i11), 1e-4f);
            Assert.AreEqual(0.5f, solver.GetFlowHForTest(i22), 1e-4f);
            Assert.AreEqual(0.5f, solver.GetFlowVForTest(i22), 1e-4f);
        }

        [Test]
        public void Determinism_SameCitySameResolve_SameDelivered()
        {
            var cfg = CrossCfg(vHouses: 6, capacity: 12f);
            var (a, _, _) = Solve(CrossCity(6, 6), cfg, withSignals: false);
            var (b, _, _) = Solve(CrossCity(6, 6), cfg, withSignals: false);
            Assert.AreEqual(a.DeliveredTotal, b.DeliveredTotal);
        }

        [Test]
        public void Override_BothAxesFullCapacity_ClearsTheJam()
        {
            // S3 도시(9/1, C=10) 재사용: 듀티 0.5면 교차로 ratioH=9/5=1.8(Jam, E=0.36).
            // 오버라이드 = 양축 풀 용량 → ratioH=9/10=0.9 — 3초간 충돌 소멸, 어떤 듀티보다 좋음.
            // (8/8 같은 대칭 기하는 측면 간선 정체가 병목을 지배해 오버라이드 효과가 안 보임 — 금지.)
            var cfg = CrossCfg(vHouses: 1, capacity: 10f);
            var (normal, _, _) = Solve(CrossCity(9, 1), cfg, withSignals: true);
            var (burst, _, _) = Solve(CrossCity(9, 1), cfg, withSignals: true, tune: sm =>
            {
                sm.TryGet(V(6, 6), out var s);
                s.OverrideUntil = 999.0;   // Resolve(simTime 기본 0) 동안 활성
            });
            Assert.AreEqual(CongestionLevel.Jam, normal.GetCongestion(V(6, 6)));      // 평상시 병목
            Assert.AreNotEqual(CongestionLevel.Jam, burst.GetCongestion(V(6, 6)));    // 마법으로 해소
            Assert.Less(normal.DeliveredTotal, burst.DeliveredTotal);
        }
    }
}
