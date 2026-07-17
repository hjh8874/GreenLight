using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CityFlow.Sim;

namespace CityFlow.Sim.Tests
{
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
    }
}
