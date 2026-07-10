using UnityEngine;

namespace CityFlow.Content
{
    public class BasicEconomySystem : MonoBehaviour
    {
        [Header("경제 설정 파일")]
        [SerializeField] private EconomyConfigSO economyConfig;

        [Header("현재 재화")]
        [SerializeField] private EconomyWallet wallet = new EconomyWallet();

        [Header("이번 주 누적 수익")]
        [SerializeField] private int weeklyAccumulatedCoin;

        public EconomyWallet Wallet => wallet;
        public int WeeklyAccumulatedCoin => weeklyAccumulatedCoin;

        private void Awake()
        {
            if (economyConfig == null)
            {
                Debug.LogWarning("[Economy] EconomyConfigSO가 연결되지 않았습니다.");
            }
        }

        // 차량 또는 흐름이 목적지에 도착했을 때 호출
        // 바로 코인을 주지 않고, 이번 주 수익에 누적한다.
        public void AddArrivalToWeeklyIncome(float destinationMultiplier = 1f)
        {
            if (economyConfig == null)
                return;

            int reward = economyConfig.GetArrivalCoin(destinationMultiplier);

            weeklyAccumulatedCoin += reward;

            Debug.Log($"[Economy] 도착 수익 누적: +{reward} / 이번 주 누적: {weeklyAccumulatedCoin}");
        }

        // 건물 수 기준 주간 수익 추가
        public void AddWeeklyBuildingIncome(int buildingCount)
        {
            if (economyConfig == null)
                return;

            int income = economyConfig.GetWeeklyBuildingIncome(buildingCount);

            weeklyAccumulatedCoin += income;

            Debug.Log($"[Economy] 건물 주간 수익 누적: +{income} / 건물 수: {buildingCount}");
        }

        // 게임 시간 기준 1주일이 끝났을 때 호출
        public void ClaimWeeklySettlement()
        {
            if (weeklyAccumulatedCoin <= 0)
            {
                Debug.Log("[Economy] 정산할 주간 수익이 없습니다.");
                return;
            }

            wallet.AddCoin(weeklyAccumulatedCoin);

            Debug.Log($"[Economy] 주간 정산 지급: {weeklyAccumulatedCoin} Coin / 현재 코인: {wallet.Coin}");

            weeklyAccumulatedCoin = 0;
        }

        // 토지 매입
        public bool TryPurchaseLand(int purchasedLandCount)
        {
            if (economyConfig == null)
                return false;

            int cost = economyConfig.GetLandCost(purchasedLandCount);

            if (!wallet.SpendCoin(cost))
            {
                Debug.Log($"[Economy] 토지 매입 실패: 코인 부족 / 필요 코인: {cost}");
                return false;
            }

            Debug.Log($"[Economy] 토지 매입 성공 / 사용 코인: {cost}");
            return true;
        }

        // 업그레이드 구매
        public bool TryPurchaseUpgrade(int currentUpgradeLevel)
        {
            if (economyConfig == null)
                return false;

            int cost = economyConfig.GetUpgradeCost(currentUpgradeLevel);

            if (!wallet.SpendCoin(cost))
            {
                Debug.Log($"[Economy] 업그레이드 실패: 코인 부족 / 필요 코인: {cost}");
                return false;
            }

            Debug.Log($"[Economy] 업그레이드 성공 / 사용 코인: {cost}");
            return true;
        }
    }
}
