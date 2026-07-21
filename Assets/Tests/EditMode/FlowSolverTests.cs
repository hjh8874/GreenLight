using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public class FlowSolverTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        // 집(0,1) ─ 접점(0,0) ─ 도로 (0,0)~(4,0) ─ 접점(4,0) ─ 회사(4,1)
        static CityGrid StraightCity()
        {
            var g = new CityGrid(5, 2);
            for (int x = 0; x <= 4; x++) g.Place(V(x, 0), TileType.Road);
            g.Place(V(0, 1), TileType.House);
            g.Place(V(4, 1), TileType.Office);
            return g;
        }

        // 산술을 고정하기 위해 테스트가 쓰는 숫자는 전부 명시(Default 드리프트 무관).
        static SimConfig Cfg(float demand, float capacity)
        {
            var c = SimConfig.Default();
            c.DemandPerHouse = demand;
            c.RoadCapacity = capacity;
            return c;
        }

        static FlowSolver Solve(CityGrid g, in SimConfig cfg)
        {
            var dm = new DemandMap(cfg);
            dm.Reassign(g, new RoadNetwork(g));
            var net = new RoadNetwork(g);
            var planner = new RoutePlanner(g.Width, g.Height);
            planner.Plan(dm, net, g, cfg);
            var solver = new FlowSolver(g.Width, g.Height);
            solver.Assign(dm, planner, cfg);
            solver.Resolve(cfg);
            return solver;
        }

        [Test]
        public void StraightRoad_UnderCapacity_DeliversFullDemand()
        {
            // 수요 1 < 용량 10 → ratio 0.1 → E=1 → delivered = 수요 그대로, 도로는 Free.
            var solver = Solve(StraightCity(), Cfg(demand: 1f, capacity: 10f));

            Assert.AreEqual(1f, solver.DeliveredTotal, 1e-4f);
            Assert.AreEqual(CongestionLevel.Free, solver.GetCongestion(V(2, 0)));
        }

        [Test]
        public void OverCapacity_DecaysDelivered_JamsRoad_AccruesPendingReward()
        {
            // 수요 15 > 용량 10 → ratio 1.5 → Jam.
            // E = 1 + (1.5-1)/(2.0-1)×(0.2-1) = 0.6 → delivered = 15×0.6 = 9.
            // pending(병목 = 경로에서 최대 ratio가 처음 나오는 타일 (0,0))
            //   = 잃은 처리량 × 틱 = 15×(1-0.6)×0.1 = 0.6.
            var solver = Solve(StraightCity(), Cfg(demand: 15f, capacity: 10f));

            Assert.AreEqual(9f, solver.DeliveredTotal, 1e-3f);
            Assert.AreEqual(CongestionLevel.Jam, solver.GetCongestion(V(2, 0)));
            Assert.AreEqual(0.6f, solver.GetPendingReward(V(0, 0)), 1e-3f);
        }

        [Test]
        public void Efficiency_MonotonicallyDecreasesWithCongestion()
        {
            // 계획 2d: 자유 구간 E=1 고정, Jam~바닥 사이 단조 감소, 바닥 밑으로는 안 내려감.
            var cfg = SimConfig.Default(); // Jam 1.0 → 2.0에서 바닥 0.2

            Assert.AreEqual(1f, FlowSolver.Efficiency(0.5f, cfg), 1e-4f);
            Assert.AreEqual(1f, FlowSolver.Efficiency(1.0f, cfg), 1e-4f);
            Assert.Greater(FlowSolver.Efficiency(1.2f, cfg), FlowSolver.Efficiency(1.8f, cfg));
            Assert.AreEqual(0.2f, FlowSolver.Efficiency(2.0f, cfg), 1e-4f);
            Assert.AreEqual(0.2f, FlowSolver.Efficiency(5.0f, cfg), 1e-4f); // 바닥 유지
        }

        [Test]
        public void DisconnectedOrNoAccess_DeliversZero_NoCrash()
        {
            // 집·회사 사이 도로 없음(수요는 생김) → 흐름 0, 예외 없음. 계획 5의 solver 조각.
            var g = new CityGrid(5, 2);
            g.Place(V(0, 1), TileType.House);
            g.Place(V(4, 1), TileType.Office);

            var solver = Solve(g, Cfg(demand: 1f, capacity: 10f));

            Assert.AreEqual(0f, solver.DeliveredTotal, 1e-4f);
            Assert.AreEqual(CongestionLevel.Free, solver.GetCongestion(V(2, 0)));
        }

        [Test]
        public void DemandPulse_TwoHumpsPerDay_AmplitudeZeroIsFlat()
        {
            var c = SimConfig.Default();   // 기본 진폭 0 = 항상 균일(기존 동작 보존)
            Assert.AreEqual(1f, SimConfig.DemandPulse(0, c), 1e-4f);
            Assert.AreEqual(1f, SimConfig.DemandPulse(37.7, c), 1e-4f);

            c.RushAmplitude = 0.6f; c.DayLengthSeconds = 120f;
            Assert.AreEqual(1.6f, SimConfig.DemandPulse(15, c), 1e-3f);   // 아침 봉우리(T/8)
            Assert.AreEqual(1f, SimConfig.DemandPulse(45, c), 1e-3f);     // 낮(사인 음수 구간 = 평시)
            Assert.AreEqual(1.6f, SimConfig.DemandPulse(75, c), 1e-3f);   // 저녁 봉우리(5T/8)
            Assert.AreEqual(SimConfig.DemandPulse(15, c), SimConfig.DemandPulse(135, c), 1e-4f); // 다음날 동일(결정론)
        }

        [Test]
        public void RoadCapacityZero_NoNaN()
        {
            // RoadCapacity=0에서도 나눗셈이 남아있으면 0/0=NaN이 밀도로 새어나간다(감사 2026-07-12).
            // AxisRatio 규약: cap≤0이면 흐름>0인 타일은 EfficiencyMinRatio(→Jam), 유휴 타일은 0.
            var solver = Solve(StraightCity(), Cfg(demand: 1f, capacity: 0f));

            float idleRatio = solver.GetRatio(V(2, 1));   // Empty 타일 — 경로가 지나지 않음
            Assert.IsFalse(float.IsNaN(idleRatio));
            Assert.AreEqual(0f, Mathf.Clamp01(idleRatio), 1e-6f);

            Assert.AreEqual(CongestionLevel.Jam, solver.GetCongestion(V(2, 0)));  // 흐름 도로 타일 → Jam
            Assert.IsFalse(float.IsNaN(solver.GetRatio(V(2, 0))));
        }

        [Test]
        public void DemandScale_ScalesDeliveredAndStats()
        {
            // 러시아워 배율 1.5 → 여유 용량에선 delivered도 1.5배, 안정도 분모(DemandRate)도 함께.
            var g = StraightCity();
            var cfg = Cfg(demand: 1f, capacity: 10f);
            var dm = new DemandMap(cfg);
            dm.Reassign(g, new RoadNetwork(g));
            var net = new RoadNetwork(g);
            var planner = new RoutePlanner(g.Width, g.Height);
            planner.Plan(dm, net, g, cfg);
            var solver = new FlowSolver(g.Width, g.Height);
            solver.Assign(dm, planner, cfg, demandScale: 1.5f);
            solver.Resolve(cfg);

            Assert.AreEqual(1.5f, solver.DeliveredTotal, 1e-3f);
            Assert.AreEqual(1.5f, solver.DemandRate, 1e-4f);
        }

        [Test]
        public void RouteSources_AlignedWithRoutes()
        {
            // 경로별 출발 건물 타일이 Routes와 나란히 노출된다 (뷰 통근 인구 구성용).
            var g = new CityGrid(9, 3);
            g.Place(new Vector2Int(0, 0), TileType.House);
            for (int x = 1; x <= 7; x++) g.Place(new Vector2Int(x, 0), TileType.Road);
            g.Place(new Vector2Int(8, 0), TileType.Office);

            var cfg = Cfg(demand: 1f, capacity: 10f);
            var dm = new DemandMap(cfg);
            dm.Reassign(g, new RoadNetwork(g));
            var net = new RoadNetwork(g);
            var planner = new RoutePlanner(g.Width, g.Height);
            planner.Plan(dm, net, g, cfg);
            var solver = new FlowSolver(g.Width, g.Height);
            solver.Assign(dm, planner, cfg);

            Assert.AreEqual(solver.Routes.Count, solver.RouteSources.Count);
            Assert.AreEqual(solver.Routes.Count, solver.RouteSinks.Count);
            Assert.AreEqual(new Vector2Int(0, 0), solver.RouteSources[0], "출발 = 집 건물 타일");
            Assert.AreEqual(new Vector2Int(8, 0), solver.RouteSinks[0], "도착 = 회사 건물 타일");
        }
    }
}
