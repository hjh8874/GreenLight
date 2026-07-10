using CityFlow.Gameplay.Economy;
using UnityEngine;

namespace CityFlow.Content
{
    public class BasicEconomySystem : MonoBehaviour
    {
        [Header("경제 설정 파일")]
        [SerializeField] private EconomyConfigSO economyConfig;

        [Header("실제 코인 관리 서비스")]
        [SerializeField] private EconomyService economyService;

        [Header("이번 주 누적 수익")]
        [SerializeField] private long weeklyAccumulatedCoin;

        /// <summary>
        /// 이번 주에 누적된 정산 대기 코인입니다.
        /// 아직 실제 보유 코인에는 포함되지 않습니다.
        /// </summary>
        public long WeeklyAccumulatedCoin =>
            weeklyAccumulatedCoin;

        private void Awake()
        {
            ValidateReferences();
        }

        /// <summary>
        /// 필요한 컴포넌트와 설정 파일이 연결되어 있는지 확인합니다.
        /// </summary>
        private void ValidateReferences()
        {
            if (economyConfig == null)
            {
                Debug.LogWarning(
                    "[BasicEconomySystem] " +
                    "EconomyConfigSO가 연결되지 않았습니다.",
                    this
                );
            }

            if (economyService == null)
            {
                economyService =
                    FindFirstObjectByType<EconomyService>();
            }

            if (economyService == null)
            {
                Debug.LogError(
                    "[BasicEconomySystem] " +
                    "EconomyService를 찾을 수 없습니다.",
                    this
                );
            }
        }

        /// <summary>
        /// 차량 또는 흐름이 목적지에 도착했을 때 발생한 수익을
        /// 주간 정산 금액에 누적합니다.
        ///
        /// EconomyService가 도착 보상을 즉시 지급하고 있다면
        /// 이 함수를 같이 호출하면 중복 지급됩니다.
        /// </summary>
        public void AddArrivalToWeeklyIncome(
            float destinationMultiplier = 1f
        )
        {
            if (!CanCalculate())
            {
                return;
            }

            int reward =
                economyConfig.GetArrivalCoin(
                    destinationMultiplier
                );

            AddWeeklyIncome(
                reward,
                "arrival"
            );
        }

        /// <summary>
        /// 현재 배치된 건물 수를 기준으로
        /// 주간 건물 수익을 누적합니다.
        /// </summary>
        public void AddWeeklyBuildingIncome(
            int buildingCount
        )
        {
            if (!CanCalculate())
            {
                return;
            }

            if (buildingCount <= 0)
            {
                Debug.Log(
                    "[BasicEconomySystem] " +
                    "주간 수익을 발생시킬 건물이 없습니다."
                );

                return;
            }

            int income =
                economyConfig.GetWeeklyBuildingIncome(
                    buildingCount
                );

            AddWeeklyIncome(
                income,
                "building income"
            );
        }

        /// <summary>
        /// 지정된 금액을 이번 주 수익에 누적합니다.
        /// 이 시점에는 실제 보유 코인이 증가하지 않습니다.
        /// </summary>
        public void AddWeeklyIncome(
            long amount,
            string reason = "weekly income"
        )
        {
            if (amount <= 0L)
            {
                return;
            }

            weeklyAccumulatedCoin += amount;

            Debug.Log(
                $"[BasicEconomySystem] Weekly income added. " +
                $"Reason: {reason}, " +
                $"Added: {amount}, " +
                $"Accumulated: {weeklyAccumulatedCoin}"
            );
        }

        /// <summary>
        /// 주간 누적 수익을 실제 보유 코인으로 지급합니다.
        ///
        /// 실제 코인은 EconomyService에서 관리하며,
        /// 지급 후 주간 누적 수익은 0으로 초기화됩니다.
        /// </summary>
        public bool ClaimWeeklySettlement()
        {
            if (economyService == null)
            {
                Debug.LogError(
                    "[BasicEconomySystem] " +
                    "EconomyService가 없어 주간 정산을 " +
                    "지급할 수 없습니다."
                );

                return false;
            }

            if (weeklyAccumulatedCoin <= 0L)
            {
                Debug.Log(
                    "[BasicEconomySystem] " +
                    "정산할 주간 수익이 없습니다."
                );

                return false;
            }

            long settlementAmount =
                weeklyAccumulatedCoin;

            economyService.AddCoins(
                settlementAmount,
                "weekly settlement"
            );

            weeklyAccumulatedCoin = 0L;

            Debug.Log(
                $"[BasicEconomySystem] " +
                $"Weekly settlement completed. " +
                $"Paid: {settlementAmount}, " +
                $"Current coins: {economyService.Coins}"
            );

            return true;
        }

        /// <summary>
        /// 현재 토지 가격을 계산하고
        /// EconomyService의 실제 코인을 사용합니다.
        /// </summary>
        public bool TryPurchaseLand(
            int purchasedLandCount
        )
        {
            if (!CanPurchase())
            {
                return false;
            }

            int safeLandCount =
                Mathf.Max(0, purchasedLandCount);

            int cost =
                economyConfig.GetLandCost(
                    safeLandCount
                );

            if (!economyService.TrySpend(cost))
            {
                Debug.Log(
                    $"[BasicEconomySystem] " +
                    $"Land purchase failed. " +
                    $"Required: {cost}, " +
                    $"Current: {economyService.Coins}"
                );

                return false;
            }

            Debug.Log(
                $"[BasicEconomySystem] " +
                $"Land purchase completed. " +
                $"Spent: {cost}, " +
                $"Remaining: {economyService.Coins}"
            );

            return true;
        }

        /// <summary>
        /// 현재 업그레이드 가격을 계산하고
        /// EconomyService의 실제 코인을 사용합니다.
        /// </summary>
        public bool TryPurchaseUpgrade(
            int currentUpgradeLevel
        )
        {
            if (!CanPurchase())
            {
                return false;
            }

            int safeUpgradeLevel =
                Mathf.Max(0, currentUpgradeLevel);

            int cost =
                economyConfig.GetUpgradeCost(
                    safeUpgradeLevel
                );

            if (!economyService.TrySpend(cost))
            {
                Debug.Log(
                    $"[BasicEconomySystem] " +
                    $"Upgrade purchase failed. " +
                    $"Required: {cost}, " +
                    $"Current: {economyService.Coins}"
                );

                return false;
            }

            Debug.Log(
                $"[BasicEconomySystem] " +
                $"Upgrade purchase completed. " +
                $"Spent: {cost}, " +
                $"Remaining: {economyService.Coins}"
            );

            return true;
        }

        /// <summary>
        /// 토지를 구매하지 않고 현재 가격만 계산합니다.
        /// 구매 버튼이나 가격 UI에서 사용할 수 있습니다.
        /// </summary>
        public long GetLandCost(
            int purchasedLandCount
        )
        {
            if (economyConfig == null)
            {
                return 0L;
            }

            return economyConfig.GetLandCost(
                Mathf.Max(0, purchasedLandCount)
            );
        }

        /// <summary>
        /// 업그레이드를 구매하지 않고 현재 가격만 계산합니다.
        /// 구매 버튼이나 가격 UI에서 사용할 수 있습니다.
        /// </summary>
        public long GetUpgradeCost(
            int currentUpgradeLevel
        )
        {
            if (economyConfig == null)
            {
                return 0L;
            }

            return economyConfig.GetUpgradeCost(
                Mathf.Max(0, currentUpgradeLevel)
            );
        }

        /// <summary>
        /// 저장된 주간 누적 수익을 복원할 때 사용합니다.
        /// </summary>
        public void RestoreWeeklyAccumulatedCoin(
            long amount
        )
        {
            weeklyAccumulatedCoin =
                System.Math.Max(0L, amount);

            Debug.Log(
                $"[BasicEconomySystem] " +
                $"Weekly income restored: " +
                $"{weeklyAccumulatedCoin}"
            );
        }

        /// <summary>
        /// 새 게임 시작 또는 정산 데이터 초기화에 사용합니다.
        /// </summary>
        public void ResetWeeklyAccumulatedCoin()
        {
            weeklyAccumulatedCoin = 0L;
        }

        private bool CanCalculate()
        {
            if (economyConfig != null)
            {
                return true;
            }

            Debug.LogError(
                "[BasicEconomySystem] " +
                "EconomyConfigSO가 없어 계산할 수 없습니다."
            );

            return false;
        }

        private bool CanPurchase()
        {
            if (!CanCalculate())
            {
                return false;
            }

            if (economyService != null)
            {
                return true;
            }

            Debug.LogError(
                "[BasicEconomySystem] " +
                "EconomyService가 없어 구매할 수 없습니다."
            );

            return false;
        }
    }
}