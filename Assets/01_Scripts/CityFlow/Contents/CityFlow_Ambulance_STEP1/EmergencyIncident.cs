using System;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Content
{
    public enum EmergencyIncidentState
    {
        Waiting,
        Dispatched,
        Treating,
        Returning,
        Resolved,
        Failed
    }

    [Serializable]
    public sealed class EmergencyIncident
    {
        [SerializeField] private int incidentId;
        [SerializeField] private Vector2Int location;
        [SerializeField] private TileType sourceType;
        [SerializeField] private EmergencyIncidentState state;
        [SerializeField] private Vector2Int assignedHospital;
        [SerializeField] private float createdAt;
        [SerializeField] private float stateRemainingSeconds;

        public int IncidentId => incidentId;
        public Vector2Int Location => location;
        public TileType SourceType => sourceType;
        public EmergencyIncidentState State => state;
        public Vector2Int AssignedHospital => assignedHospital;
        public float CreatedAt => createdAt;
        public float StateRemainingSeconds => stateRemainingSeconds;
        public bool IsFinished => state == EmergencyIncidentState.Resolved || state == EmergencyIncidentState.Failed;

        public EmergencyIncident(int incidentId, Vector2Int location, TileType sourceType, float createdAt)
        {
            this.incidentId = incidentId;
            this.location = location;
            this.sourceType = sourceType;
            this.createdAt = Mathf.Max(0f, createdAt);
            state = EmergencyIncidentState.Waiting;
            assignedHospital = new Vector2Int(-1, -1);
            stateRemainingSeconds = 0f;
        }

        public void Dispatch(Vector2Int hospitalTile, float travelSeconds)
        {
            assignedHospital = hospitalTile;
            state = EmergencyIncidentState.Dispatched;
            stateRemainingSeconds = Mathf.Max(0.01f, travelSeconds);
        }

        public void BeginTreatment(float treatmentSeconds)
        {
            state = EmergencyIncidentState.Treating;
            stateRemainingSeconds = Mathf.Max(0.01f, treatmentSeconds);
        }

        public void BeginReturn(float travelSeconds)
        {
            state = EmergencyIncidentState.Returning;
            stateRemainingSeconds = Mathf.Max(0.01f, travelSeconds);
        }

        public bool AdvanceTimer(float deltaTime)
        {
            if (stateRemainingSeconds <= 0f) return true;
            stateRemainingSeconds = Mathf.Max(0f, stateRemainingSeconds - Mathf.Max(0f, deltaTime));
            return stateRemainingSeconds <= 0f;
        }

        public void Resolve()
        {
            state = EmergencyIncidentState.Resolved;
            stateRemainingSeconds = 0f;
        }

        public void Fail()
        {
            state = EmergencyIncidentState.Failed;
            stateRemainingSeconds = 0f;
        }
    }
}
