using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Content
{
    /// <summary>
    /// 학교 건물의 커버리지와 인구 상한 보너스를 계산합니다.
    ///
    /// 현재 단계에서는 계산 책임만 담당합니다.
    /// 실제 적용은 건물 배치 또는 스쿨버스 방문 완료 로직에서
    /// 이 서비스를 호출해 처리합니다.
    /// </summary>
    public sealed class BuildingEffectService
    {
        /// <summary>
        /// 학교가 혜택을 제공할 수 있는 주거 건물 수를 계산합니다.
        ///
        /// 주거 건물 수가 학교 수용량보다 많으면
        /// SchoolCoverageCapacity까지만 인정합니다.
        /// </summary>
        /// <param name="school">학교 건물 데이터</param>
        /// <param name="nearbyBuildings">
        /// 학교 주변 또는 스쿨버스가 방문한 건물 목록
        /// </param>
        /// <returns>학교 혜택을 받는 주거 건물 수</returns>
        public int CalculateCoveredHouseCount(
            BuildingDefinitionSO school,
            IReadOnlyList<BuildingDefinitionSO> nearbyBuildings)
        {
            if (!IsValidSchool(school) || nearbyBuildings == null)
            {
                return 0;
            }

            int residentialCount = 0;

            for (int i = 0; i < nearbyBuildings.Count; i++)
            {
                BuildingDefinitionSO building = nearbyBuildings[i];

                if (building == null)
                {
                    continue;
                }

                if (building.category != BuildingCategory.Residential)
                {
                    continue;
                }

                residentialCount++;

                // 필요한 수량을 모두 찾았다면
                // 남은 건물을 더 확인하지 않습니다.
                if (residentialCount >= school.SchoolCoverageCapacity)
                {
                    break;
                }
            }

            return Mathf.Min(
                residentialCount,
                school.SchoolCoverageCapacity);
        }

        /// <summary>
        /// 학교 혜택을 받은 주거 건물 수를 기준으로
        /// 전체 인구 상한 보너스를 계산합니다.
        /// </summary>
        /// <param name="school">학교 건물 데이터</param>
        /// <param name="coveredHouseCount">
        /// 실제로 학교 혜택을 받은 주거 건물 수
        /// </param>
        /// <returns>추가되는 전체 인구 상한</returns>
        public int CalculatePopulationCapBonus(
            BuildingDefinitionSO school,
            int coveredHouseCount)
        {
            if (!IsValidSchool(school))
            {
                return 0;
            }

            int safeCoveredHouseCount = Mathf.Clamp(
                coveredHouseCount,
                0,
                school.SchoolCoverageCapacity);

            return safeCoveredHouseCount *
                   school.CoveredPopulationCapBonus;
        }

        /// <summary>
        /// 방문한 건물 목록을 바탕으로
        /// 학교의 전체 인구 상한 보너스를 한 번에 계산합니다.
        /// </summary>
        /// <param name="school">학교 건물 데이터</param>
        /// <param name="visitedBuildings">
        /// 스쿨버스가 실제로 방문했거나 학교가 커버한 건물 목록
        /// </param>
        /// <returns>추가되는 전체 인구 상한</returns>
        public int CalculatePopulationCapBonus(
            BuildingDefinitionSO school,
            IReadOnlyList<BuildingDefinitionSO> visitedBuildings)
        {
            int coveredHouseCount = CalculateCoveredHouseCount(
                school,
                visitedBuildings);

            return CalculatePopulationCapBonus(
                school,
                coveredHouseCount);
        }

        /// <summary>
        /// 전달받은 건물이 유효한 학교인지 확인합니다.
        /// </summary>
        private static bool IsValidSchool(
            BuildingDefinitionSO school)
        {
            if (school == null)
            {
                return false;
            }

            if (school.category != BuildingCategory.Education)
            {
                return false;
            }

            if (school.SchoolCoverageCapacity <= 0)
            {
                return false;
            }

            return true;
        }
    }
}