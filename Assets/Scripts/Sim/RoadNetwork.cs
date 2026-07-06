using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Sim
{
    // 도로 타일 위의 최단 경로(BFS) + (출발,도착) 경로 캐시.
    // topology(배치도)가 바뀌면 캐시를 통째로 무효화하고 다시 계산.
    // ponytail: 별도 인접 그래프 없이 BFS가 grid를 직접 읽음(20×20엔 충분). 느려지면 인접리스트.
    internal sealed class RoadNetwork
    {
        // 이웃 4방을 항상 같은 순서로 훑는다(상·우·하·좌) → 결정론.
        static readonly int[] DX = { 0, 1, 0, -1 };
        static readonly int[] DY = { 1, 0, -1, 0 };

        readonly CityGrid _grid;
        readonly int _w, _h;

        // BFS 선할당 버퍼 — 매 탐색 Array.Clear로 재사용(틱 중 new 0).
        readonly bool[] _visited;
        readonly int[] _cameFrom;   // flat index → 직전 칸 index (출발은 -1)
        readonly int[] _queue;

        // 경로 캐시: (출발,도착) → 경로(미연결 null도 캐시). 조회 전용 → Dictionary OK.
        readonly Dictionary<(Vector2Int, Vector2Int), List<Vector2Int>> _cache = new();
        int _cachedVersion = -1;

        public RoadNetwork(CityGrid grid)
        {
            _grid = grid;
            _w = grid.Width;
            _h = grid.Height;
            int n = _w * _h;
            _visited = new bool[n];
            _cameFrom = new int[n];
            _queue = new int[n];   // 각 칸 최대 1번 인큐 → n칸이면 충분
        }

        // topology 변경 시 호출: 캐시 통째 무효화 + 현재 버전 기록.
        public void Rebuild()
        {
            _cache.Clear();
            _cachedVersion = _grid.TopologyVersion;
        }

        public List<Vector2Int> FindPath(Vector2Int from, Vector2Int to)
        {
            // 안전장치: Rebuild를 안 불렀어도 버전 어긋나면 자동 무효화.
            if (_cachedVersion != _grid.TopologyVersion) Rebuild();

            var key = (from, to);
            if (_cache.TryGetValue(key, out var cached)) return cached;

            var path = Bfs(from, to);   // 미연결이면 null
            _cache[key] = path;
            return path;
        }

        bool InBounds(Vector2Int v) => v.x >= 0 && v.x < _w && v.y >= 0 && v.y < _h;
        bool IsRoad(Vector2Int v) => InBounds(v) && _grid.GetTile(v) == TileType.Road;

        List<Vector2Int> Bfs(Vector2Int from, Vector2Int to)
        {
            // 끝점이 도로가 아니면 경로 없음.
            if (!IsRoad(from) || !IsRoad(to)) return null;

            Array.Clear(_visited, 0, _visited.Length);
            int start = from.y * _w + from.x;
            int goal = to.y * _w + to.x;

            int head = 0, tail = 0;      // _queue의 읽기/쓰기 위치
            _queue[tail++] = start;
            _visited[start] = true;
            _cameFrom[start] = -1;

            bool found = start == goal;  // 출발==도착이면 이미 도달
            while (head < tail && !found)
            {
                int cur = _queue[head++];
                int cx = cur % _w, cy = cur / _w;   // flat → (x,y) 역변환
                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + DX[d], ny = cy + DY[d];
                    if (nx < 0 || nx >= _w || ny < 0 || ny >= _h) continue;
                    int ni = ny * _w + nx;
                    if (_visited[ni]) continue;
                    if (_grid.GetTile(new Vector2Int(nx, ny)) != TileType.Road) continue;
                    _visited[ni] = true;
                    _cameFrom[ni] = cur;
                    if (ni == goal) { found = true; break; }
                    _queue[tail++] = ni;
                }
            }

            if (!found) return null;   // 큐 말랐는데 목적지 못 만남 = 미연결

            // cameFrom 따라 도착→출발 역추적 후 뒤집어 정방향 경로로.
            var path = new List<Vector2Int>();
            for (int node = goal; node != -1; node = _cameFrom[node])
                path.Add(new Vector2Int(node % _w, node / _w));
            path.Reverse();
            return path;
        }
    }
}
