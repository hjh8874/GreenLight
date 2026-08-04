using UnityEngine;
using UnityEngine.Serialization;

namespace CityFlow.Content
{
    [CreateAssetMenu(
        fileName = "EconomyConfig",
        menuName = "CityFlow/Content/Economy Config"
    )]
    public class EconomyConfigSO : ScriptableObject
    {
        [Header("도착 보상")]
        [Tooltip(
            "교통수단 1대가 목적지에 도착했을 때 지급되는 기본 코인입니다. " +
            "소수점 없이 정수만 사용합니다."
        )]
        [FormerlySerializedAs("baseCoinPerArrival")]
        [Min(0)]
        [SerializeField]
        private int coinBase = 5;

        [Tooltip(
            "목적지 종류에 따른 기본 보상 비율입니다. " +
            "100은 기본 보상, 150은 1.5배, 200은 2배입니다."
        )]
        [Min(0)]
        [SerializeField]
        private int defaultDestinationRewardPercent = 100;

        [Header("주간 정산")]
        [Tooltip("게임 시간 기준 며칠마다 정산할지 설정합니다.")]
        [Min(1)]
        [SerializeField]
        private int settlementDays = 7;

        [Header("도시 성장 비용")]
        [Tooltip(
            "도시 단계별 승급 비용입니다. " +
            "순서: 소도시, 중도시, 대도시"
        )]
        [SerializeField]
        private int[] cityUnlockCosts =
        {
            1000,
            5000,
            20000
        };

        [Header("토지 매입 비용")]
        [Tooltip(
            "토지를 구매하는 순서대로 적용되는 정수 가격표입니다. " +
            "예: 첫 번째 확장 500, 두 번째 확장 900"
        )]
        [SerializeField]
        private int[] landCosts =
        {
            500,
            900,
            1500,
            2400,
            3800
        };

        [Header("업그레이드 비용")]
        [Tooltip(
            "업그레이드 레벨별 정수 가격표입니다. " +
            "배열 0번은 0레벨에서 1레벨로 올릴 때의 비용입니다."
        )]
        [SerializeField]
        private int[] upgradeCosts =
        {
            200,
            350,
            600,
            1000,
            1600
        };

        [Header("철거 환급")]
        [Tooltip(
            "건설 직후 적용되는 최초 환급률입니다. " +
            "100은 건설 비용 전액 환급입니다."
        )]
        [Range(0, 100)]
        [SerializeField]
        private int initialRefundPercent = 100;

        [Tooltip(
            "환급 감소 주기마다 차감되는 비율입니다. " +
            "10이면 100%에서 90%로 감소합니다."
        )]
        [Range(0, 100)]
        [SerializeField]
        private int refundDecreasePercent = 10;

        [Tooltip(
            "환급률이 더 이상 내려가지 않는 최저값입니다."
        )]
        [Range(0, 100)]
        [SerializeField]
        private int minimumRefundPercent = 50;

        [Tooltip(
            "환급률이 감소하는 게임 내 일수 단위입니다. " +
            "1이면 하루마다 감소합니다."
        )]
        [Min(1)]
        [SerializeField]
        private int refundDecreaseIntervalDays = 1;

        [Header("게임 시간")]
        [Tooltip(
            "게임 내 하루가 현실에서 몇 분인지 설정합니다."
        )]
        [Min(1)]
        [SerializeField]
        private int realMinutesPerGameDay = 14;

        [Header("Flow Burst")]
        [Tooltip(
            "Flow Burst 중 도착 보상 비율입니다. " +
            "200이면 기본 보상의 2배입니다."
        )]
        [Min(100)]
        [SerializeField]
        private int flowBurstRewardPercent = 200;

        [Tooltip("Flow Burst가 유지되는 현실 시간(초)입니다.")]
        [Min(1)]
        [SerializeField]
        private int flowBurstDurationSeconds = 10;

        /*
         * 외부 시스템에서 설정값을 읽을 때 사용하는 프로퍼티입니다.
         * 값 변경은 Inspector의 EconomyConfigSO 에셋에서만 수행합니다.
         */

        public int CoinBase => coinBase;

        public int DefaultDestinationRewardPercent =>
            defaultDestinationRewardPercent;

        public int SettlementDays => settlementDays;

        public int InitialRefundPercent =>
            initialRefundPercent;

        public int RefundDecreasePercent =>
            refundDecreasePercent;

        public int MinimumRefundPercent =>
            minimumRefundPercent;

        public int RefundDecreaseIntervalDays =>
            refundDecreaseIntervalDays;

        public int RealMinutesPerGameDay =>
            realMinutesPerGameDay;

        public int FlowBurstRewardPercent =>
            flowBurstRewardPercent;

        public int FlowBurstDurationSeconds =>
            flowBurstDurationSeconds;

        /// <summary>
        /// 목적지 보상 비율을 적용한 도착 코인을 계산합니다.
        ///
        /// 모든 계산은 정수로 처리하며,
        /// 소수점이 발생하는 나머지는 버립니다.
        ///
        /// rewardPercent 예시:
        /// 100 = 기본 보상
        /// 150 = 1.5배
        /// 200 = 2배
        /// </summary>
        public int GetArrivalCoin(
            int rewardPercent
        )
        {
            if (coinBase <= 0 || rewardPercent <= 0)
            {
                return 0;
            }

            return coinBase * rewardPercent / 100;
        }

        /// <summary>
        /// 기본 목적지 보상 비율을 적용한 도착 코인을 반환합니다.
        /// </summary>
        public int GetDefaultArrivalCoin()
        {
            return GetArrivalCoin(
                defaultDestinationRewardPercent
            );
        }

        /// <summary>
        /// Flow Burst 보상 비율을 적용한 도착 코인을 반환합니다.
        /// </summary>
        public int GetFlowBurstArrivalCoin()
        {
            return GetArrivalCoin(
                flowBurstRewardPercent
            );
        }

        /// <summary>
        /// 구매한 토지 수를 기준으로
        /// 다음 토지의 가격을 반환합니다.
        ///
        /// 가격표 범위를 넘어가면 0을 반환하여
        /// 추가 구매가 진행되지 않도록 합니다.
        /// </summary>
        public int GetLandCost(
            int purchasedLandCount
        )
        {
            int safeIndex =
                Mathf.Max(0, purchasedLandCount);

            return GetCostFromTable(
                landCosts,
                safeIndex,
                "토지"
            );
        }

        /// <summary>
        /// 현재 업그레이드 레벨을 기준으로
        /// 다음 업그레이드 비용을 반환합니다.
        ///
        /// 배열 0번은 0레벨에서 1레벨로
        /// 올릴 때의 비용입니다.
        /// </summary>
        public int GetUpgradeCost(
            int currentLevel
        )
        {
            int safeIndex =
                Mathf.Max(0, currentLevel);

            return GetCostFromTable(
                upgradeCosts,
                safeIndex,
                "업그레이드"
            );
        }

        /// <summary>
        /// 도시 승급 단계에 맞는 비용을 반환합니다.
        ///
        /// cityUpgradeIndex:
        /// 0 = 마을에서 소도시
        /// 1 = 소도시에서 중도시
        /// 2 = 중도시에서 대도시
        /// </summary>
        public int GetCityUnlockCost(
            int cityUpgradeIndex
        )
        {
            int safeIndex =
                Mathf.Max(0, cityUpgradeIndex);

            return GetCostFromTable(
                cityUnlockCosts,
                safeIndex,
                "도시 승급"
            );
        }

        /// <summary>
        /// 건설 후 경과한 게임 일수를 기준으로
        /// 철거 환급률을 계산합니다.
        ///
        /// 예시:
        /// 0일 = 100%
        /// 1일 = 90%
        /// 2일 = 80%
        /// 5일 이상 = 50%
        /// </summary>
        public int GetRefundPercent(
            int elapsedGameDays
        )
        {
            int safeElapsedDays =
                Mathf.Max(0, elapsedGameDays);

            int decreaseCount =
                safeElapsedDays /
                refundDecreaseIntervalDays;

            int calculatedPercent =
                initialRefundPercent -
                decreaseCount *
                refundDecreasePercent;

            return Mathf.Max(
                minimumRefundPercent,
                calculatedPercent
            );
        }

        /// <summary>
        /// 건설 당시 실제 지불한 가격과 경과 일수를 기준으로
        /// 철거 환급 코인을 계산합니다.
        ///
        /// 소수점은 사용하지 않으며,
        /// 나머지는 자동으로 버려집니다.
        /// </summary>
        public int GetRefundCoin(
            int paidBuildCost,
            int elapsedGameDays
        )
        {
            if (paidBuildCost <= 0)
            {
                return 0;
            }

            int refundPercent =
                GetRefundPercent(
                    elapsedGameDays
                );

            return paidBuildCost *
                   refundPercent /
                   100;
        }

        /// <summary>
        /// 가격 배열에서 지정된 순서의 값을 가져옵니다.
        /// 정의되지 않은 순서라면 0을 반환합니다.
        /// </summary>
        private int GetCostFromTable(
            int[] costTable,
            int index,
            string costName
        )
        {
            if (costTable == null ||
                costTable.Length == 0)
            {
                Debug.LogWarning(
                    $"[EconomyConfigSO] " +
                    $"{costName} 가격표가 비어 있습니다.",
                    this
                );

                return 0;
            }

            if (index < 0 ||
                index >= costTable.Length)
            {
                Debug.LogWarning(
                    $"[EconomyConfigSO] " +
                    $"{costName} 가격표 범위를 벗어났습니다. " +
                    $"Index: {index}, " +
                    $"Count: {costTable.Length}",
                    this
                );

                return 0;
            }

            return Mathf.Max(
                0,
                costTable[index]
            );
        }

#if UNITY_EDITOR
        /// <summary>
        /// Inspector에서 값을 변경할 때
        /// 잘못된 음수와 환급률 설정을 자동 보정합니다.
        /// </summary>
        private void OnValidate()
        {
            coinBase =
                Mathf.Max(0, coinBase);

            defaultDestinationRewardPercent =
                Mathf.Max(
                    0,
                    defaultDestinationRewardPercent
                );

            settlementDays =
                Mathf.Max(1, settlementDays);

            refundDecreaseIntervalDays =
                Mathf.Max(
                    1,
                    refundDecreaseIntervalDays
                );

            realMinutesPerGameDay =
                Mathf.Max(
                    1,
                    realMinutesPerGameDay
                );

            flowBurstRewardPercent =
                Mathf.Max(
                    100,
                    flowBurstRewardPercent
                );

            flowBurstDurationSeconds =
                Mathf.Max(
                    1,
                    flowBurstDurationSeconds
                );

            if (minimumRefundPercent >
                initialRefundPercent)
            {
                minimumRefundPercent =
                    initialRefundPercent;
            }

            ClampCostTable(cityUnlockCosts);
            ClampCostTable(landCosts);
            ClampCostTable(upgradeCosts);
        }

        private void ClampCostTable(
            int[] costTable
        )
        {
            if (costTable == null)
            {
                return;
            }

            for (int i = 0;
                 i < costTable.Length;
                 i++)
            {
                costTable[i] =
                    Mathf.Max(
                        0,
                        costTable[i]
                    );
            }
        }
#endif
    }
}
