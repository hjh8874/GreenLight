using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CityFlow.Sim;

namespace CityFlow.Sim.Tests
{
    internal sealed class FakeDeviceState : IDeviceState
    {
        private readonly HashSet<Vector2Int> _roundabouts = new();
        private readonly HashSet<Vector2Int> _overpasses = new();
        private readonly Dictionary<Vector2Int, RoadAxis> _priority = new();
        private readonly Dictionary<Vector2Int, Vector2Int> _oneways = new();
        private readonly HashSet<(Vector2Int tile, Dir entry, Dir exit)> _blockedTurns = new();

        public void AddRoundabout(Vector2Int tile) => _roundabouts.Add(tile);
        public void AddOverpass(Vector2Int tile) => _overpasses.Add(tile);
        public void SetPriority(Vector2Int tile, RoadAxis axis) => _priority[tile] = axis;
        public void SetOneway(Vector2Int tile, Vector2Int direction) => _oneways[tile] = direction;
        public void BlockTurn(Vector2Int tile, Dir entry, Dir exit) =>
            _blockedTurns.Add((tile, entry, exit));

        public bool IsRoundabout(Vector2Int tile) => _roundabouts.Contains(tile);
        public bool IsOverpass(Vector2Int tile) => _overpasses.Contains(tile);
        public RoadAxis PriorityAxis(Vector2Int tile) =>
            _priority.TryGetValue(tile, out RoadAxis axis) ? axis : RoadAxis.None;
        public Vector2Int OnewayDir(Vector2Int tile) =>
            _oneways.TryGetValue(tile, out Vector2Int direction)
                ? direction
                : Vector2Int.zero;
        public bool IsTurnAllowed(Vector2Int tile, Dir entry, Dir exit) =>
            !_blockedTurns.Contains((tile, entry, exit));
    }

    public class RoadQueueDeviceTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static SimConfig Cfg()
        {
            SimConfig cfg = SimConfig.Default();
            cfg.QueueCapacityPerTile = 4;
            cfg.QueueServicePerTick = 1;
            cfg.GridlockValveTicks = 8;
            return cfg;
        }

        private sealed class FakeRouteProvider : ICarRouteProvider
        {
            private readonly Dictionary<int, Vector2Int[]> _routes = new();

            public void Add(int carId, params Vector2Int[] route) =>
                _routes.Add(carId, route);

            public bool TryGetNextTile(
                int carId,
                Vector2Int current,
                out Vector2Int next,
                out Dir entryDirAtNext)
            {
                next = default;
                entryDirAtNext = default;
                if (!_routes.TryGetValue(carId, out Vector2Int[] route))
                {
                    return false;
                }

                for (int i = 0; i < route.Length - 1; i++)
                {
                    if (route[i] != current) continue;
                    next = route[i + 1];
                    Vector2Int delta = next - current;
                    if (delta == Vector2Int.up) entryDirAtNext = Dir.N;
                    else if (delta == Vector2Int.right) entryDirAtNext = Dir.E;
                    else if (delta == Vector2Int.down) entryDirAtNext = Dir.S;
                    else if (delta == Vector2Int.left) entryDirAtNext = Dir.W;
                    else Assert.Fail($"직교 인접 스텝이 아님: {delta}");
                    return true;
                }

                return false;
            }

            public bool IsDestination(int carId, Vector2Int tile)
            {
                return _routes.TryGetValue(carId, out Vector2Int[] route)
                    && route.Length > 0
                    && route[route.Length - 1] == tile;
            }
        }

        private sealed class FakeSignalGate : ISignalGate
        {
            private struct Window
            {
                public Vector2Int Tile;
                public int Start;
                public int End;
            }

            private readonly List<Window> _windows = new();
            public int ClosedAttempts { get; private set; }

            public void AddWindow(Vector2Int tile, int start, int end)
            {
                _windows.Add(new Window { Tile = tile, Start = start, End = end });
            }

            public bool IsServiceOpen(Vector2Int tile, Dir entryDir, int tick)
            {
                for (int i = 0; i < _windows.Count; i++)
                {
                    Window window = _windows[i];
                    if (window.Tile != tile) continue;
                    bool open = tick >= window.Start && tick <= window.End;
                    if (!open) ClosedAttempts++;
                    return open;
                }

                return true;
            }
        }

        [Test]
        public void Signal_RedAccumulatesQueue_ThenGreenDrainsOnePerTick()
        {
            var q = new RoadQueueNetwork(3, 1, Cfg());
            var routes = new FakeRouteProvider();
            var signals = new FakeSignalGate();
            Vector2Int signalTile = V(1, 0);
            signals.AddWindow(signalTile, start: 3, end: 20);

            for (int id = 0; id < 3; id++)
            {
                routes.Add(id, signalTile);
                Assert.IsTrue(q.TryEnqueue(signalTile, Dir.E, id));
                StepResult red = q.Step(routes, signals, tick: id);
                Assert.AreEqual(0, red.Arrivals);
                Assert.AreEqual(id + 1, q.QueueCount(signalTile, Dir.E));
            }

            for (int tick = 3; tick < 6; tick++)
            {
                StepResult green = q.Step(routes, signals, tick);
                Assert.AreEqual(1, green.Arrivals);
                Assert.AreEqual(5 - tick, q.QueueCount(signalTile, Dir.E));
            }
        }

        [Test]
        public void GreenWave_AlignedOffsets_PlatoonPassesWithoutClosedGate()
        {
            RunPlatoon(aligned: true, out int arrivals, out int closedAttempts);

            Assert.AreEqual(3, arrivals);
            Assert.AreEqual(0, closedAttempts, "정렬된 두 신호에서 소대 무정차");
        }

        [Test]
        public void GreenWave_MisalignedOffsets_PlatoonWaitsAtSecondSignal()
        {
            RunPlatoon(aligned: false, out _, out int closedAttempts);

            Assert.Greater(closedAttempts, 0, "offset 대조군은 두 번째 신호에서 대기");
        }

        private static void RunPlatoon(
            bool aligned,
            out int arrivals,
            out int closedAttempts)
        {
            var q = new RoadQueueNetwork(5, 1, Cfg());
            var routes = new FakeRouteProvider();
            var signals = new FakeSignalGate();
            Vector2Int firstSignal = V(1, 0);
            Vector2Int secondSignal = V(3, 0);
            signals.AddWindow(firstSignal, start: 1, end: 3);
            signals.AddWindow(
                secondSignal,
                start: aligned ? 3 : 0,
                end: aligned ? 5 : 2);

            for (int id = 0; id < 3; id++)
            {
                routes.Add(id, V(0, 0), V(1, 0), V(2, 0), V(3, 0), V(4, 0));
                Assert.IsTrue(q.TryEnqueue(V(0, 0), Dir.E, id));
            }

            arrivals = 0;
            for (int tick = 0; tick < 9; tick++)
            {
                arrivals += q.Step(routes, signals, tick).Arrivals;
            }

            closedAttempts = signals.ClosedAttempts;
        }

        [Test]
        public void Oneway_ReverseDirectionQueueRejectsEnqueue()
        {
            var grid = StraightGrid(3);
            var devices = new FakeDeviceState();
            devices.SetOneway(V(1, 0), Vector2Int.right);
            var q = new RoadQueueNetwork(3, 1, Cfg());
            q.RebuildTopology(grid, devices);

            Assert.IsFalse(q.TryEnqueue(V(1, 0), Dir.W, 10));
            Assert.AreEqual(0, q.QueueCount(V(1, 0), Dir.W));
            Assert.IsTrue(q.TryEnqueue(V(1, 0), Dir.E, 11));
        }

        [Test]
        public void TurnRestriction_BlocksIntentAndIncrementsCounterWithoutLosingCar()
        {
            CityGrid grid = CrossGrid();
            Vector2Int center = V(1, 1);
            var devices = new FakeDeviceState();
            devices.BlockTurn(center, Dir.E, Dir.N);
            var q = new RoadQueueNetwork(3, 3, Cfg());
            q.RebuildTopology(grid, devices);
            var routes = new FakeRouteProvider();
            routes.Add(20, center, V(1, 2));
            Assert.IsTrue(q.TryEnqueue(center, Dir.E, 20));

            q.Step(routes);

            Assert.AreEqual(1, q.TurnRestrictionBlockCount);
            Assert.AreEqual(20, q.CarAtHead(center, Dir.E));
            Assert.AreEqual(0, q.QueueCount(V(1, 2), Dir.N));
        }

        [Test]
        public void IntersectionSharedBudget_PriorityAxisWinsAndOtherQueueRemains()
        {
            CityGrid grid = CrossGrid();
            Vector2Int center = V(1, 1);
            var devices = new FakeDeviceState();
            devices.SetPriority(center, RoadAxis.Horizontal);
            var q = new RoadQueueNetwork(3, 3, Cfg());
            q.RebuildTopology(grid, devices);
            var routes = new FakeRouteProvider();
            routes.Add(30, center, V(2, 1));
            routes.Add(31, center, V(1, 2));
            Assert.IsTrue(q.TryEnqueue(center, Dir.E, 30));
            Assert.IsTrue(q.TryEnqueue(center, Dir.N, 31));

            q.Step(routes);

            Assert.AreEqual(0, q.QueueCount(center, Dir.E), "우선 가로축 먼저 서비스");
            Assert.AreEqual(1, q.QueueCount(center, Dir.N), "비우선 세로축 대기");
        }

        [Test]
        public void IntersectionSharedBudget_WithoutPriority_UsesTurnOrder()
        {
            CityGrid grid = CrossGrid();
            Vector2Int center = V(1, 1);
            var q = new RoadQueueNetwork(3, 3, Cfg());
            q.RebuildTopology(grid, new FakeDeviceState());
            var routes = new FakeRouteProvider();
            routes.Add(40, center, V(2, 1));       // E→E 직진
            routes.Add(41, center, V(1, 0));       // W→S 좌회전
            routes.Add(42, center, V(2, 1));       // N→E 우회전
            Assert.IsTrue(q.TryEnqueue(center, Dir.E, 40));
            Assert.IsTrue(q.TryEnqueue(center, Dir.W, 41));
            Assert.IsTrue(q.TryEnqueue(center, Dir.N, 42));

            q.Step(routes);
            Assert.AreEqual(0, q.QueueCount(center, Dir.E), "직진 1순위");

            q.Step(routes);
            Assert.AreEqual(0, q.QueueCount(center, Dir.N), "우회전 2순위");
            Assert.AreEqual(1, q.QueueCount(center, Dir.W), "좌회전은 계속 대기");
        }

        [Test]
        public void Overpass_CrossAxesServiceIndependentlyInSameTick()
        {
            CityGrid grid = CrossGrid();
            Vector2Int center = V(1, 1);
            var devices = new FakeDeviceState();
            devices.AddOverpass(center);
            var q = new RoadQueueNetwork(3, 3, Cfg());
            q.RebuildTopology(grid, devices);
            var routes = new FakeRouteProvider();
            routes.Add(50, center, V(2, 1));
            routes.Add(51, center, V(1, 2));
            Assert.IsTrue(q.TryEnqueue(center, Dir.E, 50));
            Assert.IsTrue(q.TryEnqueue(center, Dir.N, 51));

            q.Step(routes);

            Assert.AreEqual(0, q.QueueCount(center, Dir.E));
            Assert.AreEqual(0, q.QueueCount(center, Dir.N));
            Assert.AreEqual(50, q.CarAtHead(V(2, 1), Dir.E));
            Assert.AreEqual(51, q.CarAtHead(V(1, 2), Dir.N));
        }

        [Test]
        public void Roundabout_FullRingCirculates_BlocksEntry_ThenExitsAndResumes()
        {
            CityGrid grid = CrossGrid();
            Vector2Int center = V(1, 1);
            var devices = new FakeDeviceState();
            devices.AddRoundabout(center);
            var q = new RoadQueueNetwork(3, 3, Cfg());
            q.RebuildTopology(grid, devices);
            var routes = new FakeRouteProvider();

            routes.Add(60, center, V(1, 0)); // E 진입→W 셀, 다음 CCW S에서 이탈
            routes.Add(61, center, V(2, 1)); // N 진입→S 셀, 다음 CCW E에서 이탈
            routes.Add(62, center, V(1, 2)); // W 진입→E 셀, 다음 CCW N에서 이탈
            routes.Add(63, center, V(0, 1)); // S 진입→N 셀, 다음 CCW W에서 이탈
            Assert.IsTrue(q.TryEnqueue(center, Dir.E, 60));
            Assert.IsTrue(q.TryEnqueue(center, Dir.N, 61));
            Assert.IsTrue(q.TryEnqueue(center, Dir.W, 62));
            Assert.IsTrue(q.TryEnqueue(center, Dir.S, 63));

            q.Step(routes);
            Assert.AreEqual(60, q.RingCellCar(center, Dir.W));
            Assert.AreEqual(61, q.RingCellCar(center, Dir.S));
            Assert.AreEqual(62, q.RingCellCar(center, Dir.E));
            Assert.AreEqual(63, q.RingCellCar(center, Dir.N));
            Assert.AreEqual(1f, q.MaxOccupancy01(center), 1e-4f);

            routes.Add(64, center, V(1, 0));
            Assert.IsTrue(q.TryEnqueue(center, Dir.E, 64));
            q.Step(routes);

            Assert.AreEqual(63, q.RingCellCar(center, Dir.W), "만석 링은 CCW 무감속 순환");
            Assert.AreEqual(64, q.CarAtHead(center, Dir.E), "점유 셀 진입 차는 대기");

            q.Step(routes);
            Assert.AreEqual(64, q.RingCellCar(center, Dir.W), "링 이탈 후 접근 차 진입 재개");
            Assert.AreEqual(-1, q.CarAtHead(center, Dir.E));
        }

        private static CityGrid StraightGrid(int width)
        {
            var grid = new CityGrid(width, 1);
            for (int x = 0; x < width; x++)
                Assert.IsTrue(grid.Place(V(x, 0), CityFlow.Contracts.TileType.Road));
            return grid;
        }

        private static CityGrid CrossGrid()
        {
            var grid = new CityGrid(3, 3);
            Assert.IsTrue(grid.Place(V(1, 1), CityFlow.Contracts.TileType.Road));
            Assert.IsTrue(grid.Place(V(1, 2), CityFlow.Contracts.TileType.Road));
            Assert.IsTrue(grid.Place(V(2, 1), CityFlow.Contracts.TileType.Road));
            Assert.IsTrue(grid.Place(V(1, 0), CityFlow.Contracts.TileType.Road));
            Assert.IsTrue(grid.Place(V(0, 1), CityFlow.Contracts.TileType.Road));
            return grid;
        }
    }
}
