using System.Collections.Generic;
using CityFlow.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Sim.Tests
{
    /// <summary>
    /// RED coverage for the P1 unsignaled-intersection round cap and the
    /// signal/roundabout ordering it is intended to create.
    /// </summary>
    public sealed class UnsignaledIntersectionCapTests
    {
        private sealed class CapRouteProvider : ICarRouteProvider
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
                if (!_routes.TryGetValue(carId, out Vector2Int[] route)) return false;

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

            public bool IsDestination(int carId, Vector2Int tile) =>
                _routes.TryGetValue(carId, out Vector2Int[] route)
                && route.Length > 0
                && route[route.Length - 1] == tile;
        }

        private sealed class CapFakeSignalGate : ISignalGate
        {
            private readonly HashSet<Vector2Int> _signalTiles = new();

            public void AddSignal(Vector2Int tile) => _signalTiles.Add(tile);

            public bool HasSignal(Vector2Int tile) => _signalTiles.Contains(tile);

            public bool IsServiceOpen(Vector2Int tile, Dir entryDir, int tick) => true;
        }

        [Test]
        public void Cap1_Unsignaled_CrossTraffic_OnePassPerRound()
        {
            SimConfig config = Config(cap: 1);
            int arrivals = RunCrossTraffic(config, signal: null, devices: null, ticks: 8);

            Assert.LessOrEqual(
                arrivals,
                2,
                "무신호 캡 1은 한 라운드에 한 대만 승인하므로 교차 수요 도착 누계가 제한되어야 한다.");
        }

        [Test]
        public void Cap0_Default_BitIdentical()
        {
            SimConfig config = Config(cap: 0);
            int arrivals = RunCrossTraffic(config, signal: null, devices: null, ticks: 8);

            Assert.AreEqual(
                4,
                arrivals,
                "캡 0은 기존 동작을 유지해 네 방향 교차 수요가 모두 도착해야 한다.");
        }

        [Test]
        public void Cap1_Signaled_Unaffected()
        {
            SimConfig config = Config(cap: 1);
            var signal = new CapFakeSignalGate();
            signal.AddSignal(Center);

            int arrivals = RunCrossTraffic(config, signal, devices: null, ticks: 8);

            Assert.AreEqual(
                4,
                arrivals,
                "신호 타일은 무신호 라운드 캡의 적용 대상이 아니어야 한다.");
        }

        [Test]
        public void Cap1_ForceIntent_BypassesCap()
        {
            SimConfig config = Config(cap: 1, valveTicks: 2);
            var network = CreateCrossTraffic(config, signal: null, devices: null);
            int arrivals = 0;
            int valveActivations = 0;

            for (int tick = 0; tick < 16; tick++)
            {
                StepResult result = network.Step(_routes, null, tick);
                arrivals += result.Arrivals;
                valveActivations += result.ValveActivations;
            }

            Assert.Greater(valveActivations, 0, "캡 대기가 임계에 도달하면 Force 이동이 발생해야 한다.");
            Assert.AreEqual(4, arrivals, "Force 인텐트는 캡을 우회해 교착을 풀어야 한다.");
        }

        [Test]
        public void Cap1_BlockedTicksAccumulate()
        {
            SimConfig config = Config(cap: 1, valveTicks: 20);
            var network = CreateCrossTraffic(config, signal: null, devices: null);

            network.Step(_routes, null, tick: 0);
            network.Step(_routes, null, tick: 1);

            Assert.Greater(
                network.GetBlockedTicks(_blockedCarId),
                0,
                "캡에 의한 미승인은 신호 대기와 달리 blockedTicks를 누적해야 한다.");
        }

        [Test]
        public void Ordering_SignalBeatsUnsignaledCap1()
        {
            int unsignaled = RunCrossTraffic(Config(cap: 1), signal: null, devices: null, ticks: 12);
            var signal = new CapFakeSignalGate();
            signal.AddSignal(Center);
            int signaled = RunCrossTraffic(Config(cap: 1), signal, devices: null, ticks: 12);

            Assert.GreaterOrEqual(signaled, unsignaled);
            Assert.Greater(signaled, unsignaled, "동일 수요에서 신호가 무신호 캡보다 한 지점 이상 앞서야 한다.");
        }

        [Test]
        public void Ordering_RoundaboutBeatsUnsignaledCap1()
        {
            int unsignaled = RunCrossTraffic(Config(cap: 1), signal: null, devices: null, ticks: 12);
            int roundabout = RunRoundaboutTraffic(Config(cap: 1), ticks: 20);

            Assert.GreaterOrEqual(
                roundabout,
                unsignaled,
                "동일 수요에서 로터리는 무신호 캡 교차로보다 뒤처지지 않아야 한다.");
        }

        private static readonly Vector2Int Center = new(1, 1);
        private static readonly int _blockedCarId = 0;
        private static CapRouteProvider _routes;

        private static SimConfig Config(int cap, int valveTicks = 8)
        {
            SimConfig config = SimConfig.Default();
            config.QueueCapacityPerTile = 4;
            config.QueueServicePerTick = 1;
            config.GridlockValveTicks = valveTicks;
            config.UnsignaledIntersectionRoundCap = cap;
            return config;
        }

        private static int RunCrossTraffic(
            SimConfig config,
            CapFakeSignalGate signal,
            FakeDeviceState devices,
            int ticks)
        {
            var network = CreateCrossTraffic(config, signal, devices);
            int arrivals = 0;
            for (int tick = 0; tick < ticks; tick++)
                arrivals += network.Step(_routes, signal, tick).Arrivals;
            return arrivals;
        }

        private static int RunRoundaboutTraffic(SimConfig config, int ticks)
        {
            var devices = new FakeDeviceState();
            Vector2Int center = V(2, 2);
            devices.AddRoundabout(center);
            var network = new RoadQueueNetwork(5, 5, config);
            network.RebuildTopology(CrossGrid5(), devices);
            var routes = new CapRouteProvider();
            routes.Add(10, V(4, 2), V(3, 2), center, V(1, 2), V(0, 2));
            routes.Add(11, V(4, 2), V(3, 2), center, V(1, 2), V(0, 2));
            routes.Add(12, V(2, 4), V(2, 3), center, V(2, 1), V(2, 0));
            routes.Add(13, V(2, 4), V(2, 3), center, V(2, 1), V(2, 0));
            Assert.IsTrue(network.TryEnqueue(V(4, 2), Dir.W, 10));
            Assert.IsTrue(network.TryEnqueue(V(4, 2), Dir.W, 11));
            Assert.IsTrue(network.TryEnqueue(V(2, 4), Dir.S, 12));
            Assert.IsTrue(network.TryEnqueue(V(2, 4), Dir.S, 13));

            int arrivals = 0;
            for (int tick = 0; tick < ticks; tick++)
                arrivals += network.Step(routes, null, tick).Arrivals;
            return arrivals;
        }

        private static RoadQueueNetwork CreateCrossTraffic(
            SimConfig config,
            CapFakeSignalGate signal,
            FakeDeviceState devices)
        {
            CityGrid grid = CrossGrid();
            var network = new RoadQueueNetwork(3, 3, config);
            network.RebuildTopology(grid, devices);
            _routes = new CapRouteProvider();
            _routes.Add(0, V(0, 1), Center, V(2, 1));
            _routes.Add(1, V(2, 1), Center, V(0, 1));
            _routes.Add(2, V(1, 0), Center, V(1, 2));
            _routes.Add(3, V(1, 2), Center, V(1, 0));
            Assert.IsTrue(network.TryEnqueue(V(0, 1), Dir.E, 0));
            Assert.IsTrue(network.TryEnqueue(V(2, 1), Dir.W, 1));
            Assert.IsTrue(network.TryEnqueue(V(1, 0), Dir.N, 2));
            Assert.IsTrue(network.TryEnqueue(V(1, 2), Dir.S, 3));
            return network;
        }

        private static CityGrid CrossGrid()
        {
            var grid = new CityGrid(3, 3);
            Assert.IsTrue(grid.Place(Center, TileType.Road));
            Assert.IsTrue(grid.Place(V(1, 2), TileType.Road));
            Assert.IsTrue(grid.Place(V(2, 1), TileType.Road));
            Assert.IsTrue(grid.Place(V(1, 0), TileType.Road));
            Assert.IsTrue(grid.Place(V(0, 1), TileType.Road));
            return grid;
        }

        private static CityGrid CrossGrid5()
        {
            var grid = new CityGrid(5, 5);
            for (int offset = 0; offset < 5; offset++)
            {
                Assert.IsTrue(grid.Place(V(offset, 2), TileType.Road));
                if (offset != 2)
                    Assert.IsTrue(grid.Place(V(2, offset), TileType.Road));
            }
            return grid;
        }

        private static Vector2Int V(int x, int y) => new(x, y);
    }
}
