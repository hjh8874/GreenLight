using System;
using UnityEngine;

namespace CityFlow.Contracts
{
    public enum PoliceCallState
    {
        WaitingForVehicle = 0,
        VehicleOutbound = 1,
        Handling = 2,
        VehicleReturning = 3,
        Completed = 4,
        VehicleReturningAfterFailure = 5,
        Failed = 6
    }

    public enum PoliceCallFailureReason
    {
        None = 0,
        DestinationUnreachable = 1,
        RouteDisconnected = 2,
        PoliceStationRemoved = 3,
        TargetRemoved = 4,
        Cancelled = 5
    }

    public readonly struct PoliceDispatchRequest
    {
        public PoliceDispatchRequest(
            Vector2Int target,
            string externalRequestId = "",
            float handlingSeconds = -1f)
        {
            Target = target;
            ExternalRequestId = externalRequestId ?? string.Empty;
            HandlingSeconds = handlingSeconds;
        }

        public Vector2Int Target { get; }
        public string ExternalRequestId { get; }
        public float HandlingSeconds { get; }
    }

    public readonly struct PoliceCallSnapshot
    {
        public PoliceCallSnapshot(
            int callId,
            string externalRequestId,
            Vector2Int target,
            Vector2Int assignedStation,
            int assignedVehicleSlot,
            PoliceCallState state,
            float remainingHandlingSeconds,
            PoliceCallFailureReason failureReason)
        {
            CallId = callId;
            ExternalRequestId = externalRequestId ?? string.Empty;
            Target = target;
            AssignedStation = assignedStation;
            AssignedVehicleSlot = assignedVehicleSlot;
            State = state;
            RemainingHandlingSeconds = Mathf.Max(
                0f,
                remainingHandlingSeconds);
            FailureReason = failureReason;
        }

        public int CallId { get; }
        public string ExternalRequestId { get; }
        public Vector2Int Target { get; }
        public Vector2Int AssignedStation { get; }
        public int AssignedVehicleSlot { get; }
        public PoliceCallState State { get; }
        public float RemainingHandlingSeconds { get; }
        public PoliceCallFailureReason FailureReason { get; }
        public bool IsFinished =>
            State is PoliceCallState.Completed
                or PoliceCallState.Failed;
    }

    public readonly struct PoliceDispatchAlertEvent
    {
        public PoliceDispatchAlertEvent(PoliceCallSnapshot call)
        {
            Call = call;
        }

        public PoliceCallSnapshot Call { get; }
    }

    public readonly struct PoliceDispatchOutcomeEvent
    {
        public PoliceDispatchOutcomeEvent(PoliceCallSnapshot call)
        {
            Call = call;
        }

        public PoliceCallSnapshot Call { get; }
        public bool Succeeded =>
            Call.State == PoliceCallState.Completed;
    }

    public interface IPoliceDispatchService
    {
        int ActiveCallCount { get; }

        event Action<PoliceCallSnapshot> CallCreated;
        event Action<PoliceCallSnapshot> CallChanged;
        event Action<PoliceCallSnapshot> CallRemoved;

        bool TryRequestDispatch(
            PoliceDispatchRequest request,
            out int callId);

        bool TryCancelDispatch(int callId);

        bool TryGetCall(
            int callId,
            out PoliceCallSnapshot call);

        PoliceCallSnapshot[] CreateActiveCallSnapshot();
    }
}
