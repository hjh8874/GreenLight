using System;

namespace CityFlow.Contracts
{
    public interface IWeeklyEconomyService
    {
        long PendingCoins { get; }
        int DaysIntoCurrentWeek { get; }
        int SettlementDays { get; }

        event Action<long> PendingCoinsChanged;
        event Action<WeeklySettlementCompletedEvent> SettlementCompleted;

        void AddPendingCoins(long amount, string reason = "weekly income");
        bool TryHarvestPendingCoins();
    }
}
