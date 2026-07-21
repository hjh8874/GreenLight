using System.Collections.Generic;
using CityFlow.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Sim.Tests
{
    public class HighwayLinkTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        [Test]
        public void Engine_PlacesPairedRampsAndRemovesFromEitherEnd()
        {
            SimConfig cfg = SimConfig.Default();
            cfg.GridWidth = 10; cfg.GridHeight = 2; cfg.AutoDetectSignals = false;
            var engine = new SimEngine(cfg, new SimEventHub());
            for (int x = 1; x <= 8; x++) engine.Place(V(x, 0), TileType.Road);

            Assert.IsFalse(engine.CanPlaceHighway(V(1, 0), V(4, 0)), "minimum distance is five");
            Assert.IsTrue(engine.TryPlaceHighway(V(1, 0), V(7, 0)));
            Assert.AreEqual(1, engine.HighwayLinks.Count);
            Assert.IsTrue(engine.IsHighwayRamp(V(1, 0)));
            Assert.IsTrue(engine.IsHighwayRamp(V(7, 0)));
            Assert.AreEqual(150, engine.HighwayCost(V(1, 0), V(7, 0)));

            Assert.IsTrue(engine.TryRemoveHighway(V(7, 0)));
            Assert.AreEqual(0, engine.HighwayLinks.Count);
        }

        [Test]
        public void Queue_TraversesNonAdjacentRampLink()
        {
            SimConfig cfg = SimConfig.Default();
            cfg.GridWidth = 10; cfg.GridHeight = 1; cfg.QueueServicePerTick = 1;
            var grid = new CityGrid(10, 1);
            for (int x = 0; x < 10; x++) grid.Place(V(x, 0), TileType.Road);
            var devices = new FakeDeviceState();
            devices.AddHighway(V(2, 0), V(7, 0));
            var queues = new RoadQueueNetwork(10, 1, cfg);
            queues.RebuildTopology(grid, devices);
            var route = new JumpRoute(V(2, 0), V(7, 0), V(8, 0));

            Assert.IsTrue(queues.TryEnqueue(V(2, 0), Dir.E, 7));
            queues.Step(route, null, 1);
            Assert.IsTrue(queues.TryLocateCar(7, out Vector2Int tile, out _, out _, out float progress));
            Assert.AreEqual(V(2, 0), tile);
            Assert.GreaterOrEqual(progress, 0f);

            queues.Step(route, null, 2);
            queues.Step(route, null, 3);
            queues.Step(route, null, 4);
            Assert.AreEqual(7, queues.CarAtHead(V(7, 0), Dir.E));
        }

        private sealed class JumpRoute : ICarRouteProvider
        {
            readonly Vector2Int[] _tiles;
            public JumpRoute(params Vector2Int[] tiles) => _tiles = tiles;
            public bool IsDestination(int carId, Vector2Int tile) => tile == _tiles[_tiles.Length - 1];
            public bool TryGetNextTile(int carId, Vector2Int current, out Vector2Int next, out Dir entryDirAtNext)
            {
                for (int i = 0; i < _tiles.Length - 1; i++)
                    if (_tiles[i] == current)
                    {
                        next = _tiles[i + 1];
                        Vector2Int d = next - current;
                        entryDirAtNext = Mathf.Abs(d.x) >= Mathf.Abs(d.y)
                            ? (d.x >= 0 ? Dir.E : Dir.W)
                            : (d.y >= 0 ? Dir.N : Dir.S);
                        return true;
                    }
                next = default; entryDirAtNext = default; return false;
            }
        }
    }
}
