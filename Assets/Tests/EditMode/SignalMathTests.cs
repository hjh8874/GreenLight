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

        [Test]
        public void PhaseForAxis_GreenYellowRed_WithClearanceAndNoOverlap()
        {
            var s = new Signal { CycleSlots = 12 };   // 6초 주기, 절반 3초: 초록1.95·노랑0.6·전적색0.45
            // 전반부 초록: 가로 Green·세로 Red
            Assert.AreEqual(SignalPhase.Green, SignalMath.PhaseForAxis(s, 0.0, true));
            Assert.AreEqual(SignalPhase.Red,   SignalMath.PhaseForAxis(s, 0.0, false));
            // 초록 끝(~2.2s) = 노랑
            Assert.AreEqual(SignalPhase.Yellow, SignalMath.PhaseForAxis(s, 2.2, true));
            // 전적색(~2.8s) = 양방향 빨강(교차로 정리)
            Assert.AreEqual(SignalPhase.Red, SignalMath.PhaseForAxis(s, 2.8, true));
            Assert.AreEqual(SignalPhase.Red, SignalMath.PhaseForAxis(s, 2.8, false));
            // 후반부: 세로 Green·가로 Red
            Assert.AreEqual(SignalPhase.Green, SignalMath.PhaseForAxis(s, 3.5, false));
            Assert.AreEqual(SignalPhase.Red,   SignalMath.PhaseForAxis(s, 3.5, true));
            // 어느 순간에도 두 방향이 동시에 통행(초록/노랑) 아님
            for (double t = 0; t < 6.0; t += 0.1)
            {
                bool hGo = SignalMath.PhaseForAxis(s, t, true) != SignalPhase.Red;
                bool vGo = SignalMath.PhaseForAxis(s, t, false) != SignalPhase.Red;
                Assert.IsFalse(hGo && vGo);
            }
        }

        [Test]
        public void GreenWave_PerfectOffset_FullEfficiency()
        {
            // 오프셋 차이(4) == 이동시간(4슬롯) → 흐름이 B 초록에 정확히 도착 → 효율 1
            var a = new Signal { CycleSlots = 12, OffsetSlots = 0 };
            var b = new Signal { CycleSlots = 12, OffsetSlots = 4 };
            Assert.AreEqual(1f, SignalMath.GreenWaveEfficiency(a, b, travelSlots: 4, floor: 0.5f), 1e-4f);
        }

        [Test]
        public void GreenWave_HalfCycleOff_HitsFloor()
        {
            // 반 주기(6슬롯) 어긋남 = 최악 → floor
            var a = new Signal { CycleSlots = 12, OffsetSlots = 0 };
            var b = new Signal { CycleSlots = 12, OffsetSlots = 0 };
            Assert.AreEqual(0.5f, SignalMath.GreenWaveEfficiency(a, b, travelSlots: 6, floor: 0.5f), 1e-4f);
        }

        [Test]
        public void GreenWave_OffsetIsTheLever_MonotonicWithMisalignment()
        {
            // 같은 신호쌍·이동시간, 오프셋만 바꿔 정렬을 좋게/나쁘게 → 효율이 달라져야(노브가 살아있음)
            var a = new Signal { CycleSlots = 12, OffsetSlots = 0 };
            float good = SignalMath.GreenWaveEfficiency(a, new Signal { CycleSlots = 12, OffsetSlots = 4 }, 4, 0.5f);
            float mid  = SignalMath.GreenWaveEfficiency(a, new Signal { CycleSlots = 12, OffsetSlots = 2 }, 4, 0.5f);
            float bad  = SignalMath.GreenWaveEfficiency(a, new Signal { CycleSlots = 12, OffsetSlots = 10 }, 4, 0.5f);
            Assert.AreEqual(1f, good, 1e-4f);
            Assert.Greater(good, mid);
            Assert.Greater(mid, bad);
            Assert.AreEqual(0.5f, bad, 1e-4f);
        }

        [Test]
        public void GreenWindowFor_AxesSeparatedByHalfCycle_SameLength()
        {
            var s = new Signal { CycleSlots = 12 };   // cycle 6s, half 3s
            var (openH, lenH) = SignalMath.GreenWindowFor(s, true);
            var (openV, lenV) = SignalMath.GreenWindowFor(s, false);
            Assert.AreEqual(0.0, openH, 1e-9);
            Assert.AreEqual(3.0, openV, 1e-9);          // 세로는 반주기 뒤에 열림
            Assert.AreEqual(1.95, lenH, 1e-6);          // half(3)·0.65
            Assert.AreEqual(lenH, lenV, 1e-9);
        }

        [Test]
        public void GreenWindowFor_OffsetDelaysOpen()   // 부호 통일: 오프셋↑ = 늦게 열림(직관)
        {
            var s = new Signal { CycleSlots = 12, OffsetSlots = 2 };   // +1.0s
            Assert.AreEqual(1.0, SignalMath.GreenWindowFor(s, true).open, 1e-9);
        }

        [Test]
        public void PhaseForAxis_OffsetDelaysGreen_UnifiedSign()
        {
            // 오프셋 2슬롯(+1.0s) → 가로 초록창이 [1.0, 2.95)로 밀림: t=0.5 아직 빨강, t=1.5 초록
            var s = new Signal { CycleSlots = 12, OffsetSlots = 2 };
            Assert.AreEqual(SignalPhase.Red,   SignalMath.PhaseForAxis(s, 0.5, true));
            Assert.AreEqual(SignalPhase.Green, SignalMath.PhaseForAxis(s, 1.5, true));
        }
    }
}
