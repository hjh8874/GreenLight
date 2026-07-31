using System;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Content
{
    public enum EmergencyIncidentState
    {
        WaitingForHospital = 0,
        AmbulanceOutbound = 1,
        Treating = 2,
        AmbulanceReturning = 3,
        Resolved = 4,
        Failed = 5,
        AmbulanceReturningAfterFailure = 6
    }

    [Serializable]
    public sealed class EmergencyIncident
    {
        private const string DefaultDefinitionId =
            "default_medical_emergency";
        private const string DefaultTitle =
            "응급 환자 발생";
        private const string DefaultDescription =
            "제한 시간 안에 구급차를 출동시키세요.";
        private const string DefaultSuccessMessage =
            "환자가 제시간에 응급 치료를 받았습니다.";
        private const string DefaultTimeoutMessage =
            "구급차가 제시간에 도착하지 못해 환자가 사망했습니다.";
        private const string DefaultUnreachableMessage =
            "응급 경로를 확보하지 못해 환자가 사망했습니다.";
        private const int DefaultDeadlineHours = 24;

        [SerializeField] private int incidentId;
        [SerializeField] private string definitionId;
        [SerializeField] private string title;
        [SerializeField] private string description;
        [SerializeField] private string successMessage;
        [SerializeField] private string timeoutMessage;
        [SerializeField] private string unreachableMessage;
        [SerializeField] private float failureHappinessPenalty;
        [SerializeField] private Vector2Int location;
        [SerializeField] private TileType sourceType;
        [SerializeField] private EmergencyIncidentState state;
        [SerializeField] private Vector2Int assignedHospital;
        [SerializeField] private float stateRemainingSeconds;
        [SerializeField] private long createdAbsoluteHour;
        [SerializeField] private long deadlineAbsoluteHour;
        [SerializeField]
        private EmergencyIncidentFailureReason failureReason;

        public int IncidentId => incidentId;
        public string DefinitionId => definitionId;
        public string Title => title;
        public string Description => description;
        public string SuccessMessage => successMessage;
        public Vector2Int Location => location;
        public TileType SourceType => sourceType;
        public EmergencyIncidentState State => state;
        public Vector2Int AssignedHospital => assignedHospital;
        public float StateRemainingSeconds =>
            stateRemainingSeconds;
        public long CreatedAbsoluteHour =>
            createdAbsoluteHour;
        public long DeadlineAbsoluteHour =>
            deadlineAbsoluteHour;
        public EmergencyIncidentFailureReason FailureReason =>
            failureReason;
        public float SuggestedFailureHappinessDelta =>
            -Mathf.Max(0f, failureHappinessPenalty);
        public bool IsResponsePending =>
            state is EmergencyIncidentState.WaitingForHospital
                or EmergencyIncidentState.AmbulanceOutbound;
        public bool IsFailed =>
            failureReason !=
                EmergencyIncidentFailureReason.None ||
            state is EmergencyIncidentState.Failed
                or EmergencyIncidentState
                    .AmbulanceReturningAfterFailure;
        public bool IsFinished =>
            state is EmergencyIncidentState.Resolved
                or EmergencyIncidentState.Failed;

        public EmergencyIncident(
            int id,
            Vector2Int source,
            TileType type)
            : this(
                id,
                source,
                type,
                definition: null,
                createdHour: 0L)
        {
        }

        public EmergencyIncident(
            int id,
            Vector2Int source,
            TileType type,
            EmergencyIncidentDefinitionSO definition,
            long createdHour)
        {
            incidentId = Mathf.Max(1, id);
            location = source;
            sourceType = type;
            state =
                EmergencyIncidentState.WaitingForHospital;
            assignedHospital = new Vector2Int(-1, -1);
            createdAbsoluteHour = Math.Max(0L, createdHour);
            ApplyDefinition(definition);
            deadlineAbsoluteHour =
                createdAbsoluteHour +
                (definition != null
                    ? definition.ResponseDeadlineHours
                    : DefaultDeadlineHours);
        }

        public void Dispatch(
            Vector2Int hospital,
            float travelSeconds)
        {
            assignedHospital = hospital;
            state =
                EmergencyIncidentState.AmbulanceOutbound;
            stateRemainingSeconds =
                Mathf.Max(0.01f, travelSeconds);
        }

        public void BeginTreatment(float seconds)
        {
            state = EmergencyIncidentState.Treating;
            stateRemainingSeconds =
                Mathf.Max(0.01f, seconds);
        }

        public void BeginReturn(float seconds)
        {
            state =
                EmergencyIncidentState.AmbulanceReturning;
            stateRemainingSeconds =
                Mathf.Max(0.01f, seconds);
        }

        public void BeginFailedReturn(
            EmergencyIncidentFailureReason reason,
            float seconds)
        {
            failureReason = NormalizeFailureReason(reason);
            state = EmergencyIncidentState
                .AmbulanceReturningAfterFailure;
            stateRemainingSeconds =
                Mathf.Max(0.01f, seconds);
        }

        public bool Advance(float deltaTime)
        {
            if (stateRemainingSeconds <= 0f)
            {
                return true;
            }

            stateRemainingSeconds = Mathf.Max(
                0f,
                stateRemainingSeconds -
                Mathf.Max(0f, deltaTime));

            return stateRemainingSeconds <= 0f;
        }

        public void Resolve()
        {
            state = EmergencyIncidentState.Resolved;
            stateRemainingSeconds = 0f;
            failureReason =
                EmergencyIncidentFailureReason.None;
        }

        public void CompleteFailure()
        {
            failureReason = NormalizeFailureReason(
                failureReason);
            state = EmergencyIncidentState.Failed;
            stateRemainingSeconds = 0f;
        }

        public void Fail(
            EmergencyIncidentFailureReason reason =
                EmergencyIncidentFailureReason
                    .DestinationUnreachable)
        {
            failureReason = NormalizeFailureReason(reason);
            CompleteFailure();
        }

        public string GetFailureMessage(
            EmergencyIncidentFailureReason reason)
        {
            return reason ==
                   EmergencyIncidentFailureReason
                       .ResponseDeadlineExceeded
                ? timeoutMessage
                : unreachableMessage;
        }

        public static EmergencyIncident Restore(
            int id,
            Vector2Int source,
            TileType type,
            EmergencyIncidentDefinitionSO definition,
            long createdHour,
            long deadlineHour,
            EmergencyIncidentState restoredState,
            Vector2Int hospital,
            float remainingSeconds,
            EmergencyIncidentFailureReason restoredFailure)
        {
            var incident = new EmergencyIncident(
                id,
                source,
                type,
                definition,
                createdHour)
            {
                deadlineAbsoluteHour = Math.Max(
                    createdHour + 1L,
                    deadlineHour),
                state = restoredState,
                assignedHospital = hospital,
                stateRemainingSeconds =
                    Mathf.Max(0f, remainingSeconds),
                failureReason = restoredFailure
            };
            return incident;
        }

        private void ApplyDefinition(
            EmergencyIncidentDefinitionSO definition)
        {
            definitionId = definition != null
                ? definition.IncidentId
                : DefaultDefinitionId;
            title = definition != null
                ? definition.Title
                : DefaultTitle;
            description = definition != null
                ? definition.Description
                : DefaultDescription;
            successMessage = definition != null
                ? definition.SuccessMessage
                : DefaultSuccessMessage;
            timeoutMessage = definition != null
                ? definition.TimeoutMessage
                : DefaultTimeoutMessage;
            unreachableMessage = definition != null
                ? definition.UnreachableMessage
                : DefaultUnreachableMessage;
            failureHappinessPenalty = definition != null
                ? definition.FailureHappinessPenalty
                : 0f;
        }

        private static EmergencyIncidentFailureReason
            NormalizeFailureReason(
                EmergencyIncidentFailureReason reason)
        {
            return reason ==
                   EmergencyIncidentFailureReason.None
                ? EmergencyIncidentFailureReason
                    .DestinationUnreachable
                : reason;
        }
    }
}
