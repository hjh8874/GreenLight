using System;
using System.Collections.Generic;
using CityFlow.Contracts;

namespace CityFlow.Sim
{
    internal sealed class TripScheduler
    {
        private sealed class PendingTrip
        {
            public PendingTrip(
                SpecialBuildingVisitTripRequest request,
                int eligibleTick)
            {
                Request = request;
                EligibleTick = Math.Max(0, eligibleTick);
            }

            public SpecialBuildingVisitTripRequest Request { get; }
            public int EligibleTick { get; private set; }

            public void MakeImmediatelyRetryable()
            {
                EligibleTick = 0;
            }
        }

        private readonly List<PendingTrip> pending = new();
        private readonly HashSet<string> knownJourneyIds = new(StringComparer.Ordinal);
        private readonly Dictionary<CommuteCar, SpecialTripJourney> active = new();
        private readonly int maxPendingTrips;
        private readonly int maxActiveTrips;

        public TripScheduler(int maxPendingTrips, int maxActiveTrips)
        {
            this.maxPendingTrips = Math.Max(1, maxPendingTrips);
            this.maxActiveTrips = Math.Max(1, maxActiveTrips);
        }

        public int PendingCount => pending.Count;
        public int ActiveCount => active.Count;

        public IEnumerable<SpecialTripJourney> ActiveJourneys => active.Values;

        public bool TryEnqueue(SpecialBuildingVisitTripRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.BuildingId) ||
                pending.Count >= maxPendingTrips)
            {
                return false;
            }

            string journeyId = CreateJourneyId(request);
            if (!knownJourneyIds.Add(journeyId))
            {
                return false;
            }

            int insertIndex = pending.FindIndex(existing =>
                Compare(request, existing.Request) < 0);
            var entry = new PendingTrip(request, 0);
            if (insertIndex < 0)
            {
                pending.Add(entry);
            }
            else
            {
                pending.Insert(insertIndex, entry);
            }

            return true;
        }

        public bool TryTakeDue(
            long currentDay,
            float currentHour,
            out SpecialBuildingVisitTripRequest request)
        {
            return TryTakeDue(currentDay, currentHour, 0, out request);
        }

        public bool TryTakeDue(
            long currentDay,
            float currentHour,
            int currentTick,
            out SpecialBuildingVisitTripRequest request)
        {
            request = default;
            if (active.Count >= maxActiveTrips)
            {
                return false;
            }

            for (int index = pending.Count - 1; index >= 0; index--)
            {
                if (pending[index].Request.Day < currentDay)
                {
                    knownJourneyIds.Remove(
                        CreateJourneyId(pending[index].Request));
                    pending.RemoveAt(index);
                }
            }

            for (int index = 0; index < pending.Count; index++)
            {
                PendingTrip entry = pending[index];
                SpecialBuildingVisitTripRequest candidate = entry.Request;
                if (candidate.Day != currentDay ||
                    candidate.ScheduledHour > currentHour ||
                    entry.EligibleTick > currentTick)
                {
                    continue;
                }

                request = candidate;
                pending.RemoveAt(index);
                return true;
            }

            return false;
        }

        public void Requeue(
            SpecialBuildingVisitTripRequest request,
            int currentTick,
            int delayTicks)
        {
            int insertIndex = pending.FindIndex(existing =>
                Compare(request, existing.Request) < 0);
            int safeDelay = Math.Max(1, delayTicks);
            int eligibleTick = currentTick > int.MaxValue - safeDelay
                ? int.MaxValue
                : Math.Max(0, currentTick) + safeDelay;
            var entry = new PendingTrip(request, eligibleTick);
            if (insertIndex < 0)
            {
                pending.Add(entry);
            }
            else
            {
                pending.Insert(insertIndex, entry);
            }
        }

        public void AllowImmediateRetry()
        {
            for (int index = 0; index < pending.Count; index++)
            {
                pending[index].MakeImmediatelyRetryable();
            }
        }

        public void Forget(SpecialBuildingVisitTripRequest request)
        {
            knownJourneyIds.Remove(CreateJourneyId(request));
        }

        public bool TryActivate(SpecialTripJourney journey)
        {
            if (journey == null || journey.Vehicle == null ||
                active.Count >= maxActiveTrips ||
                active.ContainsKey(journey.Vehicle))
            {
                return false;
            }

            active.Add(journey.Vehicle, journey);
            return true;
        }

        public bool TryGetActive(
            CommuteCar vehicle,
            out SpecialTripJourney journey)
        {
            journey = null;
            return vehicle != null &&
                active.TryGetValue(vehicle, out journey);
        }

        public bool RemoveActive(CommuteCar vehicle)
        {
            if (vehicle == null ||
                !active.TryGetValue(vehicle, out SpecialTripJourney journey))
            {
                return false;
            }

            active.Remove(vehicle);
            knownJourneyIds.Remove(CreateJourneyId(journey.Request));
            return true;
        }

        public void Clear()
        {
            pending.Clear();
            knownJourneyIds.Clear();
            active.Clear();
        }

        public static string CreateJourneyId(
            SpecialBuildingVisitTripRequest request) =>
            $"special:{request.Day}:{request.BuildingId}:" +
            $"{request.Destination.x}:{request.Destination.y}:" +
            request.VisitIndex;

        private static int Compare(
            SpecialBuildingVisitTripRequest left,
            SpecialBuildingVisitTripRequest right)
        {
            int day = left.Day.CompareTo(right.Day);
            if (day != 0)
            {
                return day;
            }

            int hour = left.ScheduledHour.CompareTo(right.ScheduledHour);
            if (hour != 0)
            {
                return hour;
            }

            int building = string.CompareOrdinal(
                left.BuildingId,
                right.BuildingId);
            if (building != 0)
            {
                return building;
            }

            int y = left.Destination.y.CompareTo(right.Destination.y);
            return y != 0
                ? y
                : left.Destination.x.CompareTo(right.Destination.x);
        }
    }

    internal sealed class SpecialTripJourney
    {
        private List<UnityEngine.Vector2Int> firstRoute;
        private List<UnityEngine.Vector2Int> secondRoute;

        public SpecialTripJourney(
            SpecialBuildingVisitTripRequest request,
            CommuteCar vehicle,
            CommuteCar routineOwner,
            UnityEngine.Vector2Int origin,
            UnityEngine.Vector2Int finalDestination,
            CarState finalRoutineState,
            List<UnityEngine.Vector2Int> firstRoute,
            List<UnityEngine.Vector2Int> secondRoute)
        {
            Request = request;
            Vehicle = vehicle;
            RoutineOwner = routineOwner;
            Origin = origin;
            FinalDestination = finalDestination;
            FinalRoutineState = finalRoutineState;
            this.firstRoute = firstRoute ?? throw new ArgumentNullException(nameof(firstRoute));
            this.secondRoute = secondRoute;
            CurrentLegIndex = 0;
            CurrentTrip = CreateTrip(
                0,
                origin,
                request.Destination);
            CurrentTrip.TryBeginRouting();
            CurrentTrip.TryBeginDriving();
        }

        public SpecialBuildingVisitTripRequest Request { get; }
        public CommuteCar Vehicle { get; }
        public CommuteCar RoutineOwner { get; }
        public UnityEngine.Vector2Int Origin { get; }
        public UnityEngine.Vector2Int FinalDestination { get; }
        public CarState FinalRoutineState { get; }
        public int CurrentLegIndex { get; private set; }
        public VehicleTrip CurrentTrip { get; private set; }
        public int ViewRouteIndex { get; set; } = -1;
        public List<UnityEngine.Vector2Int> CurrentRoute =>
            CurrentLegIndex == 0 ? firstRoute : secondRoute;
        public bool HasContinuation => secondRoute != null && secondRoute.Count > 0;

        public VehicleTripSnapshot CompleteCurrentLeg()
        {
            CurrentTrip.TryArrive();
            return CurrentTrip.CreateSnapshot();
        }

        public bool TryBeginContinuation()
        {
            if (CurrentLegIndex != 0 || !HasContinuation)
            {
                return false;
            }

            CurrentLegIndex = 1;
            CurrentTrip = CreateTrip(
                1,
                Request.Destination,
                FinalDestination);
            CurrentTrip.TryBeginRouting();
            CurrentTrip.TryBeginDriving();
            return true;
        }

        public void ReplaceRoutes(
            List<UnityEngine.Vector2Int> currentRoute,
            List<UnityEngine.Vector2Int> continuationRoute)
        {
            if (CurrentLegIndex == 0)
            {
                firstRoute = currentRoute;
                secondRoute = continuationRoute;
            }
            else
            {
                secondRoute = currentRoute;
            }
        }

        private VehicleTrip CreateTrip(
            int legIndex,
            UnityEngine.Vector2Int from,
            UnityEngine.Vector2Int to)
        {
            string journeyId = TripScheduler.CreateJourneyId(Request);
            return new VehicleTrip(
                $"{journeyId}:leg:{legIndex}",
                journeyId,
                legIndex,
                from,
                to,
                VehicleTripPurpose.SpecialBuildingVisit,
                Request.BuildingId,
                // 보상은 방문 leg(0: 집→건물)에만 싣는다. 귀가는 0 — 통근 outbound 규칙과 동일.
                legIndex == 0 ? Request.RewardCoins : 0);
        }
    }
}
