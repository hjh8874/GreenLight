using System.Collections.Generic;
using CityFlow.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Sim.Tests
{
    public class RoadRoutePlanningServiceTests
    {
        private static Vector2Int V(int x, int y) =>
            new Vector2Int(x, y);

        private static SimConfig CreateConfig()
        {
            SimConfig config = SimConfig.Default();
            config.GridWidth = 6;
            config.GridHeight = 3;
            config.AutoDetectSignals = false;
            config.DemandChoicePool = 1;
            return config;
        }

        [Test]
        public void TryPlanRoadRoute_ReturnsConnectedRoadTiles()
        {
            SimConfig config = CreateConfig();
            var engine = new SimEngine(config, new SimEventHub());
            for (int x = 0; x <= 3; x++)
            {
                Assert.IsTrue(engine.Place(V(x, 1), TileType.Road));
            }

            var service = (IRoadRoutePlanningService)engine;

            Assert.IsTrue(service.TryPlanRoadRoute(
                V(0, 1),
                V(3, 1),
                out RoadRoutePlan route));
            CollectionAssert.AreEqual(
                new[] { V(0, 1), V(1, 1), V(2, 1), V(3, 1) },
                route.Tiles);
            Assert.AreEqual(V(0, 1), route.Origin);
            Assert.AreEqual(V(3, 1), route.Destination);
        }

        [Test]
        public void TryPlanRoadRoute_UsesLatestTrafficRules()
        {
            SimConfig config = CreateConfig();
            var engine = new SimEngine(config, new SimEventHub());
            for (int x = 0; x <= 3; x++)
            {
                Assert.IsTrue(engine.Place(V(x, 1), TileType.Road));
            }

            var service = (IRoadRoutePlanningService)engine;
            Assert.IsTrue(service.TryPlanRoadRoute(
                V(3, 1),
                V(0, 1),
                out _));
            Assert.IsTrue(engine.TryPlaceOneway(V(1, 1), Vector2Int.right));

            Assert.IsFalse(service.TryPlanRoadRoute(
                V(3, 1),
                V(0, 1),
                out _));
            Assert.IsTrue(service.TryPlanRoadRoute(
                V(0, 1),
                V(3, 1),
                out _));
        }

        [Test]
        public void RoadRoutePlan_OwnsACopyOfInputTiles()
        {
            var source = new List<Vector2Int>
            {
                V(1, 1),
                V(2, 1)
            };
            var route = new RoadRoutePlan(source);

            source[0] = V(5, 2);
            source.Clear();

            Assert.AreEqual(2, route.TileCount);
            Assert.AreEqual(V(1, 1), route.Origin);
            Assert.AreEqual(V(2, 1), route.Destination);
        }
    }

    // Unity integration: run as an EditMode test after script compilation.
}
