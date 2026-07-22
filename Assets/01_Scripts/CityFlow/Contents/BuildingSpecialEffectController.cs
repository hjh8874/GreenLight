using System;
using UnityEngine;

namespace CityFlow.Content
{
    /// <summary>
    /// BuildingDefinitionSO에 정의된 학교 커버리지와 병원 이벤트 효과를
    /// 런타임에서 실제로 소비하는 컴포넌트입니다.
    /// </summary>
    public sealed class BuildingSpecialEffectController : MonoBehaviour
    {
        [SerializeField]
        private BuildingDefinitionSO buildingDefinition;

        private float patientEventElapsedSeconds;

        public event Action PatientEventRequested;
        public event Action<int> EmergencyTransportCompleted;

        public BuildingDefinitionSO BuildingDefinition => buildingDefinition;

        public bool IsSchool =>
            buildingDefinition != null &&
            buildingDefinition.category == BuildingCategory.Education;

        public bool IsHospital =>
            buildingDefinition != null &&
            buildingDefinition.category == BuildingCategory.Medical;

        private void Update()
        {
            TickPatientEvent(Time.deltaTime);
        }

        /// <summary>
        /// 학교가 현재 주거 건물을 몇 개까지 커버할 수 있는지 반환합니다.
        /// </summary>
        public int CalculateCoveredResidentialCount(int residentialBuildingCount)
        {
            if (!IsSchool)
            {
                return 0;
            }

            int safeResidentialCount = Mathf.Max(0, residentialBuildingCount);
            return Mathf.Min(
                safeResidentialCount,
                buildingDefinition.SchoolCoverageCapacity);
        }

        /// <summary>
        /// 학교 커버를 받은 주거 건물에 적용할 총 인구 상한 보너스를 계산합니다.
        /// </summary>
        public int CalculatePopulationCapBonus(int residentialBuildingCount)
        {
            int coveredResidentialCount =
                CalculateCoveredResidentialCount(residentialBuildingCount);

            return coveredResidentialCount *
                   buildingDefinition.CoveredPopulationCapBonus;
        }

        /// <summary>
        /// 병원 데이터의 이벤트 간격을 기준으로 환자 이벤트 발생 시간을 누적합니다.
        /// </summary>
        public void TickPatientEvent(float deltaTime)
        {
            if (!IsHospital)
            {
                patientEventElapsedSeconds = 0f;
                return;
            }

            float interval =
                buildingDefinition.PatientEventIntervalSeconds;

            if (interval <= 0f)
            {
                patientEventElapsedSeconds = 0f;
                return;
            }

            patientEventElapsedSeconds += Mathf.Max(0f, deltaTime);

            if (patientEventElapsedSeconds < interval)
            {
                return;
            }

            patientEventElapsedSeconds %= interval;
            PatientEventRequested?.Invoke();
        }

        /// <summary>
        /// 환자 이송 완료 시 병원 데이터에 설정된 보상을 외부 경제 시스템에 전달합니다.
        /// </summary>
        public int CompleteEmergencyTransport()
        {
            if (!IsHospital)
            {
                return 0;
            }

            int reward = buildingDefinition.EmergencyReward;
            EmergencyTransportCompleted?.Invoke(reward);
            return reward;
        }

        /// <summary>
        /// 프리팹 또는 런타임 생성 코드에서 사용할 건물 데이터를 지정합니다.
        /// </summary>
        public void SetBuildingDefinition(BuildingDefinitionSO definition)
        {
            buildingDefinition = definition;
            patientEventElapsedSeconds = 0f;
        }
    }
}
