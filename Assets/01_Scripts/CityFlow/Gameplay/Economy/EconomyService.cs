using System;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.Gameplay.Economy
{
    public sealed class EconomyService : MonoBehaviour, ICityFlowServiceConsumer, IEconomyService, IEconomySaveSource
    {
        [Header("Initial State")]
        [SerializeField] private long startingCoins;

        private CityFlowServices services;

        public long Coins { get; private set; }

        public event Action<long> CoinsChanged;

        private void Awake()
        {
            Coins = Math.Max(0L, startingCoins);
        }

        public void Initialize(CityFlowServices services)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            this.services = services;
            services.RegisterEconomy(this);

            services.Events.Arrival += OnArrival;
            services.Events.FlowBurst += OnFlowBurst;
            services.Events.SettlementComputed += OnSettlementComputed;

            PublishCoinsChanged();
            Debug.Log("[EconomyService] Economy service initialized.");
        }

        private void OnDestroy()
        {
            if (services == null)
            {
                return;
            }

            services.Events.Arrival -= OnArrival;
            services.Events.FlowBurst -= OnFlowBurst;
            services.Events.SettlementComputed -= OnSettlementComputed;
        }

        public bool TrySpend(long amount)
        {
            if (amount < 0L)
            {
                Debug.LogWarning($"[EconomyService] Spend amount cannot be negative: {amount}");
                return false;
            }

            if (Coins < amount)
            {
                return false;
            }

            Coins -= amount;
            PublishCoinsChanged();
            Debug.Log($"[EconomyService] Spent {amount} coins. Current coins: {Coins}");
            return true;
        }

        public EconomySaveData CreateSnapshot()
        {
            return new EconomySaveData
            {
                Coins = Coins
            };
        }

        public void RestoreSnapshot(EconomySaveData snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            Coins = Math.Max(0L, snapshot.Coins);
            PublishCoinsChanged();
            Debug.Log($"[EconomyService] Restored coins: {Coins}");
        }

        private void OnArrival(ArrivalEvent e)
        {
            AddCoins(e.Coins, "arrival");
        }

        private void OnFlowBurst(FlowBurstEvent e)
        {
            AddCoins(e.Reward, "flow burst");
        }

        private void OnSettlementComputed(SettlementEvent e)
        {
            AddCoins(e.Coins, "offline settlement");
        }

        public void AddCoins(long amount, string reason)
        {
            if (amount <= 0L)
            {
                return;
            }

            Coins += amount;
            PublishCoinsChanged();
            Debug.Log($"[EconomyService] Added {amount} coins from {reason}. Current coins: {Coins}");
        }

        private void PublishCoinsChanged()
        {
            CoinsChanged?.Invoke(Coins);
        }

        // Unity setup: Add this component to the CityFlow scene root or any active scene object.
        // CityBootstrap will call Initialize automatically through ICityFlowServiceConsumer.
    }
}
