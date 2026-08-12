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
            bool connectedCommute = false,
            int hospitals = 0,
            string readyResearchId = "",
            string activeResearchId = "",
            string unbuiltSpecialBuildingId = "",
            bool schoolResearchUnlocked = false,
            bool hospitalResearchUnlocked = false,
            int signals = 0,
            int roundabouts = 0,
            int busStops = 0,
            bool intersectionFacilitiesAvailable = false,
            bool busStopsAvailable = false,
            bool busOperating = false)
        {
            return new CityQuestSnapshot(
                roads,
                houses,
                offices,
                schools,
                arrivals,
                pending,
                harvested,
                jams,
                connectedCommute,
                hospitals,
                readyResearchId,
                activeResearchId,
                unbuiltSpecialBuildingId,
                schoolResearchUnlocked,
                hospitalResearchUnlocked,
                signals,
                roundabouts,
                busStops,
                intersectionFacilitiesAvailable,
                busStopsAvailable,
                busOperating);
        }

        [Test]
        public void BlankCity_StartsWithRoadQuest()
        {
            var director = new CityQuestDirector();
            Assert.IsTrue(director.Tick(Snapshot(), 0.5f));
            Assert.AreEqual(CityQuestId.BuildRoad, director.ActiveQuest.Id);
        }

        [Test]
        public void NewGame_ShowsEveryPlayerShortcutGuideBeforeRoadQuest()
        {
            var director = new CityQuestDirector(showShortcutGuide: true);
            string allMessages = string.Empty;

            for (int page = 0;
                 page < CityQuestDirector.ShortcutGuideCount;
                 page++)
            {
                Assert.IsTrue(director.Tick(Snapshot(), 0.5f));
                Assert.IsTrue(director.ActiveQuest.CanAcknowledge);
                allMessages += "\n" + director.ActiveQuest.Message;
                Assert.IsTrue(director.Acknowledge());
            }

            Assert.IsTrue(director.Tick(Snapshot(), 0.5f));
            Assert.AreEqual(CityQuestId.BuildRoad, director.ActiveQuest.Id);
            StringAssert.Contains("Tab", allMessages);
            StringAssert.Contains("휠", allMessages);
            StringAssert.Contains("가운데 버튼", allMessages);
            StringAssert.Contains("마우스 뒤/앞 버튼", allMessages);
            StringAssert.DoesNotContain("건물 회전", allMessages);
            StringAssert.DoesNotContain("우클릭: 취소", allMessages);
            StringAssert.Contains("우클릭: 철거", allMessages);
            StringAssert.Contains("ESC", allMessages);
            StringAssert.Contains("1 / 2 / 3", allMessages);
            StringAssert.Contains("차량 좌클릭", allMessages);
            StringAssert.Contains("차량 뷰 진입", allMessages);
            StringAssert.Contains("차량 뷰 종료", allMessages);
            StringAssert.DoesNotContain("다음 신호", allMessages);
            StringAssert.DoesNotContain("신호 제어", allMessages);
        }

        [Test]
        public void NewGame_ShowsVehicleViewAsItsOwnShortcutGuide()
        {
            var director = new CityQuestDirector(showShortcutGuide: true);

            for (int page = 0; page < 2; page++)
            {
                director.Tick(Snapshot(), 0.5f);
                Assert.IsTrue(director.Acknowledge());
            }

            Assert.IsTrue(director.Tick(Snapshot(), 0.5f));
            Assert.AreEqual(
                CityQuestId.ShortcutVehicle,
                director.ActiveQuest.Id);
            StringAssert.Contains("차량 좌클릭", director.ActiveQuest.Message);
            StringAssert.Contains("ESC", director.ActiveQuest.Message);
            Assert.AreEqual("다음", director.ActiveQuest.ActionLabel);
        }

        [Test]
        public void RestoredGame_CanSkipShortcutGuide()
        {
            var director = new CityQuestDirector(showShortcutGuide: true);
            director.RestoreShortcutGuideStage(
                CityQuestDirector.ShortcutGuideCount);

            director.Tick(Snapshot(), 0.5f);

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
        public void CommuteQuest_RemainsUntilACommuteRouteIsConnected()
        {
            var director = new CityQuestDirector();

            director.Tick(
                Snapshot(roads: 3, houses: 1, offices: 1),
                0.5f);

            Assert.AreEqual(CityQuestId.ConnectCommute, director.ActiveQuest.Id);
        }

        [Test]
        public void CommuteQuest_CompletesWhenRouteIsConnectedBeforeArrival()
        {
            var director = new CityQuestDirector();
            director.RestoreTutorialStage(3);

            director.Tick(
                Snapshot(roads: 3, houses: 1, offices: 1),
                0.5f);

            Assert.AreEqual(CityQuestId.ConnectCommute, director.ActiveQuest.Id);
            Assert.IsTrue(director.Tick(
                Snapshot(
                    roads: 3,
                    houses: 1,
                    offices: 1,
                    connectedCommute: true),
                0.5f));
            Assert.IsNull(director.ActiveQuest);
            Assert.AreEqual(4, director.TutorialStage);
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
        public void HarvestTutorial_RemainsVisibleWhileWaitingForFirstIncome()
        {
            var director = new CityQuestDirector();
            director.RestoreTutorialStage(4);

            Assert.IsTrue(director.Tick(
                Snapshot(roads: 3, houses: 1, offices: 1, arrivals: 1),
                0.5f));
            Assert.AreEqual(
                CityQuestId.HarvestFirstIncome,
                director.ActiveQuest.Id);
            StringAssert.Contains(
                "차량이 회사에 도착하면",
                director.ActiveQuest.Message);
            StringAssert.Contains("HARVEST", director.ActiveQuest.Message);
        }

        [Test]
        public void OfficeBuiltWithConnectedRoute_ShowsIncomeQuestAfterTransitionDelay()
        {
            var director = new CityQuestDirector();
            director.RestoreTutorialStage(2);

            director.Tick(
                Snapshot(roads: 3, houses: 1),
                0.5f);
            Assert.AreEqual(CityQuestId.BuildOffice, director.ActiveQuest.Id);

            director.Tick(
                Snapshot(
                    roads: 3,
                    houses: 1,
                    offices: 1,
                    connectedCommute: true),
                0.5f);
            Assert.IsNull(director.ActiveQuest);

            director.Tick(
                Snapshot(
                    roads: 3,
                    houses: 1,
                    offices: 1,
                    connectedCommute: true),
                3f);

            Assert.AreEqual(
                CityQuestId.HarvestFirstIncome,
                director.ActiveQuest?.Id,
                "회사 건설과 동시에 출근길이 연결돼도 첫 수익을 기다리는 퀘스트가 이어져야 한다");
        }

        [Test]
        public void FirstHarvest_IsFollowedByResearchPreparationQuest()
        {
            var director = new CityQuestDirector();
            director.RestoreTutorialStage(4);

            director.Tick(
                Snapshot(
                    roads: 6,
                    houses: 1,
                    offices: 1,
                    connectedCommute: true),
                0.5f);
            Assert.AreEqual(CityQuestId.HarvestFirstIncome, director.ActiveQuest.Id);

            Assert.IsTrue(director.Tick(
                Snapshot(
                    roads: 6,
                    houses: 1,
                    offices: 1,
                    harvested: true,
                    connectedCommute: true),
                0.5f));
            Assert.IsNull(director.ActiveQuest);

            director.Tick(
                Snapshot(
                    roads: 6,
                    houses: 1,
                    offices: 1,
                    harvested: true,
                    connectedCommute: true),
                3f);

            Assert.AreEqual(
                CityQuestId.PrepareSchoolResearch,
                director.ActiveQuest?.Id);
            StringAssert.Contains("집 3채", director.ActiveQuest.Message);
            StringAssert.Contains("회사 2곳", director.ActiveQuest.Message);
        }

        [Test]
        public void ResearchPreparation_CompletesAtSchoolUnlockConditions()
        {
            var director = new CityQuestDirector();
            director.RestoreTutorialStage(5);

            Assert.IsTrue(director.Tick(
                Snapshot(
                    roads: 8,
                    houses: 3,
                    offices: 1,
                    harvested: true),
                1f));
            Assert.AreEqual(
                CityQuestId.PrepareSchoolResearch,
                director.ActiveQuest.Id);

            Assert.IsTrue(director.Tick(
                Snapshot(
                    roads: 8,
                    houses: 3,
                    offices: 2,
                    harvested: true),
                0.5f));
            Assert.IsNull(director.ActiveQuest);
        }

        [Test]
        public void ReadyResearch_SkipsPreparationAndShowsResearchQuest()
        {
            var director = new CityQuestDirector();
            director.RestoreTutorialStage(5);

            Assert.IsTrue(director.Tick(
                Snapshot(
                    roads: 6,
                    houses: 1,
                    offices: 1,
                    harvested: true,
                    readyResearchId: "research_building_coffee_shop"),
                2f));

            Assert.AreEqual(CityQuestId.StartResearch, director.ActiveQuest.Id);
        }

        [Test]
        public void TutorialComplete_WithNoSpecificNeed_ShowsGrowthMilestone()
        {
            var director = new CityQuestDirector();
            director.RestoreTutorialStage(5);

            Assert.IsTrue(director.Tick(
                Snapshot(
                    roads: 20,
                    houses: 5,
                    offices: 5,
                    schools: 1,
                    hospitals: 0,
                    harvested: true),
                0.5f));

            Assert.AreEqual(CityQuestId.ExpandCity, director.ActiveQuest.Id);
            StringAssert.Contains("15곳", director.ActiveQuest.Message);
        }

        [Test]
        public void GrowthMilestone_CompletesAtItsCapturedTarget()
        {
            var director = new CityQuestDirector();
            director.RestoreTutorialStage(5);

            director.Tick(
                Snapshot(
                    roads: 20,
                    houses: 5,
                    offices: 5,
                    schools: 1,
                    harvested: true),
                0.5f);
            Assert.AreEqual(CityQuestId.ExpandCity, director.ActiveQuest.Id);

            Assert.IsTrue(director.Tick(
                Snapshot(
                    roads: 20,
                    houses: 7,
                    offices: 7,
                    schools: 1,
                    harvested: true),
                0.5f));
            Assert.IsNull(director.ActiveQuest);
        }

        [Test]
        public void GrowthMilestone_YieldsToNewSpecificQuest()
        {
            var director = new CityQuestDirector();
            director.RestoreTutorialStage(5);

            director.Tick(
                Snapshot(
                    roads: 20,
                    houses: 5,
                    offices: 5,
                    schools: 1,
                    harvested: true),
                0.5f);
            Assert.AreEqual(CityQuestId.ExpandCity, director.ActiveQuest.Id);

            Assert.IsTrue(director.Tick(
                Snapshot(
                    roads: 20,
                    houses: 5,
                    offices: 5,
                    schools: 1,
                    harvested: true,
                    readyResearchId: "research_building_coffee_shop"),
                2f));

            Assert.AreEqual(CityQuestId.StartResearch, director.ActiveQuest.Id);
        }

        [Test]
        public void OfficeCapacityQuest_StartsOnlyAfterCapacityIsExceeded()
        {
            var director = new CityQuestDirector();
            director.RestoreTutorialStage(5);

            director.Tick(
                Snapshot(
                    roads: 72,
                    houses: 36,
                    offices: 6,
                    schools: 3,
                    hospitals: 1,
                    harvested: true),
                5f);

            Assert.AreNotEqual(
                CityQuestId.AddOfficeCapacity,
                director.ActiveQuest?.Id,
                "회사 6곳의 총 정원 36채가 정확히 찬 상태는 초과가 아니다");

            var exceededDirector = new CityQuestDirector();
            exceededDirector.RestoreTutorialStage(5);
            Assert.IsTrue(exceededDirector.Tick(
                Snapshot(
                    roads: 72,
                    houses: 37,
                    offices: 6,
                    schools: 3,
                    hospitals: 1,
                    harvested: true),
                5f));

            Assert.AreEqual(
                CityQuestId.AddOfficeCapacity,
                exceededDirector.ActiveQuest.Id,
                "회사 6곳의 총 정원 36채를 초과한 37채부터 증설 안내가 필요하다");
            StringAssert.Contains("초과", exceededDirector.ActiveQuest.Message);

            Assert.IsTrue(exceededDirector.Tick(
                Snapshot(
                    roads: 72,
                    houses: 37,
                    offices: 7,
                    schools: 3,
                    hospitals: 1,
                    harvested: true),
                0.5f));
            Assert.IsNull(exceededDirector.ActiveQuest);
        }

        [Test]
        public void TrafficInfrastructureQuests_ContinueInFirstBuildOrder()
        {
            var director = new CityQuestDirector();
            director.RestoreTutorialStage(5);
            CityQuestSnapshot noInfrastructure = Snapshot(
                roads: 20,
                houses: 5,
                offices: 5,
                schools: 1,
                harvested: true,
                intersectionFacilitiesAvailable: true,
                busStopsAvailable: true);

            Assert.IsTrue(director.Tick(noInfrastructure, 2f));
            Assert.AreEqual(CityQuestId.BuildSignal, director.ActiveQuest.Id);

            Assert.IsTrue(director.Tick(
                Snapshot(
                    roads: 20,
                    houses: 5,
                    offices: 5,
                    schools: 1,
                    harvested: true,
                    signals: 1,
                    intersectionFacilitiesAvailable: true,
                    busStopsAvailable: true),
                0.5f));
            Assert.IsNull(director.ActiveQuest);

            director.Tick(
                Snapshot(
                    roads: 20,
                    houses: 5,
                    offices: 5,
                    schools: 1,
                    harvested: true,
                    signals: 1,
                    intersectionFacilitiesAvailable: true,
                    busStopsAvailable: true),
                3f);
            Assert.AreEqual(CityQuestId.BuildRoundabout, director.ActiveQuest?.Id);

            Assert.IsTrue(director.Tick(
                Snapshot(
                    roads: 20,
                    houses: 5,
                    offices: 5,
                    schools: 1,
                    harvested: true,
                    signals: 1,
                    roundabouts: 1,
                    intersectionFacilitiesAvailable: true,
                    busStopsAvailable: true),
                0.5f));
            Assert.IsNull(director.ActiveQuest);

            director.Tick(
                Snapshot(
                    roads: 20,
                    houses: 5,
                    offices: 5,
                    schools: 1,
                    harvested: true,
                    signals: 1,
                    roundabouts: 1,
                    intersectionFacilitiesAvailable: true,
                    busStopsAvailable: true),
                3f);
            Assert.AreEqual(CityQuestId.BuildBusStop, director.ActiveQuest?.Id);
            StringAssert.Contains("2개", director.ActiveQuest.Message);
            StringAssert.Contains("도로로 연결", director.ActiveQuest.Message);

            Assert.IsFalse(director.Tick(
                Snapshot(
                    roads: 20,
                    houses: 5,
                    offices: 5,
                    schools: 1,
                    harvested: true,
                    signals: 1,
                    roundabouts: 1,
                    busStops: 1,
                    intersectionFacilitiesAvailable: true,
                    busStopsAvailable: true),
                0.5f));
            Assert.AreEqual(CityQuestId.BuildBusStop, director.ActiveQuest.Id);

            Assert.IsFalse(director.Tick(
                Snapshot(
                    roads: 20,
                    houses: 5,
                    offices: 5,
                    schools: 1,
                    harvested: true,
                    signals: 1,
                    roundabouts: 1,
                    busStops: 2,
                    intersectionFacilitiesAvailable: true,
                    busStopsAvailable: true),
                0.5f));
            Assert.AreEqual(CityQuestId.BuildBusStop, director.ActiveQuest.Id);

            Assert.IsTrue(director.Tick(
                Snapshot(
                    roads: 20,
                    houses: 5,
                    offices: 5,
                    schools: 1,
                    harvested: true,
                    signals: 1,
                    roundabouts: 1,
                    busStops: 2,
                    intersectionFacilitiesAvailable: true,
                    busStopsAvailable: true,
                    busOperating: true),
                0.5f));
            Assert.IsNull(director.ActiveQuest);
        }

        [Test]
        public void UnsupportedInfrastructure_DoesNotCreateImpossibleQuest()
        {
            var director = new CityQuestDirector();
            director.RestoreTutorialStage(5);

            director.Tick(
                Snapshot(
                    roads: 20,
                    houses: 5,
                    offices: 5,
                    schools: 1,
                    harvested: true),
                2f);

            Assert.AreNotEqual(CityQuestId.BuildSignal, director.ActiveQuest?.Id);
            Assert.AreNotEqual(CityQuestId.BuildRoundabout, director.ActiveQuest?.Id);
            Assert.AreNotEqual(CityQuestId.BuildBusStop, director.ActiveQuest?.Id);
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
            CityQuestSnapshot tutorialDone = Snapshot(
                roads: 3,
                houses: 1,
                offices: 1,
                schools: 1,
                arrivals: 1,
                harvested: true,
                jams: 1,
                connectedCommute: true);

            Assert.IsFalse(director.Tick(tutorialDone, 9f));
            Assert.IsNull(director.ActiveQuest);

            Assert.IsTrue(director.Tick(tutorialDone, 1f));
            Assert.AreEqual(CityQuestId.ResolveCongestion, director.ActiveQuest.Id);
        }

        [Test]
        public void HigherPriorityNeed_WinsWhenSeveralAreEligible()
        {
            var director = new CityQuestDirector();
            CityQuestSnapshot needs = Snapshot(
                roads: 3,
                houses: 7,
                offices: 1,
                arrivals: 1,
                harvested: true,
                jams: 1,
                connectedCommute: true);

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
                    harvested: true,
                    schoolResearchUnlocked: true),
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
                    harvested: true,
                    schoolResearchUnlocked: true),
                5f);
            Assert.AreEqual(
                CityQuestId.BuildSchool,
                director.ActiveQuest?.Id,
                "주택이 학교 한 곳의 10채 수용 기준을 넘으면 학교 건설 퀘스트가 나타나야 한다.");
        }

        [Test]
        public void SchoolQuest_DoesNotAppearBeforeItsResearchUnlock()
        {
            var director = new CityQuestDirector();
            director.RestoreTutorialStage(5);

            director.Tick(
                Snapshot(
                    roads: 20,
                    houses: 11,
                    offices: 2,
                    schools: 1,
                    arrivals: 1,
                    harvested: true),
                120f);

            Assert.AreNotEqual(
                CityQuestId.BuildSchool,
                director.ActiveQuest?.Id);
        }

        [Test]
        public void ResearchQuests_FollowReadyAndActiveResearchState()
        {
            var director = new CityQuestDirector();
            director.RestoreTutorialStage(5);
            CityQuestSnapshot ready = Snapshot(
                roads: 3,
                houses: 1,
                offices: 1,
                schools: 1,
                harvested: true,
                readyResearchId: "research_building_coffee_shop");

            Assert.IsTrue(director.Tick(ready, 2f));
            Assert.AreEqual(CityQuestId.StartResearch, director.ActiveQuest.Id);

            Assert.IsTrue(director.Tick(
                Snapshot(
                    roads: 3,
                    houses: 1,
                    offices: 1,
                    schools: 1,
                    harvested: true,
                    activeResearchId: "research_building_coffee_shop"),
                0.5f));
            Assert.IsNull(director.ActiveQuest);

            director.Tick(
                Snapshot(
                    roads: 3,
                    houses: 1,
                    offices: 1,
                    schools: 1,
                    harvested: true,
                    activeResearchId: "research_building_coffee_shop"),
                3f);
            Assert.AreEqual(
                CityQuestId.CompleteResearch,
                director.ActiveQuest?.Id);
        }

        [Test]
        public void UnlockedResearchBuildingQuest_CompletesWhenTargetIsBuilt()
        {
            var director = new CityQuestDirector();
            director.RestoreTutorialStage(5);
            CityQuestSnapshot unbuilt = Snapshot(
                roads: 3,
                houses: 1,
                offices: 1,
                schools: 1,
                harvested: true,
                unbuiltSpecialBuildingId: "coffee_shop");

            Assert.IsTrue(director.Tick(unbuilt, 3f));
            Assert.AreEqual(
                CityQuestId.BuildUnlockedFacility,
                director.ActiveQuest.Id);

            Assert.IsTrue(director.Tick(
                Snapshot(
                    roads: 3,
                    houses: 1,
                    offices: 1,
                    schools: 1,
                    harvested: true),
                0.5f));
            Assert.IsNull(director.ActiveQuest);
        }

        [Test]
        public void HospitalQuest_AppearsOnlyAfterResearchAndCompletesOnBuild()
        {
            var director = new CityQuestDirector();
            director.RestoreTutorialStage(5);
            CityQuestSnapshot unlocked = Snapshot(
                roads: 3,
                houses: 1,
                offices: 1,
                schools: 1,
                harvested: true,
                hospitalResearchUnlocked: true);

            Assert.IsTrue(director.Tick(unlocked, 5f));
            Assert.AreEqual(CityQuestId.BuildHospital, director.ActiveQuest.Id);

            Assert.IsTrue(director.Tick(
                Snapshot(
                    roads: 3,
                    houses: 1,
                    offices: 1,
                    schools: 1,
                    hospitals: 1,
                    harvested: true,
                    hospitalResearchUnlocked: true),
                0.5f));
            Assert.IsNull(director.ActiveQuest);
        }

    }
}
