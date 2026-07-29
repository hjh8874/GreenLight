using System;
using System.Collections.Generic;
using CityFlow.Contracts;

namespace CityFlow.Sim
{
    internal sealed class CommuteTripSource
    {
        private readonly Dictionary<CommuteCar, VehicleTrip> active = new();
        private long nextSequence;

        public int ActiveCount => active.Count;

        public void SyncDepartures(
            IReadOnlyList<CommuteCar> cars,
            int rewardCoins)
        {
            if (cars == null)
            {
                return;
            }

            for (int index = 0; index < cars.Count; index++)
            {
                CommuteCar car = cars[index];
                if (car == null || car.IsTransient ||
                    car.SpecialTripReserved || active.ContainsKey(car))
                {
                    continue;
                }

                if (car.State != CarState.Outbound &&
                    car.State != CarState.Inbound)
                {
                    continue;
                }

                bool outbound = car.State == CarState.Outbound;
                UnityEngine.Vector2Int origin = outbound
                    ? car.Home
                    : car.Work;
                UnityEngine.Vector2Int destination = outbound
                    ? car.Work
                    : car.Home;
                string direction = outbound ? "out" : "return";
                string journeyId =
                    $"routine:{nextSequence++}:{car.Home.x}:{car.Home.y}:" +
                    $"{car.Work.x}:{car.Work.y}:{direction}";
                var trip = new VehicleTrip(
                    $"{journeyId}:leg:0",
                    journeyId,
                    0,
                    origin,
                    destination,
                    car.RoutinePurpose,
                    string.Empty,
                    outbound ? Math.Max(0, rewardCoins) : 0);
                trip.TryBeginRouting();
                trip.TryBeginDriving();
                active.Add(car, trip);
            }
        }

        public bool TryComplete(
            CommuteCar car,
            out VehicleTripSnapshot completed)
        {
            completed = default;
            if (car == null || !active.TryGetValue(car, out VehicleTrip trip))
            {
                return false;
            }

            trip.TryArrive();
            completed = trip.CreateSnapshot();
            active.Remove(car);
            return true;
        }

        public void ReplaceWithReturnTrip(
            CommuteCar car,
            UnityEngine.Vector2Int origin)
        {
            if (car == null)
            {
                return;
            }

            if (active.TryGetValue(car, out VehicleTrip previous))
            {
                previous.TryCancel();
                active.Remove(car);
            }

            string journeyId =
                $"routine:{nextSequence++}:{car.Home.x}:{car.Home.y}:" +
                $"{car.Work.x}:{car.Work.y}:work-lost-return";
            var replacement = new VehicleTrip(
                $"{journeyId}:leg:0",
                journeyId,
                0,
                origin,
                car.Home,
                car.RoutinePurpose,
                string.Empty,
                0);
            replacement.TryBeginRouting();
            replacement.TryBeginDriving();
            active.Add(car, replacement);
        }

        public void Prune(IReadOnlyList<CommuteCar> cars)
        {
            var live = new HashSet<CommuteCar>(cars);
            var stale = new List<CommuteCar>();
            foreach (CommuteCar car in active.Keys)
            {
                if (!live.Contains(car))
                {
                    stale.Add(car);
                }
            }

            for (int index = 0; index < stale.Count; index++)
            {
                active[stale[index]].TryCancel();
                active.Remove(stale[index]);
            }
        }

        public void Clear()
        {
            CancelActiveTrips();
            nextSequence = 0L;
        }

        public void CancelActiveTrips()
        {
            foreach (VehicleTrip trip in active.Values)
            {
                trip.TryCancel();
            }

            active.Clear();
        }
    }
}
