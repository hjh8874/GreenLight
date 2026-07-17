using System;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.Gameplay.Economy
{
    // Player coins are earned from completed trips (ArrivalEvent) only.
    // FlowBurst is an effect-only event and must not affect the economy.
    public sealed class EconomyService :
        MonoBehaviour,
        ICityFlowServiceConsumer,
        IEconomyService,
        IEconomySaveSource
    {
        [Header("Initial State")]
        [SerializeField] private long startingCoins;

        private CityFlowServices services;

        /// <summary>
        /// �÷��̾ ������ �����ϰ� �ִ� �����Դϴ�.
        /// HUD, ���� �ý���, ���� �ý����� �� ���� �������� ����մϴ�.
        /// </summary>
        public long Coins { get; private set; }

        /// <summary>
        /// ������ ����Ǿ��� �� HUD � ���� ������ �����մϴ�.
        /// </summary>
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
            services.Events.SettlementComputed += OnSettlementComputed;

            PublishCoinsChanged();

            Debug.Log(
                "[EconomyService] Economy service initialized."
            );
        }

        private void OnDestroy()
        {
            if (services == null)
            {
                return;
            }

            services.Events.Arrival -= OnArrival;
            services.Events.SettlementComputed -= OnSettlementComputed;
        }

        /// <summary>
        /// ������ �߰��մϴ�.
        ///
        /// ���� ����, Flow Burst, �������� ����Ӹ� �ƴ϶�
        /// BasicEconomySystem�� �ְ� ���꿡���� ����� �� �ֽ��ϴ�.
        /// </summary>
        public void AddCoins(long amount, string reason = "external reward")
        {
            if (amount <= 0L)
            {
                Debug.LogWarning(
                    $"[EconomyService] Add amount must be positive: {amount}"
                );

                return;
            }

            Coins += amount;

            PublishCoinsChanged();

            Debug.Log(
                $"[EconomyService] Added {amount} coins from {reason}. " +
                $"Current coins: {Coins}"
            );
        }

        /// <summary>
        /// ���� ����� �õ��մϴ�.
        /// �����ϸ� ������ �����ϰ� true�� ��ȯ�մϴ�.
        /// </summary>
        public bool TrySpend(long amount)
        {
            if (amount <= 0L)
            {
                Debug.LogWarning(
                    $"[EconomyService] Spend amount must be positive: {amount}"
                );

                return false;
            }

            if (Coins < amount)
            {
                Debug.Log(
                    $"[EconomyService] Not enough coins. " +
                    $"Required: {amount}, Current: {Coins}"
                );

                return false;
            }

            Coins -= amount;

            PublishCoinsChanged();

            Debug.Log(
                $"[EconomyService] Spent {amount} coins. " +
                $"Current coins: {Coins}"
            );

            return true;
        }

        /// <summary>
        /// ���� �ý��ۿ� ������ ���� �����͸� �����մϴ�.
        /// </summary>
        public EconomySaveData CreateSnapshot()
        {
            return new EconomySaveData
            {
                Coins = Coins
            };
        }

        /// <summary>
        /// ����� ���� �����͸� �����մϴ�.
        /// </summary>
        public void RestoreSnapshot(EconomySaveData snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            Coins = Math.Max(0L, snapshot.Coins);

            PublishCoinsChanged();

            Debug.Log(
                $"[EconomyService] Restored coins: {Coins}"
            );
        }

        /// <summary>
        /// Handles vehicle arrival rewards through the active economy policy.
        /// </summary>
        private void OnArrival(ArrivalEvent e)
        {
            if (services.WeeklyEconomy != null)
            {
                return;
            }

            AddCoins(e.Coins, "arrival");
        }

        /// <summary>
        /// �������� ���� ����� �Ϸ�Ǿ��� �� ȣ��˴ϴ�.
        /// </summary>
        private void OnSettlementComputed(SettlementEvent e)
        {
            if (e.Coins <= 0L)
            {
                return;
            }

            AddCoins(e.Coins, "offline settlement");
        }

        /// <summary>
        /// ���� ���� �̺�Ʈ�� �߻���ŵ�ϴ�.
        /// </summary>
        private void PublishCoinsChanged()
        {
            CoinsChanged?.Invoke(Coins);
        }

        // Unity setup:
        // Add this component to the CityFlow scene root
        // or any active scene object.
        //
        // CityBootstrap will call Initialize automatically
        // through ICityFlowServiceConsumer.
    }
}