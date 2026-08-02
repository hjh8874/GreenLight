using NUnit.Framework;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Sim.Tests
{
    public class VisitTimeProfileSamplerTests
    {
        [Test]
        public void AllDay_MatchesLegacyUniformFormula()
        {
            const int count = 7;
            for (int i = 0; i < count; i++)
            {
                float legacy = 24f * (i + 0.5f) / count;
                float sampled = VisitTimeProfileSampler.SampleHour(
                    VisitTimeProfile.AllDay, i, count);
                Assert.That(sampled, Is.EqualTo(legacy).Within(1e-3f));
            }
        }

        [Test]
        public void Evening_AllSamplesInsideWindow()
        {
            const int count = 10;
            for (int i = 0; i < count; i++)
            {
                float hour = VisitTimeProfileSampler.SampleHour(
                    VisitTimeProfile.Evening, i, count);
                Assert.That(hour,
                    Is.GreaterThanOrEqualTo(17f).And.LessThan(23f),
                    $"index {i}");
            }
        }

        [Test]
        public void Rush_SamplesFallOnlyInMorningOrEveningPeaks()
        {
            const int count = 16;
            for (int i = 0; i < count; i++)
            {
                float hour = VisitTimeProfileSampler.SampleHour(
                    VisitTimeProfile.Rush, i, count);
                bool morning = hour >= 6f && hour < 10f;
                bool evening = hour >= 17f && hour < 21f;
                Assert.That(morning || evening, Is.True,
                    $"index {i} → {hour}");
            }
        }

        [Test]
        public void Rush_OddCountMiddleIndex_StaysInsideWindows()
        {
            float hour = VisitTimeProfileSampler.SampleHour(
                VisitTimeProfile.Rush, index: 1, count: 3);
            bool morning = hour >= 6f && hour < 10f;
            bool evening = hour >= 17f && hour < 21f;
            Assert.That(morning || evening, Is.True, $"hour {hour}");
        }

        [Test]
        public void SampleHour_IsMonotonicInIndex()
        {
            const int count = 32;
            float previous = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                float hour = VisitTimeProfileSampler.SampleHour(
                    VisitTimeProfile.Afternoon, i, count);
                Assert.That(hour, Is.GreaterThanOrEqualTo(previous));
                previous = hour;
            }
        }

        [Test]
        public void SingleVisit_LandsMidWindow()
        {
            // Evening 창 [17,23)의 중앙 = 20시
            float hour = VisitTimeProfileSampler.SampleHour(
                VisitTimeProfile.Evening, 0, 1);
            Assert.That(hour, Is.EqualTo(20f).Within(1e-3f));
        }

        [Test]
        public void BuildOption_DefaultsVisitTimeProfileToAllDay()
        {
            var option = new SpecialBuildingBuildOption(
                "mall", "Mall", "Commercial", string.Empty,
                null, Color.white,
                SpecialBuildingMenuCategory.Commercial,
                buildCost: 100, isUnlocked: true,
                requiredResearchId: string.Empty,
                canReceiveVisitors: true, visitsPerPeriod: 1,
                periodDays: 7, visitorCapacity: 4,
                attractionWeight: 1f, coinPerVisit: 10);
            Assert.That(option.VisitTimeProfile,
                Is.EqualTo(VisitTimeProfile.AllDay));
        }
    }
}
