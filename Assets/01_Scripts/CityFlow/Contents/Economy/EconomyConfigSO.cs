using UnityEngine;

namespace CityFlow.Content
{
    [CreateAssetMenu(
        fileName = "EconomyConfig",
        menuName = "CityFlow/Content/Economy Config"
    )]
    public class EconomyConfigSO : ScriptableObject
    {
        [Header("도착 보상")]
        [Tooltip("차량 또는 흐름 1회가 도착했을 때 주간 수익에 누적되는 기본 코인")]
        public int baseCoinPerArrival = 10;

        [Tooltip("도착 건물 보상 배율 기본값")]
        public float defaultDestinationRewardMultiplier = 1f;

        [Header("주간 정산")]
        [Tooltip("게임 시간 기준 며칠마다 정산할지. 기본값은 7일")]
        public int settlementDays = 7;

        [Tooltip("건물 1개당 주간 정산에 추가되는 기본 코인")]
        public int weeklyCoinPerBuilding = 20;

        [Header("도시 성장")]
        public int smallCityCost = 500;
        public int middleCityCost = 2000;
        public int bigCityCost = 8000;

        [Header("토지 매입")]
        public int firstLandCost = 500;
        public float landCostMultiplier = 2f;

        [Header("업그레이드")]
        public int baseUpgradeCost = 100;
        public float upgradeCostMultiplier = 1.6f;

        public int GetArrivalCoin(float destinationMultiplier)
        {
            return Mathf.RoundToInt(baseCoinPerArrival * destinationMultiplier);
        }

        public int GetWeeklyBuildingIncome(int buildingCount)
        {
            return buildingCount * weeklyCoinPerBuilding;
        }

        public int GetLandCost(int purchasedLandCount)
        {
            return Mathf.RoundToInt(firstLandCost * Mathf.Pow(landCostMultiplier, purchasedLandCount));
        }

        public int GetUpgradeCost(int currentLevel)
        {
            return Mathf.RoundToInt(baseUpgradeCost * Mathf.Pow(upgradeCostMultiplier, currentLevel));
        }
    }
}