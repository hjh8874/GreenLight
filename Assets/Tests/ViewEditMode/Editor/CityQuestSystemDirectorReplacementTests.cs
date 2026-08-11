using System;
using System.Collections.Generic;
using System.Reflection;
using CityFlow.Gameplay.Quests;
using CityFlow.Sim.Quests;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Tests.ViewEditMode
{
    // 리뷰 #251 [P2]: EditMode 쪽 CityQuestDirectorCompletionEventTests 는
    // 구독 교체를 테스트 안에서 재현했기 때문에 제품 코드가 다시 잘못돼도 통과한다.
    // 여기서는 실제 CityQuestSystem 인스턴스의 ReplaceDirector 를 호출해
    // "director 를 갈아끼워도 QuestCompleted 가 정확히 계속 중계된다"를 고정한다.
    //
    // ReplaceDirector 는 private 이고 CityQuestSystem 은 Assembly-CSharp,
    // 이 테스트는 Assembly-CSharp-Editor 라 internal 로도 안 보인다.
    // 그래서 리플렉션을 쓴다 — 이름이 바뀌면 Assert 가 그 사실을 알려준다.
    public sealed class CityQuestSystemDirectorReplacementTests
    {
        private static CityQuestSnapshot Empty() =>
            new CityQuestSnapshot(0, 0, 0, 0, 0L, 0L, false, 0);

        // 첫 튜토리얼(BuildRoad)의 완료 조건은 도로 3개다.
        private static CityQuestSnapshot WithRoads() =>
            new CityQuestSnapshot(3, 0, 0, 0, 0L, 0L, false, 0);

        private static CityQuestSnapshot WithRoadsAndHouse() =>
            new CityQuestSnapshot(3, 1, 0, 0, 0L, 0L, false, 0);

        private GameObject host;
        private CityQuestSystem system;
        private MethodInfo replaceDirector;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("CityQuestSystemTestHost");
            system = host.AddComponent<CityQuestSystem>();

            replaceDirector = typeof(CityQuestSystem).GetMethod(
                "ReplaceDirector",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(
                replaceDirector,
                "CityQuestSystem.ReplaceDirector 를 찾지 못했다 — " +
                "이름이 바뀌었다면 이 테스트도 함께 고쳐야 한다");
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null)
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private void Replace(CityQuestDirector next) =>
            replaceDirector.Invoke(system, new object[] { next });

        [Test]
        public void QuestCompleted_KeepsRelaying_AfterDirectorReplacement()
        {
            var fired = new List<CityQuestId>();
            system.QuestCompleted += id => fired.Add(id);

            var first = new CityQuestDirector();
            Replace(first);

            first.Tick(Empty(), 1f);
            first.Tick(WithRoads(), 1f);
            Assert.AreEqual(
                1,
                fired.Count,
                "전제: 첫 director 의 완료가 시스템을 통해 중계된다");

            // Initialize() 재호출과 같은 상황 — director 인스턴스가 통째로 교체된다.
            var second = new CityQuestDirector();
            Replace(second);

            second.Tick(Empty(), 1f);
            second.Tick(WithRoads(), 1f);

            Assert.AreEqual(
                2,
                fired.Count,
                "교체된 director 의 완료도 중계돼야 한다 " +
                "(지연 구독 + bool 가드였을 때 여기서 영구 무음이 됐다)");
        }

        // 교체 전 director 가 구독을 물고 있으면 같은 완료가 두 번 중계된다.
        // 이걸 잡으려면 옛 director 를 "실제로 한 번 더 완료"시켜야 한다 —
        // 완료 직후 두 번 Tick 하는 것만으로는 다음 퀘스트 지연 때문에
        // 구독을 안 끊어도 아무 일이 안 일어나 검증이 되지 않는다.
        [Test]
        public void QuestCompleted_IsNotRelayed_ByTheReplacedDirector()
        {
            var fired = new List<CityQuestId>();
            system.QuestCompleted += id => fired.Add(id);

            var first = new CityQuestDirector();
            Replace(first);
            first.Tick(Empty(), 1f);
            first.Tick(WithRoads(), 1f);
            Assert.AreEqual(1, fired.Count, "전제: 첫 완료가 중계됐다");

            Replace(new CityQuestDirector());

            // 끊긴 director 를 다음 퀘스트까지 실제로 진행시킨다(nextQuestDelay = 3초).
            for (int tick = 0; tick < 5; tick++)
            {
                first.Tick(WithRoads(), 1f);
            }

            if (first.ActiveQuest == null)
            {
                Assert.Inconclusive(
                    "옛 director 에서 다음 퀘스트가 활성화되지 않았다 — " +
                    "퀘스트 진행 조건이 바뀌었는지 확인 필요");
            }

            first.Tick(WithRoadsAndHouse(), 1f);

            Assert.AreEqual(
                1,
                fired.Count,
                "교체 전 director 는 구독이 끊겨 더 이상 중계되면 안 된다 " +
                "(안 끊으면 여기서 2가 되어 연출이 두 번 터진다)");
        }
    }
}
