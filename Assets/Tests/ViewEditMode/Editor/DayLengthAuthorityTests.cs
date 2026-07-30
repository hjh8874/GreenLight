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
    }
}
