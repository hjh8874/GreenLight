using System.Collections.Generic;
using CityFlow.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Tests
{
    public sealed class LeisureTripPlannerTests
    {
        [Test]
        public void SameDayAndHomes_SelectSameSet()
        {
            var homes = new List<Vector2Int>
            {
                new(1, 1), new(4, 2), new(8, 5), new(11, 3)
            };

            CollectionAssert.AreEqual(
                LeisureTripPlanner.SelectHouseholds(homes, 12, .5f),
                LeisureTripPlanner.SelectHouseholds(homes, 12, .5f));
        }

        [Test]
        public void RatioZero_IsDisabled()
        {
            Assert.IsEmpty(LeisureTripPlanner.SelectHouseholds(
                new[] { new Vector2Int(1, 1) }, 12, 0f));
        }

        [Test]
        public void WeekendRatio_IsHigher()
        {
            Assert.AreEqual(.5f, LeisureTripPlanner.EffectiveRatio(.25f, 5));
            Assert.AreEqual(.25f, LeisureTripPlanner.EffectiveRatio(.25f, 4));
        }

        [Test]
        public void DestinationResolution_UsesThreeFallbacks()
        {
            var home = new Vector2Int(1, 1);
            Assert.AreEqual(LeisureDestinationKind.SpecialBuilding,
                LeisureTripPlanner.ResolveDestination(home,
                    new[] { new Vector2Int(3, 3) },
                    new[] { new Vector2Int(7, 7) },
                    new[] { new Vector2Int(2, 1) }).Kind);
            Assert.AreEqual(LeisureDestinationKind.NeighbourHome,
                LeisureTripPlanner.ResolveDestination(home,
                    new Vector2Int[0],
                    new[] { new Vector2Int(7, 7) },
                    new Vector2Int[0]).Kind);
            Assert.AreEqual(LeisureDestinationKind.RoadLoop,
                LeisureTripPlanner.ResolveDestination(home,
                    new Vector2Int[0], new Vector2Int[0],
                    new[] { new Vector2Int(2, 1) }).Kind);
        }

        [Test]
        public void EveningTrips_AreInsideEveningProfile()
        {
            for (int i = 0; i < 20; i++)
            {
                float hour = LeisureTripPlanner.SampleEveningHour(i, 20);
                Assert.That(hour, Is.GreaterThanOrEqualTo(17f));
                Assert.That(hour, Is.LessThan(23f));
            }
        }
    }
}
