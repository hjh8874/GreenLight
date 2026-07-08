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
        public void GreenWave_ArrivalInsideGreen_IsPlateauOne()
        {
            // 도착이 하류 초록창 안이면(정렬 포함) 1.0 — 초록 어디 잡아도 완전 통과(플래토)
            var a = new Signal { CycleSlots = 12, OffsetSlots = 0 };
            Assert.AreEqual(1f, SignalMath.GreenWaveEfficiency(a, new Signal { CycleSlots = 12, OffsetSlots = 4 }, 4, 0.5f), 1e-4f); // δ=0
            Assert.AreEqual(1f, SignalMath.GreenWaveEfficiency(a, new Signal { CycleSlots = 12, OffsetSlots = 2 }, 4, 0.5f), 1e-4f); // δ=1.0 < 1.95
        }

        [Test]
        public void GreenWave_ArrivalPastGreen_FallsMonotonicToFloor()
        {
            // 도착이 초록창 뒤로 점점 깊어질수록 효율 감소, 반대편 한복판 ≈ floor.
            var a = new Signal { CycleSlots = 12, OffsetSlots = 0 };
            float d2 = SignalMath.GreenWaveEfficiency(a, new Signal { CycleSlots = 12, OffsetSlots = 0  }, 4, 0.5f); // δ=2.0
            float d3 = SignalMath.GreenWaveEfficiency(a, new Signal { CycleSlots = 12, OffsetSlots = 10 }, 4, 0.5f); // δ=3.0
            float d4 = SignalMath.GreenWaveEfficiency(a, new Signal { CycleSlots = 12, OffsetSlots = 8  }, 4, 0.5f); // δ=4.0(≈최악)
            Assert.Greater(d2, d3);
            Assert.Greater(d3, d4);
            Assert.AreEqual(0.5f, d4, 0.02f);   // 최악 ≈ floor
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

        [Test]
        public void SignalModels_Agree_EfficiencyMatchesViewGreen()
        {
            // 통합 불변식: 엔진이 "완전 통과(1.0)"면 뷰도 그 도착 순간 초록. 어긋나면 둘 다 손실/빨강.
            // 두 모델이 다시 갈라지면 이 테스트가 빨개진다(anti-drift 못).
            var from = new Signal { CycleSlots = 12, OffsetSlots = 0 };
            var to   = new Signal { CycleSlots = 12, OffsetSlots = 4 };   // 정렬(offsetTo-offsetFrom=travel)
            const int travel = 4;
            double open = SignalMath.GreenWindowFor(from, true).open;
            double arrive = open + travel * SignalMath.SlotSeconds;       // 상류 초록 선두 출발 → 도착

            Assert.AreEqual(1f, SignalMath.GreenWaveEfficiency(from, to, travel, 0.5f), 1e-4f);   // 엔진: 통과
            Assert.AreEqual(SignalPhase.Green, SignalMath.PhaseForAxis(to, arrive, true));         // 뷰: 초록 ← 일치

            to.OffsetSlots = 8;                                            // 어긋남
            Assert.Less(SignalMath.GreenWaveEfficiency(from, to, travel, 0.5f), 1f);               // 엔진: 손실
            Assert.AreNotEqual(SignalPhase.Green, SignalMath.PhaseForAxis(to, arrive, true));      // 뷰: 초록 아님 ← 일치
        }

        // ── 공식 가정 검증(갭 3의 솔로 반쪽): 경계 입력에서도 공식이 안 깨지는지 고정 ──

        [Test]
        public void GreenWave_OffsetWrapsAroundCycle()
        {
            // 튜너를 계속/거꾸로 돌려도 같은 조율: 오프셋 k ≡ k±주기(슬롯).
            var a = new Signal { CycleSlots = 12, OffsetSlots = 0 };
            float baseline = SignalMath.GreenWaveEfficiency(a, new Signal { CycleSlots = 12, OffsetSlots = 4 }, 4, 0.5f);
            Assert.AreEqual(baseline, SignalMath.GreenWaveEfficiency(a, new Signal { CycleSlots = 12, OffsetSlots = 16 }, 4, 0.5f), 1e-4f); // +주기
            Assert.AreEqual(baseline, SignalMath.GreenWaveEfficiency(a, new Signal { CycleSlots = 12, OffsetSlots = -8 }, 4, 0.5f), 1e-4f); // -주기
        }

        [Test]
        public void GreenWave_TravelBeyondCycle_LandsNextGreenWindow()
        {
            // 먼 신호쌍: 이동시간이 주기를 넘어도 '다음 주기' 초록 안착이면 그린웨이브 유지.
            var a = new Signal { CycleSlots = 12, OffsetSlots = 0 };
            var b = new Signal { CycleSlots = 12, OffsetSlots = 4 };
            Assert.AreEqual(1f, SignalMath.GreenWaveEfficiency(a, b, travelSlots: 4 + 12, floor: 0.5f), 1e-4f);
            Assert.AreEqual(
                SignalMath.GreenWaveEfficiency(a, b, 8, 0.5f),
                SignalMath.GreenWaveEfficiency(a, b, 8 + 12, 0.5f), 1e-4f);   // 어긋난 정도도 주기 등가
        }

        [Test]
        public void GreenWave_MismatchedCycles_StaysInRange()
        {
            // 같은 주기 가정의 안전망: 주기가 다르면 수치 의미는 미정의(2차)지만
            // 어떤 travel에서도 [floor, 1] 범위·NaN 없음은 보장돼야 엔진이 안 깨진다.
            var a = new Signal { CycleSlots = 12 };
            var b = new Signal { CycleSlots = 8, OffsetSlots = 3 };
            for (int travel = 0; travel <= 24; travel++)
            {
                float e = SignalMath.GreenWaveEfficiency(a, b, travel, 0.5f);
                Assert.IsFalse(float.IsNaN(e), $"travel={travel}: NaN");
                Assert.GreaterOrEqual(e, 0.5f, $"travel={travel}: floor 아래");
                Assert.LessOrEqual(e, 1f, $"travel={travel}: 1 초과");
            }
        }

        [Test]
        public void GreenWave_ExtremeFloorAndZeroCycle_AreSafe()
        {
            // 튜닝 실수 방어: floor=1이면 항상 1(페널티 무력화), floor=0도 [0,1] 유지.
            // 주기 0 신호는 규약대로 페널티 없음(상류) / 범위 유지(하류).
            var a = new Signal { CycleSlots = 12, OffsetSlots = 0 };
            var worst = new Signal { CycleSlots = 12, OffsetSlots = 8 };   // δ=4.0 ≈ 최악

            Assert.AreEqual(1f, SignalMath.GreenWaveEfficiency(a, worst, 4, floor: 1f), 1e-4f);
            float e0 = SignalMath.GreenWaveEfficiency(a, worst, 4, floor: 0f);
            Assert.GreaterOrEqual(e0, 0f); Assert.LessOrEqual(e0, 1f);

            Assert.AreEqual(1f, SignalMath.GreenWaveEfficiency(new Signal { CycleSlots = 0 }, worst, 4, 0.5f), 1e-4f); // 상류 주기 0 = 통과
            float eTo = SignalMath.GreenWaveEfficiency(a, new Signal { CycleSlots = 0 }, 4, 0.5f);                     // 하류 주기 0
            Assert.IsFalse(float.IsNaN(eTo));
            Assert.GreaterOrEqual(eTo, 0.5f); Assert.LessOrEqual(eTo, 1f);
        }
    }
}
