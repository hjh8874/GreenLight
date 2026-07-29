using System;
using UnityEngine;

namespace CityFlow.Contracts
{
    public enum VehicleTripPurpose
    {
        Commute = 0,
        School = 1,
        SpecialBuildingVisit = 2
    }

    public enum VehicleTripState
    {
        Queued = 0,
        Routing = 1,
        Driving = 2,
        Arrived = 3,
        Cancelled = 4
    }

    [Serializable]
    public sealed class VehicleTrip
    {
        [SerializeField] private string tripId;
        [SerializeField] private string journeyId;
        [SerializeField] private int legIndex;
        [SerializeField] private Vector2Int origin;
        [SerializeField] private Vector2Int destination;
        [SerializeField] private VehicleTripPurpose purpose;
        [SerializeField] private string relatedBuildingId;
        [SerializeField] private int rewardCoins;
        [SerializeField] private VehicleTripState state;

        public string TripId => tripId;
        public string JourneyId => journeyId;
        public int LegIndex => legIndex;
        public Vector2Int Origin => origin;
        public Vector2Int Destination => destination;
        public VehicleTripPurpose Purpose => purpose;
        public string RelatedBuildingId => relatedBuildingId;
        public int RewardCoins => rewardCoins;
        public VehicleTripState State => state;

        public VehicleTrip(
            string tripId,
            string journeyId,
            int legIndex,
            Vector2Int origin,
            Vector2Int destination,
            VehicleTripPurpose purpose,
            string relatedBuildingId,
            int rewardCoins)
        {
            this.tripId = tripId ?? string.Empty;
            this.journeyId = journeyId ?? string.Empty;
            this.legIndex = Mathf.Max(0, legIndex);
            this.origin = origin;
            this.destination = destination;
            this.purpose = purpose;
            this.relatedBuildingId = relatedBuildingId ?? string.Empty;
            this.rewardCoins = Mathf.Max(0, rewardCoins);
            state = VehicleTripState.Queued;
        }

        public bool TryBeginRouting()
        {
            if (state != VehicleTripState.Queued)
            {
                return false;
            }

            state = VehicleTripState.Routing;
            return true;
        }

        public bool TryBeginDriving()
        {
            if (state != VehicleTripState.Routing)
            {
                return false;
            }

            state = VehicleTripState.Driving;
            return true;
        }

        public bool TryArrive()
        {
            if (state != VehicleTripState.Driving)
            {
                return false;
            }

            state = VehicleTripState.Arrived;
            return true;
        }

        public bool TryCancel()
        {
            if (state == VehicleTripState.Arrived ||
                state == VehicleTripState.Cancelled)
            {
                return false;
            }

            state = VehicleTripState.Cancelled;
            return true;
        }

        public VehicleTripSnapshot CreateSnapshot() => new(
            tripId,
            journeyId,
            legIndex,
            origin,
            destination,
            purpose,
            relatedBuildingId,
            rewardCoins,
            state);
    }

    public readonly struct VehicleTripSnapshot
    {
        public VehicleTripSnapshot(
            string tripId,
            string journeyId,
            int legIndex,
            Vector2Int origin,
            Vector2Int destination,
            VehicleTripPurpose purpose,
            string relatedBuildingId,
            int rewardCoins,
            VehicleTripState state)
        {
            TripId = tripId ?? string.Empty;
            JourneyId = journeyId ?? string.Empty;
            LegIndex = Mathf.Max(0, legIndex);
            Origin = origin;
            Destination = destination;
            Purpose = purpose;
            RelatedBuildingId = relatedBuildingId ?? string.Empty;
            RewardCoins = Mathf.Max(0, rewardCoins);
            State = state;
        }

        public string TripId { get; }
        public string JourneyId { get; }
        public int LegIndex { get; }
        public Vector2Int Origin { get; }
        public Vector2Int Destination { get; }
        public VehicleTripPurpose Purpose { get; }
        public string RelatedBuildingId { get; }
        public int RewardCoins { get; }
        public VehicleTripState State { get; }
    }
}
