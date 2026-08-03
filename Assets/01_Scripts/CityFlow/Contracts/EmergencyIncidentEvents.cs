using UnityEngine;

namespace CityFlow.Contracts
{
    public enum EmergencyIncidentFailureReason
    {
        None = 0,
        ResponseDeadlineExceeded = 1,
        DestinationUnreachable = 2,
        RouteDisconnected = 3,
        HospitalRemoved = 4,
        TargetRemoved = 5
    }

    public enum EmergencyIncidentOutcome
    {
        Resolved = 0,
        Failed = 1
    }

    public readonly struct EmergencyIncidentAlertEvent
    {
        public EmergencyIncidentAlertEvent(
            int incidentId,
            string definitionId,
            string title,
            string message,
            Vector2Int location,
            long deadlineAbsoluteHour)
        {
            IncidentId = incidentId;
            DefinitionId = definitionId;
            Title = title;
            Message = message;
            Location = location;
            DeadlineAbsoluteHour = deadlineAbsoluteHour;
        }

        public int IncidentId { get; }
        public string DefinitionId { get; }
        public string Title { get; }
        public string Message { get; }
        public Vector2Int Location { get; }
        public long DeadlineAbsoluteHour { get; }
    }

    public readonly struct EmergencyIncidentOutcomeEvent
    {
        public EmergencyIncidentOutcomeEvent(
            int incidentId,
            string definitionId,
            string title,
            string message,
            Vector2Int location,
            EmergencyIncidentOutcome outcome,
            EmergencyIncidentFailureReason failureReason,
            float suggestedHappinessDelta)
        {
            IncidentId = incidentId;
            DefinitionId = definitionId;
            Title = title;
            Message = message;
            Location = location;
            Outcome = outcome;
            FailureReason = failureReason;
            SuggestedHappinessDelta = suggestedHappinessDelta;
        }

        public int IncidentId { get; }
        public string DefinitionId { get; }
        public string Title { get; }
        public string Message { get; }
        public Vector2Int Location { get; }
        public EmergencyIncidentOutcome Outcome { get; }
        public EmergencyIncidentFailureReason FailureReason { get; }

        // A future happiness system can consume this value without the
        // ambulance feature depending on a concrete happiness implementation.
        public float SuggestedHappinessDelta { get; }
    }
}
