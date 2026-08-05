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
            int jams = 0)
        {
            return new CityQuestSnapshot(roads, houses, offices, schools, arrivals, pending, harvested, jams);
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
        public void CommuteQuest_RequiresAnActualArrival()
        {
            var director = new CityQuestDirector();

            director.Tick(
                Snapshot(roads: 3, houses: 1, offices: 1),
                0.5f);

            Assert.AreEqual(CityQuestId.ConnectCommute, director.ActiveQuest.Id);
        }

        [Test]
        public void RestoredCompletedTutorial_DoesNotShowFirstHarvestQuest()
        {
            var director = new CityQuestDirector();
            director.RestoreTutorialStage(5);

            director.Tick(
                Snapshot(roads: 3, houses: 1, offices: 1),
                0.5f);

            Assert.IsTrue(director.IsTutorialComplete);
            Assert.AreNotEqual(
                CityQuestId.HarvestFirstIncome,
                director.ActiveQuest?.Id);
        }

        [Test]
        public void HarvestTutorial_WaitsUntilIncomeIsActuallyPending()
        {
            var director = new CityQuestDirector();
            director.RestoreTutorialStage(4);

            Assert.IsFalse(director.Tick(
                Snapshot(roads: 3, houses: 1, offices: 1, arrivals: 1),
                0.5f));
            Assert.IsNull(director.ActiveQuest);

            Assert.IsTrue(director.Tick(
                Snapshot(
                    roads: 3,
                    houses: 1,
                    offices: 1,
                    arrivals: 1,
                    pending: 10),
                0.5f));
            Assert.AreEqual(
                CityQuestId.HarvestFirstIncome,
                director.ActiveQuest.Id);
        }

        [Test]
        public void ResumedTutorial_UsesResumeMessage()
        {
            var director = new CityQuestDirector();
            director.SetResumeMode(true);
            director.RestoreTutorialStage(3);

            director.Tick(
                Snapshot(roads: 3, houses: 1, offices: 1),
                0.5f);

            Assert.AreEqual(
                "출근길을 다시 확인해 주세요",
                director.ActiveQuest.Title);
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
            CityQuestSnapshot needs = Snapshot(roads: 3, houses: 7, offices: 1, arrivals: 1, harvested: true, jams: 1);

            director.Tick(needs, 10f);

            Assert.AreEqual(CityQuestId.ResolveCongestion, director.ActiveQuest.Id);
        }

        [Test]
        public void SchoolQuest_RequiresOneSchoolPerTenHouses()
        {
            var director = new CityQuestDirector();
            director.RestoreTutorialStage(5);

            director.Tick(
                Snapshot(
                    roads: 20,
                    houses: 10,
                    offices: 2,
                    schools: 1,
                    arrivals: 1,
                    harvested: true),
                120f);
            Assert.AreNotEqual(
                CityQuestId.BuildSchool,
                director.ActiveQuest?.Id);

            director.Tick(
                Snapshot(
                    roads: 20,
                    houses: 11,
                    offices: 2,
                    schools: 1,
                    arrivals: 1,
                    harvested: true),
                5f);
            Assert.AreEqual(
                CityQuestId.BuildSchool,
                director.ActiveQuest?.Id,
                "주택이 학교 한 곳의 10채 수용 기준을 넘으면 학교 건설 퀘스트가 나타나야 한다.");
        }

    }
}
