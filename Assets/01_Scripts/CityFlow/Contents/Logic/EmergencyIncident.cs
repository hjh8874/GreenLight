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
        Failed = 5
    }

    [Serializable]
    public sealed class EmergencyIncident
    {
        [SerializeField] private int incidentId;
        [SerializeField] private Vector2Int location;
        [SerializeField] private TileType sourceType;
        [SerializeField] private EmergencyIncidentState state;
        [SerializeField] private Vector2Int assignedHospital;
        [SerializeField] private float stateRemainingSeconds;

        public int IncidentId => incidentId;
        public Vector2Int Location => location;
        public TileType SourceType => sourceType;
        public EmergencyIncidentState State => state;
        public Vector2Int AssignedHospital =>
            assignedHospital;
        public float StateRemainingSeconds =>
            stateRemainingSeconds;
        public bool IsFinished =>
            state is EmergencyIncidentState.Resolved
                or EmergencyIncidentState.Failed;

        public EmergencyIncident(
            int id,
            Vector2Int source,
            TileType type)
        {
            incidentId = Mathf.Max(1, id);
            location = source;
            sourceType = type;
            state =
                EmergencyIncidentState.WaitingForHospital;
            assignedHospital = new Vector2Int(-1, -1);
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
        }

        public void Fail()
        {
            state = EmergencyIncidentState.Failed;
            stateRemainingSeconds = 0f;
        }
    }
}
