using NUnit.Framework;

namespace CityFlow.Sim.Tests
{
    public class SignalMathTests
    {
        [Test]
        public void IsGreen_CycleStartsGreen_TurnsRedAfterGreenWindow()
        {
            // 주기 12슬롯(6초), 초록 6슬롯(3초) → [0,3)초 초록, [3,6)초 빨강, 6초에 다음 주기
            var s = new Signal { CycleSlots = 12, GreenSlots = 6 };
            Assert.IsTrue(SignalMath.IsGreen(s, 0.0));
            Assert.IsTrue(SignalMath.IsGreen(s, 2.9));
            Assert.IsFalse(SignalMath.IsGreen(s, 3.0));
            Assert.IsTrue(SignalMath.IsGreen(s, 6.0));   // 랩어라운드
        }

        [Test]
        public void IsGreen_OffsetShiftsGreenWindow()
        {
            // 오프셋 6슬롯(+3초) → 초록창이 뒤로 밀림: 0초 빨강, 3초 초록
            var s = new Signal { CycleSlots = 12, GreenSlots = 6, OffsetSlots = 6 };
            Assert.IsFalse(SignalMath.IsGreen(s, 0.0));
            Assert.IsTrue(SignalMath.IsGreen(s, 3.0));
        }

        [Test]
        public void GreenRatio_IsDutyCycle()
        {
            // 초록 6 / 주기 12 = 0.5 → 교차로가 절반 시간만 통과 → 유효 용량 절반
            Assert.AreEqual(0.5f, SignalMath.GreenRatio(new Signal { CycleSlots = 12, GreenSlots = 6 }), 1e-4f);
            Assert.AreEqual(0.3f, SignalMath.GreenRatio(new Signal { CycleSlots = 10, GreenSlots = 3 }), 1e-4f);
        }

        [Test]
        public void GreenRatio_ClampsToUnitRange()
        {
            // 초록>주기(오설정) → 1, 주기 0(방어) → 0. 다운스트림 용량 계산이 안 깨지게.
            Assert.AreEqual(1f, SignalMath.GreenRatio(new Signal { CycleSlots = 6, GreenSlots = 99 }), 1e-4f);
            Assert.AreEqual(0f, SignalMath.GreenRatio(new Signal { CycleSlots = 0, GreenSlots = 6 }), 1e-4f);
        }
    }
}
