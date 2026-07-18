using System;
using System.Collections.Generic;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim
{
    // 직교 차 라우팅: 재건축 시 수요를 고정 순서로 하나씩 배정하고,
    // 앞 차량이 채운 부하를 뒤 차량이 비용으로 회피 → 평행 분산·우회 흡수가 창발.
    // 스텝 비용 = 타일 1 × (1 + w × load/용량). 재계획은 topology 변경 시에만 —
    // 신호 레버·맥동은 트리거 아님("차들은 습관대로, 건설이 습관을 바꾼다").
    // ponytail: 20×20이라 배열 스캔 Dijkstra(O(n²))로 충분 — 힙 불요. 틱 밖이라 경로 List 할당 허용.
    internal sealed class RoutePlanner
    {
        // 이웃 순서 N,E,S,W 고정(결정론 공유 규약). 대각 연결은 차 큐로 표현할 수 없어 지원하지 않는다.
        static readonly int[] DX = { 0, 1, 0, -1 };
        static readonly int[] DY = { 1, 0, -1, 0 };

        // 턴 제한 표지판(스펙 2026-07-12): 상태 확장 탐색의 진입방향 인덱스 고정 순회(결정론).
        // DX/DY 카디널 부분(idx0..3 = N,E,S,W)을 이 순서로 재사상: N→3,E→0,S→1,W→2.
        static readonly int[] CardinalToStateIdx = { 3, 0, 1, 2 };

        readonly int _w, _h;
        readonly float[] _cost;      // Dijkstra 누적 비용
        readonly bool[] _done;
        readonly int[] _cameFrom;
        readonly float[] _load;      // 이번 계획에서 이미 배정된 흐름(대/초)

        // 턴 제한 표지판 상태 확장(스펙 2026-07-12): 상태 = 타일×진입방향(4) — [n×4] 1회 할당.
        readonly float[] _turnCost;
        readonly bool[] _turnDone;
        readonly int[] _turnCameFrom;

        readonly List<List<Vector2Int>> _carRoutes = new(128);
        readonly List<List<Vector2Int>> _returnRoutes = new(128);

        // 소유권: 내부 List를 그대로 노출 — 소비자는 읽기 전용 계약. 변형 금지.
        public IReadOnlyList<List<Vector2Int>> Routes => _carRoutes;
        public IReadOnlyList<List<Vector2Int>> CarRoutes => _carRoutes;
        public IReadOnlyList<List<Vector2Int>> ReturnRoutes => _returnRoutes;

        public RoutePlanner(int width, int height)
        {
            _w = width; _h = height;
            int n = width * height;
            _cost = new float[n];
            _done = new bool[n];
            _cameFrom = new int[n];
            _load = new float[n];
            _turnCost = new float[n * 4];
            _turnDone = new bool[n * 4];
            _turnCameFrom = new int[n * 4];
        }

        // 수요별 경로 테이블 계산. 각 경로는 차 토큰 1대의 부하를 적립한다.
        // 접점(from/to)은 DemandMap이 배정 시 채택한 값을 그대로 쓴다(단일 출처화, 감사 픽스 2) —
        // 여기서 net.TryGetAccessRoad로 다시 계산하면 건물에 프론티지가 여러 개일 때 DemandMap과
        // 다른 접점을 고를 수 있고, 그 불일치가 "배정은 됐는데 흐름은 0"인 버그의 원인이었다.
        // net 파라미터는 시그니처 호환용으로 유지(다른 호출자·테스트가 이 형태로 호출).
        // 기존 4-인자 호출자는 oneways=null로 위임(일방통행 미도입 시 무비용 경로, 테스트 무수정 생존).
        public void Plan(DemandMap demand, RoadNetwork net, CityGrid grid, in SimConfig cfg)
            => Plan(demand, net, grid, cfg, null);

        // oneways: 엔진 소유 좌표→단위방향 맵(조회만 — 소유·갱신은 SimEngine, 로터리/입체와 같은 패턴).
        public void Plan(DemandMap demand, RoadNetwork net, CityGrid grid, in SimConfig cfg,
                          IReadOnlyDictionary<Vector2Int, Vector2Int> oneways)
            => Plan(demand, net, grid, cfg, oneways, null);

        // 턴 제한 표지판(스펙 2026-07-12): null 위임 체인 연장. turnSigns가 있을 때만 상태 확장 탐색으로 분기(Search 내부).
        public void Plan(DemandMap demand, RoadNetwork net, CityGrid grid, in SimConfig cfg,
                          IReadOnlyDictionary<Vector2Int, Vector2Int> oneways,
                          IReadOnlyDictionary<Vector2Int, TurnMode> turnSigns)
        {
            _carRoutes.Clear();
            _returnRoutes.Clear();
            Array.Clear(_load, 0, _load.Length);

            var demands = demand.Demands;
            for (int i = 0; i < demands.Count; i++)
            {
                var carPath = Search(grid, demands[i].SourceRoad, demands[i].SinkRoad, cfg, oneways, turnSigns);
                var returnPath = Search(grid, demands[i].SinkRoad, demands[i].SourceRoad, cfg, oneways, turnSigns);
                _carRoutes.Add(carPath);
                _returnRoutes.Add(returnPath);
                if (carPath == null) continue;
                for (int p = 0; p < carPath.Count; p++)
                    _load[carPath[p].y * _w + carPath[p].x] += 1f;
            }
        }

        // 현재 _load 기준 최소 비용 경로(내부 + 테스트 seam). 미연결/비도로 끝점 = null.
        internal List<Vector2Int> Search(CityGrid grid, Vector2Int from, Vector2Int to, in SimConfig cfg)
            => Search(grid, from, to, cfg, null);

        // 일방통행 간선 필터(스펙 2026-07-12 §핵심결정, 상태 확장 없음 — 이웃 확장에서 3규칙 조기 continue):
        // ① 일방 타일에서 나가는 스텝은 그 방향(D)만. ② 일방 타일로 들어가는 스텝은 -D 금지
        // (역주행 진입 차단, 측면 합류는 허용). oneways가 null/빈 경우 Dictionary 조회 없이 스킵.
        internal List<Vector2Int> Search(CityGrid grid, Vector2Int from, Vector2Int to, in SimConfig cfg,
                                          IReadOnlyDictionary<Vector2Int, Vector2Int> oneways)
            => SearchCore(grid, from, to, cfg, oneways);

        private List<Vector2Int> SearchCore(CityGrid grid, Vector2Int from, Vector2Int to, in SimConfig cfg,
                                             IReadOnlyDictionary<Vector2Int, Vector2Int> oneways)
        {
            if (!IsRoad(grid, from.x, from.y) || !IsRoad(grid, to.x, to.y)) return null;
            bool hasOneways = oneways != null && oneways.Count > 0;

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
                bool curOneway = false;
                Vector2Int curDir = default;
                if (hasOneways) curOneway = oneways.TryGetValue(new Vector2Int(cx, cy), out curDir);

                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + DX[d], ny = cy + DY[d];
                    if (!IsRoad(grid, nx, ny)) continue;
                    int ni = ny * _w + nx;
                    if (_done[ni]) continue;

                    if (hasOneways)
                    {
                        bool nbrOneway = oneways.TryGetValue(new Vector2Int(nx, ny), out var nbrDir);
                        var stepDir = new Vector2Int(DX[d], DY[d]);
                        if (curOneway && stepDir != curDir) continue;                // ① 나가는 스텝 = D만
                        if (nbrOneway && stepDir == -nbrDir) continue;               // ② 들어가는 스텝 ≠ -D
                    }

                    float step = 1f + w * _load[ni] * capInv;
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

        // 턴 제한 표지판(스펙 2026-07-12 §핵심결정 "표지판 0개 = 레거시 탐색"): turnSigns가 없거나
        // 비어 있으면 위 5-인자 Search를 무수정 그대로 호출 — 기존 180 테스트·라이브 결정론의
        // 무풍지대(경로·tie-break 완전 동일)를 이 분기 자체가 증명한다. 있으면 상태 확장 탐색.
        internal List<Vector2Int> Search(CityGrid grid, Vector2Int from, Vector2Int to, in SimConfig cfg,
                                          IReadOnlyDictionary<Vector2Int, Vector2Int> oneways,
                                          IReadOnlyDictionary<Vector2Int, TurnMode> turnSigns)
        {
            if (turnSigns == null || turnSigns.Count == 0)
                return Search(grid, from, to, cfg, oneways);
            return SearchWithTurnState(grid, from, to, cfg, oneways, turnSigns);
        }

        // 상태 확장 Dijkstra: 상태 = (타일 × 진입방향, E=0/S=1/W=2/N=3 고정 순회 — CardinalToStateIdx).
        // 표지판 타일 T에 진입방향 d_in으로 들어온 상태에서 나가는 스텝 d_out은
        // Turn(d_in,모드)만 허용: LeftOnly→(d_in+3)%4, RightOnly→(d_in+1)%4 — U턴(+2)·직진(+0)·
        // 반대턴은 이 산술만으로 자동 배제(별도 예외 처리 불요).
        // 시작 타일은 진입방향 미확립 — 첫 스텝은 턴 필터 없이 확장(일방통행 규칙은 그대로 적용).
        // 일방통행 3규칙은 기존 5-인자 Search와 동일하게 이 경로에도 적용(두 도구 공존).
        // dist/cameFrom 배열은 생성자 1회 할당(_turnCost/_turnDone/_turnCameFrom) — 매 호출 클리어만.
        internal List<Vector2Int> SearchWithTurnState(CityGrid grid, Vector2Int from, Vector2Int to, in SimConfig cfg,
                                                       IReadOnlyDictionary<Vector2Int, Vector2Int> oneways,
                                                       IReadOnlyDictionary<Vector2Int, TurnMode> turnSigns)
            => SearchWithTurnState(grid, from, to, cfg, oneways, turnSigns);

        private List<Vector2Int> SearchWithTurnState(CityGrid grid, Vector2Int from, Vector2Int to, in SimConfig cfg,
                                                      IReadOnlyDictionary<Vector2Int, Vector2Int> oneways,
                                                      IReadOnlyDictionary<Vector2Int, TurnMode> turnSigns)
        {
            if (!IsRoad(grid, from.x, from.y) || !IsRoad(grid, to.x, to.y)) return null;
            if (from == to) return new List<Vector2Int> { from };   // legacy Search_SameTile_ReturnsSingle과 동형

            bool hasOneways = oneways != null && oneways.Count > 0;
            int goal = to.y * _w + to.x;

            int statesN = _turnCost.Length;
            for (int i = 0; i < statesN; i++) { _turnCost[i] = float.MaxValue; _turnDone[i] = false; }

            float capInv = cfg.RoadCapacity > 0f ? 1f / cfg.RoadCapacity : 0f;
            float w = cfg.RoutingCongestionWeight;

            // ── 시작 타일 씨앗: 진입방향 미확립 — 턴 필터 없이 확장(일방통행은 그대로) ──
            int sx = from.x, sy = from.y;
            bool startOneway = false;
            Vector2Int startOnewayDir = default;
            if (hasOneways) startOneway = oneways.TryGetValue(from, out startOnewayDir);

            for (int d = 0; d < 4; d++)
            {
                int nx = sx + DX[d], ny = sy + DY[d];
                if (!IsRoad(grid, nx, ny)) continue;
                var nbrTile = new Vector2Int(nx, ny);
                bool nbrOneway = false;
                Vector2Int nbrOnewayDir = default;
                if (hasOneways) nbrOneway = oneways.TryGetValue(nbrTile, out nbrOnewayDir);

                if (hasOneways)
                {
                    var stepDir = new Vector2Int(DX[d], DY[d]);
                    if (startOneway && stepDir != startOnewayDir) continue;   // ① 나가는 스텝 = D만
                    if (nbrOneway && stepDir == -nbrOnewayDir) continue;      // ② 들어가는 스텝 ≠ -D
                }
                // 시작 타일 표지판은 무제약(진입이 아니므로) — 턴 필터 미적용.

                float step = 1f + w * _load[ny * _w + nx] * capInv;
                int newDirIn = CardinalToStateIdx[d];
                int state = (ny * _w + nx) * 4 + newDirIn;
                if (step < _turnCost[state]) { _turnCost[state] = step; _turnCameFrom[state] = -1; }
            }

            // ── 본 루프: flat 오름차순 스캔(결정론) — 노드가 아니라 상태 단위 ──
            int goalState = -1;
            while (true)
            {
                int cur = -1;
                float best = float.MaxValue;
                for (int i = 0; i < statesN; i++)
                    if (!_turnDone[i] && _turnCost[i] < best) { best = _turnCost[i]; cur = i; }
                if (cur == -1) return null;                       // 프런티어 고갈 = 미연결(무사고)

                int curNode = cur / 4;
                if (curNode == goal) { goalState = cur; break; }   // 어느 진입방향이든 노드 도달이면 종료
                _turnDone[cur] = true;

                int curDirIn = cur % 4;
                int cx = curNode % _w, cy = curNode / _w;
                var curTile = new Vector2Int(cx, cy);
                bool curIsSign = turnSigns.TryGetValue(curTile, out var curMode);
                bool curOneway = false;
                Vector2Int curOnewayDir = default;
                if (hasOneways) curOneway = oneways.TryGetValue(curTile, out curOnewayDir);

                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + DX[d], ny = cy + DY[d];
                    if (!IsRoad(grid, nx, ny)) continue;
                    int ni = ny * _w + nx;
                    var nbrTile = new Vector2Int(nx, ny);
                    bool nbrOneway = false;
                    Vector2Int nbrOnewayDir = default;
                    if (hasOneways) nbrOneway = oneways.TryGetValue(nbrTile, out nbrOnewayDir);

                    if (hasOneways)
                    {
                        var stepDir = new Vector2Int(DX[d], DY[d]);
                        if (curOneway && stepDir != curOnewayDir) continue;   // ① 나가는 스텝 = D만
                        if (nbrOneway && stepDir == -nbrOnewayDir) continue;  // ② 들어가는 스텝 ≠ -D
                    }

                    if (curIsSign)                                           // 표지판 타일 이탈 = Turn(d_in,모드)만
                    {
                        int expected = curMode == TurnMode.LeftOnly
                            ? (curDirIn + 3) % 4    // 좌회전(반시계 90°)
                            : (curDirIn + 1) % 4;   // 우회전(시계 90°) — U턴(+2)·직진(+0)은 둘 다 아님
                        if (CardinalToStateIdx[d] != expected) continue;
                    }

                    float step = 1f + w * _load[ni] * capInv;
                    float cand = _turnCost[cur] + step;
                    int newDirIn = CardinalToStateIdx[d];
                    int nState = ni * 4 + newDirIn;
                    if (cand < _turnCost[nState]) { _turnCost[nState] = cand; _turnCameFrom[nState] = cur; }
                }
            }

            // 재구성 = (타일,방향) 체인 → 타일 리스트. 타일 중복 허용(P턴 — 같은 노드를 다른
            // 진입방향으로 재방문 가능, 상태가 다르므로 무한루프 없음: cameFrom은 항상 더 이른 상태).
            var path = new List<Vector2Int>();
            for (int state = goalState; state != -1; state = _turnCameFrom[state])
            {
                int node = state / 4;
                path.Add(new Vector2Int(node % _w, node / _w));
            }
            path.Add(from);
            path.Reverse();
            return path;
        }

        internal bool TryGetAverageRouteDistance(DemandMap demand, Vector2Int destination, out float distanceTiles)
        {
            float total = 0f;
            int count = 0;
            for (int i = 0; i < _carRoutes.Count && i < demand.Demands.Count; i++)
            {
                List<Vector2Int> route = _carRoutes[i];
                if (route == null || demand.Demands[i].Sink != destination) continue;
                total += Mathf.Max(0, route.Count - 1);
                count++;
            }
            distanceTiles = count > 0 ? total / count : 0f;
            return count > 0;
        }

        internal bool TryGetCityAverageRouteDistance(out float distanceTiles)
        {
            float total = 0f;
            int count = 0;
            for (int i = 0; i < _carRoutes.Count; i++)
            {
                List<Vector2Int> route = _carRoutes[i];
                if (route == null) continue;
                total += Mathf.Max(0, route.Count - 1);
                count++;
            }
            distanceTiles = count > 0 ? total / count : 0f;
            return count > 0;
        }

        bool IsRoad(CityGrid grid, int x, int y) =>
            x >= 0 && x < _w && y >= 0 && y < _h &&
            grid.GetTile(new Vector2Int(x, y)) == CityFlow.Contracts.TileType.Road;
    }
}
