using System;
using System.Collections.Generic;
using CityFlow.Contracts;
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
        private readonly Dictionary<Vector2Int, Vector2Int> _highways = new();

        public void AddRoundabout(Vector2Int tile) => _roundabouts.Add(tile);
        public void AddOverpass(Vector2Int tile) => _overpasses.Add(tile);
        public void SetPriority(Vector2Int tile, RoadAxis axis) => _priority[tile] = axis;
        public void SetOneway(Vector2Int tile, Vector2Int direction) => _oneways[tile] = direction;
        public void BlockTurn(Vector2Int tile, Dir entry, Dir exit) =>
            _blockedTurns.Add((tile, entry, exit));
        public void AddHighway(Vector2Int a, Vector2Int b)
        {
            _highways[a] = b;
            _highways[b] = a;
        }

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
        public bool TryGetHighwayPartner(Vector2Int ramp, out Vector2Int partner) =>
            _highways.TryGetValue(ramp, out partner);
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
        public void Signal_RedBlocksEntry_CarWaitsAtApproachTile()
        {
            var q = new RoadQueueNetwork(3, 1, Cfg());
            var routes = new FakeRouteProvider();
            var signals = new FakeSignalGate();
            Vector2Int approach = V(0, 0);
            Vector2Int signalTile = V(1, 0);
            signals.AddWindow(signalTile, start: 3, end: 20);
            routes.Add(0, approach, signalTile, V(2, 0));
            Assert.IsTrue(q.TryEnqueue(approach, Dir.E, 0));

            for (int tick = 0; tick < 3; tick++)
            {
                q.Step(routes, signals, tick);
                Assert.AreEqual(0, q.QueueCount(signalTile, Dir.E), "적색 동안 교차로 진입 금지");
                Assert.AreEqual(0, q.CarAtHead(approach, Dir.E), "정지선(접근 타일)에서 대기");
            }

            q.Step(routes, signals, tick: 3);
            Assert.AreEqual(1, q.QueueCount(signalTile, Dir.E), "초록에 진입");
        }

        [Test]
        public void Signal_RedStillDrainsCarInsideIntersection()
        {
            var q = new RoadQueueNetwork(3, 1, Cfg());
            var routes = new FakeRouteProvider();
            var signals = new FakeSignalGate();
            Vector2Int signalTile = V(1, 0);
            signals.AddWindow(signalTile, start: 10, end: 20);   // 관측 구간 내내 적색
            routes.Add(0, signalTile, V(2, 0));
            Assert.IsTrue(q.TryEnqueue(signalTile, Dir.E, 0));

            q.Step(routes, signals, tick: 0);

            Assert.AreEqual(0, q.QueueCount(signalTile, Dir.E), "적색이어도 교차로 위의 차는 빠져나간다");
            Assert.AreEqual(1, q.QueueCount(V(2, 0), Dir.E), "교차로 비우기");
        }

        // 진입 게이트 의미론(2026-07-21): 적색 동안 대기줄은 접근 타일(정지선)에 쌓이고,
        // 초록이 열리면 틱당 1대(큐 머리)씩 교차로로 진입한다.
        [Test]
        public void Signal_RedAccumulatesQueue_ThenGreenDrainsOnePerTick()
        {
            var q = new RoadQueueNetwork(4, 1, Cfg());
            var routes = new FakeRouteProvider();
            var signals = new FakeSignalGate();
            Vector2Int approach = V(0, 0);
            Vector2Int signalTile = V(1, 0);
            signals.AddWindow(signalTile, start: 3, end: 20);

            for (int id = 0; id < 3; id++)
            {
                routes.Add(id, approach, signalTile, V(2, 0), V(3, 0));
                Assert.IsTrue(q.TryEnqueue(approach, Dir.E, id));
                q.Step(routes, signals, tick: id);
                Assert.AreEqual(0, q.QueueCount(signalTile, Dir.E), "적색: 교차로는 비어 있어야");
                Assert.AreEqual(id + 1, q.QueueCount(approach, Dir.E), "적색: 정지선에 누적");
            }

            for (int tick = 3; tick < 6; tick++)
            {
                q.Step(routes, signals, tick);
                Assert.AreEqual(5 - tick, q.QueueCount(approach, Dir.E), "초록: 틱당 1대 진입");
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
            // 진입 게이트 의미론: 검사가 접근 타일에서 이뤄지므로 창이 구(舊) 의미론보다
            // 1틱 앞으로 당겨진다. 선두 타임라인: t0 진입1 → t2 진입2 검사 → t2~ 통과.
            signals.AddWindow(firstSignal, start: 0, end: 2);
            signals.AddWindow(
                secondSignal,
                start: aligned ? 2 : 0,
                end: aligned ? 4 : 1);

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
            Vector2Int west = V(0, 1);
            Vector2Int south = V(1, 0);
            var devices = new FakeDeviceState();
            devices.SetPriority(center, RoadAxis.Horizontal);
            var q = new RoadQueueNetwork(3, 3, Cfg());
            q.RebuildTopology(grid, devices);
            var routes = new FakeRouteProvider();
            routes.Add(30, west, center, V(2, 1));
            routes.Add(31, south, center, V(1, 2));
            Assert.IsTrue(q.TryEnqueue(west, Dir.E, 30));
            Assert.IsTrue(q.TryEnqueue(south, Dir.N, 31));

            q.Step(routes);

            Assert.AreEqual(30, q.CarAtHead(center, Dir.E), "The horizontal approach enters first.");
            Assert.AreEqual(31, q.CarAtHead(south, Dir.N), "The vertical approach keeps waiting.");
        }

        [Test]
        public void IntersectionSharedBudget_AllowsCompatibleStraightAndTurnTogether()
        {
            CityGrid grid = CrossGrid();
            Vector2Int center = V(1, 1);
            Vector2Int west = V(0, 1);
            Vector2Int north = V(1, 2);
            var q = new RoadQueueNetwork(3, 3, Cfg());
            q.RebuildTopology(grid, new FakeDeviceState());
            var routes = new FakeRouteProvider();
            routes.Add(40, west, center, V(2, 1));
            routes.Add(41, north, center, west);
            Assert.IsTrue(q.TryEnqueue(west, Dir.E, 40));
            Assert.IsTrue(q.TryEnqueue(north, Dir.S, 41));

            q.Step(routes);
            Assert.AreEqual(40, q.CarAtHead(center, Dir.E));
            Assert.AreEqual(41, q.CarAtHead(center, Dir.S),
                "Compatible straight and right-turn paths may enter in the same tick.");
        }

        [Test]
        public void Signal_TurnAlreadyInside_ClearsAfterSignalCloses()
        {
            CityGrid grid = CrossGrid();
            Vector2Int center = V(1, 1);
            Vector2Int west = V(0, 1);
            var q = new RoadQueueNetwork(3, 3, Cfg());
            q.RebuildTopology(grid, new FakeDeviceState());
            var routes = new FakeRouteProvider();
            routes.Add(90, west, center, V(1, 2));
            var signal = new FakeSignalGate();
            signal.AddWindow(center, start: 0, end: 0);
            Assert.IsTrue(q.TryEnqueue(west, Dir.E, 90));

            q.Step(routes, signal, tick: 0);
            Assert.IsTrue(q.TryLocateCar(90, out _, out _, out _, out float progress));
            Assert.AreEqual(0.75f, progress, 1e-4f,
                "A clear turn should cross without an artificial entry dwell.");

            q.Step(routes, signal, tick: 1);
            Assert.IsTrue(q.TryLocateCar(90, out _, out _, out _, out progress));
            Assert.AreEqual(-1f, progress, 1e-4f, "교차로 내부 차량은 적색이어도 빠져나가야 한다");
            Assert.AreEqual(0, signal.ClosedAttempts, "내부 진행은 신호 게이트를 다시 검사하지 않아야 한다");
        }

        [Test]
        public void IntersectionEntry_ConflictingStraightPaths_YieldBeforeEntering()
        {
            CityGrid grid = CrossGrid();
            Vector2Int center = V(1, 1);
            Vector2Int west = V(0, 1);
            Vector2Int north = V(1, 2);
            var q = new RoadQueueNetwork(3, 3, Cfg());
            q.RebuildTopology(grid, new FakeDeviceState());
            var routes = new FakeRouteProvider();
            routes.Add(43, west, center, V(2, 1));
            routes.Add(44, north, center, V(1, 0));
            Assert.IsTrue(q.TryEnqueue(west, Dir.E, 43));
            Assert.IsTrue(q.TryEnqueue(north, Dir.S, 44));

            q.Step(routes);

            Assert.AreEqual(43, q.CarAtHead(center, Dir.E));
            Assert.AreEqual(44, q.CarAtHead(north, Dir.S));
            Assert.IsTrue(q.TryLocateCar(43, out _, out _, out _, out float eastProgress));
            Assert.IsTrue(q.TryLocateCar(44, out _, out _, out _, out float southProgress));
            Assert.AreEqual(0.75f, eastProgress, 1e-4f);
            Assert.AreEqual(-1f, southProgress, 1e-4f);

            q.Step(routes);

            Assert.IsTrue(q.TryLocateCar(43, out _, out _, out _, out eastProgress));
            Assert.IsTrue(q.TryLocateCar(44, out _, out _, out _, out southProgress));
            Assert.AreEqual(-1f, eastProgress, 1e-4f);
            Assert.AreEqual(-1f, southProgress, 1e-4f,
                "The conflicting path remains reserved during the first vehicle's exit tick.");

            q.Step(routes);

            Assert.IsTrue(q.TryLocateCar(44, out _, out _, out _, out southProgress));
            Assert.AreEqual(0.75f, southProgress, 1e-4f);
        }

        [Test]
        public void IntersectionEntry_OpposingStraightPaths_EnterTogether()
        {
            CityGrid grid = CrossGrid();
            Vector2Int center = V(1, 1);
            Vector2Int west = V(0, 1);
            Vector2Int east = V(2, 1);
            var q = new RoadQueueNetwork(3, 3, Cfg());
            q.RebuildTopology(grid, new FakeDeviceState());
            var routes = new FakeRouteProvider();
            routes.Add(45, west, center, east);
            routes.Add(46, east, center, west);
            Assert.IsTrue(q.TryEnqueue(west, Dir.E, 45));
            Assert.IsTrue(q.TryEnqueue(east, Dir.W, 46));

            q.Step(routes);

            Assert.AreEqual(45, q.CarAtHead(center, Dir.E));
            Assert.AreEqual(46, q.CarAtHead(center, Dir.W));
            Assert.IsTrue(q.TryLocateCar(45, out _, out _, out _, out float eastProgress));
            Assert.IsTrue(q.TryLocateCar(46, out _, out _, out _, out float westProgress));
            Assert.AreEqual(0.75f, eastProgress, 1e-4f);
            Assert.AreEqual(0.75f, westProgress, 1e-4f);
        }

        [Test]
        public void EmptyIntersection_DoesNotPersistConflictAsADwellStage()
        {
            CityGrid grid = CrossGrid();
            Vector2Int center = V(1, 1);
            Vector2Int west = V(0, 1);
            Vector2Int east = V(2, 1);
            var q = new RoadQueueNetwork(3, 3, Cfg());
            q.RebuildTopology(grid, new FakeDeviceState());
            var routes = new FakeRouteProvider();
            routes.Add(50, west, center, east);
            Assert.IsTrue(q.TryEnqueue(west, Dir.E, 50));

            q.Step(routes);
            Assert.AreEqual(50, q.CarAtHead(center, Dir.E));
            Assert.IsTrue(q.TryLocateCar(50, out _, out _, out _, out float exitProgress));
            Assert.AreEqual(0.75f, exitProgress, 1e-4f);

            q.Step(routes);
            Assert.AreEqual(50, q.CarAtHead(east, Dir.E));
            Assert.AreEqual(0, q.QueueCount(center, Dir.E));
        }

        [Test]
        public void IntersectionProgress_IsClearedWhenTopologyBecomesNormalRoad()
        {
            CityGrid grid = CrossGrid();
            Vector2Int center = V(1, 1);
            Vector2Int west = V(0, 1);
            var q = new RoadQueueNetwork(3, 3, Cfg());
            q.RebuildTopology(grid, new FakeDeviceState());
            var routes = new FakeRouteProvider();
            routes.Add(49, west, center, V(2, 1));
            Assert.IsTrue(q.TryEnqueue(west, Dir.E, 49));

            q.Step(routes);
            Assert.IsTrue(q.TryLocateCar(49, out _, out _, out _, out float progress));
            Assert.AreEqual(0.75f, progress, 1e-4f);

            Assert.IsTrue(grid.Remove(V(1, 2)));
            Assert.IsTrue(grid.Remove(V(1, 0)));
            q.RebuildTopology(grid, new FakeDeviceState());

            Assert.IsTrue(q.TryLocateCar(49, out _, out _, out _, out progress));
            Assert.AreEqual(-1f, progress, 1e-4f);
        }

        [Test]
        public void IntersectionEntry_ConflictingOccupant_ClearsBeforeNextEntry()
        {
            CityGrid grid = CrossGrid();
            Vector2Int center = V(1, 1);
            Vector2Int west = V(0, 1);
            Vector2Int north = V(1, 2);
            var q = new RoadQueueNetwork(3, 3, Cfg());
            q.RebuildTopology(grid, new FakeDeviceState());
            var routes = new FakeRouteProvider();
            routes.Add(47, west, center, V(2, 1));
            routes.Add(48, north, center, V(1, 0));
            Assert.IsTrue(q.TryEnqueue(west, Dir.E, 47));

            q.Step(routes);
            Assert.AreEqual(47, q.CarAtHead(center, Dir.E));

            Assert.IsTrue(q.TryEnqueue(north, Dir.S, 48));
            q.Step(routes);

            Assert.AreEqual(47, q.CarAtHead(V(2, 1), Dir.E));
            Assert.AreEqual(48, q.CarAtHead(north, Dir.S));

            q.Step(routes);

            Assert.AreEqual(48, q.CarAtHead(center, Dir.S));
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
        public void RoundaboutState_EntryChecksTargetAndUpstreamCell()
        {
            var state = new RoundaboutTrafficState();
            Assert.AreEqual(Dir.E, EnterRing(state, Dir.E, 10));

            state.BeginTick();
            state.RegisterEntryDemand(Dir.N);
            state.SelectEntrySide();
            Assert.IsFalse(
                state.TryReserveApproach(Dir.N),
                "A north-side entry must yield to a vehicle in its upstream east ring cell.");
        }

        [Test]
        public void RoundaboutState_TransitionReservesDestinationAndReleasesSource()
        {
            var state = new RoundaboutTrafficState();
            EnterRing(state, Dir.E, 11);

            state.BeginTick();
            state.AdvanceCounterClockwise();

            Assert.AreEqual(-1, state.NodeAt(Dir.E));
            Assert.AreEqual(11, state.NodeAt(Dir.N));
            Assert.IsFalse(state.IsReserved(Dir.E));
            Assert.IsTrue(state.IsReserved(Dir.N));
        }

        [Test]
        public void RoundaboutState_FollowerEntersCellVacatedByLeadVehicle()
        {
            var state = new RoundaboutTrafficState();
            EnterRing(state, Dir.E, 11);

            state.BeginTick();
            state.AdvanceCounterClockwise();
            state.RegisterEntryDemand(Dir.E);
            state.SelectEntrySide();

            Assert.AreEqual(11, state.NodeAt(Dir.N));
            Assert.AreEqual(-1, state.NodeAt(Dir.E));
            Assert.IsTrue(state.TryReserveApproach(Dir.E),
                "A follower may enter the merge cell vacated by its lead vehicle.");
        }

        [Test]
        public void RoundaboutState_ThreeVehicleStreamOccupiesDistinctCells()
        {
            var state = new RoundaboutTrafficState();
            EnterRing(state, Dir.E, 11);

            state.BeginTick();
            state.AdvanceCounterClockwise();
            state.RegisterEntryDemand(Dir.E);
            state.SelectEntrySide();
            Assert.IsTrue(state.TryReserveRingEntry(Dir.E, 12, out Dir secondTarget));
            Assert.IsTrue(state.CommitEntry(Dir.E, secondTarget, 12));

            state.BeginTick();
            state.AdvanceCounterClockwise();
            state.RegisterEntryDemand(Dir.E);
            state.SelectEntrySide();
            Assert.IsTrue(state.TryReserveRingEntry(Dir.E, 13, out Dir thirdTarget));
            Assert.IsTrue(state.CommitEntry(Dir.E, thirdTarget, 13));

            Assert.AreEqual(11, state.NodeAt(Dir.W));
            Assert.AreEqual(12, state.NodeAt(Dir.N));
            Assert.AreEqual(13, state.NodeAt(Dir.E));
        }

        [Test]
        public void RoundaboutState_ServingSideKeepsPriorityUntilDemandClears()
        {
            var state = new RoundaboutTrafficState();
            state.BeginTick();
            RegisterAllEntryDirections(state);
            state.SelectEntrySide();

            Assert.IsTrue(state.TryReserveApproach(Dir.E));
            Assert.IsFalse(state.TryReserveApproach(Dir.S));
            Assert.IsFalse(state.TryReserveApproach(Dir.N));
            Assert.IsFalse(state.TryReserveApproach(Dir.W));
            Assert.IsTrue(state.CommitApproach(Dir.E, 20));

            state.BeginTick();
            Assert.IsTrue(state.TryReserveRingEntry(Dir.E, 20, out Dir eastTarget));
            Assert.IsTrue(state.CommitEntry(Dir.E, eastTarget, 20));
            Assert.AreEqual(20, state.Remove(Dir.E));

            state.BeginTick();
            RegisterAllEntryDirections(state);
            state.SelectEntrySide();

            Assert.IsTrue(state.TryReserveApproach(Dir.E));
            Assert.IsFalse(state.TryReserveApproach(Dir.N));
            Assert.IsFalse(state.TryReserveApproach(Dir.W));
            Assert.IsFalse(state.TryReserveApproach(Dir.S));

            state.BeginTick();
            state.RegisterEntryDemand(Dir.N);
            state.SelectEntrySide();

            Assert.IsTrue(state.TryReserveApproach(Dir.N));
            Assert.IsFalse(state.TryReserveApproach(Dir.E));
        }

        [Test]
        public void RoundaboutState_EntryAllowsVehicleTwoCellsUpstream()
        {
            var state = new RoundaboutTrafficState();
            EnterRing(state, Dir.E, 12);

            state.BeginTick();
            state.AdvanceCounterClockwise();
            state.BeginTick();
            state.AdvanceCounterClockwise();
            state.BeginTick();
            state.RegisterEntryDemand(Dir.E);
            state.SelectEntrySide();

            Assert.AreEqual(12, state.NodeAt(Dir.W));
            Assert.IsTrue(
                state.TryReserveApproach(Dir.E),
                "A vehicle two cells upstream does not conflict with the merge.");
        }

        [Test]
        public void RoundaboutState_AdmitsOnlyOneSidePerTick()
        {
            var state = new RoundaboutTrafficState();
            state.BeginTick();
            state.RegisterEntryDemand(Dir.E);
            state.RegisterEntryDemand(Dir.N);
            state.SelectEntrySide();

            Assert.IsTrue(state.TryReserveApproach(Dir.E));
            Assert.IsFalse(state.TryReserveApproach(Dir.N));
        }

        [Test]
        public void RoundaboutState_ProgressFollowsAuthorizedCounterClockwiseCells()
        {
            Assert.AreEqual(0.25f, RoundaboutTrafficState.Progress01(Dir.E, Dir.N, Dir.W), 1e-4f);
            Assert.AreEqual(0.50f, RoundaboutTrafficState.Progress01(Dir.E, Dir.N, Dir.S), 1e-4f);
            Assert.AreEqual(0.75f, RoundaboutTrafficState.Progress01(Dir.E, Dir.N, Dir.E), 1e-4f);
            Assert.AreEqual(1.00f, RoundaboutTrafficState.Progress01(Dir.E, Dir.N, Dir.N), 1e-4f);

            Assert.AreEqual(0.50f, RoundaboutTrafficState.Progress01(Dir.E, Dir.S, Dir.W), 1e-4f);
            Assert.AreEqual(1.00f, RoundaboutTrafficState.Progress01(Dir.E, Dir.S, Dir.S), 1e-4f);
            Assert.AreEqual(-1f, RoundaboutTrafficState.Progress01(Dir.E, Dir.S, Dir.E), 1e-4f);
        }

        [Test]
        public void Roundabout_LegacyCenterQueuesUsePhysicalSideOrder()
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
            Assert.AreEqual(62, q.RingCellCar(center, Dir.E));
            Assert.AreEqual(-1, q.RingCellCar(center, Dir.S));
            Assert.AreEqual(60, q.CarAtHead(center, Dir.E), "Adjacent east entry waits.");
            Assert.AreEqual(61, q.CarAtHead(center, Dir.N), "Adjacent north entry waits.");
            Assert.AreEqual(63, q.CarAtHead(center, Dir.S), "Opposite entry waits for merge clearance.");
            Assert.AreEqual(0.25f, q.MaxOccupancy01(center), 1e-4f);

            q.Step(routes);
            Assert.AreEqual(62, q.RingCellCar(center, Dir.N));
            Assert.AreEqual(60, q.CarAtHead(center, Dir.E));
            Assert.AreEqual(61, q.CarAtHead(center, Dir.N));

            q.Step(routes);
            Assert.AreEqual(63, q.RingCellCar(center, Dir.N), "The next counter-clockwise side enters after clearance.");
            Assert.AreEqual(60, q.CarAtHead(center, Dir.E));
        }

        [Test]
        public void Roundabout_SimultaneousArms_EnterEastThenNorthWhenMergeCellsClear()
        {
            CityGrid grid = CrossGrid();
            Vector2Int center = V(1, 1);
            Vector2Int north = V(1, 2);
            Vector2Int south = V(1, 0);
            Vector2Int east = V(2, 1);
            var devices = new FakeDeviceState();
            devices.AddRoundabout(center);
            var q = new RoadQueueNetwork(3, 3, Cfg());
            q.RebuildTopology(grid, devices);
            var routes = new FakeRouteProvider();

            routes.Add(70, north, center, south);
            routes.Add(73, east, center, V(0, 1));
            Assert.IsTrue(q.TryEnqueue(north, Dir.S, 70));
            Assert.IsTrue(q.TryEnqueue(east, Dir.W, 73));

            q.Step(routes);

            Assert.AreEqual(73, q.RingCellCar(center, Dir.E));
            Assert.AreEqual(-1, q.RingCellCar(center, Dir.N));
            Assert.AreEqual(70, q.CarAtHead(north, Dir.S));
            Assert.AreEqual(-1, q.CarAtHead(east, Dir.W));
            Assert.AreEqual(0, q.QueueCount(center, Dir.N));
            Assert.AreEqual(0, q.QueueCount(center, Dir.E));
            Assert.AreEqual(0, q.QueueCount(center, Dir.S));
            Assert.AreEqual(0, q.QueueCount(center, Dir.W));

            q.Step(routes);
            Assert.AreEqual(73, q.RingCellCar(center, Dir.N));
            Assert.AreEqual(70, q.CarAtHead(north, Dir.S));

            q.Step(routes);
            Assert.AreEqual(73, q.RingCellCar(center, Dir.W));
            Assert.AreEqual(70, q.RingCellCar(center, Dir.N));
            Assert.AreEqual(-1, q.CarAtHead(north, Dir.S));
        }

        [Test]
        public void Roundabout_ThreeEastVehiclesDrainBeforeNorthEntry()
        {
            CityGrid grid = CrossGrid5();
            Vector2Int center = V(2, 2);
            Vector2Int northArm = V(2, 3);
            Vector2Int northApproach = V(2, 4);
            Vector2Int eastArm = V(3, 2);
            Vector2Int eastApproach = V(4, 2);
            Vector2Int westArm = V(1, 2);
            Vector2Int westApproach = V(0, 2);
            Vector2Int southArm = V(2, 1);
            Vector2Int southApproach = V(2, 0);
            var devices = new FakeDeviceState();
            devices.AddRoundabout(center);
            var q = new RoadQueueNetwork(5, 5, Cfg());
            q.RebuildTopology(grid, devices);
            var routes = new FakeRouteProvider();

            routes.Add(100, eastApproach, eastArm, center, westArm, westApproach);
            routes.Add(101, eastApproach, eastArm, center, westArm, westApproach);
            routes.Add(102, eastApproach, eastArm, center, westArm, westApproach);
            routes.Add(103, northApproach, northArm, center, southArm, southApproach);
            Assert.IsTrue(q.TryEnqueue(eastApproach, Dir.W, 100));
            Assert.IsTrue(q.TryEnqueue(eastApproach, Dir.W, 101));
            Assert.IsTrue(q.TryEnqueue(eastApproach, Dir.W, 102));
            Assert.IsTrue(q.TryEnqueue(northApproach, Dir.S, 103));

            var enteredOrder = new List<int>();
            var enteredCars = new HashSet<int>();
            int maxRingOccupancy = 0;
            for (int tick = 0; tick < 20; tick++)
            {
                q.Step(routes);
                int ringOccupancy = 0;
                foreach (Dir cell in new[] { Dir.N, Dir.E, Dir.S, Dir.W })
                {
                    int carId = q.RingCellCar(center, cell);
                    if (carId < 0) continue;

                    ringOccupancy++;
                    if (enteredCars.Add(carId)) enteredOrder.Add(carId);
                }

                maxRingOccupancy = Math.Max(maxRingOccupancy, ringOccupancy);
            }

            CollectionAssert.AreEqual(new[] { 100, 101, 102, 103 }, enteredOrder,
                "The serving east queue must drain before north receives entry priority.");
            Assert.GreaterOrEqual(maxRingOccupancy, 2,
                "A following vehicle must enter before its lead vehicle leaves the ring.");
        }

        [Test]
        public void RoundaboutArm_HoldsOnlyOneVehicleAcrossDirectionQueues()
        {
            CityGrid grid = CrossGrid();
            Vector2Int center = V(1, 1);
            Vector2Int eastArm = V(2, 1);
            var devices = new FakeDeviceState();
            devices.AddRoundabout(center);
            var q = new RoadQueueNetwork(3, 3, Cfg());
            q.RebuildTopology(grid, devices);

            Assert.IsTrue(q.TryEnqueue(eastArm, Dir.W, 80));
            Assert.IsFalse(q.TryEnqueue(eastArm, Dir.E, 81));
            Assert.AreEqual(1f, q.MaxOccupancy01(eastArm), 1e-4f);
        }

        [Test]
        public void Roundabout_CompetingVehicleWaitsOnApproachBeforeArm()
        {
            CityGrid grid = CrossGrid5();
            Vector2Int center = V(2, 2);
            Vector2Int eastArm = V(3, 2);
            Vector2Int eastApproach = V(4, 2);
            Vector2Int northArm = V(2, 3);
            Vector2Int northApproach = V(2, 4);
            var devices = new FakeDeviceState();
            devices.AddRoundabout(center);
            var q = new RoadQueueNetwork(5, 5, Cfg());
            q.RebuildTopology(grid, devices);
            var routes = new FakeRouteProvider();

            routes.Add(90, eastApproach, eastArm, center, V(1, 2), V(0, 2));
            routes.Add(91, northApproach, northArm, center, V(2, 1), V(2, 0));
            Assert.IsTrue(q.TryEnqueue(eastApproach, Dir.W, 90));
            Assert.IsTrue(q.TryEnqueue(northApproach, Dir.S, 91));

            q.Step(routes);

            Assert.AreEqual(90, q.CarAtHead(eastArm, Dir.W));
            Assert.AreEqual(91, q.CarAtHead(northApproach, Dir.S));
            Assert.AreEqual(-1, q.CarAtHead(northArm, Dir.S));
            Assert.AreEqual(-1, q.RingCellCar(center, Dir.E));

            q.Step(routes);

            Assert.AreEqual(90, q.RingCellCar(center, Dir.E));
            Assert.AreEqual(91, q.CarAtHead(northApproach, Dir.S));
            Assert.AreEqual(-1, q.CarAtHead(northArm, Dir.S));
        }

        private static void RegisterAllEntryDirections(RoundaboutTrafficState state)
        {
            state.RegisterEntryDemand(Dir.N);
            state.RegisterEntryDemand(Dir.E);
            state.RegisterEntryDemand(Dir.S);
            state.RegisterEntryDemand(Dir.W);
        }

        private static Dir EnterRing(RoundaboutTrafficState state, Dir side, int node)
        {
            state.BeginTick();
            state.RegisterEntryDemand(side);
            state.SelectEntrySide();
            Assert.IsTrue(state.TryReserveApproach(side));
            Assert.IsTrue(state.CommitApproach(side, node));

            state.BeginTick();
            Assert.IsTrue(state.TryReserveRingEntry(side, node, out Dir target));
            Assert.IsTrue(state.CommitEntry(side, target, node));
            return target;
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

        private static CityGrid CrossGrid5()
        {
            var grid = new CityGrid(5, 5);
            for (int offset = 0; offset < 5; offset++)
            {
                Assert.IsTrue(grid.Place(V(offset, 2), CityFlow.Contracts.TileType.Road));
                if (offset != 2)
                    Assert.IsTrue(grid.Place(V(2, offset), CityFlow.Contracts.TileType.Road));
            }
            return grid;
        }
    }
}
