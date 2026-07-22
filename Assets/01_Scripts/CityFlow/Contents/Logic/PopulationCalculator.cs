using System;
using System.Collections.Generic;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Content
{
    public static class PopulationCalculator
    {
        public static int CalculatePopulation(
            TileType tileType,
            Vector2Int tile,
            int basePopulation,
            int schoolCoverageBonus,
            int schoolCoverageRadius,
            IEnumerable<Vector2Int> schoolTiles
        )
        {
            int safeBasePopulation =
                Math.Max(0, basePopulation);

            if (tileType != TileType.House ||
                safeBasePopulation == 0 ||
                schoolCoverageBonus <= 0 ||
                schoolCoverageRadius < 0 ||
                schoolTiles == null)
            {
                return safeBasePopulation;
            }

            foreach (Vector2Int schoolTile in schoolTiles)
            {
                if (!IsWithinSchoolCoverage(
                    tile,
                    schoolTile,
                    schoolCoverageRadius
                ))
                {
                    continue;
                }

                long coveredPopulation =
                    (long)safeBasePopulation +
                    schoolCoverageBonus;

                return coveredPopulation > int.MaxValue
                    ? int.MaxValue
                    : (int)coveredPopulation;
            }

            return safeBasePopulation;
        }

        public static bool IsWithinSchoolCoverage(
            Vector2Int tile,
            Vector2Int schoolTile,
            int schoolCoverageRadius
        )
        {
            if (schoolCoverageRadius < 0)
            {
                return false;
            }

            long horizontalDistance =
                Math.Abs(
                    (long)tile.x -
                    schoolTile.x
                );
            long verticalDistance =
                Math.Abs(
                    (long)tile.y -
                    schoolTile.y
                );

            return horizontalDistance +
                verticalDistance <=
                schoolCoverageRadius;
        }
    }
}
