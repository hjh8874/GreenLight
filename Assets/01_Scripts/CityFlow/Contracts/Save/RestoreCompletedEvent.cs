using System;

namespace CityFlow.Contracts.Save
{
    public readonly struct RestoreCompletedEvent
    {
        public double SettledOfflineSeconds { get; }
        public bool IncludesOfflineProgression { get; }

        public RestoreCompletedEvent(
            double settledOfflineSeconds,
            bool includesOfflineProgression)
        {
            SettledOfflineSeconds = settledOfflineSeconds;
            IncludesOfflineProgression = includesOfflineProgression;
        }
    }

    public readonly struct OfflineSettlementCompletedEvent
    {
        public double SettledOfflineSeconds { get; }
        public long InitialCoins { get; }
        public long EarnedCoins { get; }
        public long CurrentCoins { get; }

        public OfflineSettlementCompletedEvent(
            double settledOfflineSeconds,
            long initialCoins,
            long earnedCoins,
            long currentCoins)
        {
            SettledOfflineSeconds = Math.Max(0.0, settledOfflineSeconds);
            InitialCoins = Math.Max(0L, initialCoins);
            EarnedCoins = Math.Max(0L, earnedCoins);
            CurrentCoins = Math.Max(0L, currentCoins);
        }
    }
}
