using System;
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public sealed class SchoolZoneTests
    {
        private static Vector2Int V(int x, int y) =>
            new Vector2Int(x, y);

        private static SimConfig SchoolConfig()
        {
            SimConfig config = CarSimTests.Cfg();
            config.SchoolMorningStartHour = 7.5f;
            config.SchoolMorningEndHour = 8.5f;
            config.SchoolReturnStartHour = 14f;
            config.SchoolReturnEndHour = 15f;
            return config;
        }

        [Test]
        public void SchoolZone_RoadWithinRadiusTwoIsMarked()
        {
            var map = new SchoolZoneMap(10, 10);
            map.Rebuild(new[] { V(5, 5) });

            Assert.IsTrue(map.IsSchoolZone(V(5, 7)));
        }

        [Test]
        public void SchoolZone_RoadOutsideRadiusTwoIsNotMarked()
        {
            var map = new SchoolZoneMap(10, 10);
            map.Rebuild(new[] { V(5, 5) });

            Assert.IsFalse(map.IsSchoolZone(V(5, 8)));
            Assert.IsFalse(map.IsSchoolZone(V(7, 7)));
        }

        [Test]
        public void SchoolZone_StandardCarSlowsDuringSchoolWindow()
        {
            SchoolZoneMap map = BuildMap();

            Assert.AreEqual(
                SchoolZoneMap.SchoolZoneNumerator,
                map.GetEffectiveNumerator(60, V(5, 7), 8f, SchoolConfig()));
            Assert.AreEqual(
                SchoolZoneMap.SchoolZoneNumerator,
                map.GetEffectiveNumerator(60, V(5, 7), 14.5f, SchoolConfig()));
        }

        [Test]
        public void SchoolZone_StandardCarIsNormalOutsideSchoolWindow()
        {
            SchoolZoneMap map = BuildMap();

            Assert.AreEqual(
                60,
                map.GetEffectiveNumerator(60, V(5, 7), 10f, SchoolConfig()));
        }

        [Test]
        public void SchoolZone_RemovingSchoolClearsTheZone()
        {
            SimConfig config = SchoolConfig();
            var grid = new CityGrid(10, 10);
            Assert.IsTrue(grid.Place(V(5, 5), TileType.School));
            Assert.IsTrue(grid.Place(V(5, 7), TileType.Road));
            var roads = new RoadNetwork(grid);
            var demands = new DemandMap(config);
            demands.RegisterCompany(V(5, 5), TileType.School, 0d);
            demands.Reassign(grid, roads);
            Assert.IsTrue(demands.IsSchoolZone(V(5, 7)));

            demands.RemoveCompany(V(5, 5));

            Assert.IsFalse(demands.IsSchoolZone(V(5, 7)));
        }

        [Test]
        public void SchoolZone_WithoutSchoolsMarksNoTile()
        {
            var map = new SchoolZoneMap(10, 10);
            map.Rebuild(Array.Empty<Vector2Int>());

            Assert.IsFalse(map.IsSchoolZone(V(5, 5)));
            Assert.IsFalse(map.IsSchoolZone(V(0, 0)));
        }

        private static SchoolZoneMap BuildMap()
        {
            var map = new SchoolZoneMap(10, 10);
            map.Rebuild(new[] { V(5, 5) });
            return map;
        }
    }
}
