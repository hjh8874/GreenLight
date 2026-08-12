using NUnit.Framework;

namespace CityFlow.Sim.Tests
{
    public class SimStatsTests
    {
        [Test]
        public void LastDayArrivalCount_CapturesFinalValueAtDayWrap()
        {
            var stats = new SimStats();
            SimConfig cfg = SimConfig.Default();

            stats.UpdateCarSim(gameHour: 8f, arrivals: 3, carCount: 4, jumped: false, jamRatio: 0f, in cfg);
            stats.UpdateCarSim(gameHour: 20f, arrivals: 2, carCount: 4, jumped: false, jamRatio: 0f, in cfg);
            Assert.AreEqual(0, stats.LastDayArrivalCount, "하루가 끝나기 전엔 어제 값이 없다");

            stats.UpdateCarSim(gameHour: 1f, arrivals: 0, carCount: 4, jumped: false, jamRatio: 0f, in cfg);   // wrap
            Assert.AreEqual(5, stats.LastDayArrivalCount, "경계에서 어제의 최종치(3+2)를 캡처한다");

            stats.UpdateCarSim(gameHour: 9f, arrivals: 7, carCount: 4, jumped: false, jamRatio: 0f, in cfg);
            Assert.AreEqual(5, stats.LastDayArrivalCount, "오늘 누적은 어제 값을 건드리지 않는다");
        }

        [Test]
        public void UpdateCarSim_ReturnsOnlyWhenGameDayWraps()
        {
            var stats = new SimStats();
            SimConfig cfg = SimConfig.Default();

            Assert.IsFalse(stats.UpdateCarSim(8f, 0, 1, false, 0f, in cfg));
            Assert.IsFalse(stats.UpdateCarSim(20f, 0, 1, false, 0f, in cfg));
            Assert.IsTrue(stats.UpdateCarSim(1f, 0, 1, false, 0f, in cfg));
            Assert.IsFalse(stats.UpdateCarSim(2f, 0, 1, false, 0f, in cfg));
        }

        [Test]
        public void RestoreCarSim_RestoresLastCompletedDayArrivalCount()
        {
            var stats = new SimStats();

            stats.RestoreCarSim(
                successRate: 0.75f,
                dayArrivals: 7,
                lastDayArrivals: 83,
                skipCurrentDay: false,
                hasData: true,
                hasLastDayArrivals: true);

            Assert.AreEqual(7, stats.DayArrivalCount);
            Assert.AreEqual(83, stats.LastDayArrivalCount);
        }

        [Test]
        public void RestoreCarSim_LegacySnapshotWithoutLastDayField_UsesSafeDefault()
        {
            var stats = new SimStats();
            SimConfig cfg = SimConfig.Default();

            stats.RestoreCarSim(
                successRate: 0.75f,
                dayArrivals: 24,
                lastDayArrivals: 83,
                skipCurrentDay: false,
                hasData: true,
                hasLastDayArrivals: false);

            Assert.AreEqual(24, stats.DayArrivalCount);
            Assert.AreEqual(0, stats.LastDayArrivalCount);

            stats.UpdateCarSim(
                gameHour: 20f,
                arrivals: 3,
                carCount: 20,
                jumped: false,
                jamRatio: 0f,
                in cfg);
            stats.UpdateCarSim(
                gameHour: 1f,
                arrivals: 0,
                carCount: 20,
                jumped: false,
                jamRatio: 0f,
                in cfg);

            Assert.AreEqual(
                27,
                stats.LastDayArrivalCount,
                "구 세이브의 오늘 누적치는 다음 정상 하루 경계에서 확정값이 된다");
        }
    }
}
