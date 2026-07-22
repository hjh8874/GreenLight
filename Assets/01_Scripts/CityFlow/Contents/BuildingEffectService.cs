using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Content
{
    /// <summary>
    /// 학교 건물의 커버리지와 인구 상한 보너스를 계산하는 서비스입니다.
    ///
    /// 현재 단계에서는 계산만 담당하며,
    /// 실제 게임 상태 반영은 호출하는 시스템에서 처리합니다.
    /// </summary>
    public sealed class BuildingEffectService
    {
        /// <summary>
        /// 학교가 혜택을 제공할 수 있는 주거 건물 수를 계산합니다.
        /// </summary>
        /// <param name="school">
        /// 학교 건물 데이터
        /// </param>
        /// <param name="nearbyBuildings">
        /// 학교 주변 또는 스쿨버스가 방문한 건물 목록
        /// </param>
        /// <returns>
        /// 학교 효과를 받는 주거 건물 수
        /// </returns>
        public int CalculateCoveredHouseCount(
            BuildingDefinitionSO school,
            IReadOnlyList<BuildingDefinitionSO> nearbyBuildings)
        {
            if (!IsValidSchool(school))
            {
                return 0;
            }

            if (nearbyBuildings == null)
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

                // 학교가 담당 가능한 최대 수에 도달하면 종료
                if (residentialCount >= school.SchoolCoverageCapacity)
                {
                    return residentialCount;
                }
            }

            return residentialCount;
        }

        /// <summary>
        /// 커버된 주거 건물 수를 이용하여
        /// 인구 상한 보너스를 계산합니다.
        /// </summary>
        /// <param name="school">
        /// 학교 건물 데이터
        /// </param>
        /// <param name="coveredHouseCount">
        /// 학교 혜택을 받는 주거 건물 수
        /// </param>
        /// <returns>
        /// 추가되는 인구 상한
        /// </returns>
        public int CalculatePopulationCapBonus(
            BuildingDefinitionSO school,
            int coveredHouseCount)
        {
            if (!IsValidSchool(school))
            {
                return 0;
            }

            int safeCoveredHouseCount =
                Mathf.Clamp(
                    coveredHouseCount,
                    0,
                    school.SchoolCoverageCapacity);

            long calculatedBonus =
                (long)safeCoveredHouseCount *
                school.CoveredPopulationCapBonus;

            calculatedBonus =
                Math.Max(0L, calculatedBonus);

            if (calculatedBonus >= int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int)calculatedBonus;
        }

        /// <summary>
        /// 건물 목록을 이용하여
        /// 인구 상한 보너스를 계산합니다.
        /// </summary>
        /// <param name="school">
        /// 학교 건물 데이터
        /// </param>
        /// <param name="visitedBuildings">
        /// 학교 효과를 받는 건물 목록
        /// </param>
        /// <returns>
        /// 추가되는 인구 상한
        /// </returns>
        public int CalculatePopulationCapBonus(
            BuildingDefinitionSO school,
            IReadOnlyList<BuildingDefinitionSO> visitedBuildings)
        {
            int coveredHouseCount =
                CalculateCoveredHouseCount(
                    school,
                    visitedBuildings);

            return CalculatePopulationCapBonus(
                school,
                coveredHouseCount);
        }

        /// <summary>
        /// 전달된 건물이 학교인지 확인합니다.
        /// </summary>
        private static bool IsValidSchool(
            BuildingDefinitionSO school)
        {
            if (school == null)
            {
                return false;
            }

            if (!school.IsSchool)
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