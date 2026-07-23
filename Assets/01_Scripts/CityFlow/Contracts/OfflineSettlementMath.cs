using System;

namespace CityFlow.Contracts
{
    public static class OfflineSettlementMath
    {
        public static long CalculateIncome(
            long observedIncomeCoins,
            double observedSeconds,
            double offlineSeconds,
            int incomePercent)
        {
            if (observedIncomeCoins <= 0L ||
                observedSeconds <= 0.0 ||
                offlineSeconds <= 0.0 ||
                incomePercent <= 0 ||
                double.IsNaN(observedSeconds) ||
                double.IsInfinity(observedSeconds) ||
                double.IsNaN(offlineSeconds) ||
                double.IsInfinity(offlineSeconds))
            {
                return 0L;
            }

            double reward =
                observedIncomeCoins / observedSeconds *
                offlineSeconds *
                Math.Min(100, incomePercent) /
                100.0;

            if (reward >= long.MaxValue)
            {
                return long.MaxValue;
            }

            return (long)Math.Floor(Math.Max(0.0, reward));
        }
    }
}
