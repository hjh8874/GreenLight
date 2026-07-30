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
            config.GridWidth = 7;
            config.GridHeight = 3;
            config.RoadCapacity = 10f;
            // 채용 램프는 **게임시간** 기준이라 1틱에 도는 게임시간이 하루 길이에 반비례한다
            // (1틱 게임시간 = TickInterval / DayLengthSeconds × 24). 100슬롯/h 은 하루 120초일 때만
            // 1틱에 정원이 찼고, 하루가 720초가 되자 0.8슬롯 → 0 이 되어 차가 안 생겼다.
            // 이 테스트의 관심사는 "배달된 가중 거리를 보고하나"뿐이므로 하루 길이와 무관하게 만든다.
            config.CompanyHiringSlotsPerGameHour = 100000f;

            var engine = new SimEngine(config, new SimEventHub());
            for (int x = 1; x <= 5; x++)
            {
                engine.Place(V(x, 2), TileType.Road);
            }

            engine.Place(V(0, 0), TileType.House);
            engine.Place(V(5, 0), TileType.Office);
            engine.Tick(config.TickInterval);

            var provider = (IRouteDistanceProvider)engine;

            Assert.IsTrue(provider.TryGetAverageRouteDistance(
                V(5, 0),
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
