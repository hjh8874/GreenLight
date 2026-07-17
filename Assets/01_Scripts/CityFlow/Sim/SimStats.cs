using UnityEngine;

namespace CityFlow.Sim
{
    // 도시 지표 집계. 안정도 = delivered ÷ 총 수요 — "도시가 통근 수요를 얼마나 처리하나".
    // 전 경로 Free(E=1)면 1.0, 병목이 생기면 E만큼 하락. 미연결 수요도 분모에 포함(연결하라는 신호).
    internal sealed class SimStats
    {
        private const float SuccessEmaAlpha = 0.5f;
        private float _lastHour;
        private bool _hasLastHour;
        private bool _skipCurrentDay;
        private int _dayArrivals;

        public float Stability01 { get; private set; } = 1f;
        internal float TripSuccessRate { get; private set; } = 1f;
        internal int DayArrivalCount => _dayArrivals;
        internal bool SkipCurrentDay => _skipCurrentDay;

        // ponytail: 정산용 롤링 평균은 D7(정산 공식)에서. 지금은 안정도만.
        public void Update(FlowSolver solver, DemandMap demand, in SimConfig cfg)
        {
            // 분모도 이번 틱의 맥동 반영 수요율(solver.DemandRate) — 러시아워라고
            // 처리만 잘 되면 안정도가 1을 넘거나 억울하게 깎이는 일이 없게.
            float totalDemand = demand.Demands.Count * solver.DemandRate;
            float baseStability = totalDemand <= 0f
                ? 1f   // 수요 없는 도시는 불안정할 것도 없음(0 나누기 방지)
                : Mathf.Clamp01(solver.DeliveredTotal / totalDemand);
            float jamPenalty = 1f - solver.JamTileRatio * cfg.StabilityJamWeight;
            Stability01 = Mathf.Clamp01(baseStability * jamPenalty);
        }

        internal void UpdateCarSim(
            float gameHour,
            int arrivals,
            int carCount,
            bool jumped,
            float jamRatio,
            in SimConfig cfg)
        {
            bool wrapped = _hasLastHour && gameHour < _lastHour;
            if (jumped)
            {
                _dayArrivals = 0;
                _skipCurrentDay = true;
            }
            else if (wrapped)
            {
                if (!_skipCurrentDay)
                {
                    int expected = carCount * 2;
                    float dayRate = expected <= 0
                        ? 1f
                        : Mathf.Clamp01((float)_dayArrivals / expected);
                    TripSuccessRate = Mathf.Lerp(TripSuccessRate, dayRate, SuccessEmaAlpha);
                }
                _dayArrivals = 0;
                _skipCurrentDay = false;
            }

            _dayArrivals += arrivals;
            _lastHour = gameHour;
            _hasLastHour = true;
            Stability01 = Mathf.Clamp01(
                TripSuccessRate * (1f - jamRatio * cfg.StabilityJamWeight));
        }

        internal void RestoreCarSim(float successRate, int dayArrivals, bool skipCurrentDay, bool hasData)
        {
            TripSuccessRate = hasData ? Mathf.Clamp01(successRate) : 1f;
            _dayArrivals = hasData ? Mathf.Max(0, dayArrivals) : 0;
            _skipCurrentDay = hasData && skipCurrentDay;
            _hasLastHour = false;
            Stability01 = TripSuccessRate;
        }
    }
}
