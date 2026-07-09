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
        readonly float[] _flow;    // 타일별 이번 틱 흐름(대/초)
        readonly float[] _ratio;   // flow / RoadCapacity
        readonly CongestionLevel[] _level;
        readonly float[] _pendingReward;   // 병목 타일에 쌓인 잃은 처리량(틱 넘어 누적, D4가 소비)

        // 이번 틱에 실제로 흐른 경로들. RoadNetwork 캐시의 참조만 담음(소유 X, 틱 중 new 0).
        readonly List<List<Vector2Int>> _routes = new(128);
        readonly List<Vector2Int> _routeSinks = new(128);   // 경로별 도착 건물 타일(_routes와 나란히)
        readonly float[] _deliveredToSink;                  // 수요처 타일별 이번 틱 처리량(대/초)

        public float DeliveredTotal { get; private set; }   // 이번 틱 총 처리량(대/초)

        // 이번 틱에 흐른 실제 경로들(뷰가 차를 이 위에 그림). 읽기 전용.
        public IReadOnlyList<List<Vector2Int>> Routes => _routes;

        public FlowSolver(int width, int height)
        {
            _w = width;
            int n = width * height;
            _flow = new float[n];
            _ratio = new float[n];
            _level = new CongestionLevel[n];
            _pendingReward = new float[n];
            _deliveredToSink = new float[n];
        }

        int Index(Vector2Int t) => t.y * _w + t.x;

        public void Assign(DemandMap demand, RoadNetwork net, in SimConfig cfg)
        {
            Array.Clear(_flow, 0, _flow.Length);
            _routes.Clear();
            _routeSinks.Clear();

            var demands = demand.Demands;
            for (int i = 0; i < demands.Count; i++)
            {
                // 접점이 없거나 미연결이면 이 수요는 흐르지 않음(무사고).
                if (!net.TryGetAccessRoad(demands[i].Source, out var from)) continue;
                if (!net.TryGetAccessRoad(demands[i].Sink, out var to)) continue;
                var path = net.FindPath(from, to);
                if (path == null) continue;

                for (int p = 0; p < path.Count; p++)
                    _flow[Index(path[p])] += cfg.DemandPerHouse;
                _routes.Add(path);
                _routeSinks.Add(demands[i].Sink);
            }
        }

        // 신호 없는 호출(기존 테스트·정산 경로 호환) — factor 항상 1.
        public void Resolve(in SimConfig cfg) => Resolve(cfg, null);

        // 신호 포함 Resolve: delivered = 수요 × E(혼잡 병목) × SignalFactor(그린웨이브 조율).
        public void Resolve(in SimConfig cfg, SignalMap signals)
        {
            // ① 타일별 혼잡: <Slow Free / Slow~Jam Slow / >Jam Jam (SimConfig 주석 규약)
            for (int i = 0; i < _flow.Length; i++)
            {
                _ratio[i] = _flow[i] / cfg.RoadCapacity;
                _level[i] = Classify(_ratio[i], cfg);
            }

            // ①' 신호 타일만 유효 용량 = RoadCapacity × GreenRatio(듀티)로 재계산.
            // 빨간불 동안 큐가 쌓이는 걸 용량 감소로 근사 — 여유 있으면 무손실, 부하 오르면
            // 신호가 먼저 병목이 되고, 초록을 늘리면(GreenSlots↑) 풀린다 = 두 번째 유저 레버.
            // ponytail: 축별 초록 분배(가로/세로 나눠먹기)는 2차 — 타일 flow가 축 미구분이라 듀티만.
            if (signals != null)
            {
                var tiles = signals.Tiles;
                for (int k = 0; k < tiles.Count; k++)
                {
                    if (!signals.TryGet(tiles[k], out var s)) continue;
                    if (s.CycleSlots <= 0) continue;   // 주기 0 = 항상 초록(IsGreen과 같은 규약)
                    float g = SignalMath.GreenRatio(s);
                    int i = Index(tiles[k]);
                    _ratio[i] = g > 0f ? _flow[i] / (cfg.RoadCapacity * g)
                              : _flow[i] > 0f ? cfg.EfficiencyMinRatio : 0f;   // 초록 0 = 흐르면 최악 병목
                    _level[i] = Classify(_ratio[i], cfg);
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
                    float rt = _ratio[Index(path[p])];
                    if (rt > bottleneck) { bottleneck = rt; bottleneckIdx = Index(path[p]); } // strict > → 첫 최대 타일(결정론)
                }

                float e = Efficiency(bottleneck, cfg);
                float delivered = cfg.DemandPerHouse * e * SignalFactor(path, signals, cfg);
                DeliveredTotal += delivered;
                _deliveredToSink[Index(_routeSinks[r])] += delivered;

                // 잃은 처리량(rate×틱=대수)을 병목에 적립 — 나중에 그 타일을 고치면 Burst 보상의 원료.
                // 신호 손실은 pending에 안 넣음: 조율의 보상은 Burst가 아니라 그린웨이브 처리량 자체(설계 §2).
                if (e < 1f && bottleneckIdx >= 0)
                    _pendingReward[bottleneckIdx] += cfg.DemandPerHouse * (1f - e) * cfg.TickInterval;
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

        public float GetRatio(Vector2Int t) => _ratio[Index(t)];

        // ArrivalEmitter용 — flat 인덱스로 수요처 처리량 조회(전 타일 순회 전제).
        public float GetDeliveredToSink(int flatIndex) => _deliveredToSink[flatIndex];

        public float GetPendingReward(Vector2Int t) => _pendingReward[Index(t)];

        // BurstDetector용 flat 인덱스 접근(전 타일 순회 전제).
        public float GetRatio(int flatIndex) => _ratio[flatIndex];
        public float GetPendingReward(int flatIndex) => _pendingReward[flatIndex];
        public void ClearPendingReward(int flatIndex) => _pendingReward[flatIndex] = 0f;   // Burst가 소비

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
