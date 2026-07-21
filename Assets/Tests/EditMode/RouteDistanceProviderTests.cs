using CityFlow.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Sim.Tests
{
    public class RouteDistanceProviderTests
    {
        private static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        [Test]
        public void SimEngine_ReportsDeliveredWeightedRouteDistance()
        {
            SimConfig config = SimConfig.Default();
            config.GridWidth = 5;
            config.GridHeight = 2;
            config.RoadCapacity = 10f;

            var engine = new SimEngine(config, new SimEventHub());
            for (int x = 0; x <= 4; x++)
            {
                engine.Place(V(x, 0), TileType.Road);
            }

            engine.Place(V(0, 1), TileType.House);
            engine.Place(V(4, 1), TileType.Office);
            engine.Tick(config.TickInterval);

            var provider = (IRouteDistanceProvider)engine;

            Assert.IsTrue(provider.TryGetAverageRouteDistance(
                V(4, 1),
                out float destinationDistance));
            Assert.AreEqual(4f, destinationDistance, 1e-5f);

            Assert.IsTrue(provider.TryGetCityAverageRouteDistance(
                out float cityDistance));
            Assert.AreEqual(4f, cityDistance, 1e-5f);
        }

        [Test]
        public void SimEngine_ReturnsFalseWhenDestinationHasNoDeliveredFlow()
        {
            SimConfig config = SimConfig.Default();
            config.GridWidth = 5;
            config.GridHeight = 2;

            var provider = (IRouteDistanceProvider)new SimEngine(
                config,
                new SimEventHub());

            Assert.IsFalse(provider.TryGetAverageRouteDistance(
                V(4, 1),
                out float destinationDistance));
            Assert.AreEqual(0f, destinationDistance);
            Assert.IsFalse(provider.TryGetCityAverageRouteDistance(out _));
        }

    }
}
