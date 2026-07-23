using System;

namespace CityFlow.Contracts.Save
{
    [Serializable]
    public sealed class WeeklySettlementSaveData
    {
        public long PendingCoins;
        public int DaysIntoCurrentWeek;
        public long LastProcessedTotalDays;
        public bool HasCycleProgress;
        public long ObservedOnlineIncomeCoins;
        public double ObservedOnlineSeconds;
    }
}
