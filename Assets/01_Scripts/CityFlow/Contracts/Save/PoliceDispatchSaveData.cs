using System;

namespace CityFlow.Contracts.Save
{
    [Serializable]
    public sealed class PoliceCallEntrySaveData
    {
        public int CallId;
        public string ExternalRequestId;
        public int TargetX;
        public int TargetY;
        public int StationX;
        public int StationY;
        public int AssignedVehicleSlot;
        public int State;
        public float HandlingSeconds;
        public float RemainingHandlingSeconds;
        public int FailureReason;
    }

    [Serializable]
    public sealed class PoliceDispatchSaveData
    {
        public int NextCallId = 1;
        public bool HasLastPatrolTotalDay;
        public long LastPatrolTotalDay;
        public PoliceCallEntrySaveData[] ActiveCalls =
            Array.Empty<PoliceCallEntrySaveData>();
    }
}
