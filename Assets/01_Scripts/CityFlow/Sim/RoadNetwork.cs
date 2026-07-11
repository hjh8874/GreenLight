using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim
{
    // 접점(TryGetAccessRoad)·도달성(RegionOf)만 담당 — 경로는 RoutePlanner.
    internal sealed class RoadNetwork
    {
        // 이웃 8방을 항상 같은 순서로: 상·우·하·좌(직각) + 우상·우하·좌하·좌상(대각) → 결정론.
        // 대각을 뒤에 붙여 동률 경로에선 직각이 이김(기존 직선 경로·신호 통과 보존).
        // 코너컷 허용(A): 두 도로 타일이 대각으로만 붙어도(사이 직각 칸 비어도) 연결로 침 — "대각 연결 당연히".
        static readonly int[] DX = { 0, 1, 0, -1, 1, 1, -1, -1 };
        static readonly int[] DY = { 1, 0, -1, 0, 1, -1, -1, 1 };

        readonly CityGrid _grid;
        readonly int _w, _h;

        // Region flood-fill 선할당 버퍼 — 매 계산 재사용(틱 중 new 0).
        readonly int[] _queue;

        // 연결 요소(Region) 캐시: 같은 섬의 도로는 같은 id. 수요 배정이 "도달 가능한가"를
        // BFS 없이 O(1)로 묻는 용도. topology 버전이 바뀐 뒤 첫 조회 때만 flood fill(O(n)).
        readonly int[] _region;
        int _regionVersion = -1;

        public RoadNetwork(CityGrid grid)
        {
            _grid = grid;
            _w = grid.Width;
            _h = grid.Height;
            int n = _w * _h;
            _queue = new int[n];   // 각 칸 최대 1번 인큐 → n칸이면 충분
            _region = new int[n];
        }

        // 이 도로 타일이 속한 섬 id(-1 = 도로 아님). RoutePlanner와 같은 8방 연결 규칙 →
        // "같은 Region = 경로 탐색 가능"이 항상 성립.
        public int RegionOf(Vector2Int roadTile)
        {
            if (!IsRoad(roadTile)) return -1;
            EnsureRegions();
            return _region[roadTile.y * _w + roadTile.x];
        }

        void EnsureRegions()
        {
            if (_regionVersion == _grid.TopologyVersion) return;
            _regionVersion = _grid.TopologyVersion;

            for (int i = 0; i < _region.Length; i++) _region[i] = -1;

            int nextId = 0;
            for (int start = 0; start < _region.Length; start++)   // flat 순서 → id 부여 결정론
            {
                if (_region[start] != -1) continue;
                if (_grid.GetTile(new Vector2Int(start % _w, start / _w)) != TileType.Road) continue;

                int id = nextId++;
                int head = 0, tail = 0;
                _queue[tail++] = start;
                _region[start] = id;
                while (head < tail)
                {
                    int cur = _queue[head++];
                    int cx = cur % _w, cy = cur / _w;
                    for (int d = 0; d < DX.Length; d++)
                    {
                        int nx = cx + DX[d], ny = cy + DY[d];
                        if (nx < 0 || nx >= _w || ny < 0 || ny >= _h) continue;
                        int ni = ny * _w + nx;
                        if (_region[ni] != -1) continue;
                        if (_grid.GetTile(new Vector2Int(nx, ny)) != TileType.Road) continue;
                        _region[ni] = id;
                        _queue[tail++] = ni;
                    }
                }
            }
        }

        // 건물(집·회사)의 접점 = 인접 8방 중 첫 도로 타일(직각 먼저, 그다음 대각).
        // BFS와 같은 스캔 순서 → 어느 도로가 접점인지 결정론적. 대각 접점도 연결.
        // ⚠ 스캔 순서(상 우선)는 테스트 기하(AxisFlowTests 등)가 접점 위치로 의존 — 바꾸면 테스트 기하 재검증.
        public bool TryGetAccessRoad(Vector2Int building, out Vector2Int road)
        {
            for (int d = 0; d < DX.Length; d++)
            {
                var v = new Vector2Int(building.x + DX[d], building.y + DY[d]);
                if (IsRoad(v)) { road = v; return true; }
            }
            road = default;
            return false;
        }

        bool InBounds(Vector2Int v) => v.x >= 0 && v.x < _w && v.y >= 0 && v.y < _h;
        bool IsRoad(Vector2Int v) => InBounds(v) && _grid.GetTile(v) == TileType.Road;
    }
}
