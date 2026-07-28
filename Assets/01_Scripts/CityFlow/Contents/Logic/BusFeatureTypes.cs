using System;
using UnityEngine;

namespace CityFlow.Content
{
    public enum BusType
    {
        None = 0,
        CityBus = 1,
        SchoolBus = 2
    }

    public enum BusOperatingState
    {
        Locked = 0,
        Idle = 1,
        Moving = 2,
        WaitingAtStop = 3,
        RouteUnavailable = 4,
        OutOfService = 5
    }

    [Serializable]
    public sealed class BusRuntime
    {
        [SerializeField] private BusOperatingState state;
        [SerializeField] private int passengerCapacity;
        [SerializeField] private int currentPassengers;
        [SerializeField] private int completedStops;
        [SerializeField] private Vector2Int currentTile;
        [SerializeField] private Vector2Int nextStop;

        public BusOperatingState State => state;
        public int PassengerCapacity => passengerCapacity;
        public int CurrentPassengers => currentPassengers;
        public int RemainingCapacity =>
            Mathf.Max(0, passengerCapacity - currentPassengers);
        public int CompletedStops => completedStops;
        public Vector2Int CurrentTile => currentTile;
        public Vector2Int NextStop => nextStop;

        public event Action<BusRuntime> Changed;

        public BusRuntime(int capacity)
        {
            passengerCapacity = Mathf.Max(1, capacity);
            state = BusOperatingState.Idle;
        }

        public void SetState(BusOperatingState value)
        {
            if (state == value)
            {
                return;
            }

            state = value;
            Changed?.Invoke(this);
        }

        public void SetRoutePosition(
            Vector2Int tile,
            Vector2Int upcomingStop)
        {
            currentTile = tile;
            nextStop = upcomingStop;
            Changed?.Invoke(this);
        }

        public int Board(int requested)
        {
            int boarded = Mathf.Min(
                Mathf.Max(0, requested),
                RemainingCapacity);

            if (boarded <= 0)
            {
                return 0;
            }

            currentPassengers += boarded;
            Changed?.Invoke(this);
            return boarded;
        }

        public int Leave(int requested)
        {
            int left = Mathf.Min(
                Mathf.Max(0, requested),
                currentPassengers);

            if (left <= 0)
            {
                return 0;
            }

            currentPassengers -= left;
            Changed?.Invoke(this);
            return left;
        }

        public void CompleteStop()
        {
            completedStops++;
            Changed?.Invoke(this);
        }

        public void ResetPassengers()
        {
            if (currentPassengers == 0)
            {
                return;
            }

            currentPassengers = 0;
            Changed?.Invoke(this);
        }
    }
}
