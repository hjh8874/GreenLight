using System;

namespace CityFlow.Contracts.Save
{
    [Serializable]
    public sealed class SchoolBusSaveData
    {
        public bool HasTripHistory;
        public long LastMorningTripDay = -1L;
        public long LastAfternoonTripDay = -1L;
    }
}
