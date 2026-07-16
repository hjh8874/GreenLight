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

        // 로터리 arc: 직진(→ 진입, → 이탈) = 180° 스윕, laneShift 반영.
        [Test]
        public void RoundaboutArc_StraightThrough_HalfSweep()
        {
            bool ok = PolylineMath.TryGetRoundaboutArc(
                Vector3.right, Vector3.right, laneOffset: 0.18f, orbitRadius: 0.68f,
                out float entry, out float sweep);
            Assert.IsTrue(ok);
            float laneShift = Mathf.Asin(0.18f / 0.68f);
            // 진입각은 브랜치 불문 mod 2π 동치로 비교 — IEEE-754 signed zero로
            // Atan2(-0f,-1f) = -π (+π 아님)라 절대값 비교는 같은 각도를 기각한다(Task 1 실측).
            float entryDelta = Mathf.Repeat(entry - (Mathf.PI + laneShift) + Mathf.PI, 2f * Mathf.PI) - Mathf.PI;
            Assert.AreEqual(0f, entryDelta, 1e-4f, "진입각 ≡ π+δ (mod 2π)");
            Assert.AreEqual(Mathf.PI - 2f * laneShift, sweep, 1e-4f, "직진 스윕 = π − 2δ");
        }
    }
}
