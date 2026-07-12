using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Sim
{
    // 유기적 혼잡 라우팅(스펙 2026-07-11): 재건축 시 수요를 고정 순서로 하나씩 배정하고,
    // 앞 수요가 채운 부하를 뒤 수요가 비용으로 회피 → 평행 분산·우회 흡수가 창발.
    // 스텝 비용 = 물리거리(직각 1, 대각 √2) × (1 + w × load/용량). 재계획은 topology 변경 시에만 —
    // 신호 레버·맥동은 트리거 아님("차들은 습관대로, 건설이 습관을 바꾼다").
    // ponytail: 20×20이라 배열 스캔 Dijkstra(O(n²))로 충분 — 힙 불요. 틱 밖이라 경로 List 할당 허용.
    internal sealed class RoutePlanner
    {
        // 이웃 순서는 RoadNetwork(접점·Region)와 동일: 직각 4 → 대각 4 (결정론 공유 규약).
        static readonly int[] DX = { 0, 1, 0, -1, 1, 1, -1, -1 };
        static readonly int[] DY = { 1, 0, -1, 0, 1, -1, -1, 1 };
        const float Sqrt2 = 1.4142135f;

        readonly int _w, _h;
        readonly float[] _cost;      // Dijkstra 누적 비용
        readonly bool[] _done;
        readonly int[] _cameFrom;
        readonly float[] _load;      // 이번 계획에서 이미 배정된 흐름(대/초)

        readonly List<List<Vector2Int>> _routes = new(128);   // 수요 인덱스 정렬, 미연결 = null

        // 소유권: 내부 List를 그대로 노출 — 소비자(FlowSolver·뷰)는 읽기 전용 계약. 변형 금지.
        public IReadOnlyList<List<Vector2Int>> Routes => _routes;

        public RoutePlanner(int width, int height)
        {
            _w = width; _h = height;
            int n = width * height;
            _cost = new float[n];
            _done = new bool[n];
            _cameFrom = new int[n];
            _load = new float[n];
        }

        // 수요별 경로 테이블 계산. 부하 적립은 DemandPerHouse(평균 — 맥동 무반영, 정산 철학과 동일).
        // 접점(from/to)은 DemandMap이 배정 시 채택한 값을 그대로 쓴다(단일 출처화, 감사 픽스 2) —
        // 여기서 net.TryGetAccessRoad로 다시 계산하면 건물에 프론티지가 여러 개일 때 DemandMap과
        // 다른 접점을 고를 수 있고, 그 불일치가 "배정은 됐는데 흐름은 0"인 버그의 원인이었다.
        // net 파라미터는 시그니처 호환용으로 유지(다른 호출자·테스트가 이 형태로 호출).
        public void Plan(DemandMap demand, RoadNetwork net, CityGrid grid, in SimConfig cfg)
        {
            _routes.Clear();
            Array.Clear(_load, 0, _load.Length);

            var demands = demand.Demands;
            for (int i = 0; i < demands.Count; i++)
            {
                var path = Search(grid, demands[i].SourceRoad, demands[i].SinkRoad, cfg);   // 경계 밖(NoRoad)도 IsRoad가 자연히 걸러 null

                _routes.Add(path);                            // null = 이 수요는 흐르지 않음(무사고)
                if (path == null) continue;
                for (int p = 0; p < path.Count; p++)
                    _load[path[p].y * _w + path[p].x] += cfg.DemandPerHouse;
            }
        }

        // 현재 _load 기준 최소 비용 경로(내부 + 테스트 seam). 미연결/비도로 끝점 = null.
        internal List<Vector2Int> Search(CityGrid grid, Vector2Int from, Vector2Int to, in SimConfig cfg)
        {
            if (!IsRoad(grid, from.x, from.y) || !IsRoad(grid, to.x, to.y)) return null;

            int n = _cost.Length;
            for (int i = 0; i < n; i++) { _cost[i] = float.MaxValue; _done[i] = false; }
            int start = from.y * _w + from.x;
            int goal = to.y * _w + to.x;
            _cost[start] = 0f;
            _cameFrom[start] = -1;

            // 용량 0 방어: 부하항 무시(순수 물리 최단으로 퇴화).
            float capInv = cfg.RoadCapacity > 0f ? 1f / cfg.RoadCapacity : 0f;
            float w = cfg.RoutingCongestionWeight;

            while (true)
            {
                // 미확정 최소 비용 노드 — flat 오름차순 스캔 + strict < = 동률 시 낮은 인덱스(결정론).
                int cur = -1;
                float best = float.MaxValue;
                for (int i = 0; i < n; i++)
                    if (!_done[i] && _cost[i] < best) { best = _cost[i]; cur = i; }
                if (cur == -1) return null;                   // 프런티어 고갈 = 미연결
                if (cur == goal) break;
                _done[cur] = true;

                int cx = cur % _w, cy = cur / _w;
                for (int d = 0; d < DX.Length; d++)
                {
                    int nx = cx + DX[d], ny = cy + DY[d];
                    if (!IsRoad(grid, nx, ny)) continue;
                    int ni = ny * _w + nx;
                    if (_done[ni]) continue;
                    float phys = d < 4 ? 1f : Sqrt2;          // 물리 거리 — 선택과 그린웨이브 타이밍 일치
                    float step = phys * (1f + w * _load[ni] * capInv);
                    float cand = _cost[cur] + step;
                    if (cand < _cost[ni]) { _cost[ni] = cand; _cameFrom[ni] = cur; }
                }
            }

            // _cameFrom은 호출 간 리셋 안 함 — 재구성 체인은 이번 호출에서 완화된 노드만 따라가므로 안전.
            var path = new List<Vector2Int>();
            for (int node = goal; node != -1; node = _cameFrom[node])
                path.Add(new Vector2Int(node % _w, node / _w));
            path.Reverse();
            return path;
        }

        bool IsRoad(CityGrid grid, int x, int y) =>
            x >= 0 && x < _w && y >= 0 && y < _h &&
            grid.GetTile(new Vector2Int(x, y)) == CityFlow.Contracts.TileType.Road;
    }
}
