using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public class HighwayRouteTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static SimConfig Cfg(float highwayFactor = 0.5f)
        {
            var c = SimConfig.Default();
            c.GridWidth = 9;
            c.GridHeight = 3;
            c.RoutingCongestionWeight = 0f;
            c.HighwayRouteCostFactor = highwayFactor;
            return c;
        }

        static CityGrid ParallelCorridors()
        {
            var g = new CityGrid(9, 3);
            for (int x = 0; x < 9; x++)
            {
                g.Place(V(x, 0), TileType.Road);
                g.Place(V(x, 2), TileType.Road);
            }
            g.Place(V(0, 1), TileType.Road);
            g.Place(V(8, 1), TileType.Road);
            return g;
        }

        static HashSet<Vector2Int> BottomHighway()
        {
            var result = new HashSet<Vector2Int>();
            for (int x = 0; x < 9; x++) result.Add(V(x, 0));
            return result;
        }

        [Test]
        public void EdgeAccess_AllowsEndpoints_BlocksInteriorSideAccess()
        {
            var highways = BottomHighway();

            Assert.IsTrue(RoutePlanner.IsHighwayEdgeAllowed(V(0, 1), V(0, 0), highways));
            Assert.IsTrue(RoutePlanner.IsHighwayEdgeAllowed(V(8, 0), V(8, 1), highways));
            Assert.IsFalse(RoutePlanner.IsHighwayEdgeAllowed(V(4, 1), V(4, 0), highways));
            Assert.IsTrue(RoutePlanner.IsHighwayEdgeAllowed(V(3, 0), V(4, 0), highways));
        }

        [Test]
        public void Search_ChoosesLongerHighwayWhenTravelCostIsLower()
        {
            CityGrid g = ParallelCorridors();
            var planner = new RoutePlanner(g.Width, g.Height);
            var highways = BottomHighway();

            List<Vector2Int> route = planner.Search(
                g, V(0, 2), V(8, 2), Cfg(0.4f), null, null, highways);

            Assert.IsNotNull(route);
            Assert.IsTrue(route.Contains(V(4, 0)), "longer lower corridor should win on travel cost");
            Assert.Greater(route.Count, 9);
        }

        [Test]
        public void Search_InteriorRampIsNotUsedEvenWhenItWouldBeShortest()
        {
            CityGrid g = ParallelCorridors();
            g.Place(V(4, 1), TileType.Road);
            var planner = new RoutePlanner(g.Width, g.Height);
            var highways = BottomHighway();

            List<Vector2Int> route = planner.Search(
                g, V(4, 2), V(8, 0), Cfg(0.4f), null, null, highways);

            Assert.IsNotNull(route);
            Assert.IsFalse(ContainsEdge(route, V(4, 1), V(4, 0)), "interior side ramp must be filtered");
        }

        [Test]
        public void Search_NoHighways_MatchesLegacyPathExactly()
        {
            CityGrid g = ParallelCorridors();
            var planner = new RoutePlanner(g.Width, g.Height);
            SimConfig cfg = Cfg();

            List<Vector2Int> legacy = planner.Search(g, V(0, 2), V(8, 2), cfg);
            List<Vector2Int> withEmptySet = planner.Search(
                g, V(0, 2), V(8, 2), cfg, null, null, new HashSet<Vector2Int>());

            CollectionAssert.AreEqual(legacy, withEmptySet);
        }

        static bool ContainsEdge(IReadOnlyList<Vector2Int> route, Vector2Int a, Vector2Int b)
        {
            for (int i = 1; i < route.Count; i++)
                if ((route[i - 1] == a && route[i] == b) || (route[i - 1] == b && route[i] == a))
                    return true;
            return false;
        }
    }
}
