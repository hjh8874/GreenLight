using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using CityFlow.Feed;

namespace CityFlow.Sim.Tests
{
    /// <summary>
    /// 규칙과 템플릿이 있어도 그 이벤트를 지원하는 작성자가 하나도 없으면
    /// SelectCandidate가 항상 null을 돌려 글이 한 건도 생성되지 않는다.
    /// 장부·DTO 테스트로는 이 경로가 잡히지 않아 별도로 둔다.
    ///
    /// 배경: CitizenFeedDataGenerator.LoadOrCreate는 에셋이 이미 있으면 Configure를
    /// 다시 부르지 않는다. 그래서 이벤트를 새로 추가해도 기존 프로필의
    /// preferredEvents는 옛 목록 그대로 남아 신규 이벤트가 조용히 무동작이 됐다.
    /// </summary>
    public class CitizenFeedAuthorSupportTests
    {
        // 제너레이터가 관리하는 폴더로 한정한다. 프로젝트 전체를 훑으면
        // 누가 다른 곳에 좁은 preferredEvents를 가진 프로필을 하나 두는 순간
        // 이 회귀 가드가 흔들린다.
        private const string GeneratedFolder = "Assets/05_ScriptableObjects/Feed";

        private static FeedAuthorProfileSO[] LoadAuthors()
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:FeedAuthorProfileSO",
                new[] { GeneratedFolder });
            var authors = new FeedAuthorProfileSO[guids.Length];
            for (int i = 0; i < guids.Length; i++)
            {
                authors[i] = AssetDatabase.LoadAssetAtPath<FeedAuthorProfileSO>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
            }

            return authors;
        }

        [Test]
        public void AuthorProfiles_Exist()
        {
            Assert.Greater(
                LoadAuthors().Length,
                0,
                "작성자 프로필이 없으면 어떤 이벤트도 글을 만들 수 없다. " +
                "Tools > GreenLight > Feed > Create or Upgrade Feed Data 실행 필요");
        }

        [TestCase(CitizenFeedEventType.CongestionStarted)]
        [TestCase(CitizenFeedEventType.CongestionResolved)]
        [TestCase(CitizenFeedEventType.FlowBurst)]
        [TestCase(CitizenFeedEventType.BuildingPlaced)]
        [TestCase(CitizenFeedEventType.EmergencyAlert)]
        [TestCase(CitizenFeedEventType.EmergencyResolved)]
        [TestCase(CitizenFeedEventType.TimePeriodChanged)]
        public void EveryPostableEvent_HasAtLeastOneSupportingAuthor(
            CitizenFeedEventType eventType)
        {
            FeedAuthorProfileSO[] authors = LoadAuthors();
            if (authors.Length == 0)
            {
                Assert.Ignore("작성자 프로필 없음 — AuthorProfiles_Exist가 따로 잡는다");
            }

            int supporters = 0;
            foreach (FeedAuthorProfileSO author in authors)
            {
                if (author != null && author.Supports(eventType)) supporters++;
            }

            Assert.Greater(
                supporters,
                0,
                $"{eventType}를 지원하는 작성자가 없다. 규칙·템플릿이 있어도 " +
                "SelectCandidate가 항상 null을 돌려 글이 생성되지 않는다.");
        }

        [Test]
        public void AddSupportedEvents_MergesWithoutDroppingExisting()
        {
            var profile = ScriptableObject.CreateInstance<FeedAuthorProfileSO>();
            try
            {
                profile.Configure(
                    "테스트시민", "테", "시민", Color.white,
                    CitizenFeedRole.Resident, CitizenFeedPersonality.Observer, 1f,
                    new[] { CitizenFeedEventType.CongestionStarted },
                    0, 24, 1f, 1f, 1f,
                    new string[0], new string[0]);

                bool changed = profile.AddSupportedEvents(
                    new[] { CitizenFeedEventType.FlowBurst });

                Assert.IsTrue(changed);
                Assert.IsTrue(
                    profile.Supports(CitizenFeedEventType.CongestionStarted),
                    "기존 지원 항목이 사라지면 안 된다");
                Assert.IsTrue(profile.Supports(CitizenFeedEventType.FlowBurst));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void AddSupportedEvents_IsIdempotent()
        {
            var profile = ScriptableObject.CreateInstance<FeedAuthorProfileSO>();
            try
            {
                profile.Configure(
                    "테스트시민", "테", "시민", Color.white,
                    CitizenFeedRole.Resident, CitizenFeedPersonality.Observer, 1f,
                    new[] { CitizenFeedEventType.FlowBurst },
                    0, 24, 1f, 1f, 1f,
                    new string[0], new string[0]);

                Assert.IsFalse(
                    profile.AddSupportedEvents(new[] { CitizenFeedEventType.FlowBurst }),
                    "이미 있는 항목만 넘기면 변경이 없어야 한다 — " +
                    "제너레이터를 여러 번 돌려도 에셋이 계속 더러워지면 안 된다");
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void AddSupportedEvents_LeavesEmptyListAlone()
        {
            var profile = ScriptableObject.CreateInstance<FeedAuthorProfileSO>();
            try
            {
                profile.Configure(
                    "테스트시민", "테", "시민", Color.white,
                    CitizenFeedRole.Resident, CitizenFeedPersonality.Observer, 1f,
                    new CitizenFeedEventType[0],
                    0, 24, 1f, 1f, 1f,
                    new string[0], new string[0]);

                Assert.IsFalse(
                    profile.AddSupportedEvents(new[] { CitizenFeedEventType.FlowBurst }),
                    "빈 목록은 Supports()가 이미 전부 허용이다 — 채우면 오히려 좁아진다");
                Assert.IsTrue(profile.Supports(CitizenFeedEventType.CongestionSlowed));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }
    }
}
