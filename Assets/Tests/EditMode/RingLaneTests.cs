using System.Collections.Generic;
using NUnit.Framework;
using CityFlow.ViewKit;

namespace CityFlow.Sim.Tests
{
    // RingLane 순수 기하(Task 6R — 공유 링 레인). arc 단위 = 호 길이.
    // 자기 자신 제외는 호출자 책임 — otherArcs/ringArcs 목록에 자신을 넣지 않는다.
    public class RingLaneTests
    {
        // CCW 랩어라운드: 원둘레 10에서 9→1 = 2 (0을 넘는 간격).
        [Test]
        public void CcwGap_WrapsAroundZero()
        {
            Assert.AreEqual(2f, RingLane.CcwGap(9f, 1f, 10f), 1e-4f);
            Assert.AreEqual(3f, RingLane.CcwGap(1f, 4f, 10f), 1e-4f);
            Assert.AreEqual(0f, RingLane.CcwGap(5f, 5f, 10f), 1e-4f, "동일 지점 = 0");
        }

        // 리더 선택 결정론(동률 없음 가정): CCW 앞쪽 최근접 하나. 링에 아무도 없으면 MaxValue.
        [Test]
        public void LeaderGap_PicksNearestCcwAhead()
        {
            var others = new List<float> { 3f, 8f, 6f };
            Assert.AreEqual(1f, RingLane.LeaderGap(5f, others, 10f), 1e-4f, "5 앞 최근접 = 6 (간격 1)");
            Assert.AreEqual(4f, RingLane.LeaderGap(9f, others, 10f), 1e-4f, "랩어라운드: 9 앞 최근접 = 3 (간격 4)");
            Assert.AreEqual(float.MaxValue, RingLane.LeaderGap(0f, new List<float>(), 10f), "빈 링 = MaxValue");
        }

        // 합류 창 판정: 합류점 상류 gapWindow 안에 링 차가 있으면 그 차→합류점 간격, 없으면 MaxValue.
        [Test]
        public void MergeGap_WindowGatesUpstreamCars()
        {
            // 합류점 2, 상류 차 1(간격 1 ≤ 창 2) → 1. 차 9는 간격 3 > 창 → 제외.
            Assert.AreEqual(1f, RingLane.MergeGap(2f, new List<float> { 1f, 9f }, 2f, 10f), 1e-4f);
            // 창 밖(상류 간격 7) → MaxValue.
            Assert.AreEqual(float.MaxValue, RingLane.MergeGap(2f, new List<float> { 5f }, 2f, 10f));
            // 합류점 직후(하류) 차는 게이트 아님 — CCW 간격 9.5 > 창.
            Assert.AreEqual(float.MaxValue, RingLane.MergeGap(2f, new List<float> { 2.5f }, 2f, 10f));
            // 랩어라운드 상류: 합류점 0.5, 차 9.5 → 간격 1 ≤ 창 2.
            Assert.AreEqual(1f, RingLane.MergeGap(0.5f, new List<float> { 9.5f }, 2f, 10f), 1e-4f);
        }
    }
}
