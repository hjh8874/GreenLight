using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public class DemandMapTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static CityGrid MakeGrid(int w, int h, params (Vector2Int pos, TileType type)[] tiles)
        {
            var g = new CityGrid(w, h);
            foreach (var t in tiles) g.Place(t.pos, t.type);
            return g;
        }

        static SimConfig OfficeCap(int cap)
        {
            var c = SimConfig.Default();
            c.OfficeCapacity = cap;
            return c;
        }

        static SimConfig NearestCfg()   // 최근접 배정을 검증하는 테스트용: 선택 풀 1
        {
            var c = SimConfig.Default();
            c.DemandChoicePool = 1;
            return c;
        }

        static bool Has(DemandMap dm, Vector2Int src, Vector2Int sink)
        {
            foreach (var d in dm.Demands)
                if (d.Source == src && d.Sink == sink) return true;
            return false;
        }

        [Test]
        public void AssignsHouseToNearestOffice()
        {
            var g = MakeGrid(8, 1,
                (V(0, 0), TileType.House),
                (V(2, 0), TileType.Office),
                (V(5, 0), TileType.Office));
            var dm = new DemandMap(NearestCfg());
            dm.Reassign(g, new RoadNetwork(g));

            Assert.AreEqual(1, dm.Demands.Count);
            Assert.IsTrue(Has(dm, V(0, 0), V(2, 0))); // 더 가까운 (2,0)
        }

        [Test]
        public void CapacityFull_OverflowsToNextNearest()
        {
            var g = MakeGrid(8, 2,
                (V(0, 0), TileType.House),
                (V(0, 1), TileType.House),
                (V(1, 0), TileType.Office),   // 두 집 모두에 가장 가까움
                (V(7, 1), TileType.Office));  // 먼 대안
            var dm = new DemandMap(OfficeCap(1));
            dm.Reassign(g, new RoadNetwork(g));

            Assert.IsTrue(Has(dm, V(0, 0), V(1, 0))); // 첫 집: 가까운 곳
            Assert.IsTrue(Has(dm, V(0, 1), V(7, 1))); // 둘째 집: 만석 → 먼 곳
        }

        [Test]
        public void NoOffice_NoDemand()
        {
            var g = MakeGrid(5, 5, (V(0, 0), TileType.House)); // 수요처 없음
            var dm = new DemandMap(SimConfig.Default());
            dm.Reassign(g, new RoadNetwork(g));

            Assert.AreEqual(0, dm.Demands.Count);
        }

        [Test]
        public void MultiSink_HouseCommutesToOfficeAndSchool()
        {
            // 계획 2c: 집 1채 → 수요처 종류마다 1건씩 = 회사·학교 수요 총 2건.
            var g = MakeGrid(5, 1,
                (V(0, 0), TileType.House),
                (V(2, 0), TileType.Office),
                (V(4, 0), TileType.School));
            var dm = new DemandMap(SimConfig.Default());
            dm.Reassign(g, new RoadNetwork(g));

            Assert.AreEqual(2, dm.Demands.Count);
            Assert.IsTrue(Has(dm, V(0, 0), V(2, 0)));
            Assert.IsTrue(Has(dm, V(0, 0), V(4, 0)));
        }

        [Test]
        public void EquidistantOffices_PicksLowerFlatIndex()
        {
            var g = MakeGrid(5, 1,
                (V(2, 0), TileType.House),
                (V(0, 0), TileType.Office),   // flat 0, 거리 2
                (V(4, 0), TileType.Office));  // flat 4, 거리 2 (동점)
            var dm = new DemandMap(NearestCfg());
            dm.Reassign(g, new RoadNetwork(g));

            Assert.IsTrue(Has(dm, V(2, 0), V(0, 0))); // 동점 → 낮은 인덱스
            Assert.IsFalse(Has(dm, V(2, 0), V(4, 0)));
        }

        static CityGrid MakeIslandGrid()
        {
            // 집(0,0)—도로(y=0, x=1..8)—회사(9,0) 연결. (0,3) 회사가 맨해튼상 더 가깝지만 접점 도로 없음(맹지).
            var tiles = new System.Collections.Generic.List<(Vector2Int, TileType)>
            {
                (V(0, 0), TileType.House),
                (V(9, 0), TileType.Office),   // 도달 가능(멀다)
                (V(0, 3), TileType.Office),   // 맹지(가깝다)
            };
            for (int x = 1; x <= 8; x++) tiles.Add((V(x, 0), TileType.Road));
            return MakeGrid(10, 5, tiles.ToArray());
        }

        [Test]
        public void UnreachableNearerOffice_PrefersReachableFarther()
        {
            var g = MakeIslandGrid();
            var dm = new DemandMap(NearestCfg());
            dm.Reassign(g, new RoadNetwork(g));

            Assert.IsTrue(Has(dm, V(0, 0), V(9, 0)));   // 도달 가능한 먼 회사로
            Assert.IsFalse(Has(dm, V(0, 0), V(0, 3)));  // 맹지 회사는 배정 안 함
        }

        [Test]
        public void ReconnectedRoad_RestoresNearestAssignment()
        {
            // 맹지 회사(0,3)에 도로를 이어주면 최근접 배정이 즉시 그쪽으로 회복.
            var g = MakeIslandGrid();
            g.Place(V(1, 1), TileType.Road);   // 본선(1,0)과 연결
            g.Place(V(1, 2), TileType.Road);   // (0,3) 회사 접점(대각 포함 8방)까지 연장

            var dm = new DemandMap(NearestCfg());
            dm.Reassign(g, new RoadNetwork(g));

            Assert.IsTrue(Has(dm, V(0, 0), V(0, 3)));   // 이제 도달 가능 + 더 가까움 → 회복
        }

        [Test]
        public void ChoicePool_SpreadsAssignments_Deterministically()
        {
            // 기본 풀(3): 가까운 3곳 중 좌표 해시로 택1 — 전부 최근접으로 쏠리지 않고,
            // 같은 도시는 몇 번을 재배정해도 같은 결과(결정론).
            var tiles = new System.Collections.Generic.List<(Vector2Int, TileType)>();
            for (int x = 0; x <= 14; x++) tiles.Add((V(x, 0), TileType.Road));
            foreach (int hx in new[] { 0, 2, 4, 6 }) tiles.Add((V(hx, 1), TileType.House));
            foreach (int ox in new[] { 9, 11, 13 }) tiles.Add((V(ox, 1), TileType.Office));
            var g = MakeGrid(16, 3, tiles.ToArray());

            var dm = new DemandMap(SimConfig.Default());
            dm.Reassign(g, new RoadNetwork(g));
            Assert.AreEqual(4, dm.Demands.Count);

            bool anySpread = false;   // 모든 집의 최근접은 (9,1) — 하나라도 다른 곳이면 분산 성공
            foreach (var d in dm.Demands)
                if (d.Sink != V(9, 1)) anySpread = true;
            Assert.IsTrue(anySpread);

            var dm2 = new DemandMap(SimConfig.Default());   // 결정론: 두 번 돌려도 동일
            dm2.Reassign(g, new RoadNetwork(g));
            for (int i = 0; i < dm.Demands.Count; i++)
            {
                Assert.AreEqual(dm.Demands[i].Source, dm2.Demands[i].Source);
                Assert.AreEqual(dm.Demands[i].Sink, dm2.Demands[i].Sink);
            }
        }

        // 감사 픽스 2: 집(2,2)의 스캔 1순위(북)에 고립 스텁 1칸, 2순위(남)에 간선 진입.
        // 간선은 (2,1)-(2,0)-(3,0)-(4,0)-(5,0)-Office(6,0)로 실제 연결됨.
        // 북쪽 스텁만 보고 접점을 고르면(구현 전) Region이 달라 도달불가로 오판 → 흐름 0.
        static CityGrid MultiFrontageGrid()
        {
            var g = new CityGrid(7, 4);
            g.Place(V(2, 3), TileType.Road);   // 고립 스텁(주변에 다른 도로 없음) — 스캔 1순위(북)
            g.Place(V(2, 1), TileType.Road);   // 간선 진입 — 스캔 3순위(남)
            g.Place(V(2, 0), TileType.Road);
            g.Place(V(3, 0), TileType.Road);
            g.Place(V(4, 0), TileType.Road);
            g.Place(V(5, 0), TileType.Road);
            g.Place(V(2, 2), TileType.House);
            g.Place(V(6, 0), TileType.Office);
            return g;
        }

        static List<Vector2Int> RouteOf(CityGrid grid, in SimConfig cfg)
        {
            var net = new RoadNetwork(grid);
            var dm = new DemandMap(cfg);
            dm.Reassign(grid, net);
            var planner = new RoutePlanner(grid.Width, grid.Height);
            planner.Plan(dm, net, grid, cfg);
            return planner.CarRoutes.Count > 0 ? planner.CarRoutes[0] : null;
        }

        [Test]
        public void MultiFrontage_IsolatedStubFirst_StillReachable()
        {
            var g = MultiFrontageGrid();
            var cfg = SimConfig.Default();

            var net = new RoadNetwork(g);
            var dm = new DemandMap(cfg);
            dm.Reassign(g, net);
            Assert.IsTrue(Has(dm, V(2, 2), V(6, 0)));

            var planner = new RoutePlanner(g.Width, g.Height);
            planner.Plan(dm, net, g, cfg);
            Assert.IsNotNull(planner.CarRoutes[0]); // 북쪽 스텁만 봤다면 미연결 오판
        }

        [Test]
        public void MultiFrontage_Deterministic()
        {
            var cfg = SimConfig.Default();
            List<Vector2Int> a = RouteOf(MultiFrontageGrid(), cfg);
            List<Vector2Int> b = RouteOf(MultiFrontageGrid(), cfg);
            CollectionAssert.AreEqual(a, b);
        }
    }
}
