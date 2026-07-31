using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    // 도시 구성·설정은 CarSimTests 의 하니스(BuildSpecialVisitCity·Cfg·V)를 재사용한다.
    public class SpecialVisitRewardTests
    {
        [Test]
        public void SpecialVisitArrival_PaysRewardCoins_CommuteStaysCoinPerTrip()
        {
            SimConfig config = CarSimTests.Cfg();
            config.MaxSimCars = 16;
            config.MaxPendingVehicleTrips = 16;
            config.MaxConcurrentSpecialTrips = 2;
            CarSimTests.BuildSpecialVisitCity(
                config,
                out CityGrid grid,
                out RoadNetwork roads,
                out DemandMap demands,
                out RoutePlanner planner,
                out RoadQueueNetwork queues);
            var sim = new CarSim(config);
            sim.Rebuild(
                demands,
                planner,
                queues,
                grid: grid,
                roadNetwork: roads);
            var hub = new SimEventHub();
            var events = new SimEventBuffer(hub);
            var completed = new List<VehicleTripSnapshot>();
            var arrivals = new List<ArrivalEvent>();
            hub.VehicleTripArrived += message => completed.Add(message.Trip);
            hub.Arrival += message => arrivals.Add(message);

            // 통근 CoinPerTrip(10)과 다른 값 — 섞이면 즉시 드러난다
            Assert.IsTrue(sim.TryScheduleSpecialBuildingVisit(
                new SpecialBuildingVisitTripRequest(
                    "coffee-shop",
                    CarSimTests.V(6, 0),
                    1L,
                    0,
                    7f,
                    rewardCoins: 7)));

            for (int tick = 0; tick < 80; tick++)
            {
                sim.Step(1L, 7f, queues, events, null, tick);
                events.Drain();
            }

            List<VehicleTripSnapshot> specialTrips = completed.FindAll(trip =>
                trip.Purpose == VehicleTripPurpose.SpecialBuildingVisit);
            Assert.AreEqual(2, specialTrips.Count, "방문 leg + 귀가 leg 완주");
            Assert.AreEqual(7, specialTrips[0].RewardCoins, "보상은 방문 leg(0)에 실린다");
            Assert.AreEqual(0, specialTrips[1].RewardCoins, "귀가 leg 는 0");

            List<ArrivalEvent> visitPayouts = arrivals.FindAll(a => a.Coins == 7);
            List<ArrivalEvent> commutePayouts =
                arrivals.FindAll(a => a.Coins == config.CoinPerTrip);
            Assert.AreEqual(1, visitPayouts.Count, "방문 도착 = RewardCoins 정확히 1건");
            Assert.AreEqual(CarSimTests.V(6, 0), visitPayouts[0].Destination);
            Assert.AreEqual(1, commutePayouts.Count, "통근 도착 = CoinPerTrip 그대로");
            Assert.AreEqual(2, arrivals.Count, "귀가 leg 등 그 외 코인 이벤트는 없다");
        }
    }
}
