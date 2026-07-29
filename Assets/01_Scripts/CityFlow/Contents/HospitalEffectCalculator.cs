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

    }
}