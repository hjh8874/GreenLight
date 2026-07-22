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
        Transit,        // 대중교통: 버스정류장, 지하철역
        Medical         // 의료: 병원
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

        [Min(0)]
        [Tooltip("이 건물이 만들어내는 기본 이동량")]
        public int trafficGenerationAmount = 1;

        [Min(0f)]
        [Tooltip("이 건물에 도착했을 때 보상 배율")]
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
        public bool unlockedByDefault = false;

        [Header("학교 기능")]
        [SerializeField]
        [Min(0)]
        [Tooltip("학교 또는 스쿨버스 한 대가 커버할 수 있는 최대 주거 건물 수")]
        private int schoolCoverageCapacity;

        [SerializeField]
        [Min(0)]
        [Tooltip("학교 커버를 받은 주거 건물에 추가되는 인구 상한")]
        private int coveredPopulationCapBonus;

        [Header("병원 기능")]
        [SerializeField]
        [Min(0)]
        [Tooltip("환자 이송 완료 시 지급하는 기본 보상")]
        private int emergencyReward;

        [SerializeField]
        [Min(0f)]
        [Tooltip("환자 이벤트 발생 간격. 0이면 병원 이벤트를 사용하지 않음")]
        private float patientEventIntervalSeconds;

        // 외부 코드에서는 값을 읽을 수만 있고 직접 변경할 수 없습니다.
        public int SchoolCoverageCapacity => schoolCoverageCapacity;
        public int CoveredPopulationCapBonus => coveredPopulationCapBonus;
        public int EmergencyReward => emergencyReward;
        public float PatientEventIntervalSeconds =>
            patientEventIntervalSeconds;

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Inspector에서 입력한 문자열의 앞뒤 공백을 제거합니다.
            buildingId = buildingId?.Trim();
            buildingName = buildingName?.Trim();
        }
#endif
    }
}