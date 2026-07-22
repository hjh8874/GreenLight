using System.Collections.Generic;
using CityFlow.Content;
using CityFlow.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Sim.Tests
{
    public class PopulationCalculatorTests
    {
        private static readonly Vector2Int HouseTile =
            new Vector2Int(4, 4);

        [Test]
        public void CalculatePopulation_NoSchool_ReturnsBasePopulation()
        {
            int population =
                PopulationCalculator.CalculatePopulation(
                    TileType.House,
                    HouseTile,
                    basePopulation: 2,
                    schoolCoverageBonus: 2,
                    schoolCoverageRadius: 3,
                    schoolTiles: new Vector2Int[0]
                );

            Assert.AreEqual(2, population);
        }

        [Test]
        public void CalculatePopulation_SchoolWithinRadius_AddsCoverageBonus()
        {
            int population =
                PopulationCalculator.CalculatePopulation(
                    TileType.House,
                    HouseTile,
                    basePopulation: 2,
                    schoolCoverageBonus: 2,
                    schoolCoverageRadius: 3,
                    schoolTiles: new[]
                    {
                        new Vector2Int(6, 5)
                    }
                );

            Assert.AreEqual(4, population);
        }

        [Test]
        public void CalculatePopulation_SchoolRemoved_ReturnsToBasePopulation()
        {
            var schools =
                new HashSet<Vector2Int>
                {
                    new Vector2Int(6, 5)
                };

            Assert.AreEqual(
                4,
                CalculatePopulation(schools)
            );

            schools.Remove(new Vector2Int(6, 5));

            Assert.AreEqual(
                2,
                CalculatePopulation(schools)
            );
        }

        private static int CalculatePopulation(
            IEnumerable<Vector2Int> schools
        )
        {
            return PopulationCalculator.CalculatePopulation(
                TileType.House,
                HouseTile,
                basePopulation: 2,
                schoolCoverageBonus: 2,
                schoolCoverageRadius: 3,
                schoolTiles: schools
            );
        }
    }
}
