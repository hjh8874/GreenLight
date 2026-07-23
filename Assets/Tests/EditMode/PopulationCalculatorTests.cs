using System.Collections.Generic;
using CityFlow.Content;
using CityFlow.Contracts;
using CityFlow.Sim;
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

        [Test]
        public void RestoredTiles_KeepSingleAnchorsForDerivedStateRebuilds()
        {
            SimConfig config = SimConfig.Default();
            var source = new SimEngine(config, new SimEventHub());

            var house = new Vector2Int(2, 2);
            var school = new Vector2Int(5, 2);
            var hospital = new Vector2Int(2, 5);

            Assert.IsTrue(source.Place(house, TileType.House));
            Assert.IsTrue(source.Place(school, TileType.School));
            Assert.IsTrue(source.Place(hospital, TileType.Hospital));

            var restored = new SimEngine(config, new SimEventHub());
            restored.RestoreSnapshot(source.CreateSnapshot());

            List<Vector2Int> houses =
                CollectAnchors(restored, config, TileType.House);
            List<Vector2Int> restoredSchools =
                CollectAnchors(restored, config, TileType.School);
            List<Vector2Int> hospitals =
                CollectAnchors(restored, config, TileType.Hospital);

            CollectionAssert.AreEqual(new[] { house }, houses);
            CollectionAssert.AreEqual(
                new[] { school },
                restoredSchools
            );
            CollectionAssert.AreEqual(new[] { hospital }, hospitals);

            int population =
                PopulationCalculator.CalculatePopulation(
                    TileType.House,
                    houses[0],
                    basePopulation: 2,
                    schoolCoverageBonus: 2,
                    schoolCoverageRadius: 3,
                    schoolTiles: restoredSchools
                );

            Assert.AreEqual(4, population);
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

        private static List<Vector2Int> CollectAnchors(
            IReadOnlyTileData tileData,
            SimConfig config,
            TileType expectedType
        )
        {
            var anchors = new List<Vector2Int>();

            for (int y = 0;
                 y < config.GridHeight;
                 y++)
            {
                for (int x = 0;
                     x < config.GridWidth;
                     x++)
                {
                    var tile = new Vector2Int(x, y);

                    if (tileData.GetTileType(tile) ==
                            expectedType &&
                        tileData.IsFootprintAnchor(tile))
                    {
                        anchors.Add(tile);
                    }
                }
            }

            return anchors;
        }
    }
}
