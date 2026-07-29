using CityFlow.Gameplay.Economy;
using UnityEngine;

namespace CityFlow.Content
{
    // 수익은 트립(Arrival)뿐 — 건물 수 기반 passive 수익 금지(엔진 방향 문서 §P1).
    public class BasicEconomySystem : MonoBehaviour
    {
        [Header("경제 설정 파일")]
        [Tooltip(
            "가격 밸런스 시트 v0의 정수 값을 입력한 " +
            "EconomyConfigSO를 연결합니다."
        )]
        [SerializeField]
        private EconomyConfigSO economyConfig;

        [Header("실제 코인 관리 서비스")]
        [SerializeField]
        private EconomyService economyService;

        [Header("이번 주 누적 수익")]
        [Tooltip(
            "각 수익은 정수 코인으로 들어오며, " +
            "누적 합계는 long 타입으로 보관합니다."
        )]
        [SerializeField]
        private long weeklyAccumulatedCoin;

        /// <summary>
        /// 이번 주에 누적된 정산 대기 코인입니다.
        ///
        /// 개별 코인 가격과 보상은 정수(int)만 사용하지만,
        /// 장시간 누적될 수 있으므로 합계는 long으로 관리합니다.
        /// </summary>
        public long WeeklyAccumulatedCoin =>
            weeklyAccumulatedCoin;

        private void Awake()
        {
            ValidateReferences();
        }

        /// <summary>
        /// EconomyConfigSO와 EconomyService가
        /// 정상적으로 연결되어 있는지 확인합니다.
        /// </summary>
        private void ValidateReferences()
        {
            if (economyConfig == null)
            {
                Debug.LogWarning(
                    "[BasicEconomySystem] " +
                    "EconomyConfigSO가 연결되지 않았습니다. " +
                    "가격 밸런스 시트 v0를 반영한 설정 파일을 연결하세요.",
                    this
                );
            }

            if (economyService == null)
            {
                economyService =
                    FindAnyObjectByType<EconomyService>();
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
        /// 정수 코인을 이번 주 수익에 누적합니다.
        ///
        /// float과 double은 받지 않으므로
        /// 소수점 반올림 문제가 발생하지 않습니다.
        ///
        /// 이 시점에는 실제 보유 코인이 증가하지 않습니다.
        /// </summary>
        public void AddWeeklyIncome(
            int amount,
            string reason = "weekly income"
        )
        {
            if (amount <= 0)
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
        /// 이번 주 누적 수익을 실제 보유 코인으로 지급합니다.
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
        /// 가격 밸런스 시트 v0를 반영한
        /// 정수 토지 가격을 계산하고 구매합니다.
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

            if (cost <= 0)
            {
                Debug.LogWarning(
                    "[BasicEconomySystem] " +
                    $"잘못된 토지 가격입니다. Cost: {cost}",
                    this
                );

                return false;
            }

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
        /// 가격 밸런스 시트 v0를 반영한
        /// 정수 업그레이드 가격을 계산하고 구매합니다.
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

            if (cost <= 0)
            {
                Debug.LogWarning(
                    "[BasicEconomySystem] " +
                    $"잘못된 업그레이드 가격입니다. Cost: {cost}",
                    this
                );

                return false;
            }

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
        /// 토지를 구매하지 않고
        /// 현재 정수 가격만 반환합니다.
        ///
        /// 가격 UI와 구매 버튼 표시에서 사용합니다.
        /// </summary>
        public int GetLandCost(
            int purchasedLandCount
        )
        {
            if (economyConfig == null)
            {
                return 0;
            }

            return economyConfig.GetLandCost(
                Mathf.Max(0, purchasedLandCount)
            );
        }

        /// <summary>
        /// 업그레이드를 구매하지 않고
        /// 현재 정수 가격만 반환합니다.
        ///
        /// 가격 UI와 구매 버튼 표시에서 사용합니다.
        /// </summary>
        public int GetUpgradeCost(
            int currentUpgradeLevel
        )
        {
            if (economyConfig == null)
            {
                return 0;
            }

            return economyConfig.GetUpgradeCost(
                Mathf.Max(0, currentUpgradeLevel)
            );
        }

        /// <summary>
        /// 저장된 주간 누적 수익을 복원합니다.
        ///
        /// 누적 합계는 장시간 플레이를 고려하여
        /// long 타입으로 유지합니다.
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
        /// 새 게임 시작 또는
        /// 정산 데이터 초기화 시 사용합니다.
        /// </summary>
        public void ResetWeeklyAccumulatedCoin()
        {
            weeklyAccumulatedCoin = 0L;
        }

        /// <summary>
        /// 경제 계산에 필요한 EconomyConfigSO가
        /// 연결되어 있는지 확인합니다.
        /// </summary>
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

        /// <summary>
        /// 구매에 필요한 EconomyConfigSO와
        /// EconomyService가 모두 있는지 확인합니다.
        /// </summary>
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
