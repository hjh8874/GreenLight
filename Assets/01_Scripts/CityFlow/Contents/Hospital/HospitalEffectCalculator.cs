using System;
using System.Collections.Generic;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Content
{
    /// <summary>
    /// 병원의 커버 범위와 수용량을 기준으로
    /// 의료 혜택을 받는 주거 타일을 계산합니다.
    /// </summary>
    public static class HospitalEffectCalculator
    {
        /// <summary>
        /// 주거 타일이 병원의 의료 범위 안에 있는지 확인합니다.
        /// 맨해튼 거리를 사용합니다.
        /// </summary>
        public static bool IsWithinHospitalCoverage(
            Vector2Int houseTile,
            Vector2Int hospitalTile,
            int coverageRadius)
        {
            if (coverageRadius < 0)
            {
                return false;
            }

            long horizontalDistance =
                Math.Abs(
                    (long)houseTile.x -
                    hospitalTile.x);

            long verticalDistance =
                Math.Abs(
                    (long)houseTile.y -
                    hospitalTile.y);

            return horizontalDistance +
                verticalDistance <=
                coverageRadius;
        }

        /// <summary>
        /// 병원 한 채가 담당할 수 있는 주거 타일을 계산합니다.
        /// 입력 목록 순서가 우선순위가 됩니다.
        /// </summary>
        public static int CalculateCoveredHouseCount(
            Vector2Int hospitalTile,
            int coverageRadius,
            int patientCapacity,
            IReadOnlyList<Vector2Int> houseTiles)
        {
            if (patientCapacity <= 0 ||
                coverageRadius < 0 ||
                houseTiles == null)
            {
                return 0;
            }

            int coveredCount = 0;

            for (int i = 0; i < houseTiles.Count; i++)
            {
                if (!IsWithinHospitalCoverage(
                    houseTiles[i],
                    hospitalTile,
                    coverageRadius))
                {
                    continue;
                }

                coveredCount++;

                if (coveredCount >= patientCapacity)
                {
                    break;
                }
            }

            return coveredCount;
        }

        /// <summary>
        /// 의료 혜택을 받는 집 수를 기준으로
        /// 총 안정도 보너스를 계산합니다.
        /// </summary>
        public static int CalculateStabilityBonus(
            int coveredHouseCount,
            int stabilityBonusPerHouse)
        {
            if (coveredHouseCount <= 0 ||
                stabilityBonusPerHouse <= 0)
            {
                return 0;
            }

            long calculatedBonus =
                (long)coveredHouseCount *
                stabilityBonusPerHouse;

            return calculatedBonus >= int.MaxValue
                ? int.MaxValue
                : (int)calculatedBonus;
        }
    }
}