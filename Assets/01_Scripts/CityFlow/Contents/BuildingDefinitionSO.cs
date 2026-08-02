using CityFlow.Contracts;
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
        Transit,        // 대중교통: 버스정류장, 지하철역
        Medical,        // 의료: 병원, 보건소
        Civic           // 공공: 경찰서, 행정 시설
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

        [SerializeField]
        private SpecialBuildingMenuCategory menuCategory =
            SpecialBuildingMenuCategory.Commercial;

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

        [Header("Visuals")]

        [SerializeField]
        private Sprite buildingIcon;

        [SerializeField]
        private GameObject visualPrefab;

        [SerializeField]
        private Vector3 visualOffset;

        [SerializeField]
        private Vector3 visualEulerAngles;

        [SerializeField]
        private Vector3 visualScale = Vector3.one;

        [SerializeField]
        private Color fallbackColor = new Color(0.25f, 0.65f, 0.8f, 1f);

        [SerializeField]
        [Min(0.1f)]
        private float fallbackHeight = 1f;

        [Header("Placement")]

        [SerializeField]
        private Vector2Int footprint = new Vector2Int(2, 2);

        [Header("Future Research")]

        [SerializeField]
        private string requiredResearchId;

        [Header("Visitor Demand")]

        [SerializeField]
        private bool canReceiveVisitors;

        [SerializeField]
        private VisitCadence visitCadence = new VisitCadence(1, 1);

        [SerializeField]
        [Min(0)]
        private int visitorCapacity;

        [SerializeField]
        [Min(0f)]
        private float attractionWeight = 1f;

        [SerializeField]
        [Min(0)]
        private int coinPerVisit;

        [SerializeField]
        [Tooltip("방문 차량이 몰리는 시간대. AllDay = 24시간 균등(기존 동작).")]
        private VisitTimeProfile visitTimeProfile = VisitTimeProfile.AllDay;

        [Header("Future Happiness")]

        [SerializeField]
        private string happinessEffectKey;

        public Sprite BuildingIcon => buildingIcon;
        public SpecialBuildingMenuCategory MenuCategory => menuCategory;
        public GameObject VisualPrefab => visualPrefab;
        public Vector3 VisualOffset => visualOffset;
        public Vector3 VisualEulerAngles => visualEulerAngles;
        public Vector3 VisualScale => visualScale;
        public Color FallbackColor => fallbackColor;
        public float FallbackHeight => Mathf.Max(0.1f, fallbackHeight);
        public Vector2Int Footprint => new Vector2Int(
            Mathf.Max(1, footprint.x),
            Mathf.Max(1, footprint.y));
        public string RequiredResearchId => requiredResearchId;
        public bool CanReceiveVisitors => canReceiveVisitors;
        public VisitCadence VisitCadence => visitCadence;
        public int VisitorCapacity => Mathf.Max(0, visitorCapacity);
        public float AttractionWeight => Mathf.Max(0f, attractionWeight);
        public int CoinPerVisit => Mathf.Max(0, coinPerVisit);
        public VisitTimeProfile VisitTimeProfile => visitTimeProfile;
        public string HappinessEffectKey => happinessEffectKey;

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

        [Header("병원 기능")]

        [SerializeField]
        [Min(0)]
        [Tooltip("병원이 의료 서비스를 제공하는 타일 반경")]
        private int hospitalCoverageRadius;

        [SerializeField]
        [Min(0)]
        [Tooltip("병원 한 채가 담당할 수 있는 최대 주거 건물 수")]
        private int hospitalPatientCapacity;

        /// <summary>
        /// 학교가 한 번에 혜택을 제공할 수 있는 최대 주거 건물 수입니다.
        /// </summary>
        public int SchoolCoverageCapacity =>
            Mathf.Max(0, schoolCoverageCapacity);

        /// <summary>
        /// 학교 혜택을 받는 주거 건물 한 채당 추가되는 인구 상한입니다.
        /// </summary>
        public int CoveredPopulationCapBonus =>
            Mathf.Max(0, coveredPopulationCapBonus);

        /// <summary>
        /// 병원이 의료 서비스를 제공하는 타일 반경입니다.
        /// </summary>
        public int HospitalCoverageRadius =>
            Mathf.Max(0, hospitalCoverageRadius);

        /// <summary>
        /// 병원 한 채가 담당할 수 있는 최대 주거 건물 수입니다.
        /// </summary>
        public int HospitalPatientCapacity =>
            Mathf.Max(0, hospitalPatientCapacity);


        /// <summary>
        /// 이 건물 데이터가 학교인지 확인합니다.
        /// </summary>
        public bool IsSchool =>
            category == BuildingCategory.Education;

        /// <summary>
        /// 이 건물 데이터가 병원인지 확인합니다.
        /// </summary>
        public bool IsHospital =>
            category == BuildingCategory.Medical;

#if UNITY_EDITOR
        /// <summary>
        /// Inspector 수정 또는 에셋 로드 시 잘못된 값을 안전한 범위로 보정합니다.
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

            visualScale.x = Mathf.Max(0.01f, visualScale.x);
            visualScale.y = Mathf.Max(0.01f, visualScale.y);
            visualScale.z = Mathf.Max(0.01f, visualScale.z);
            fallbackHeight = Mathf.Max(0.1f, fallbackHeight);
            footprint.x = Mathf.Max(1, footprint.x);
            footprint.y = Mathf.Max(1, footprint.y);
            requiredResearchId = requiredResearchId?.Trim();
            visitCadence = visitCadence.Sanitized();
            visitorCapacity = Mathf.Max(0, visitorCapacity);
            attractionWeight = Mathf.Max(0f, attractionWeight);
            coinPerVisit = Mathf.Max(0, coinPerVisit);
            happinessEffectKey = happinessEffectKey?.Trim();

            schoolCoverageCapacity =
                Mathf.Max(0, schoolCoverageCapacity);

            coveredPopulationCapBonus =
                Mathf.Max(0, coveredPopulationCapBonus);

            hospitalCoverageRadius =
                Mathf.Max(0, hospitalCoverageRadius);

            hospitalPatientCapacity =
                Mathf.Max(0, hospitalPatientCapacity);
        }
#endif
    }
}
