using System;

namespace CityFlow.Contracts.Save
{
    [Serializable]
    public sealed class EmergencyIncidentSaveData
    {
        public int NextIncidentId;
        public long NextAutomaticDispatchDay;
        public long AutomaticDispatchCountDay;
        public int AutomaticDispatchCount;
        public EmergencyIncidentEntrySaveData[] ActiveIncidents;
        public EmergencyIncidentTargetSaveData[] RecentTargets;
    }

    [Serializable]
    public sealed class EmergencyIncidentEntrySaveData
    {
        public int IncidentId;
        public string DefinitionId;
        public int LocationX;
        public int LocationY;
        public int SourceType;
        public int State;
        public int HospitalX;
        public int HospitalY;
        public float StateRemainingSeconds;
        public long CreatedAbsoluteHour;
        public long DeadlineAbsoluteHour;
        public int FailureReason;
    }

    [Serializable]
    public sealed class EmergencyIncidentTargetSaveData
    {
        public int X;
        public int Y;
    }
}
