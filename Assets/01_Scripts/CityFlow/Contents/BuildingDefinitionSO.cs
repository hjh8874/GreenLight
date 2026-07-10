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
        menuName = "CityFlow/Content/Building Definition"
    )]
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

        [Tooltip("이 건물이 만들어내는 기본 이동량")]
        public int trafficGenerationAmount = 1;

        [Tooltip("이 건물에 도착했을 때 보상 배율")]
        public float destinationRewardMultiplier = 1f;

        [Header("경제")]
        public int buildCost = 100;

        [Tooltip("하루 정산 시 이 건물이 제공하는 기본 코인")]
        public int dailyCoinValue = 20;

        [Header("번성도")]
        [Tooltip("건물을 배치했을 때 증가하는 번성도")]
        public int prosperityValue = 1;

        [Header("해금")]
        public bool unlockedByDefault = false;
    }
}
