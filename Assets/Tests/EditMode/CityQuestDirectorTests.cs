using CityFlow.Sim.Quests;
using NUnit.Framework;

namespace CityFlow.Sim.Tests
{
    public class CityQuestDirectorTests
    {
        private static CityQuestSnapshot Snapshot(
            int roads = 0,
            int houses = 0,
            int offices = 0,
            int schools = 0,
            long arrivals = 0,
            long pending = 0,
            bool harvested = false,
            int jams = 0,
            float stability = 1f,
            int usedRoads = 0,
            int maxRoads = 60)
        {
            return new CityQuestSnapshot(roads, houses, offices, schools, arrivals, pending, harvested, jams, stability, usedRoads, maxRoads);
        }

        [Test]
        public void BlankCity_StartsWithRoadQuest()
        {
            var director = new CityQuestDirector();
            Assert.IsTrue(director.Tick(Snapshot(), 0.5f));
            Assert.AreEqual(CityQuestId.BuildRoad, director.ActiveQuest.Id);
        }

        [Test]
        public void Tutorial_AdvancesInFixedOrder()
        {
            var director = new CityQuestDirector();
            director.Tick(Snapshot(), 0.5f);

            Assert.IsTrue(director.Tick(Snapshot(roads: 3), 0.5f));
            Assert.IsNull(director.ActiveQuest);

            director.Tick(Snapshot(roads: 3), 3f);
            Assert.AreEqual(CityQuestId.BuildHouse, director.ActiveQuest.Id);

            director.Tick(Snapshot(roads: 3, houses: 1), 0.5f);
            director.Tick(Snapshot(roads: 3, houses: 1), 3f);
            Assert.AreEqual(CityQuestId.BuildOffice, director.ActiveQuest.Id);
        }

        [Test]
        public void CloseAction_MinimizesWithoutCompletingQuest()
        {
            var director = new CityQuestDirector();
            director.Tick(Snapshot(), 0.5f);

            Assert.IsTrue(director.Minimize());
            Assert.IsTrue(director.IsMinimized);
            Assert.AreEqual(CityQuestId.BuildRoad, director.ActiveQuest.Id);

            Assert.IsTrue(director.Restore());
            Assert.IsFalse(director.IsMinimized);
        }

        [Test]
        public void CongestionQuest_RequiresPersistentCondition()
        {
            var director = new CityQuestDirector();
            CityQuestSnapshot tutorialDone = Snapshot(roads: 3, houses: 1, offices: 1, schools: 1, arrivals: 1, harvested: true, jams: 1);

            Assert.IsFalse(director.Tick(tutorialDone, 9f));
            Assert.IsNull(director.ActiveQuest);

            Assert.IsTrue(director.Tick(tutorialDone, 1f));
            Assert.AreEqual(CityQuestId.ResolveCongestion, director.ActiveQuest.Id);
        }

        [Test]
        public void HigherPriorityNeed_WinsWhenSeveralAreEligible()
        {
            var director = new CityQuestDirector();
            CityQuestSnapshot needs = Snapshot(roads: 3, houses: 7, offices: 1, arrivals: 1, harvested: true, jams: 1, stability: 0.5f);

            director.Tick(needs, 10f);

            Assert.AreEqual(CityQuestId.ResolveCongestion, director.ActiveQuest.Id);
        }
    }
}
