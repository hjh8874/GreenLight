using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim
{
    // Jam→Free '전이'를 감지해 그 타일에 쌓인 pendingReward를 Burst 보상으로 발행.
    // 감각의 안정성 장치 두 개(blueprint §2):
    //  - 히스테리시스: 진입선(≥1.0)과 복귀선(≤0.6)을 벌려 경계 진동에 무반응
    //  - 쿨다운: 타일당 10s — 같은 곳 연사 방지
    // ★재미리서치: Burst는 화면 전체가 아니라 "고친 그 세그먼트"에 국소적, 보상은 병목 크기에 비례.
    internal sealed class BurstDetector
    {
        readonly int _w;
        readonly bool[] _jammed;         // 타일별 히스테리시스 상태
        readonly float[] _cooldownLeft;  // 타일별 남은 쿨다운(초)

        public BurstDetector(int width, int height)
        {
            _w = width;
            int n = width * height;
            _jammed = new bool[n];
            _cooldownLeft = new float[n];
        }

        // 복원 = 전체 교체: 이전 도시의 jam 상태·쿨다운이 새 도시의 정당한 Burst를 억누르지 않게(감사 2026-07-12).
        public void ClearAll()
        {
            System.Array.Clear(_jammed, 0, _jammed.Length);
            System.Array.Clear(_cooldownLeft, 0, _cooldownLeft.Length);
        }

        public void Scan(FlowSolver solver, SimEventBuffer events, in SimConfig cfg)
        {
            for (int i = 0; i < _jammed.Length; i++)
            {
                if (_cooldownLeft[i] > 0f) _cooldownLeft[i] -= cfg.TickInterval;

                float ratio = solver.GetRatio(i);

                if (!_jammed[i])
                {
                    if (ratio >= cfg.BurstJamEnterRatio) _jammed[i] = true;
                    continue;
                }
                if (ratio > cfg.BurstFreeReturnRatio) continue;   // 복귀선 위 → Jam 유지(진동 무시)

                _jammed[i] = false;                               // Jam → Free 전이!
                float pending = solver.GetPendingReward(i);
                if (pending < cfg.BurstRewardThreshold) continue; // 티끌 병목은 발행 안 함
                if (_cooldownLeft[i] > 0f) continue;              // 연사 방지(pending은 유지)

                // pending은 '잃은 차량 수' 단위 — 도착 코인과 같은 환율(CoinBase)로 환전해야 파밍 중립이 CoinBase≠1에서도 유지(감사 2026-07-12)
                int reward = Mathf.RoundToInt(pending * cfg.BurstRewardMultiplier * cfg.CoinBase);
                events.QueueBurst(new FlowBurstEvent(
                    new Vector2Int(i % _w, i / _w),
                    reward));
                solver.ClearPendingReward(i);                     // 보상은 1회만 — 소비
                _cooldownLeft[i] = cfg.BurstCooldownSeconds;
            }
        }
    }
}
