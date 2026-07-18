using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Gameplay.Economy
{
    public sealed class DistanceRewardService :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        [Header("Configuration")]
        [SerializeField] private DistanceRewardConfigSO rewardConfig;

        [Header("Debug")]
        [SerializeField] private bool logRewards = true;

        private CityFlowServices services;
        private IRouteDistanceProvider routeDistanceProvider;
        private bool initialized;
        private double bonusRemainder;

        public void Initialize(CityFlowServices cityFlowServices)
        {
            if (initialized || !isActiveAndEnabled)
            {
                return;
            }

            if (rewardConfig == null)
            {
                Debug.LogWarning(
                    "[DistanceRewardService] DistanceRewardConfigSO is not assigned.",
                    this);
                return;
            }

            routeDistanceProvider =
                cityFlowServices.Placement as IRouteDistanceProvider;
            if (routeDistanceProvider == null)
            {
                Debug.LogWarning(
                    "[DistanceRewardService] Route distance provider is unavailable.",
                    this);
                return;
            }

            services = cityFlowServices;
            services.Events.Arrival += OnArrival;
            initialized = true;

            Debug.Log("[DistanceRewardService] Distance rewards initialized.", this);
        }

        private void OnDestroy()
        {
            if (!initialized || services == null)
            {
                return;
            }

            services.Events.Arrival -= OnArrival;
        }

        private void OnArrival(ArrivalEvent arrival)
        {
            if (arrival.Coins <= 0 ||
                !routeDistanceProvider.TryGetAverageRouteDistance(
                    arrival.Destination,
                    out float distanceTiles))
            {
                return;
            }

            long bonus = AccumulateBonus(arrival.Coins, distanceTiles);
            if (bonus <= 0L)
            {
                return;
            }

            if (services.WeeklyEconomy != null)
            {
                services.WeeklyEconomy.AddPendingCoins(
                    bonus,
                    "distance reward");
                LogOnlineReward(
                    arrival.Destination,
                    distanceTiles,
                    arrival.Coins,
                    bonus,
                    services.WeeklyEconomy.PendingCoins);
                return;
            }

            if (services.Economy == null)
            {
                return;
            }

            services.Economy.AddCoins(bonus, "distance reward");
            LogOnlineReward(
                arrival.Destination,
                distanceTiles,
                arrival.Coins,
                bonus,
                services.Economy.Coins);
        }

        private void LogOnlineReward(
            Vector2Int destination,
            float distanceTiles,
            long baseCoins,
            long bonus,
            long currentAmount)
        {
            if (!logRewards)
            {
                return;
            }

            Debug.Log(
                $"[DistanceRewardService] Online distance bonus granted. " +
                $"Destination: {destination}, Distance: {distanceTiles:F2} tiles, " +
                $"Multiplier: {rewardConfig.CalculateMultiplier(distanceTiles):F2}x, " +
                $"Base: {baseCoins}, Bonus: {bonus}, " +
                $"Current pending/balance: {currentAmount}.",
                this);
        }

        private long AccumulateBonus(long baseReward, float distanceTiles)
        {
            double accruedBonus = bonusRemainder +
                rewardConfig.CalculateBonusAmount(baseReward, distanceTiles);
            if (accruedBonus >= long.MaxValue)
            {
                bonusRemainder = 0.0;
                return long.MaxValue;
            }

            long wholeBonus = (long)System.Math.Floor(accruedBonus);
            bonusRemainder = accruedBonus - wholeBonus;
            return wholeBonus;
        }

        // Unity setup: Attach beside the economy services and assign DistanceRewardConfigSO.
    }
}
