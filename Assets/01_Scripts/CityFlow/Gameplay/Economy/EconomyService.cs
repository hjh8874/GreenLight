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
        /// 플레이어가 실제로 보유하고 있는 코인입니다.
        /// HUD, 저장 시스템, 구매 시스템은 이 값을 기준으로 사용합니다.
        /// </summary>
        public long Coins { get; private set; }

        /// <summary>
        /// 코인이 변경되었을 때 HUD 등에 현재 코인을 전달합니다.
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
            services.Events.Maintenance += OnMaintenance;
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
            services.Events.Maintenance -= OnMaintenance;
            services.Events.SettlementComputed -= OnSettlementComputed;
        }

        /// <summary>
        /// 코인을 추가합니다.
        ///
        /// 차량 도착, Flow Burst, 오프라인 정산뿐만 아니라
        /// BasicEconomySystem의 주간 정산에서도 사용할 수 있습니다.
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
        /// 코인 사용을 시도합니다.
        /// 성공하면 코인을 차감하고 true를 반환합니다.
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
        /// 저장 시스템에 전달할 코인 데이터를 생성합니다.
        /// </summary>
        public EconomySaveData CreateSnapshot()
        {
            return new EconomySaveData
            {
                Coins = Coins
            };
        }

        /// <summary>
        /// 저장된 코인 데이터를 복원합니다.
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
        /// Deducts road maintenance without allowing a negative balance.
        /// </summary>
        private void OnMaintenance(MaintenanceEvent e)
        {
            if (e.Cost <= 0L)
            {
                return;
            }

            long deducted = Math.Min(Coins, e.Cost);
            Coins -= deducted;
            if (deducted > 0L)
            {
                PublishCoinsChanged();
            }

            Debug.Log(
                "[EconomyService] Deducted " + deducted +
                " coins from maintenance. Requested: " + e.Cost +
                ", Current coins: " + Coins
            );
        }

        /// <summary>
        /// 오프라인 정산 계산이 완료되었을 때 호출됩니다.
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
        /// 코인 변경 이벤트를 발생시킵니다.
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