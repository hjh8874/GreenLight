using System;
using UnityEngine;

namespace CityFlow.Content.Transit
{
    /// <summary>
    /// 운행 중인 버스 한 대의 변경 가능한 상태입니다.
    ///
    /// UI는 이 클래스를 읽어 현재 승객, 상태, 정류장,
    /// 운행 횟수 등을 표시합니다.
    /// </summary>
    [Serializable]
    public sealed class BusRuntime
    {
        [SerializeField]
        private BusDefinitionSO definition;

        [SerializeField]
        private BusOperatingState state =
            BusOperatingState.Idle;

        [SerializeField]
        private int currentPassengers;

        [SerializeField]
        private int currentStopIndex;

        [SerializeField]
        private Vector2Int currentTile;

        [SerializeField]
        private Vector2Int nextStopTile;

        [SerializeField]
        private int completedTrips;

        [SerializeField]
        private int totalTransportedPassengers;

        [SerializeField]
        private bool unlocked;

        [SerializeField]
        private bool serviceEnabled = true;

        public BusDefinitionSO Definition =>
            definition;

        public string BusId =>
            definition != null
                ? definition.BusId
                : string.Empty;

        public string BusName =>
            definition != null
                ? definition.BusName
                : "버스";

        public BusType BusType =>
            definition != null
                ? definition.BusType
                : BusType.None;

        public BusOperatingState State =>
            state;

        public int CurrentPassengers =>
            currentPassengers;

        public int PassengerCapacity =>
            definition != null
                ? definition.PassengerCapacity
                : 0;

        public int RemainingCapacity =>
            Mathf.Max(
                0,
                PassengerCapacity -
                currentPassengers);

        public int CurrentStopIndex =>
            currentStopIndex;

        public Vector2Int CurrentTile =>
            currentTile;

        public Vector2Int NextStopTile =>
            nextStopTile;

        public int CompletedTrips =>
            completedTrips;

        public int TotalTransportedPassengers =>
            totalTransportedPassengers;

        public bool IsUnlocked =>
            unlocked;

        public bool IsServiceEnabled =>
            serviceEnabled;

        public bool IsFull =>
            PassengerCapacity > 0 &&
            currentPassengers >=
            PassengerCapacity;

        public event Action<BusRuntime>
            Changed;

        public BusRuntime(
            BusDefinitionSO definition)
        {
            Initialize(definition);
        }

        public void Initialize(
            BusDefinitionSO busDefinition)
        {
            definition = busDefinition;

            unlocked =
                definition != null &&
                definition.UnlockedByDefault;

            currentPassengers = 0;
            currentStopIndex = 0;
            currentTile = default;
            nextStopTile = default;
            completedTrips = 0;
            totalTransportedPassengers = 0;

            state = unlocked
                ? BusOperatingState.Idle
                : BusOperatingState.Locked;

            serviceEnabled = true;

            NotifyChanged();
        }

        public void SetUnlocked(bool value)
        {
            unlocked = value;

            if (!unlocked)
            {
                state =
                    BusOperatingState.Locked;
            }
            else if (state ==
                     BusOperatingState.Locked)
            {
                state =
                    BusOperatingState.Idle;
            }

            NotifyChanged();
        }

        public void SetServiceEnabled(bool value)
        {
            serviceEnabled = value;

            if (!serviceEnabled &&
                state != BusOperatingState.Locked)
            {
                state =
                    BusOperatingState.OutOfService;
            }
            else if (serviceEnabled &&
                     state ==
                     BusOperatingState.OutOfService)
            {
                state =
                    BusOperatingState.Idle;
            }

            NotifyChanged();
        }

        public void SetState(
            BusOperatingState newState)
        {
            if (state == newState)
            {
                return;
            }

            state = newState;
            NotifyChanged();
        }

        public void SetCurrentTile(
            Vector2Int tile)
        {
            if (currentTile == tile)
            {
                return;
            }

            currentTile = tile;
            NotifyChanged();
        }

        public void SetNextStop(
            Vector2Int tile,
            int stopIndex)
        {
            nextStopTile = tile;
            currentStopIndex =
                Mathf.Max(0, stopIndex);

            NotifyChanged();
        }

        public int BoardPassengers(int count)
        {
            int safeCount =
                Mathf.Max(0, count);

            int boarded =
                Mathf.Min(
                    safeCount,
                    RemainingCapacity);

            if (boarded <= 0)
            {
                return 0;
            }

            currentPassengers += boarded;

            NotifyChanged();
            return boarded;
        }

        public int LeavePassengers(int count)
        {
            int safeCount =
                Mathf.Max(0, count);

            int left =
                Mathf.Min(
                    safeCount,
                    currentPassengers);

            if (left <= 0)
            {
                return 0;
            }

            currentPassengers -= left;

            NotifyChanged();
            return left;
        }

        public int UnloadAllPassengers()
        {
            int unloaded =
                currentPassengers;

            currentPassengers = 0;

            NotifyChanged();
            return unloaded;
        }

        public void CompleteTrip(
            int transportedPassengers)
        {
            completedTrips++;

            totalTransportedPassengers +=
                Mathf.Max(
                    0,
                    transportedPassengers);

            NotifyChanged();
        }

        private void NotifyChanged()
        {
            Changed?.Invoke(this);
        }
    }
}