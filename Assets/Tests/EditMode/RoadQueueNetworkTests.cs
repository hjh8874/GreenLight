using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;
using CityFlow.Sim;

namespace CityFlow.Sim.Tests
{
    public class RoadQueueNetworkTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static SimConfig Cfg()
        {
            var c = SimConfig.Default();
            c.QueueCapacityPerTile = 4;
            c.QueueServicePerTick = 1;
            return c;
        }

        private sealed class FakeRouteProvider : ICarRouteProvider
        {
            private sealed class Route
            {
                public Vector2Int[] Tiles;
                public bool DestinationAtEnd;
            }

            private readonly Dictionary<int, Route> _routes = new();

            public void AddRoute(
                int carId,
                bool destinationAtEnd,
                params Vector2Int[] tiles)
            {
                _routes.Add(carId, new Route
                {
                    Tiles = tiles,
                    DestinationAtEnd = destinationAtEnd
                });
            }

            public bool TryGetNextTile(
                int carId,
                Vector2Int current,
                out Vector2Int next,
                out Dir entryDirAtNext)
            {
                next = default;
                entryDirAtNext = default;
                if (!_routes.TryGetValue(carId, out Route route))
                {
                    return false;
                }

                for (int i = 0; i < route.Tiles.Length - 1; i++)
                {
                    if (route.Tiles[i] != current)
                    {
                        continue;
                    }

                    next = route.Tiles[i + 1];
                    entryDirAtNext = DirectionOf(next - current);
                    return true;
                }

                return false;
            }

            public bool IsDestination(int carId, Vector2Int tile)
            {
                return _routes.TryGetValue(carId, out Route route)
                    && route.DestinationAtEnd
                    && route.Tiles.Length > 0
                    && route.Tiles[route.Tiles.Length - 1] == tile;
            }

            private static Dir DirectionOf(Vector2Int delta)
            {
                if (delta == Vector2Int.up) return Dir.N;
                if (delta == Vector2Int.right) return Dir.E;
                if (delta == Vector2Int.down) return Dir.S;
                if (delta == Vector2Int.left) return Dir.W;
                Assert.Fail($"직교 인접 스텝이 아님: {delta}");
                return default;
            }
        }

        [Test]
        public void Enqueue_UpToCapacity_ThenRejects()
        {
            var q = new RoadQueueNetwork(5, 5, Cfg());
            for (int i = 0; i < 4; i++)
            {
                Assert.IsTrue(
                    q.TryEnqueue(V(2, 2), Dir.E, carId: i),
                    $"{i}번째 인큐");
            }

            Assert.IsFalse(
                q.TryEnqueue(V(2, 2), Dir.E, 99),
                "용량 초과 거부");
            Assert.AreEqual(4, q.QueueCount(V(2, 2), Dir.E));
            Assert.AreEqual(0, q.CarAtHead(V(2, 2), Dir.E), "FIFO 머리");
        }

        [Test]
        public void Occupancy_IsMaxOverDirections()
        {
            var q = new RoadQueueNetwork(5, 5, Cfg());
            q.TryEnqueue(V(1, 1), Dir.N, 0);
            q.TryEnqueue(V(1, 1), Dir.N, 1);
            q.TryEnqueue(V(1, 1), Dir.E, 2);

            Assert.AreEqual(
                0.5f,
                q.MaxOccupancy01(V(1, 1)),
                1e-4f,
                "N큐 2/4가 최대");
        }

        [Test]
        public void DirectionQueues_AreIndependent()
        {
            var q = new RoadQueueNetwork(5, 5, Cfg());
            for (int i = 0; i < 4; i++)
            {
                q.TryEnqueue(V(3, 3), Dir.N, i);
            }

            Assert.IsTrue(
                q.TryEnqueue(V(3, 3), Dir.S, 10),
                "다른 방향 큐는 독립");
        }

        [Test]
        public void Step_StraightFiveTiles_ArrivesAfterFiveTicks()
        {
            var q = new RoadQueueNetwork(5, 1, Cfg());
            var routes = new FakeRouteProvider();
            routes.AddRoute(
                7,
                destinationAtEnd: true,
                V(0, 0), V(1, 0), V(2, 0), V(3, 0), V(4, 0));
            Assert.IsTrue(q.TryEnqueue(V(0, 0), Dir.E, 7));

            for (int tick = 1; tick <= 4; tick++)
            {
                StepResult result = q.Step(routes);
                Assert.AreEqual(0, result.Arrivals, $"tick {tick} 조기 도착 금지");
                Assert.AreEqual(7, q.CarAtHead(V(tick, 0), Dir.E));
            }

            StepResult arrival = q.Step(routes);
            Assert.AreEqual(1, arrival.Arrivals);
            Assert.AreEqual(-1, q.CarAtHead(V(4, 0), Dir.E));
        }

        [Test]
        public void Step_FullDownstream_WaitsUntilHeadDrains()
        {
            SimConfig cfg = Cfg();
            cfg.QueueCapacityPerTile = 4;
            var q = new RoadQueueNetwork(3, 1, cfg);
            var routes = new FakeRouteProvider();
            routes.AddRoute(0, true, V(0, 0), V(1, 0), V(2, 0));
            Assert.IsTrue(q.TryEnqueue(V(0, 0), Dir.E, 0));

            for (int id = 10; id < 14; id++)
            {
                routes.AddRoute(id, true, V(1, 0));
                Assert.IsTrue(q.TryEnqueue(V(1, 0), Dir.E, id));
            }

            q.Step(routes);
            Assert.AreEqual(0, q.CarAtHead(V(0, 0), Dir.E), "만석이면 상류 대기");
            Assert.AreEqual(3, q.QueueCount(V(1, 0), Dir.E), "하류 머리 1대 드레인");

            q.Step(routes);
            Assert.AreEqual(-1, q.CarAtHead(V(0, 0), Dir.E), "공간이 생긴 다음 틱에 진행");
            Assert.AreEqual(3, q.QueueCount(V(1, 0), Dir.E), "이동 차가 하류 FIFO에 보존");
        }

        [Test]
        public void Step_BlockedEnd_SpillsBackAcrossTwoUpstreamTiles()
        {
            SimConfig cfg = Cfg();
            cfg.QueueCapacityPerTile = 2;
            var q = new RoadQueueNetwork(3, 1, cfg);
            var routes = new FakeRouteProvider();

            for (int id = 0; id < 2; id++)
            {
                routes.AddRoute(id, destinationAtEnd: false, V(2, 0));
                Assert.IsTrue(q.TryEnqueue(V(2, 0), Dir.E, id));
            }

            for (int id = 2; id < 6; id++)
            {
                routes.AddRoute(id, true, V(0, 0), V(1, 0), V(2, 0));
                Assert.IsTrue(q.TryEnqueue(V(0, 0), Dir.E, id));
                q.Step(routes);
            }

            Assert.AreEqual(2, q.QueueCount(V(2, 0), Dir.E), "막힌 끝 타일 만석");
            Assert.AreEqual(2, q.QueueCount(V(1, 0), Dir.E), "첫 상류까지 스필백");
            Assert.AreEqual(2, q.QueueCount(V(0, 0), Dir.E), "둘째 상류까지 스필백");
        }

        [Test]
        public void Step_SameSetup_ProducesIdenticalStateEveryTick()
        {
            SimConfig cfg = Cfg();
            cfg.QueueCapacityPerTile = 2;
            var first = BuildDeterminismScenario(cfg, out FakeRouteProvider firstRoutes);
            var second = BuildDeterminismScenario(cfg, out FakeRouteProvider secondRoutes);

            for (int tick = 0; tick < 8; tick++)
            {
                StepResult firstResult = first.Step(firstRoutes);
                StepResult secondResult = second.Step(secondRoutes);
                Assert.AreEqual(firstResult.Arrivals, secondResult.Arrivals, $"tick {tick} 도착");

                for (int x = 0; x < 4; x++)
                {
                    foreach (Dir dir in new[] { Dir.N, Dir.E, Dir.S, Dir.W })
                    {
                        Assert.AreEqual(
                            first.QueueCount(V(x, 0), dir),
                            second.QueueCount(V(x, 0), dir),
                            $"tick {tick}, tile {x}, dir {dir} count");
                        Assert.AreEqual(
                            first.CarAtHead(V(x, 0), dir),
                            second.CarAtHead(V(x, 0), dir),
                            $"tick {tick}, tile {x}, dir {dir} head");
                    }
                }
            }
        }

        private static RoadQueueNetwork BuildDeterminismScenario(
            SimConfig cfg,
            out FakeRouteProvider routes)
        {
            var q = new RoadQueueNetwork(4, 1, cfg);
            routes = new FakeRouteProvider();
            for (int id = 0; id < 4; id++)
            {
                routes.AddRoute(id, true, V(0, 0), V(1, 0), V(2, 0), V(3, 0));
            }

            q.TryEnqueue(V(0, 0), Dir.E, 0);
            q.TryEnqueue(V(0, 0), Dir.E, 1);
            q.TryEnqueue(V(1, 0), Dir.E, 2);
            q.TryEnqueue(V(2, 0), Dir.E, 3);
            return q;
        }

        [Test]
        public void Step_IntersectionExitFull_DoesNotEnterIntersection()
        {
            SimConfig cfg = Cfg();
            cfg.QueueCapacityPerTile = 1;
            cfg.GridlockValveTicks = 8;
            var q = new RoadQueueNetwork(5, 3, cfg);
            CityGrid grid = BuildCrossIntersection();
            q.RebuildTopology(grid, new FakeDeviceState());

            var routes = new FakeRouteProvider();
            routes.AddRoute(0, true, V(1, 1), V(2, 1), V(3, 1));
            routes.AddRoute(10, destinationAtEnd: false, V(3, 1));
            Assert.IsTrue(q.TryEnqueue(V(1, 1), Dir.E, 0));
            Assert.IsTrue(q.TryEnqueue(V(3, 1), Dir.E, 10));

            q.Step(routes);

            Assert.AreEqual(0, q.CarAtHead(V(1, 1), Dir.E), "출구가 차면 진입 전 대기");
            Assert.AreEqual(0, q.QueueCount(V(2, 1), Dir.E), "교차로 내부 점유 금지");
        }

        [Test]
        public void Step_FullCycle_ValveActivatesOnConfiguredTick()
        {
            SimConfig cfg = Cfg();
            cfg.QueueCapacityPerTile = 1;
            cfg.GridlockValveTicks = 8;
            RoadQueueNetwork q = BuildFullCycle(cfg, out FakeRouteProvider routes);

            for (int tick = 1; tick < cfg.GridlockValveTicks; tick++)
            {
                StepResult beforeThreshold = q.Step(routes);
                Assert.AreEqual(
                    0,
                    beforeThreshold.ValveActivations,
                    $"tick {tick} 조기 밸브 금지");
            }

            StepResult atThreshold = q.Step(routes);
            Assert.Greater(atThreshold.ValveActivations, 0, "설정 틱에 순환 데드락 탈출");
        }

        [Test]
        public void Step_ValveActivation_PreservesTotalCarCount()
        {
            SimConfig cfg = Cfg();
            cfg.QueueCapacityPerTile = 1;
            cfg.GridlockValveTicks = 8;
            RoadQueueNetwork q = BuildFullCycle(cfg, out FakeRouteProvider routes);
            int before = TotalCars(q, width: 2, height: 2);

            int activations = 0;
            for (int tick = 0; tick < cfg.GridlockValveTicks; tick++)
            {
                activations += q.Step(routes).ValveActivations;
            }

            Assert.Greater(activations, 0);
            Assert.AreEqual(before, TotalCars(q, width: 2, height: 2), "밸브 후 증발·복제 금지");
        }

        private static CityGrid BuildCrossIntersection()
        {
            var grid = new CityGrid(5, 3);
            Assert.IsTrue(grid.Place(V(2, 1), TileType.Road));
            Assert.IsTrue(grid.Place(V(1, 1), TileType.Road));
            Assert.IsTrue(grid.Place(V(3, 1), TileType.Road));
            Assert.IsTrue(grid.Place(V(2, 0), TileType.Road));
            Assert.IsTrue(grid.Place(V(2, 2), TileType.Road));
            Assert.IsTrue(grid.IsIntersection(V(2, 1)));
            return grid;
        }

        private static RoadQueueNetwork BuildFullCycle(
            SimConfig cfg,
            out FakeRouteProvider routes)
        {
            var q = new RoadQueueNetwork(2, 2, cfg);
            routes = new FakeRouteProvider();
            Vector2Int a = V(0, 0);
            Vector2Int b = V(1, 0);
            Vector2Int c = V(1, 1);
            Vector2Int d = V(0, 1);

            routes.AddRoute(0, false, a, b, c, d, a);
            routes.AddRoute(1, false, b, c, d, a, b);
            routes.AddRoute(2, false, c, d, a, b, c);
            routes.AddRoute(3, false, d, a, b, c, d);

            // 큐 키는 다음 진행 방향이 아니라 현재 타일에 들어온 방향이다.
            Assert.IsTrue(q.TryEnqueue(a, Dir.S, 0));
            Assert.IsTrue(q.TryEnqueue(b, Dir.E, 1));
            Assert.IsTrue(q.TryEnqueue(c, Dir.N, 2));
            Assert.IsTrue(q.TryEnqueue(d, Dir.W, 3));
            return q;
        }

        private static int TotalCars(RoadQueueNetwork q, int width, int height)
        {
            int total = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    total += q.QueueCount(V(x, y), Dir.N);
                    total += q.QueueCount(V(x, y), Dir.E);
                    total += q.QueueCount(V(x, y), Dir.S);
                    total += q.QueueCount(V(x, y), Dir.W);
                }
            }

            return total;
        }
    }
}
