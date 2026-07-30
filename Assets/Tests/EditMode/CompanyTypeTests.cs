using CityFlow.Contracts;
using NUnit.Framework;

namespace CityFlow.Sim.Tests
{
    public class CompanyTypeTests
    {
        static CompanyTypeInfo NewType(string id, float start, float end, int capacity = 6) =>
            new CompanyTypeInfo(new CommuteWindow(id, start, 4f, end, 4f), capacity);

        [Test]
        public void CompanyTypes_LookUpById_AndRejectUnknown()
        {
            var engine = new SimEngine(SimConfig.Default(), new SimEventHub());
            engine.SetCompanyTypes(new[] { NewType("office", 6f, 17f), NewType("factory", 20f, 5f) });

            Assert.IsTrue(engine.TryGetCompanyType("office", out CompanyTypeInfo office));
            Assert.AreEqual(6f, office.Window.StartHour);
            Assert.IsTrue(engine.TryGetCompanyType("factory", out CompanyTypeInfo factory));
            Assert.AreEqual(20f, factory.Window.StartHour, "공장은 야간 출근");
            Assert.AreEqual(5f, factory.Window.EndHour, "퇴근이 출근보다 이르다 = 자정을 넘는다");

            Assert.IsFalse(engine.TryGetCompanyType("warehouse", out _), "없는 id는 false");
            Assert.IsFalse(engine.TryGetCompanyType(null, out _), "null도 false");
            Assert.IsFalse(engine.TryGetCompanyType("", out _), "빈 문자열도 false");
        }

        [Test]
        public void FallbackWindow_ComesFromSimConfig()
        {
            SimConfig cfg = SimConfig.Default();
            var engine = new SimEngine(cfg, new SimEventHub());

            CommuteWindow w = engine.FallbackCommuteWindow();
            Assert.AreEqual(string.Empty, w.CompanyTypeId, "폴백은 무명 유형");
            Assert.AreEqual(cfg.MorningStartHour, w.StartHour);
            Assert.AreEqual(cfg.MorningEndHour - cfg.MorningStartHour, w.StartWindow);
            Assert.AreEqual(cfg.EveningStartHour, w.EndHour);
            Assert.AreEqual(cfg.EveningEndHour - cfg.EveningStartHour, w.EndWindow);
        }

        [Test]
        public void SetCompanyTypes_ReplacesTable_AndSkipsNamelessEntries()
        {
            var engine = new SimEngine(SimConfig.Default(), new SimEventHub());
            engine.SetCompanyTypes(new[] { NewType("office", 6f, 17f), NewType("  ", 6f, 17f) });
            Assert.AreEqual(1, engine.CompanyTypeCountForTest, "무명 유형은 표에 들어가지 않는다");

            engine.SetCompanyTypes(new[] { NewType("factory", 20f, 5f) });
            Assert.IsFalse(engine.TryGetCompanyType("office", out _), "재주입은 표를 교체한다");
            Assert.IsTrue(engine.TryGetCompanyType("factory", out _));

            engine.SetCompanyTypes(null);
            Assert.AreEqual(0, engine.CompanyTypeCountForTest, "null 은 표를 비운다");
        }
    }
}
