using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Content
{
    internal sealed class PoliceCall
    {
        private static readonly Vector2Int InvalidStation =
            new(-1, -1);

        public PoliceCall(
            int callId,
            PoliceDispatchRequest request,
            float defaultHandlingSeconds)
        {
            CallId = Mathf.Max(1, callId);
            ExternalRequestId =
                request.ExternalRequestId ?? string.Empty;
            Target = request.Target;
            HandlingSeconds = request.HandlingSeconds > 0f
                ? request.HandlingSeconds
                : Mathf.Max(0.01f, defaultHandlingSeconds);
            AssignedStation = InvalidStation;
            AssignedVehicleSlot = -1;
            State = PoliceCallState.WaitingForVehicle;
        }

        public int CallId { get; private set; }
        public string ExternalRequestId { get; private set; }
        public Vector2Int Target { get; private set; }
        public Vector2Int AssignedStation { get; private set; }
        public int AssignedVehicleSlot { get; private set; }
        public PoliceCallState State { get; private set; }
        public float HandlingSeconds { get; private set; }
        public float RemainingHandlingSeconds { get; private set; }
        public PoliceCallFailureReason FailureReason { get; private set; }
        public bool IsFinished =>
            State is PoliceCallState.Completed
                or PoliceCallState.Failed;

        public void Dispatch(
            Vector2Int station,
            int vehicleSlot)
        {
            AssignedStation = station;
            AssignedVehicleSlot = Mathf.Max(0, vehicleSlot);
            State = PoliceCallState.VehicleOutbound;
        }

        public void BeginHandling()
        {
            State = PoliceCallState.Handling;
            RemainingHandlingSeconds = HandlingSeconds;
        }

        public bool AdvanceHandling(float deltaTime)
        {
            RemainingHandlingSeconds = Mathf.Max(
                0f,
                RemainingHandlingSeconds -
                Mathf.Max(0f, deltaTime));
            return RemainingHandlingSeconds <= 0f;
        }

        public void BeginReturn()
        {
            State = PoliceCallState.VehicleReturning;
            RemainingHandlingSeconds = 0f;
        }

        public void BeginFailedReturn(
            PoliceCallFailureReason reason)
        {
            FailureReason = NormalizeFailure(reason);
            State = PoliceCallState
                .VehicleReturningAfterFailure;
            RemainingHandlingSeconds = 0f;
        }

        public void Complete()
        {
            State = PoliceCallState.Completed;
            FailureReason = PoliceCallFailureReason.None;
            RemainingHandlingSeconds = 0f;
        }

        public void CompleteFailure(
            PoliceCallFailureReason reason)
        {
            State = PoliceCallState.Failed;
            FailureReason = NormalizeFailure(reason);
            RemainingHandlingSeconds = 0f;
        }

        public PoliceCallSnapshot CreateSnapshot() => new(
            CallId,
            ExternalRequestId,
            Target,
            AssignedStation,
            AssignedVehicleSlot,
            State,
            RemainingHandlingSeconds,
            FailureReason);

        public static PoliceCall Restore(
            int callId,
            string externalRequestId,
            Vector2Int target,
            Vector2Int assignedStation,
            int assignedVehicleSlot,
            PoliceCallState state,
            float handlingSeconds,
            float remainingHandlingSeconds,
            PoliceCallFailureReason failureReason)
        {
            var call = new PoliceCall(
                callId,
                new PoliceDispatchRequest(
                    target,
                    externalRequestId,
                    handlingSeconds),
                handlingSeconds)
            {
                AssignedStation = assignedStation,
                AssignedVehicleSlot = assignedVehicleSlot,
                State = state,
                HandlingSeconds = Mathf.Max(
                    0.01f,
                    handlingSeconds),
                RemainingHandlingSeconds = Mathf.Max(
                    0f,
                    remainingHandlingSeconds),
                FailureReason = failureReason
            };
            return call;
        }

        private static PoliceCallFailureReason NormalizeFailure(
            PoliceCallFailureReason reason)
        {
            return reason == PoliceCallFailureReason.None
                ? PoliceCallFailureReason.DestinationUnreachable
                : reason;
        }
    }
}
