using UnityEngine;

namespace CityFlow.Content
{
    /// <summary>
    /// 건물의 용도 분류입니다.
    /// UI 분류, 건설 제한, 건물 효과 판별 등에 사용합니다.
    /// </summary>
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

    /// <summary>
    /// 건물 하나의 기본 데이터와 밸런스 값을 관리하는 ScriptableObject입니다.
    /// </summary>
    [CreateAssetMenu(
        fileName = "BuildingDefinition",
        menuName = "CityFlow/Content/Building Definition"
    )]
    public class BuildingDefinitionSO : ScriptableObject
    {
        [Header("기본 정보")]

        [Tooltip("저장 및 조회에 사용하는 건물 고유 ID")]
        public string buildingId;

        [Tooltip("UI에 표시하는 건물 이름")]
        public string buildingName;

        [Tooltip("건물의 용도 분류")]
        public BuildingCategory category;

        [TextArea]
        [Tooltip("건물 설명")]
        public string description;

        [Header("교통 역할")]

        [Tooltip("이 건물이 차량 이동 수요를 생성할 수 있는지 여부")]
        public bool canGenerateTraffic;

        [Tooltip("이 건물이 차량의 목적지가 될 수 있는지 여부")]
        public bool canReceiveTraffic;

        [Min(0)]
        [Tooltip("이 건물이 만들어내는 기본 이동량")]
        public int trafficGenerationAmount = 1;

        [Min(0f)]
        [Tooltip("이 건물에 도착했을 때 적용하는 보상 배율")]
        public float destinationRewardMultiplier = 1f;

        [Header("경제")]

        [Min(0)]
        [Tooltip("건물 건설 비용")]
        public int buildCost = 100;

        [Min(0)]
        [Tooltip("하루 정산 시 이 건물이 제공하는 기본 코인")]
        public int dailyCoinValue = 20;

        [Header("번성도")]

        [Min(0)]
        [Tooltip("건물을 배치했을 때 증가하는 번성도")]
        public int prosperityValue = 1;

        [Header("해금")]

        [Tooltip("게임 시작 시 기본으로 해금되는 건물인지 여부")]
        public bool unlockedByDefault;

        [Header("학교 기능")]

        [SerializeField]
        [Min(0)]
        [Tooltip(
            "학교 또는 스쿨버스 한 대가 혜택을 제공할 수 있는 " +
            "최대 주거 건물 수"
        )]
        private int schoolCoverageCapacity;

        [SerializeField]
        [Min(0)]
        [Tooltip(
            "학교 혜택을 받는 주거 건물 한 채당 " +
            "추가되는 인구 상한"
        )]
        private int coveredPopulationCapBonus;

        /// <summary>
        /// 학교가 한 번에 혜택을 제공할 수 있는 최대 주거 건물 수입니다.
        /// 음수 데이터가 직렬화되어 있어도 외부에는 0 이상만 반환합니다.
        /// </summary>
        public int SchoolCoverageCapacity =>
            Mathf.Max(0, schoolCoverageCapacity);

        /// <summary>
        /// 학교 혜택을 받는 주거 건물 한 채당 추가되는 인구 상한입니다.
        /// 음수 데이터가 직렬화되어 있어도 외부에는 0 이상만 반환합니다.
        /// </summary>
        public int CoveredPopulationCapBonus =>
            Mathf.Max(0, coveredPopulationCapBonus);

        /// <summary>
        /// 이 건물 데이터가 학교인지 확인합니다.
        /// </summary>
        public bool IsSchool =>
            category == BuildingCategory.Education;

#if UNITY_EDITOR
        /// <summary>
        /// Inspector 수정 또는 에셋 로드 시 잘못된 값을 안전한 범위로 보정합니다.
        /// Min 속성만으로 보호되지 않는 기존 YAML 값도 여기서 정리합니다.
        /// </summary>
        private void OnValidate()
        {
            buildingId = buildingId?.Trim();
            buildingName = buildingName?.Trim();

            trafficGenerationAmount =
                Mathf.Max(0, trafficGenerationAmount);

            destinationRewardMultiplier =
                Mathf.Max(0f, destinationRewardMultiplier);

            buildCost =
                Mathf.Max(0, buildCost);

            dailyCoinValue =
                Mathf.Max(0, dailyCoinValue);

            prosperityValue =
                Mathf.Max(0, prosperityValue);

            schoolCoverageCapacity =
                Mathf.Max(0, schoolCoverageCapacity);

            coveredPopulationCapBonus =
                Mathf.Max(0, coveredPopulationCapBonus);
        }
#endif
    }
}