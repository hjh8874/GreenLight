using System;
using System.Collections.Generic;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim
{
    // 틱의 심장. Assign: 수요마다 접점→캐시 경로→경로 타일에 flow 누적.
    // Resolve: 타일 ratio→혼잡 레벨, 경로 병목→E→delivered 합산.
    // 세그먼트 상태는 SoA(필드별 flat 배열) — 순회 빠르고 스냅샷·저장 쉬움. 틱 중 new 0.
    internal sealed class FlowSolver
    {
        readonly int _w;
        readonly float[] _flowH;   // 타일별 가로축 흐름(대/초). 대각 스텝은 양축 0.5씩(근사)
        readonly float[] _flowV;
        readonly float[] _ratioH;  // 축별 ratio. 교차로가 아니면 양축 동일(합산/C = 기존 규약)
        readonly float[] _ratioV;
        readonly CongestionLevel[] _level;
        readonly float[] _pendingReward;   // 병목 타일에 쌓인 잃은 처리량(틱 넘어 누적, D4가 소비)

        // 이번 틱에 실제로 흐른 경로들. RoadNetwork 캐시의 참조만 담음(소유 X, 틱 중 new 0).
        readonly List<List<Vector2Int>> _routes = new(128);
        readonly List<Vector2Int> _routeSinks = new(128);   // 경로별 도착 건물 타일(_routes와 나란히)
        readonly float[] _deliveredToSink;                  // 수요처 타일별 이번 틱 처리량(대/초)

        public float DeliveredTotal { get; private set; }   // 이번 틱 총 처리량(대/초)

        // 이번 틱의 가구당 수요율(대/초) = DemandPerHouse × 맥동 배율. Assign이 기록,
        // Resolve·SimStats가 같은 값을 쓴다 — 분자·분모가 못 갈라짐.
        public float DemandRate { get; private set; }

        // 이번 틱에 흐른 실제 경로들(뷰가 차를 이 위에 그림). 읽기 전용.
        public IReadOnlyList<List<Vector2Int>> Routes => _routes;

        public FlowSolver(int width, int height)
        {
            _w = width;
            int n = width * height;
            _flowH = new float[n];
            _flowV = new float[n];
            _ratioH = new float[n];
            _ratioV = new float[n];
            _level = new CongestionLevel[n];
            _pendingReward = new float[n];
            _deliveredToSink = new float[n];
        }

        int Index(Vector2Int t) => t.y * _w + t.x;

        // demandScale: 이번 틱 수요 맥동 배율(SimConfig.DemandPulse). 1 = 균일(기존 동작).
        // 경로는 RoutePlanner가 재건축 시 계획한 테이블(수요 인덱스 정렬)을 읽음 — 매 틱 탐색 없음.
        public void Assign(DemandMap demand, RoutePlanner planner, in SimConfig cfg, float demandScale = 1f)
        {
            Array.Clear(_flowH, 0, _flowH.Length);
            Array.Clear(_flowV, 0, _flowV.Length);
            _routes.Clear();
            _routeSinks.Clear();
            DemandRate = cfg.DemandPerHouse * demandScale;

            var demands = demand.Demands;
            var planned = planner.Routes;
            // 불변식: Reassign 직후 반드시 Plan이 돈다(SimEngine 더티 블록이 쌍으로 보장) — 어긋나면 즉시 진단.
            UnityEngine.Debug.Assert(planned.Count == demands.Count, "RoutePlanner.Plan이 최신 Reassign을 반영하지 않음");
            for (int i = 0; i < demands.Count; i++)
            {
                var path = planned[i];
                if (path == null) continue;                   // 접점 없음/미연결 = 흐르지 않음(무사고)

                for (int p = 0; p < path.Count; p++)
                {
                    var (wH, wV) = AxisWeights(path, p);
                    int i2 = Index(path[p]);
                    _flowH[i2] += DemandRate * wH;
                    _flowV[i2] += DemandRate * wV;
                }
                _routes.Add(path);
                _routeSinks.Add(demands[i].Sink);
            }
        }

        // 타일 p의 축 가중치 — 진입 스텝 기준(첫 타일은 출발 스텝). 대각 = 양축 절반.
        // 단일 타일 경로(출발=도착)는 축 모호 → 0.5/0.5 (결정론적 근사).
        static (float wH, float wV) AxisWeights(List<Vector2Int> path, int p)
        {
            if (path.Count < 2) return (0.5f, 0.5f);
            var step = p > 0 ? path[p] - path[p - 1] : path[1] - path[0];
            if (step.x != 0 && step.y != 0) return (0.5f, 0.5f);
            return step.y == 0 ? (1f, 0f) : (0f, 1f);
        }

        // 축별 ratio — 용량 0 규약은 기존과 동일(흐르면 최악 병목).
        static float AxisRatio(float flow, float cap, in SimConfig cfg) =>
            cap > 0f ? flow / cap : flow > 0f ? cfg.EfficiencyMinRatio : 0f;

        // 신호·grid 없는 호출(기존 테스트 호환) — 전 타일 일반 도로 규약.
        public void Resolve(in SimConfig cfg) => Resolve(cfg, null, null);

        // grid 없는 호출(기존 테스트 호환) — 무신호 간섭 없음(신호 타일만 축별 듀티).
        public void Resolve(in SimConfig cfg, SignalMap signals, double simTime = 0)
            => Resolve(cfg, signals, null, simTime);

        // 로터리 없는 호출(기존 테스트 호환).
        public void Resolve(in SimConfig cfg, SignalMap signals, CityGrid grid, double simTime = 0)
            => Resolve(cfg, signals, grid, null, simTime);

        // 캐노니컬: delivered = 수요 × E(축별 병목) × SignalFactor(그린웨이브).
        // roundabouts = 엔진 소유 배치 셋(조회만 — 소유·갱신은 SimEngine, 스펙 §2).
        public void Resolve(in SimConfig cfg, SignalMap signals, CityGrid grid,
                            HashSet<Vector2Int> roundabouts, double simTime = 0)
        {
            // ① 기본: 전 타일 합산 ratio(일반 도로 — 직선엔 교차 충돌 없음). 교차로만 아래서 덮어씀.
            for (int i = 0; i < _flowH.Length; i++)
            {
                float r = (_flowH[i] + _flowV[i]) / cfg.RoadCapacity;
                _ratioH[i] = r;
                _ratioV[i] = r;
                _level[i] = Classify(r, cfg);
            }

            // ①' 신호 교차로: 축별 듀티 용량(가로 d·세로 1−d) — "보는 것 = 버는 것".
            // 오버라이드(정령 마법)는 양축 풀 용량 = 3초간 충돌 소멸(스펙 §3).
            if (signals != null)
            {
                var tiles = signals.Tiles;
                for (int k = 0; k < tiles.Count; k++)
                {
                    if (!signals.TryGet(tiles[k], out var s)) continue;
                    if (s.CycleSlots <= 0) continue;   // 주기 0 = 항상 초록(IsGreen과 같은 규약)
                    bool ovr = s.OverrideUntil > simTime;
                    float g = SignalMath.GreenRatio(s);
                    int i = Index(tiles[k]);
                    _ratioH[i] = AxisRatio(_flowH[i], cfg.RoadCapacity * (ovr ? 1f : g), cfg);
                    _ratioV[i] = AxisRatio(_flowV[i], cfg.RoadCapacity * (ovr ? 1f : 1f - g), cfg);
                    _level[i] = Classify(Mathf.Max(_ratioH[i], _ratioV[i]), cfg);
                }
            }

            // ①'' 무신호 교차로: 간섭 모델 — 교차 교통이 양보 협상만큼(λ) 내 축을 방해(스펙 §2).
            // 자동생성 유지 중엔 라이브 미노출(모든 교차로에 신호) — 구매 피벗 2단계 대비.
            // 로터리는 λr·cf(스펙 2026-07-11).
            if (grid != null)
            {
                for (int y = 0; y < grid.Height; y++)
                    for (int x = 0; x < grid.Width; x++)
                    {
                        var t = new Vector2Int(x, y);
                        if (!grid.IsIntersection(t)) continue;
                        if (signals != null && signals.TryGet(t, out _)) continue;   // 신호가 처리함
                        int i = Index(t);
                        if (roundabouts != null && roundabouts.Contains(t))
                        {
                            // 로터리: 양보 간섭 급감(λr) 대신 전원 감속(용량 ×cf) — 스펙 §1 수식.
                            float cap = cfg.RoadCapacity * cfg.RoundaboutCapacityFactor;
                            _ratioH[i] = (_flowH[i] + cfg.RoundaboutInterference * _flowV[i]) / cap;
                            _ratioV[i] = (_flowV[i] + cfg.RoundaboutInterference * _flowH[i]) / cap;
                        }
                        else
                        {
                            _ratioH[i] = (_flowH[i] + cfg.UnsignaledInterference * _flowV[i]) / cfg.RoadCapacity;
                            _ratioV[i] = (_flowV[i] + cfg.UnsignaledInterference * _flowH[i]) / cfg.RoadCapacity;
                        }
                        _level[i] = Classify(Mathf.Max(_ratioH[i], _ratioV[i]), cfg);
                    }
            }

            // ② 경로별: 병목(최대 ratio) → E → delivered + 잃은 만큼 병목 타일에 pending 적립
            DeliveredTotal = 0f;
            Array.Clear(_deliveredToSink, 0, _deliveredToSink.Length);
            for (int r = 0; r < _routes.Count; r++)
            {
                var path = _routes[r];
                float bottleneck = 0f;
                int bottleneckIdx = -1;
                for (int p = 0; p < path.Count; p++)
                {
                    var (wH, wV) = AxisWeights(path, p);
                    int idx = Index(path[p]);
                    float rt = wH > 0f && wV > 0f ? Mathf.Max(_ratioH[idx], _ratioV[idx])
                             : wH > 0f ? _ratioH[idx] : _ratioV[idx];
                    if (rt > bottleneck) { bottleneck = rt; bottleneckIdx = idx; } // strict > → 결정론
                }

                float e = Efficiency(bottleneck, cfg);
                float delivered = DemandRate * e * SignalFactor(path, signals, cfg);
                DeliveredTotal += delivered;
                _deliveredToSink[Index(_routeSinks[r])] += delivered;

                // 잃은 처리량(rate×틱=대수)을 병목에 적립 — 나중에 그 타일을 고치면 Burst 보상의 원료.
                // 신호 손실은 pending에 안 넣음: 조율의 보상은 Burst가 아니라 그린웨이브 처리량 자체(설계 §2).
                if (e < 1f && bottleneckIdx >= 0)
                    _pendingReward[bottleneckIdx] += DemandRate * (1f - e) * cfg.TickInterval;
            }
        }

        // 경로가 지나는 인접 신호쌍들의 그린웨이브 효율을 min으로 합성(설계 §4 — 병목 철학과 동일).
        // 신호 0~1개면 조율할 짝이 없으니 1. travelSlots = 신호 사이 물리 거리(직각 1슬롯,
        // 대각 √2슬롯) — 인덱스 거리로 세면 대각 경로가 실제보다 빨리 도착한 걸로 계산돼
        // 그린웨이브 조율이 어긋남(대각 치팅).
        static float SignalFactor(List<Vector2Int> path, SignalMap signals, in SimConfig cfg)
        {
            if (signals == null) return 1f;

            float factor = 1f;
            Signal prev = null;
            float slots = 0f, prevSlots = 0f;   // 누적 이동 슬롯(물리 거리)
            for (int p = 0; p < path.Count; p++)
            {
                if (p > 0)
                {
                    var step = path[p] - path[p - 1];
                    slots += (step.x != 0 && step.y != 0) ? Sqrt2 : 1f;
                }
                if (!signals.TryGet(path[p], out var sig)) continue;
                if (prev != null)
                {
                    float e = SignalMath.GreenWaveEfficiency(prev, sig, slots - prevSlots, cfg.GreenWaveFloor);
                    if (e < factor) factor = e;
                }
                prev = sig;
                prevSlots = slots;
            }
            return factor;
        }

        const float Sqrt2 = 1.4142135f;   // 대각 스텝의 물리 거리(타일)

        public CongestionLevel GetCongestion(Vector2Int t) => _level[Index(t)];

        // 단일값 소비자(BurstDetector·CongestionNotifier·차량 감속·안정도)용 — 최악 축.
        // 배관이지 화면 페인트 아님: 혼잡 표현은 차 중심 원칙(스펙 §2, 환 2026-07-11).
        public float GetRatio(Vector2Int t) => GetRatio(Index(t));
        public float GetRatio(int flatIndex) => Mathf.Max(_ratioH[flatIndex], _ratioV[flatIndex]);

        // 테스트 관찰용 seam(InternalsVisibleTo) — 축 적립 검증.
        internal float GetFlowHForTest(int flatIndex) => _flowH[flatIndex];
        internal float GetFlowVForTest(int flatIndex) => _flowV[flatIndex];

        // ArrivalEmitter용 — flat 인덱스로 수요처 처리량 조회(전 타일 순회 전제).
        public float GetDeliveredToSink(int flatIndex) => _deliveredToSink[flatIndex];

        public float GetPendingReward(Vector2Int t) => _pendingReward[Index(t)];

        // BurstDetector용 flat 인덱스 접근(전 타일 순회 전제).
        public float GetPendingReward(int flatIndex) => _pendingReward[flatIndex];
        public void ClearPendingReward(int flatIndex) => _pendingReward[flatIndex] = 0f;   // Burst가 소비
        public void ClearPendingReward(Vector2Int t) => _pendingReward[Index(t)] = 0f;     // 철거 소각용
        // 장부는 도시 상태와 생명주기 공유 — 세이브 복원 시 이전 도시의 유령 장부 방지(리뷰 2026-07-11).
        public void ClearAllPendingRewards() => Array.Clear(_pendingReward, 0, _pendingReward.Length);

        static CongestionLevel Classify(float ratio, in SimConfig cfg) =>
            ratio > cfg.JamRatio ? CongestionLevel.Jam
            : ratio >= cfg.SlowRatio ? CongestionLevel.Slow
            : CongestionLevel.Free;

        // E(병목): ratio ≤ JamRatio → 1(자유 흐름), EfficiencyMinRatio까지 선형 하락, 이후 바닥.
        internal static float Efficiency(float ratio, in SimConfig cfg)
        {
            if (ratio <= cfg.JamRatio) return 1f;
            if (ratio >= cfg.EfficiencyMinRatio) return cfg.EfficiencyMin;
            float t = (ratio - cfg.JamRatio) / (cfg.EfficiencyMinRatio - cfg.JamRatio);
            return 1f + t * (cfg.EfficiencyMin - 1f);
        }
    }
}
