using UnityEngine;

namespace CityFlow.Sim
{
    // 도시 지표 집계. 안정도 = delivered ÷ 총 수요 — "도시가 통근 수요를 얼마나 처리하나".
    // 전 경로 Free(E=1)면 1.0, 병목이 생기면 E만큼 하락. 미연결 수요도 분모에 포함(연결하라는 신호).
    internal sealed class SimStats
    {
        public float Stability01 { get; private set; } = 1f;

        // ponytail: 정산용 롤링 평균은 D7(정산 공식)에서. 지금은 안정도만.
        public void Update(FlowSolver solver, DemandMap demand, in SimConfig cfg)
        {
            // 분모도 이번 틱의 맥동 반영 수요율(solver.DemandRate) — 러시아워라고
            // 처리만 잘 되면 안정도가 1을 넘거나 억울하게 깎이는 일이 없게.
            float totalDemand = demand.Demands.Count * solver.DemandRate;
            Stability01 = totalDemand <= 0f
                ? 1f   // 수요 없는 도시는 불안정할 것도 없음(0 나누기 방지)
                : Mathf.Clamp01(solver.DeliveredTotal / totalDemand);
        }
    }
}
