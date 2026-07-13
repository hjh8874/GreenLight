using System;
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

        // 복원 = 전체 교체: 이전 도시의 이월 소수가 새 도시의 첫 도착 타이밍을 오염시키지 않게(감사 2026-07-12).
        public void ClearAll() => Array.Clear(_acc, 0, _acc.Length);

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
                    // 절삭이 아니라 반올림 — CoinBase<1 튜닝에서 코인 0 고착 방지(감사 2026-07-12). 소수 누적 설계는 경제(진우) 몫
                    events.QueueArrival(new ArrivalEvent(tile, Mathf.RoundToInt(cfg.CoinBase)));
                }
            }
        }

        // 오프라인 정산: 온라인과 '같은 누산기(_acc)'로 경과시간을 한 방에 적분.
        // 이월이 자동으로 이어져 복귀 보상 = 켜놨을 때 보상(정수 코인 기준)이 구조로 보장된다.
        public long SettleOffline(FlowSolver solver, double elapsedSeconds, in SimConfig cfg)
        {
            long arrivals = 0;
            for (int i = 0; i < _acc.Length; i++)
            {
                double acc = _acc[i] + solver.GetDeliveredToSink(i) * elapsedSeconds;
                long n = (long)acc;              // 소수점 버림 = 도착 정수
                arrivals += n;
                _acc[i] = (float)(acc - n);      // 나머지는 다음(온라인/오프라인)으로 이월
            }
            // 절삭이 아니라 반올림 — CoinBase<1 튜닝에서 코인 0 고착 방지(감사 2026-07-12). 소수 누적 설계는 경제(진우) 몫
            return (long)System.Math.Round(arrivals * (double)cfg.CoinBase);
        }
    }
}
