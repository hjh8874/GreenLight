using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim
{
    // delivered(rate, 대/초)를 시간으로 적분해 도착 '정수'를 방출. 소수는 _acc에 이월 —
    // 이 이월이 온라인 합계 = 오프라인 정산 공식 산술 일치의 핵심(blueprint §3).
    internal sealed class ArrivalEmitter
    {
        readonly int _w;
        readonly float[] _acc;   // 수요처 타일별 도착 누산(소수 이월, 틱 넘어 유지)

        public ArrivalEmitter(int width, int height)
        {
            _w = width;
            _acc = new float[width * height];
        }

        public void Emit(FlowSolver solver, SimEventBuffer events, in SimConfig cfg)
        {
            for (int i = 0; i < _acc.Length; i++)
            {
                _acc[i] += solver.GetDeliveredToSink(i) * cfg.TickInterval;
                while (_acc[i] >= 1f)
                {
                    _acc[i] -= 1f;
                    var tile = new Vector2Int(i % _w, i / _w);
                    // ponytail: coin = CoinBase 고정. distanceFactor(L)는 D7 정산 공식 확정 때.
                    events.QueueArrival(new ArrivalEvent(tile, (int)cfg.CoinBase));
                }
            }
        }
    }
}
