using NUnit.Framework;
using UnityEngine;
using CityFlow.ViewKit;

namespace CityFlow.Sim.Tests
{
    public class PolylineMathTests
    {
        // 쿼터서클 베지어: t=0/1에서 끝점 일치, 중간 반경 오차 < 2%.
        [Test]
        public void QuarterCircleBezier_EndpointsAndRadius()
        {
            Vector3 entry = new Vector3(1f, 0f, 0f);
            Vector3 exit = new Vector3(0f, 1f, 0f);
            const float k = 0.55228475f;
            Vector3 cIn = entry + new Vector3(0f, k, 0f);
            Vector3 cOut = exit + new Vector3(k, 0f, 0f);

            Assert.AreEqual(0f, Vector3.Distance(entry,
                PolylineMath.EvaluateCubicBezier(entry, cIn, cOut, exit, 0f)), 1e-4f);
            Assert.AreEqual(0f, Vector3.Distance(exit,
                PolylineMath.EvaluateCubicBezier(entry, cIn, cOut, exit, 1f)), 1e-4f);
            Vector3 mid = PolylineMath.EvaluateCubicBezier(entry, cIn, cOut, exit, 0.5f);
            Assert.AreEqual(1f, mid.magnitude, 0.02f, "쿼터서클 반경 근사");
        }

        // 아크렝스 리맵: 0→0, 1→1, 단조 증가.
        [Test]
        public void ArcLengthRemap_MonotonicAndClamped()
        {
            Vector3 a = Vector3.zero, b = new Vector3(1f, 0f, 0f);
            Vector3 c1 = new Vector3(0.3f, 0.5f, 0f), c2 = new Vector3(0.7f, 0.5f, 0f);
            float prev = -1f;
            for (int i = 0; i <= 10; i++)
            {
                float u = PolylineMath.RemapBezierParameterByArcLength(a, c1, c2, b, i / 10f);
                Assert.Greater(u, prev, "단조 증가");
                prev = u;
            }
            Assert.AreEqual(0f, PolylineMath.RemapBezierParameterByArcLength(a, c1, c2, b, 0f), 1e-3f);
            Assert.AreEqual(1f, PolylineMath.RemapBezierParameterByArcLength(a, c1, c2, b, 1f), 1e-3f);
        }

        [Test]
        public void InterpolateTickDistance_PreservesSpacingBetweenSimSnapshots()
        {
            float lead = PolylineMath.InterpolateTickDistance(1f, 2f, 0.5f);
            float follow = PolylineMath.InterpolateTickDistance(0.6f, 1.6f, 0.5f);

            Assert.AreEqual(0.4f, lead - follow, 1e-4f,
                "같은 틱 위상으로 보간하면 이전·현재 스냅샷의 차간격이 유지돼야 한다");
        }

        // 순항 중에는 등속이어야 한다. SmoothStep 이징은 t=0·t=1에서 미분이 0이라
        // 매 틱(=타일 하나)마다 차를 세웠다 출발시켜, 틱이 길수록 맥동이 보인다
        // (환 라이브 2026-07-18: TickInterval 0.33에서 뚝뚝 끊김).
        [Test]
        public void TickEase_Cruising_IsLinear()
        {
            Assert.AreEqual(0.25f, PolylineMath.TickEase(0.25f, cruising: true), 1e-4f);
            Assert.AreEqual(0.75f, PolylineMath.TickEase(0.75f, cruising: true), 1e-4f);

            float early = PolylineMath.TickEase(0.2f, true) - PolylineMath.TickEase(0.0f, true);
            float middle = PolylineMath.TickEase(0.6f, true) - PolylineMath.TickEase(0.4f, true);
            Assert.AreEqual(early, middle, 1e-4f, "순항은 같은 위상구간 = 같은 이동(등속)");
        }

        // 출발·정지 틱은 이징 유지 — 가감속이 실제로 일어나는 순간이라 자연스럽다.
        [Test]
        public void TickEase_NotCruising_EasesEnds()
        {
            Assert.Less(PolylineMath.TickEase(0.25f, cruising: false), 0.25f,
                "출발 구간은 천천히 시작해야 한다");
            Assert.Greater(PolylineMath.TickEase(0.75f, cruising: false), 0.75f,
                "정지 직전은 감속해 붙어야 한다");
            Assert.AreEqual(0f, PolylineMath.TickEase(0f, false), 1e-4f);
            Assert.AreEqual(1f, PolylineMath.TickEase(1f, false), 1e-4f);
        }

        [Test]
        public void ParkingSlotOffset_SixSlotsUseSpacedGrid()
        {
            var slots = new Vector2[6];
            for (int i = 0; i < slots.Length; i++)
                slots[i] = PolylineMath.ParkingSlotOffset(i, slots.Length, 0.32f);

            for (int i = 0; i < slots.Length; i++)
            for (int j = i + 1; j < slots.Length; j++)
                Assert.GreaterOrEqual(Vector2.Distance(slots[i], slots[j]), 0.3f - 1e-4f);
        }

        // 로터리 arc(mouth±α, QA G): 직진(→ 진입, → 이탈)은 진입각 = mouth+α, 스윕 = π/2.
        static readonly float Alpha = 45f * Mathf.Deg2Rad;

        [Test]
        public void RoundaboutArc_StraightThrough_HalfSweep()
        {
            bool ok = PolylineMath.TryGetRoundaboutArc(
                Vector3.right, Vector3.right, Alpha, out float entry, out float sweep);
            Assert.IsTrue(ok);
            // 진입각은 브랜치 불문 mod 2π 동치로 비교 — IEEE-754 signed zero로
            // Atan2(-0f,-1f) = -π (+π 아님)라 절대값 비교는 같은 각도를 기각한다(Task 1 실측).
            float entryDelta = Mathf.Repeat(entry - (Mathf.PI + Alpha) + Mathf.PI, 2f * Mathf.PI) - Mathf.PI;
            Assert.AreEqual(0f, entryDelta, 1e-4f, "진입각 ≡ π+α (mod 2π)");
            Assert.AreEqual(Mathf.PI / 2f, sweep, 1e-3f, "직진 스윕 = π/2");
        }

        // 좌회전(+x 진입, +y 이탈): CCW 스윕 = π(반바퀴).
        [Test]
        public void RoundaboutArc_LeftTurn_FullSweep()
        {
            bool ok = PolylineMath.TryGetRoundaboutArc(
                Vector3.right, Vector3.up, Alpha, out _, out float sweep);
            Assert.IsTrue(ok);
            Assert.AreEqual(Mathf.PI, sweep, 1e-3f, "좌회전 스윕 = π");
        }

        // 우회전(+x 진입, −y 이탈): α=45°에서 스윕 ≈ 0 → 호출부가 링 생략(< 0.1) 판정.
        [Test]
        public void RoundaboutArc_RightTurn_RingSkipped()
        {
            bool ok = PolylineMath.TryGetRoundaboutArc(
                Vector3.right, Vector3.down, Alpha, out _, out float sweep);
            Assert.IsTrue(ok);
            Assert.Less(sweep, 0.1f, "우회전 스윕 ≈ 0 → 링 생략");
        }
    }
}
