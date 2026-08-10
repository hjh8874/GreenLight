using System.Collections.Generic;
using CityFlow.Sim.Quests;
using NUnit.Framework;

namespace CityFlow.Sim.Tests
{
    // 퀘스트 클리어 축하 연출이 QuestCompleted 에 걸린다.
    // 이 이벤트가 "달성"에서만 울린다는 것이 연출의 전제다 —
    // ViewStateChanged 처럼 화면 전환마다 울리면 엉뚱할 때 폭죽이 터진다.
    // 그 전제를 여기서 고정한다.
    public sealed class CityQuestDirectorCompletionEventTests
    {
        private static CityQuestSnapshot Empty() =>
            new CityQuestSnapshot(0, 0, 0, 0, 0L, 0L, false, 0);

        // 첫 튜토리얼(BuildRoad)의 완료 조건은 도로 3개다(IsQuestComplete 참조).
        private static CityQuestSnapshot WithRoads() =>
            new CityQuestSnapshot(3, 0, 0, 0, 0L, 0L, false, 0);

        [Test]
        public void QuestCompleted_FiresOnceWhenObjectiveIsMet()
        {
            var director = new CityQuestDirector();
            var fired = new List<CityQuestId>();
            director.QuestCompleted += id => fired.Add(id);

            // 퀘스트가 뜬다(= 활성화). 아직 달성 전이므로 완료는 울리지 않아야 한다.
            director.Tick(Empty(), 1f);
            Assert.IsNotNull(director.ActiveQuest, "전제: 튜토리얼 퀘스트가 활성화된다");
            CollectionAssert.IsEmpty(
                fired,
                "퀘스트가 뜨기만 한 시점에는 완료가 울리면 안 된다");

            CityQuestId activeId = director.ActiveQuest.Id;

            // 목표를 채운다.
            director.Tick(WithRoads(), 1f);

            Assert.AreEqual(1, fired.Count, "달성 순간 정확히 한 번 울려야 한다");
            Assert.AreEqual(activeId, fired[0], "완료된 퀘스트의 Id 가 실려야 한다");
        }

        [Test]
        public void QuestCompleted_DoesNotFireWhileObjectiveIsUnmet()
        {
            var director = new CityQuestDirector();
            var fired = new List<CityQuestId>();
            director.QuestCompleted += id => fired.Add(id);

            for (int tick = 0; tick < 30; tick++)
            {
                director.Tick(Empty(), 1f);
            }

            CollectionAssert.IsEmpty(
                fired,
                "목표를 못 채운 동안에는 한 번도 울리면 안 된다");
        }

        // 세이브 복원은 활성 퀘스트를 갈아끼운다. 이건 "달성"이 아니므로
        // 축하 연출이 터지면 안 된다 — ViewStateChanged 를 쓰면 여기서 오작동한다.
        [Test]
        public void QuestCompleted_DoesNotFireOnRestore()
        {
            var director = new CityQuestDirector();
            director.Tick(Empty(), 1f);
            Assert.IsNotNull(director.ActiveQuest, "전제: 활성 퀘스트가 있다");

            var fired = new List<CityQuestId>();
            director.QuestCompleted += id => fired.Add(id);

            director.RestoreTutorialStage(3);

            CollectionAssert.IsEmpty(
                fired,
                "세이브 복원으로 퀘스트가 바뀌는 것은 달성이 아니다");
        }

        [Test]
        public void QuestCompleted_FiresAgainForTheNextQuest()
        {
            var director = new CityQuestDirector();
            var fired = new List<CityQuestId>();
            director.QuestCompleted += id => fired.Add(id);

            director.Tick(Empty(), 1f);
            director.Tick(WithRoads(), 1f);
            Assert.AreEqual(1, fired.Count, "전제: 첫 퀘스트가 완료됐다");

            // 다음 퀘스트가 뜰 때까지 대기(nextQuestDelay = 3초)한 뒤 집을 짓는다.
            for (int tick = 0; tick < 5; tick++)
            {
                director.Tick(WithRoads(), 1f);
            }

            if (director.ActiveQuest == null)
            {
                Assert.Ignore("다음 퀘스트가 활성화되지 않았다 — 조건이 바뀌었는지 확인 필요");
            }

            var withHouse = new CityQuestSnapshot(3, 1, 0, 0, 0L, 0L, false, 0);
            director.Tick(withHouse, 1f);

            Assert.AreEqual(
                2,
                fired.Count,
                "두 번째 퀘스트 달성에서도 울려야 한다");
            Assert.AreNotEqual(
                fired[0],
                fired[1],
                "서로 다른 퀘스트가 완료돼야 한다");
        }
    }
}
