using System.Collections.Generic;
using CityFlow.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Sim.Tests
{
    public sealed class ChunkedRoutePlannerTests
    {
        [Test]
        public void Search_AcrossThreeRegions_ComposesContinuousFullPath()
        {
            CityGrid grid = CreateHorizontalRoad(60, 5, 2);
            var planner = new RoutePlanner(grid.Width, grid.Height);

            List<Vector2Int> path = planner.Search(
                grid,
                V(0, 2),
                V(59, 2),
                Config());

            Assert.NotNull(path);
            Assert.AreEqual(60, path.Count);
            Assert.AreEqual(V(0, 2), path[0]);
            Assert.AreEqual(V(59, 2), path[path.Count - 1]);
            AssertContinuous(path);
            AssertBoundedDiagnostics(planner, expectedRegionCount: 3);
        }

        [Test]
        public void Search_TwoHundredByTwoHundred_EndToEndUsesBoundedSegments()
        {
            var grid = new CityGrid(200, 200);
            for (int x = 0; x < 200; x++)
            {
                grid.Place(V(x, 0), TileType.Road);
            }

            for (int y = 1; y < 200; y++)
            {
                grid.Place(V(199, y), TileType.Road);
            }

            var planner = new RoutePlanner(grid.Width, grid.Height);
            List<Vector2Int> path = planner.Search(
                grid,
                V(0, 0),
                V(199, 199),
                Config());

            Assert.NotNull(path);
            Assert.AreEqual(399, path.Count);
            AssertContinuous(path);
            RouteSearchDiagnostics diagnostics =
                planner.LastChunkedSearchDiagnostics;
            Assert.AreEqual(100, diagnostics.RegionCount);
            Assert.LessOrEqual(diagnostics.MaxLocalTileCount, 400);
            Assert.GreaterOrEqual(diagnostics.VisitedRegionCount, 19);
        }

        [Test]
        public void Search_MultiplePortals_UsesReachablePortal()
        {
            var grid = new CityGrid(40, 20);
            grid.Place(V(19, 2), TileType.Road);
            grid.Place(V(20, 2), TileType.Road);
            for (int x = 0; x < 40; x++)
            {
                grid.Place(V(x, 15), TileType.Road);
            }

            var planner = new RoutePlanner(grid.Width, grid.Height);
            List<Vector2Int> path = planner.Search(
                grid,
                V(0, 15),
                V(39, 15),
                Config());

            Assert.NotNull(path);
            CollectionAssert.DoesNotContain(path, V(19, 2));
            CollectionAssert.Contains(path, V(19, 15));
            CollectionAssert.Contains(path, V(20, 15));
            AssertContinuous(path);
        }

        [Test]
        public void Search_DisconnectedRegions_ReturnsNull()
        {
            var grid = new CityGrid(40, 5);
            for (int x = 0; x < 19; x++)
            {
                grid.Place(V(x, 2), TileType.Road);
            }

            for (int x = 21; x < 40; x++)
            {
                grid.Place(V(x, 2), TileType.Road);
            }

            var planner = new RoutePlanner(grid.Width, grid.Height);
            Assert.IsNull(planner.Search(
                grid,
                V(0, 2),
                V(39, 2),
                Config()));
        }

        [Test]
        public void Search_AfterBoundaryRoadChanges_RebuildsPortalIndex()
        {
            var grid = new CityGrid(40, 5);
            for (int x = 0; x < 40; x++)
            {
                if (x != 20)
                {
                    grid.Place(V(x, 2), TileType.Road);
                }
            }

            var planner = new RoutePlanner(grid.Width, grid.Height);
            Assert.IsNull(planner.Search(
                grid,
                V(0, 2),
                V(39, 2),
                Config()));

            grid.Place(V(20, 2), TileType.Road);
            Assert.NotNull(planner.Search(
                grid,
                V(0, 2),
                V(39, 2),
                Config()));

            grid.Remove(V(20, 2));
            Assert.IsNull(planner.Search(
                grid,
                V(0, 2),
                V(39, 2),
                Config()));
        }

        [Test]
        public void Search_OnewayAtPortal_AllowsOnlyConfiguredDirection()
        {
            CityGrid grid = CreateHorizontalRoad(40, 5, 2);
            var planner = new RoutePlanner(grid.Width, grid.Height);
            var oneways = new Dictionary<Vector2Int, Vector2Int>
            {
                [V(19, 2)] = Vector2Int.right
            };

            Assert.NotNull(planner.Search(
                grid,
                V(0, 2),
                V(39, 2),
                Config(),
                oneways));
            Assert.IsNull(planner.Search(
                grid,
                V(39, 2),
                V(0, 2),
                Config(),
                oneways));
        }

        [Test]
        public void Search_TurnSignAtPortal_PreservesIncomingDirection()
        {
            var grid = new CityGrid(40, 5);
            grid.Place(V(19, 0), TileType.Road);
            grid.Place(V(19, 1), TileType.Road);
            grid.Place(V(20, 1), TileType.Road);
            grid.Place(V(21, 1), TileType.Road);
            var planner = new RoutePlanner(grid.Width, grid.Height);

            var rightOnly = new Dictionary<Vector2Int, TurnMode>
            {
                [V(19, 1)] = TurnMode.RightOnly
            };
            Assert.NotNull(planner.Search(
                grid,
                V(19, 0),
                V(21, 1),
                Config(),
                null,
                rightOnly));

            var leftOnly = new Dictionary<Vector2Int, TurnMode>
            {
                [V(19, 1)] = TurnMode.LeftOnly
            };
            Assert.IsNull(planner.Search(
                grid,
                V(19, 0),
                V(21, 1),
                Config(),
                null,
                leftOnly));
        }

        [Test]
        public void Search_HighwayAcrossRegions_IsAHighLevelPortal()
        {
            var grid = new CityGrid(60, 5);
            grid.Place(V(1, 1), TileType.Road);
            grid.Place(V(58, 1), TileType.Road);
            var planner = new RoutePlanner(grid.Width, grid.Height);
            var highways = new[]
            {
                new HighwayLink(V(1, 1), V(58, 1))
            };

            List<Vector2Int> path = planner.Search(
                grid,
                V(1, 1),
                V(58, 1),
                Config(),
                highways);

            CollectionAssert.AreEqual(
                new[] { V(1, 1), V(58, 1) },
                path);
            AssertBoundedDiagnostics(planner, expectedRegionCount: 3);
        }

        private static CityGrid CreateHorizontalRoad(
            int width,
            int height,
            int y)
        {
            var grid = new CityGrid(width, height);
            for (int x = 0; x < width; x++)
            {
                grid.Place(V(x, y), TileType.Road);
            }

            return grid;
        }

        private static SimConfig Config()
        {
            SimConfig config = SimConfig.Default();
            config.GridWidth = 200;
            config.GridHeight = 200;
            config.RoutingCongestionWeight = 0f;
            return config;
        }

        private static Vector2Int V(int x, int y) =>
            new Vector2Int(x, y);

        private static void AssertContinuous(IReadOnlyList<Vector2Int> path)
        {
            for (int i = 1; i < path.Count; i++)
            {
                int distance = Mathf.Abs(path[i].x - path[i - 1].x) +
                    Mathf.Abs(path[i].y - path[i - 1].y);
                Assert.AreEqual(
                    1,
                    distance,
                    $"Path is discontinuous at index {i}.");
            }
        }

        private static void AssertBoundedDiagnostics(
            RoutePlanner planner,
            int expectedRegionCount)
        {
            RouteSearchDiagnostics diagnostics =
                planner.LastChunkedSearchDiagnostics;
            Assert.IsTrue(diagnostics.UsedChunkedSearch);
            Assert.AreEqual(expectedRegionCount, diagnostics.RegionCount);
            Assert.Greater(diagnostics.LocalSearchCount, 0);
            Assert.LessOrEqual(diagnostics.MaxLocalTileCount, 400);
        }
    }
}
