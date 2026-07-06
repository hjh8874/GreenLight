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

        public float DeliveredTotal { get; private set; }   // 이번 틱 총 처리량(대/초)

        public FlowSolver(int width, int height)
        {
            _w = width;
            int n = width * height;
            _flow = new float[n];
            _ratio = new float[n];
            _level = new CongestionLevel[n];
            _pendingReward = new float[n];
        }

        int Index(Vector2Int t) => t.y * _w + t.x;

        public void Assign(DemandMap demand, RoadNetwork net, in SimConfig cfg)
        {
            Array.Clear(_flow, 0, _flow.Length);
            _routes.Clear();

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
            }
        }

        public void Resolve(in SimConfig cfg)
        {
            // ① 타일별 혼잡: <Slow Free / Slow~Jam Slow / >Jam Jam (SimConfig 주석 규약)
            for (int i = 0; i < _flow.Length; i++)
            {
                _ratio[i] = _flow[i] / cfg.RoadCapacity;
                _level[i] = _ratio[i] > cfg.JamRatio ? CongestionLevel.Jam
                          : _ratio[i] >= cfg.SlowRatio ? CongestionLevel.Slow
                          : CongestionLevel.Free;
            }

            // ② 경로별: 병목(최대 ratio) → E → delivered + 잃은 만큼 병목 타일에 pending 적립
            DeliveredTotal = 0f;
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
                DeliveredTotal += cfg.DemandPerHouse * e;

                // 잃은 처리량(rate×틱=대수)을 병목에 적립 — 나중에 그 타일을 고치면 Burst 보상의 원료.
                if (e < 1f && bottleneckIdx >= 0)
                    _pendingReward[bottleneckIdx] += cfg.DemandPerHouse * (1f - e) * cfg.TickInterval;
            }
        }

        public CongestionLevel GetCongestion(Vector2Int t) => _level[Index(t)];

        public float GetPendingReward(Vector2Int t) => _pendingReward[Index(t)];

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
