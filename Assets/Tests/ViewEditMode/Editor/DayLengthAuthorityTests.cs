using CityFlow.Bootstrap;
using CityFlow.Gameplay.Progression;
using CityFlow.Sim;
using NUnit.Framework;

namespace CityFlow.Tests
{
    // 하루 길이는 두 곳에 값이 있다:
    //   GameTimeSettingsSO.DefaultRealMinutesPerGameDay — 단일 출처(표시·진행 시계)
    //   SimConfig.DayLengthSeconds                     — Sim 내부 사본
    //
    // Sim 어셈블리는 GameTimeSettingsSO(Assembly-CSharp)를 참조할 수 없어 값을 복제한다.
    // 복제는 어긋날 수 있고, 어긋나도 **어떤 테스트도 실패하지 않는다** — DayLengthSeconds 를
    // 쓰는 테스트 4개가 전부 자기 값을 직접 세팅하기 때문이다(24f 또는 120f).
    // 2026-07-30 실제로 120 vs 720 으로 6배 어긋난 채 방치돼 있었다. 이 테스트가 그 재발을 막는다.
    //
    // 이 어셈블리(기본 에디터 어셈블리)만 두 타입을 동시에 볼 수 있어 여기 둔다.
    public sealed class DayLengthAuthorityTests
    {
        [Test]
        public void GameTimeSettings_AndSimConfig_AgreeOnDayLength()
        {
            float authoritative =
                GameTimeSettingsSO.DefaultRealMinutesPerGameDay * 60f;

            Assert.AreEqual(
                authoritative,
                SimConfig.Default().DayLengthSeconds,
                0.001f,
                "SimConfig.DayLengthSeconds 는 GameTimeSettingsSO.DefaultRealMinutesPerGameDay×60 과 "
                + "같아야 한다. 한쪽만 바꾸면 러시아워 위상·채용 램프·공사시간이 조용히 어긋난다.");
        }

        // 위 가드는 **기본값끼리만** 비교한다. 씬이 다른 GameTimeSettings 에셋을 물리면
        // (예: SchoolBusDebugGameTimeSettings = 0.4분 = 24초) 기본값이 맞아도 갈라진다 —
        // 리뷰 지적 2026-07-30(`CityFlowIntegrated_Lee`가 24초 설정 + 720초 SimConfig 참조 = 30배).
        // 그래서 CityBootstrap 이 런타임에 씬의 캘린더 값을 Sim 으로 밀어넣는다. 그 판정을 고정한다.
        [Test]
        public void SimDayLength_FollowsSceneCalendar_WhenTheyDiffer()
        {
            // 디버그 씬: 표시 시계 24초 vs SimConfig 720초 → Sim 이 24초를 따라가야 한다
            Assert.IsTrue(
                CityBootstrap.TryResolveSimDayLength(24f, 720f, out float resolved),
                "값이 다르면 동기화한다");
            Assert.AreEqual(24f, resolved, 0.001f, "씬의 캘린더가 권위다");
        }

        [Test]
        public void SimDayLength_IgnoresDegenerateAndMatchingValues()
        {
            Assert.IsFalse(
                CityBootstrap.TryResolveSimDayLength(0f, 720f, out float zero),
                "캘린더가 0이면 무시한다 — 0으로 덮으면 Sim 시간이 멈춘다");
            Assert.AreEqual(720f, zero, 0.001f);

            Assert.IsFalse(
                CityBootstrap.TryResolveSimDayLength(-5f, 720f, out _),
                "음수도 무시한다");

            Assert.IsFalse(
                CityBootstrap.TryResolveSimDayLength(720f, 720f, out _),
                "같으면 ApplyConfig 를 부르지 않는다(불필요한 재빌드 방지)");
        }
    }
}
