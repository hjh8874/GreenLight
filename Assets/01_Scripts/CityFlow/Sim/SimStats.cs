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
        private int _lastDayArrivals;

        internal float TripSuccessRate { get; private set; } = 1f;
        internal int DayArrivalCount => _dayArrivals;
        internal int LastDayArrivalCount => _lastDayArrivals;
        internal bool SkipCurrentDay => _skipCurrentDay;

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
                if (!_skipCurrentDay)
                {
                    _lastDayArrivals = _dayArrivals;   // 시각 점프로 끊긴 날은 캡처하지 않는다
                }
                _dayArrivals = 0;
                _skipCurrentDay = false;
            }

            _dayArrivals += arrivals;
            _lastHour = gameHour;
            _hasLastHour = true;
        }

        internal void RestoreCarSim(float successRate, int dayArrivals, bool skipCurrentDay, bool hasData)
        {
            TripSuccessRate = hasData ? Mathf.Clamp01(successRate) : 1f;
            _dayArrivals = hasData ? Mathf.Max(0, dayArrivals) : 0;
            _skipCurrentDay = hasData && skipCurrentDay;
            _hasLastHour = false;
        }
    }
}
