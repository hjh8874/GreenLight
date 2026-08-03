using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public class IntersectionDepartureTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        // CarSimTests 의 ResumeRouteProvider 는 private 중첩 클래스라 여기서 못 쓴다
        // (2차 리뷰 P0). 이 파일 전용으로 최소 구현을 둔다.
        private sealed class RouteProvider : ICarRouteProvider
        {
            private readonly System.Collections.Generic.Dictionary<int, Vector2Int[]> _routes = new();

            public void Add(int carId, params Vector2Int[] route) => _routes[carId] = route;

            public bool IsDestination(int carId, Vector2Int tile) =>
                _routes.TryGetValue(carId, out var r) && r.Length > 0 && r[r.Length - 1] == tile;

            public bool TryGetNextTile(int carId, Vector2Int from, out Vector2Int next, out Dir exit)
            {
                next = default;
                exit = Dir.N;
                if (!_routes.TryGetValue(carId, out var r)) return false;
                for (int i = 0; i < r.Length - 1; i++)
                {
                    if (r[i] != from) continue;
                    next = r[i + 1];
                    Vector2Int d = next - from;
                    if (d == new Vector2Int(0, 1)) exit = Dir.N;
                    else if (d == new Vector2Int(1, 0)) exit = Dir.E;
                    else if (d == new Vector2Int(0, -1)) exit = Dir.S;
                    else if (d == new Vector2Int(-1, 0)) exit = Dir.W;
                    else return false;
                    return true;
                }
                return false;
            }
        }

        static SimConfig Cfg()
        {
            SimConfig cfg = SimConfig.Default();
            cfg.GridWidth = 8;
            cfg.GridHeight = 8;
            cfg.TickInterval = 0.25f;
            return cfg;
        }

        // 십자 교차로 (4,4). 팔 4개.
        static CityGrid CrossGrid()
        {
            var grid = new CityGrid(8, 8);
            Assert.IsTrue(grid.Place(V(4, 4), TileType.Road));
            Assert.IsTrue(grid.Place(V(4, 5), TileType.Road));
            Assert.IsTrue(grid.Place(V(4, 3), TileType.Road));
            Assert.IsTrue(grid.Place(V(3, 4), TileType.Road));
            Assert.IsTrue(grid.Place(V(5, 4), TileType.Road));
            Assert.IsTrue(grid.IsIntersection(V(4, 4)));
            return grid;
        }

        [Test]
        public void SpawnAtIntersection_EmptyCells_Succeeds()
        {
            SimConfig cfg = Cfg();
            var grid = CrossGrid();
            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);

            Assert.IsTrue(
                net.TryEnqueueAtIntersection(V(4, 4), Dir.N, Dir.N, carId: 7, occupancyUnits: 1),
                "빈 교차로에는 스테이지를 부여해 스폰할 수 있어야 한다");
            Assert.IsTrue(net.TryLocateCar(7, out Vector2Int tile, out _, out int slot));
            Assert.AreEqual(V(4, 4), tile);
            Assert.GreaterOrEqual(slot, 0, "스폰한 차는 네트워크에 실제로 올라와 있어야 한다");
        }

        [Test]
        public void SpawnAtIntersection_ConflictingCells_FailsThenSucceedsWhenCleared()
        {
            SimConfig cfg = Cfg();
            var grid = CrossGrid();
            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);

            // 북진 직진이 동쪽 절반(SouthEast|NorthEast)을 점유한다.
            Assert.IsTrue(net.TryEnqueueAtIntersection(V(4, 4), Dir.N, Dir.N, 1, 1));
            // 같은 동쪽 절반을 요구하는 두 번째 북진은 들어갈 수 없다.
            Assert.IsFalse(
                net.TryEnqueueAtIntersection(V(4, 4), Dir.N, Dir.N, 2, 1),
                "이미 예약된 셀과 겹치면 스폰은 실패하고 다음 틱에 재시도한다");

            Assert.IsTrue(net.TryRemoveCarForRescue(1));
            Assert.IsTrue(
                net.TryEnqueueAtIntersection(V(4, 4), Dir.N, Dir.N, 2, 1),
                "셀이 비면 반드시 진입한다 — 이것이 수렴 보장이다");
        }

        // 설계 §6 T2. 스테이지별 점유 범위가 실제 계약대로인지 고정한다.
        //   회전 스폰 = Entry  → OccupancyMask = StageMask = EntryCell(entry) 한 칸
        //   직진 스폰 = Exit   → OccupancyMask = MovementMask(entry, exit) 두 칸
        // (IntersectionMicroGrid.cs:41-47,107-125)
        // 최초 계획서는 회전 스폰이 MovementMask 두 칸을 잡는다고 잘못 적었다. 리뷰가 잡았다.
        [Test]
        public void SpawnAtIntersection_TurnHoldsEntryCellOnly()
        {
            SimConfig cfg = Cfg();
            var grid = CrossGrid();
            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);

            // 북진 좌회전 = Entry 스테이지 → EntryCell(N) = SouthEast 한 칸만 예약
            Assert.IsTrue(
                net.TryEnqueueAtIntersection(V(4, 4), Dir.N, Dir.W, 1, 1),
                "빈 교차로에서 좌회전 스폰은 성공한다");

            // 동진 직진 = Exit 스테이지 → MovementMask(E,E) = SouthWest|SouthEast.
            // SouthEast 가 겹치므로 막힌다.
            Assert.IsFalse(
                net.TryEnqueueAtIntersection(V(4, 4), Dir.E, Dir.E, 2, 1),
                "Entry 가 쥔 SouthEast 를 요구하는 직진은 동시에 진입할 수 없다");

            // 서진 직진 = MovementMask(W,W) = NorthEast|NorthWest. SouthEast 와 안 겹친다.
            Assert.IsTrue(
                net.TryEnqueueAtIntersection(V(4, 4), Dir.W, Dir.W, 3, 1),
                "Entry 는 한 칸만 쥔다 — 겹치지 않는 경로는 통과한다");
        }

        // 설계 §6 T2 후반 + 리뷰 P0-3. 앞차가 출구 타일로 넘어갔어도 뒤꽁무니가 교차로에
        // 남아 있는 동안에는 충돌 경로 스폰을 승인하면 안 된다.
        [Test]
        public void SpawnAtIntersection_RearClearanceBlocksConflictingSpawn()
        {
            SimConfig cfg = Cfg();
            var grid = CrossGrid();
            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);

            var routes = new RouteProvider();
            // 북진 직진: (4,3) → (4,4) 교차로 → (4,5) 출구
            routes.Add(10, V(4, 3), V(4, 4), V(4, 5));
            Assert.IsTrue(net.TryEnqueue(V(4, 3), Dir.N, 10));

            // 차가 교차로를 지나 출구 타일에 올라설 때까지 진행시킨다.
            bool reachedExitTile = false;
            for (int tick = 0; tick < 8 && !reachedExitTile; tick++)
            {
                net.Step(routes);
                reachedExitTile =
                    net.TryLocateCar(10, out Vector2Int at, out _, out _)
                    && at == V(4, 5);
            }
            Assert.IsTrue(reachedExitTile, "전제: 차가 출구 타일까지 전진했다");

            // 뒤꽁무니가 아직 교차로 안이다. 같은 경로를 요구하는 스폰은 막혀야 한다.
            Assert.IsFalse(
                net.TryEnqueueAtIntersection(V(4, 4), Dir.N, Dir.N, 11, 1),
                "출구로 넘어간 앞차의 rear clearance 를 무시하고 스폰하면 안 된다");
        }

        // 2차 리뷰 P1 — 이 설계의 핵심 주장("수렴한다")을 잠그는 테스트.
        // Entry 로 앉힌 차가 실제로 Entry→Exit→출구타일로 전진하지 않으면
        // 스톨 위치만 옮긴 것이다. 이 단정이 없으면 전진 배선이 빠진 구현도 통과한다.
        [Test]
        public void SpawnedTurn_AdvancesOutWithinFiniteTicks()
        {
            SimConfig cfg = Cfg();
            var grid = CrossGrid();
            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);

            var routes = new RouteProvider();
            // 북진 진입 → 동쪽으로 우회전해서 (5,4) 로 빠진다.
            routes.Add(21, V(4, 4), V(5, 4));
            Assert.IsTrue(net.TryEnqueueAtIntersection(V(4, 4), Dir.N, Dir.E, 21, 1),
                "전제: 회전 스폰 성공");

            bool left = false;
            for (int tick = 0; tick < 10 && !left; tick++)
            {
                net.Step(routes);
                left = !net.TryLocateCar(21, out Vector2Int at, out _, out _) || at != V(4, 4);
            }
            Assert.IsTrue(left,
                "Entry 로 스폰한 차는 유한 틱 안에 교차로를 빠져나가야 한다 — 이게 수렴 보장이다");
        }

        // 설계 D5 + 2차 리뷰 P0. 로터리는 중심뿐 아니라 "팔"도 범위 밖이다.
        // UsesSharedBudget 이 팔을 제외하지 않으므로 별도 가드가 필요하다.
        [Test]
        public void SpawnAtIntersection_RoundaboutCenterAndArm_BothRejected()
        {
            SimConfig cfg = Cfg();
            var grid = CrossGrid();
            // 팔이 교차로가 되도록 (5,4) 에 세 번째 도로 가지를 붙인다.
            Assert.IsTrue(grid.Place(V(5, 5), TileType.Road));
            Assert.IsTrue(grid.Place(V(5, 3), TileType.Road));
            Assert.IsTrue(grid.IsIntersection(V(5, 4)), "전제: 팔 타일이 grid 교차로다");

            var devices = new FakeDeviceState();
            devices.AddRoundabout(V(4, 4));           // 중심 (4,4), 팔은 상하좌우
            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid, devices);

            Assert.IsFalse(
                net.TryEnqueueAtIntersection(V(4, 4), Dir.N, Dir.N, 5, 1),
                "로터리 중심은 링 예약 모델이라 범위 밖이다");
            Assert.IsFalse(
                net.TryEnqueueAtIntersection(V(5, 4), Dir.E, Dir.E, 6, 1),
                "로터리 팔도 범위 밖이다 — 팔이 교차로여도 링 상태기계를 우회하면 안 된다");
        }

        [Test]
        public void SpawnAtIntersection_NonIntersectionTile_Rejected()
        {
            SimConfig cfg = Cfg();
            var grid = CrossGrid();
            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);

            Assert.IsFalse(
                net.TryEnqueueAtIntersection(V(4, 5), Dir.N, Dir.N, 3, 1),
                "교차로가 아닌 타일은 기존 TryEnqueue 경로를 써야 한다");
        }
    }
}
