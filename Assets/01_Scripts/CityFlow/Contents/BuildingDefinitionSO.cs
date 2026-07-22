using UnityEngine;

namespace CityFlow.Content
{
    public enum BuildingCategory
    {
        Residential,    // 주거: 집, 아파트
        Business,       // 업무: 회사, 오피스
        Education,      // 교육: 학교
        Commercial,     // 상업: 식당, 쇼핑몰
        Utility,        // 보조: 주유소, 공영주차장
        Finance,        // 금융: 은행
        Transit         // 대중교통: 버스정류장, 지하철역
    }

    [CreateAssetMenu(
        fileName = "BuildingDefinition",
        menuName = "CityFlow/Content/Building Definition")]
    public class BuildingDefinitionSO : ScriptableObject
    {
        [Header("기본 정보")]
        public string buildingId;
        public string buildingName;
        public BuildingCategory category;

        [TextArea]
        public string description;

        [Header("교통 역할")]
        public bool canGenerateTraffic;
        public bool canReceiveTraffic;

        [Min(0)]
        [Tooltip("이 건물이 만들어내는 기본 이동량")]
        public int trafficGenerationAmount = 1;

        [Min(0f)]
        [Tooltip("이 건물에 도착했을 때 적용하는 보상 배율")]
        public float destinationRewardMultiplier = 1f;

        [Header("경제")]
        [Min(0)]
        public int buildCost = 100;

        [Min(0)]
        [Tooltip("하루 정산 시 이 건물이 제공하는 기본 코인")]
        public int dailyCoinValue = 20;

        [Header("번성도")]
        [Min(0)]
        [Tooltip("건물을 배치했을 때 증가하는 번성도")]
        public int prosperityValue = 1;

        [Header("해금")]
        public bool unlockedByDefault;

        [Header("학교 기능")]
        [SerializeField]
        [Min(0)]
        [Tooltip("학교 또는 스쿨버스 한 대가 혜택을 제공할 수 있는 최대 주거 건물 수")]
        private int schoolCoverageCapacity;

        [SerializeField]
        [Min(0)]
        [Tooltip("학교 혜택을 받는 주거 건물 한 채당 추가되는 인구 상한")]
        private int coveredPopulationCapBonus;

        public int SchoolCoverageCapacity => schoolCoverageCapacity;

        public int CoveredPopulationCapBonus =>
            coveredPopulationCapBonus;

#if UNITY_EDITOR
        private void OnValidate()
        {
            buildingId = buildingId?.Trim();
            buildingName = buildingName?.Trim();
        }
#endif
    }
}