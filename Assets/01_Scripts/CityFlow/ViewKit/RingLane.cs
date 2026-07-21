using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.ViewKit
{
    // 로터리 공유 1차선의 순수 기하(Task 6R — 아키텍처 문서 §5 축소판). 무상태:
    // 링 위 차의 좌표는 호출자가 atan2(pos − center)에 반경을 곱해 "호 길이"로 환산해 넘긴다.
    // 우선권은 링이 절대·단방향 — 진입 차만 MergeGap으로 기다리고, 링 차는 LeaderGap(앞차)만
    // 본다. 상호 대기가 불가능해 데드락이 원천 차단된다(링은 출구로 항상 드레인).
    // 자기 자신 제외는 호출자 책임 — otherArcs/ringArcs에 자신을 넣지 말 것.
    public static class RingLane
    {
        // from에서 CCW로 to까지의 호 길이 [0, circumference).
        public static float CcwGap(float fromArc, float toArc, float circumference)
        {
            return Mathf.Repeat(toArc - fromArc, circumference);
        }

        // 내 앞(CCW 방향) 가장 가까운 링 차와의 호 간격. 링에 아무도 없으면 float.MaxValue.
        public static float LeaderGap(float myArc, IReadOnlyList<float> otherArcs, float circumference)
        {
            float nearest = float.MaxValue;
            for (int i = 0; i < otherArcs.Count; i++)
            {
                float gap = CcwGap(myArc, otherArcs[i], circumference);
                if (gap < nearest)
                {
                    nearest = gap;
                }
            }

            return nearest;
        }

        // 합류점 상류(entryArc로 CCW 접근 중) gapWindow 안에 링 차가 있으면
        // 가장 가까운 차→합류점 호 간격, 없으면 float.MaxValue. 하류(합류점을 지난) 차는 게이트 아님.
        public static float MergeGap(float entryArc, IReadOnlyList<float> ringArcs, float gapWindow, float circumference)
        {
            float nearest = float.MaxValue;
            for (int i = 0; i < ringArcs.Count; i++)
            {
                float gap = CcwGap(ringArcs[i], entryArc, circumference);
                if (gap <= gapWindow && gap < nearest)
                {
                    nearest = gap;
                }
            }

            return nearest;
        }
    }
}
