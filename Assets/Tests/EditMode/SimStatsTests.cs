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
    }
}
