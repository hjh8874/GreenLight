using System;

namespace CityFlow.Contracts.Save
{
    [Serializable]
    public sealed class GameCalendarSaveData
    {
        public int Year;
        public int Month;
        public int Day;
        public int Hour;
        public int TotalMonths;
        public float AccumulatedRealSeconds;
    }
}
