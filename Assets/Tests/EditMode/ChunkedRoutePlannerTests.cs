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
        public void PlanVehicleTrip_DirectionConstraintsCrossRegionsWithoutUTurn()
        {
            var grid = new CityGrid(45, 4);
            for (int x = 0; x < 45; x++)
            {
                grid.Place(V(x, 2), TileType.Road);
            }

            for (int x = 39; x < 45; x++)
            {
                grid.Place(V(x, 0), TileType.Road);
            }

            grid.Place(V(39, 1), TileType.Road);
            grid.Place(V(44, 1), TileType.Road);

            SimConfig config = Config();
            var network = new RoadNetwork(grid);
            var demand = new DemandMap(config);
            demand.Reassign(grid, network);
            var planner = new RoutePlanner(grid.Width, grid.Height);
            planner.Plan(demand, network, grid, config);

            List<Vector2Int> route = planner.PlanVehicleTrip(
                V(0, 2),
                V(42, 2),
                requiredFirstDirection: Vector2Int.right,
                requiredArrivalDirection: Vector2Int.left);

            Assert.NotNull(route);
            Assert.AreEqual(Vector2Int.right, route[1] - route[0]);
            Assert.AreEqual(
                Vector2Int.left,
                route[route.Count - 1] - route[route.Count - 2]);
            for (int index = 1; index < route.Count - 1; index++)
            {
                Assert.AreNotEqual(route[index - 1], route[index + 1]);
            }

            AssertBoundedDiagnostics(planner, expectedRegionCount: 3);
        }

        [Test]
        public void PlanVehicleTrip_StartPortalAppliesRequiredFirstAndIncomingDirection()
        {
            var grid = new CityGrid(40, 5);
            for (int x = 18; x <= 22; x++)
            {
                Assert.IsTrue(grid.Place(V(x, 2), TileType.Road));
            }

            SimConfig config = Config();
            var network = new RoadNetwork(grid);
            var demand = new DemandMap(config);
            demand.Reassign(grid, network);
            var planner = new RoutePlanner(grid.Width, grid.Height);
            planner.Plan(demand, network, grid, config);

            CollectionAssert.AreEqual(
                new[] { V(19, 2), V(20, 2), V(21, 2), V(22, 2) },
                planner.PlanVehicleTrip(
                    V(19, 2),
                    V(22, 2),
                    requiredFirstDirection: Vector2Int.right,
                    requiredArrivalDirection: Vector2Int.right,
                    initialIncomingDirection: Vector2Int.right));
            Assert.IsNull(planner.PlanVehicleTrip(
                V(19, 2),
                V(22, 2),
                requiredFirstDirection: Vector2Int.right,
                requiredArrivalDirection: Vector2Int.right,
                initialIncomingDirection: Vector2Int.left));
        }

        [Test]
        public void PlanVehicleTrip_DirectionConstraintsSkipLongHighwayAtEndpoint()
        {
            CityGrid grid = CreateHorizontalRoad(6, 4, 1);
            Vector2Int start = V(0, 1);
            Vector2Int goal = V(4, 1);

            SimConfig config = Config();
            var network = new RoadNetwork(grid);
            var demand = new DemandMap(config);
            demand.Reassign(grid, network);
            var planner = new RoutePlanner(grid.Width, grid.Height);
            var highways = new[] { new HighwayLink(start, V(3, 1)) };
            planner.Plan(
                demand,
                network,
                grid,
                config,
                oneways: null,
                turnSigns: null,
                highways: highways);

            List<Vector2Int> route = planner.PlanVehicleTrip(
                start,
                goal,
                requiredFirstDirection: Vector2Int.right,
                requiredArrivalDirection: Vector2Int.right);

            CollectionAssert.AreEqual(
                new[]
                {
                    V(0, 1), V(1, 1), V(2, 1), V(3, 1), V(4, 1)
                },
                route);
        }

        [Test]
        public void PlanVehicleTrip_ArrivalOnly_GoalPortalRepeatDoesNotHideValidCrossRegionRoute()
        {
            var grid = new CityGrid(40, 5);
            Vector2Int[] roads =
            {
                V(17, 1), V(18, 1), V(19, 1), V(19, 0),
                V(17, 2), V(17, 3), V(18, 3), V(19, 3),
                V(20, 0), V(20, 1), V(20, 2), V(20, 3)
            };
            for (int index = 0; index < roads.Length; index++)
            {
                Assert.IsTrue(grid.Place(roads[index], TileType.Road));
            }

            SimConfig config = Config();
            var network = new RoadNetwork(grid);
            var demand = new DemandMap(config);
            demand.Reassign(grid, network);
            var planner = new RoutePlanner(grid.Width, grid.Height);
            planner.Plan(demand, network, grid, config);

            List<Vector2Int> route = planner.PlanVehicleTrip(
                V(17, 1),
                V(19, 1),
                requiredFirstDirection: null,
                requiredArrivalDirection: Vector2Int.up);

            CollectionAssert.AreEqual(
                new[]
                {
                    V(17, 1), V(17, 2), V(17, 3), V(18, 3),
                    V(19, 3), V(20, 3), V(20, 2), V(20, 1),
                    V(20, 0), V(19, 0), V(19, 1)
                },
                route);
            Assert.AreEqual(
                Vector2Int.up,
                route[route.Count - 1] - route[route.Count - 2]);
            Assert.AreEqual(1, route.FindAll(tile => tile == V(19, 1)).Count);
            AssertBoundedDiagnostics(planner, expectedRegionCount: 2);
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
