using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    // 직교 차 라우팅: 증분 배정 + 결정론적 타일 거리.
    // 기하 테스트(구 RoadNetworkTests에서 이관)는 Search seam으로, 분산·흡수는 Plan으로 핀.
    public class RoutePlannerTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static CityGrid GridWithRoads(int w, int h, params Vector2Int[] roads)
        {
            var g = new CityGrid(w, h);
            foreach (var r in roads) g.Place(r, TileType.Road);
            return g;
        }

        static RoutePlanner Fresh(CityGrid g) => new RoutePlanner(g.Width, g.Height);

        static SimConfig Cfg()
        {
            var c = SimConfig.Default();
            c.RoadCapacity = 10f;
            c.DemandChoicePool = 1;
            return c;
        }

        // ── 기하(이관): 빈 부하 Search = 물리 최단 ──

        [Test]
        public void Search_StraightLine_InOrder()
        {
            var g = GridWithRoads(5, 5, V(0, 0), V(1, 0), V(2, 0));
            Assert.AreEqual(new[] { V(0, 0), V(1, 0), V(2, 0) },
                Fresh(g).Search(g, V(0, 0), V(2, 0), Cfg()));
        }

        [Test]
        public void Search_LShaped_UsesCardinalRoadsOnly()
        {
            var g = GridWithRoads(5, 5, V(0, 0), V(0, 1), V(0, 2), V(1, 2), V(2, 2));
            Assert.AreEqual(new[] { V(0, 0), V(0, 1), V(0, 2), V(1, 2), V(2, 2) },
                Fresh(g).Search(g, V(0, 0), V(2, 2), Cfg()));
        }

        [Test]
        public void Search_DiagonalStaircase_IsDisconnected()
        {
            var g = GridWithRoads(5, 5, V(0, 0), V(1, 1), V(2, 2));
            Assert.IsNull(Fresh(g).Search(g, V(0, 0), V(2, 2), Cfg()));
        }

        [Test]
        public void Search_PrefersStraightOverZigzag_PhysicalDistance()
        {
            // (0,0)→(4,0): 직교 단일 경로는 직선을 선택한다.
            var g = GridWithRoads(5, 3,
                V(0, 0), V(1, 0), V(2, 0), V(3, 0), V(4, 0),   // 직선
                V(1, 1), V(3, 1));                               // 지그재그 유혹
            var path = Fresh(g).Search(g, V(0, 0), V(4, 0), Cfg());
            Assert.AreEqual(new[] { V(0, 0), V(1, 0), V(2, 0), V(3, 0), V(4, 0) }, path);
        }

        [Test]
        public void Search_SameTile_ReturnsSingle()
        {
            var g = GridWithRoads(5, 5, V(2, 2));
            Assert.AreEqual(new[] { V(2, 2) }, Fresh(g).Search(g, V(2, 2), V(2, 2), Cfg()));
        }

        [Test]
        public void Search_DisconnectedOrNonRoad_ReturnsNull()
        {
            var g = GridWithRoads(6, 5, V(0, 0), V(1, 0), V(4, 0), V(5, 0));
            Assert.IsNull(Fresh(g).Search(g, V(0, 0), V(5, 0), Cfg()));   // 미연결
            Assert.IsNull(Fresh(g).Search(g, V(0, 0), V(2, 0), Cfg()));   // 끝점이 도로 아님
        }

        // ── 창발(신규): 분산·흡수·결정론 ──

        // 평행 도시: 서 접점 (1,1) ─ 북로 y=0 / 남로 y=2 (같은 물리 길이) ─ 동 접점 (6,1) ─ Office(7,1).
        // 집 2채가 같은 접점(1,1)에서 출발 → 첫 수요가 한 줄을 채우면 둘째는 반대 줄.
        static CityGrid ParallelCity()
        {
            var g = new CityGrid(8, 3);
            g.Place(V(1, 1), TileType.Road);
            g.Place(V(6, 1), TileType.Road);
            for (int x = 2; x <= 5; x++) { g.Place(V(x, 0), TileType.Road); g.Place(V(x, 2), TileType.Road); }
            g.Place(V(1, 0), TileType.House);    // 접점: 하(1,1)? 스캔 상(1,1)아님… 하(1,-1)OOB → 검증: 상(1,1) road ✓
            g.Place(V(0, 1), TileType.House);    // 우(1,1) road ✓
            g.Place(V(7, 1), TileType.Office);   // 좌(6,1) road ✓
            return g;
        }



        // 우회 도시: 간선 y=1 (1..7) — (1,1) 출발 기준 직선 6.0 / 우회 y=2 (대각 진입) 6.83.
        // w=2: 수요1이 간선을 채우면 간선 7.2 vs 우회 7.11 — 얇지만(0.086) 결정적 마진으로 우회가 이김.
        static CityGrid BypassCity()
        {
            var g = new CityGrid(9, 4);
            for (int x = 1; x <= 7; x++) g.Place(V(x, 1), TileType.Road);          // 간선
            for (int x = 2; x <= 6; x++) g.Place(V(x, 2), TileType.Road);          // 우회(대각 진입/진출)
            g.Place(V(0, 0), TileType.House); g.Place(V(1, 0), TileType.House);    // 접점 둘 다 (1,1)
            g.Place(V(2, 0), TileType.House); g.Place(V(3, 0), TileType.House);
            g.Place(V(4, 0), TileType.House); g.Place(V(5, 0), TileType.House);    // 접점 상(x,1)
            g.Place(V(8, 1), TileType.Office);                                      // 접점 좌(7,1)
            return g;
        }



        [Test]
        public void Plan_Deterministic_SameCitySamePlan()
        {
            var cfg = Cfg();
            var g = BypassCity();
            var net = new RoadNetwork(g);
            var dm = new DemandMap(cfg); dm.Reassign(g, net);

            var a = Fresh(g); a.Plan(dm, net, g, cfg);
            var b = Fresh(g); b.Plan(dm, net, g, cfg);

            Assert.AreEqual(a.Routes.Count, b.Routes.Count);
            for (int i = 0; i < a.Routes.Count; i++)
                CollectionAssert.AreEqual(a.Routes[i], b.Routes[i]);
        }

        [Test]
        public void Plan_UnreachableDemand_NullRoute()
        {
            // 섬 분리: 집 섬과 회사 섬이 안 이어짐 → 해당 수요 경로 null(무사고).
            var g = new CityGrid(7, 3);
            g.Place(V(0, 1), TileType.Road); g.Place(V(1, 1), TileType.Road);
            g.Place(V(5, 1), TileType.Road); g.Place(V(6, 1), TileType.Road);
            g.Place(V(0, 0), TileType.House);   // 접점 하(0,1)? 스캔 상(0,1)! → 상(0,1) road ✓
            g.Place(V(6, 0), TileType.Office);  // 상? (6,1) road ✓
            var cfg = Cfg();
            var net = new RoadNetwork(g);
            var dm = new DemandMap(cfg); dm.Reassign(g, net);
            Assert.AreEqual(1, dm.Demands.Count);   // 도달성 폴백(최근접 배정, 흐름 0 예정)

            var planner = Fresh(g);
            planner.Plan(dm, net, g, cfg);
            Assert.IsNull(planner.Routes[0]);
        }
    }
}
