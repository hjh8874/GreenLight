using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public class HighwayQueueTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        private sealed class Routes : ICarRouteProvider
        {
            readonly Dictionary<int, Vector2Int[]> _routes = new();
            public void Add(int id, params Vector2Int[] route) => _routes[id] = route;
            public bool IsDestination(int carId, Vector2Int tile) =>
                _routes.TryGetValue(carId, out var route) && route[route.Length - 1] == tile;
            public bool TryGetNextTile(int carId, Vector2Int current, out Vector2Int next, out Dir dir)
            {
                next = default; dir = default;
                if (!_routes.TryGetValue(carId, out var route)) return false;
                for (int i = 0; i < route.Length - 1; i++)
                {
                    if (route[i] != current) continue;
                    next = route[i + 1];
                    Vector2Int delta = next - current;
                    if (delta == Vector2Int.right) dir = Dir.E;
                    else if (delta == Vector2Int.left) dir = Dir.W;
                    else if (delta == Vector2Int.up) dir = Dir.N;
                    else if (delta == Vector2Int.down) dir = Dir.S;
                    else Assert.Fail($"non-cardinal route step {delta}");
                    return true;
                }
                return false;
            }
        }

        static RoadQueueNetwork Build(out FakeDeviceState devices, int moves = 2)
        {
            var cfg = SimConfig.Default();
            cfg.QueueServicePerTick = 1;
            cfg.QueueCapacityPerTile = 4;
            cfg.HighwayMovesPerTick = moves;
            var grid = new CityGrid(6, 1);
            for (int x = 0; x < 6; x++) grid.Place(V(x, 0), TileType.Road);
            devices = new FakeDeviceState();
            for (int x = 1; x <= 4; x++) devices.AddHighway(V(x, 0));
            var q = new RoadQueueNetwork(6, 1, cfg);
            q.RebuildTopology(grid, devices);
            return q;
        }

        [Test]
        public void ContinuousHighway_AdvancesTwoTilesInOneTick()
        {
            RoadQueueNetwork q = Build(out _);
            var routes = new Routes();
            routes.Add(1, V(1, 0), V(2, 0), V(3, 0), V(4, 0), V(5, 0));
            Assert.IsTrue(q.TryEnqueue(V(1, 0), Dir.E, 1));

            q.Step(routes);

            Assert.IsTrue(q.TryLocateCar(1, out Vector2Int tile, out _, out _));
            Assert.AreEqual(V(3, 0), tile);
        }

        [Test]
        public void HighwayEntry_ConsumesWholeTickBeforeAcceleration()
        {
            RoadQueueNetwork q = Build(out _);
            var routes = new Routes();
            routes.Add(2, V(0, 0), V(1, 0), V(2, 0), V(3, 0));
            Assert.IsTrue(q.TryEnqueue(V(0, 0), Dir.E, 2));

            q.Step(routes);

            Assert.IsTrue(q.TryLocateCar(2, out Vector2Int tile, out _, out _));
            Assert.AreEqual(V(1, 0), tile);
        }

        [Test]
        public void HighwayExit_DoesNotUseSecondMoveForTransition()
        {
            RoadQueueNetwork q = Build(out _);
            var routes = new Routes();
            routes.Add(3, V(3, 0), V(4, 0), V(5, 0));
            Assert.IsTrue(q.TryEnqueue(V(3, 0), Dir.E, 3));

            q.Step(routes);

            Assert.IsTrue(q.TryLocateCar(3, out Vector2Int tile, out _, out _));
            Assert.AreEqual(V(4, 0), tile);
        }

        [Test]
        public void NormalRoad_RemainsOneTilePerTick()
        {
            RoadQueueNetwork q = Build(out FakeDeviceState devices);
            devices.ClearHighways();
            var grid = new CityGrid(6, 1);
            for (int x = 0; x < 6; x++) grid.Place(V(x, 0), TileType.Road);
            q.RebuildTopology(grid, devices);
            var routes = new Routes();
            routes.Add(4, V(1, 0), V(2, 0), V(3, 0));
            Assert.IsTrue(q.TryEnqueue(V(1, 0), Dir.E, 4));

            q.Step(routes);

            Assert.IsTrue(q.TryLocateCar(4, out Vector2Int tile, out _, out _));
            Assert.AreEqual(V(2, 0), tile);
        }
    }
}
