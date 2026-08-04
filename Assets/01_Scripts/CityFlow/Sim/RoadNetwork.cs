using System.Collections.Generic;
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
        static readonly PlacementDirection[] AutoDirectionOrder =
        {
            PlacementDirection.North,
            PlacementDirection.East,
            PlacementDirection.South,
            PlacementDirection.West
        };

        readonly CityGrid _grid;
        readonly int _w, _h;

        // Region flood-fill 선할당 버퍼 — 매 계산 재사용(틱 중 new 0).
        readonly int[] _queue;
        // TryGetAccessRoad도 CollectAccessRoads와 같은 우선순위를 쓰기 위한 재사용 버퍼.
        readonly List<Vector2Int> _accessRoadBuffer = new(16);

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

        // 배치 프리뷰와 실제 배치가 공유하는 자동 방향 결정 규칙.
        // 주차면(frontage)에 도로가 있는 첫 방향을 우선순위 순으로 고른다.
        // priority 미지정이면 North→East→South→West. 1면 접촉이면 순서와 무관하게 그 면이 뽑힌다 —
        // 순서는 여러 면이 도로일 때의 타이브레이크다(UI가 카메라 기준 순서를 넘길 수 있게 인자화).
        public bool TryResolveAutoDirection(
            Vector2Int tile,
            TileType type,
            out PlacementDirection direction,
            IReadOnlyList<PlacementDirection> priority = null)
        {
            direction = PlacementDirection.North;
            if (!TileFootprint.IsBuilding(type)) return false;

            IReadOnlyList<PlacementDirection> order = priority ?? AutoDirectionOrder;
            for (int i = 0; i < order.Count; i++)
            {
                PlacementDirection candidate = order[i];
                Vector2Int size = TileFootprint.GetRotatedSize(type, candidate);
                if (!TryGetRoadAlongFront(tile, size, candidate, out _)) continue;

                direction = candidate;
                return true;
            }

            return false;
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

        // 건물(집·회사)의 접점은 배치 방향의 앞면 도로를 사용한다.
        // R로 회전한 비기본 방향은 앞면 도로가 없으면 접근 불가로 두어 건물 관통을 막는다.
        // 구버전 저장의 기본 North만 기존 자동 정렬과 8방 스캔 폴백을 유지한다.
        public bool TryGetAccessRoad(Vector2Int building, out Vector2Int road)
        {
            _accessRoadBuffer.Clear();
            CollectAccessRoads(building, _accessRoadBuffer);
            if (_accessRoadBuffer.Count <= 0)
            {
                road = default;
                return false;
            }

            road = _accessRoadBuffer[0];
            return true;
        }

        // 건물의 선택 앞면 프론티지를 먼저 buffer에 누적한다. 비기본 회전 방향은 앞면만 허용한다.
        // 구버전 저장의 기본 North는 자동 정렬 폴백과 다른 Region 접근성 보존을 위해 나머지
        // 8방 프론티지도 누적한다. 호출자가 buffer 소유(재사용 패턴, Reassign/Plan 같은 재구축
        // 경로에서만 호출). Clear는 호출자 책임
        // (DemandMap.Collect와 동일 관례).
        // 감사 픽스 2: 건물이 서로 다른 Region에 프론티지를 여러 개 가질 때(막다른 스텁 + 간선)
        // TryGetAccessRoad 하나만 보면 도달 가능한 프론티지를 놓칠 수 있음 → 전수 수집으로 대응.
        public void CollectAccessRoads(Vector2Int building, List<Vector2Int> buffer)
        {
            int firstAdded = buffer.Count;
            Vector2Int size = GetBuildingFootprintSize(building);
            CollectPreferredFrontRoads(building, size, buffer);
            if (!HasExplicitPlacementDirection(building))
            {
                CollectFallbackFacingRoads(building, size, buffer);

                for (int y = 0; y < size.y; y++)
                {
                    for (int x = 0; x < size.x; x++)
                    {
                        Vector2Int occupied = building + new Vector2Int(x, y);
                        for (int d = 0; d < DX.Length; d++)
                        {
                            var v = new Vector2Int(occupied.x + DX[d], occupied.y + DY[d]);
                            AddRoadIfPresent(v, buffer);
                        }
                    }
                }
            }

            StablePrioritizeOrdinaryRoads(buffer, firstAdded);
        }

        // 차고 진출 방향: 건물 풋프린트 셀 중 진입로와 직교 인접한 셀에서 도로로 향하는 진행 방향.
        // 직사각형 풋프린트라 후보는 최대 1개다(둘이면 사이 칸까지 덮어야 해 모순).
        // 대각으로만 닿으면 false — 호출자가 exit 방향 폴백을 쓴다(설계 D2-2).
        internal bool TryGetDepartureEntryDir(
            Vector2Int building,
            Vector2Int road,
            out Dir entry)
        {
            Vector2Int size = GetBuildingFootprintSize(building);
            for (int y = 0; y < size.y; y++)
            {
                for (int x = 0; x < size.x; x++)
                {
                    Vector2Int cell = building + new Vector2Int(x, y);
                    Vector2Int delta = road - cell;
                    if (delta == new Vector2Int(0, 1)) { entry = Dir.N; return true; }
                    if (delta == new Vector2Int(1, 0)) { entry = Dir.E; return true; }
                    if (delta == new Vector2Int(0, -1)) { entry = Dir.S; return true; }
                    if (delta == new Vector2Int(-1, 0)) { entry = Dir.W; return true; }
                }
            }
            entry = default;
            return false;
        }

        private Vector2Int GetBuildingFootprintSize(Vector2Int building)
        {
            TileType type = _grid.GetTile(building);
            return TileFootprint.GetRotatedSize(
                type,
                _grid.GetDirection(building));
        }

        private bool HasExplicitPlacementDirection(
            Vector2Int building) =>
            TileFootprint.IsBuilding(_grid.GetTile(building)) &&
            _grid.GetDirection(building) != PlacementDirection.North;

        private void CollectPreferredFrontRoads(
            Vector2Int building,
            Vector2Int size,
            List<Vector2Int> buffer)
        {
            TileType type = _grid.GetTile(building);
            if (!TileFootprint.IsBuilding(type))
            {
                return;
            }

            CollectRoadsAlongFront(
                building,
                size,
                _grid.GetDirection(building),
                buffer);
        }

        private void CollectFallbackFacingRoads(
            Vector2Int building,
            Vector2Int size,
            List<Vector2Int> buffer)
        {
            if (TileFootprint.IsBuilding(_grid.GetTile(building)) &&
                TryGetFallbackFacingDirection(
                    building,
                    size,
                    out PlacementDirection direction))
            {
                CollectRoadsAlongFront(
                    building,
                    size,
                    direction,
                    buffer);
            }
        }

        private bool TryGetFallbackFacingDirection(
            Vector2Int building,
            Vector2Int size,
            out PlacementDirection direction)
        {
            int south = CountRoadsAlongFront(
                building,
                size,
                PlacementDirection.North);
            int east = CountRoadsAlongFront(
                building,
                size,
                PlacementDirection.East);
            int north = CountRoadsAlongFront(
                building,
                size,
                PlacementDirection.South);
            int west = CountRoadsAlongFront(
                building,
                size,
                PlacementDirection.West);

            int best = Mathf.Max(south, east, north, west);
            if (best <= 0)
            {
                direction = default;
                return false;
            }

            direction = best == south
                ? PlacementDirection.North
                : best == east
                    ? PlacementDirection.East
                    : best == north
                        ? PlacementDirection.South
                        : PlacementDirection.West;
            return true;
        }

        private bool TryGetRoadAlongFront(
            Vector2Int building,
            Vector2Int size,
            PlacementDirection direction,
            out Vector2Int road)
        {
            Vector2Int front = TileFootprint.GetFrontOffset(direction);
            if (front.x != 0)
            {
                int x = front.x > 0
                    ? building.x + size.x
                    : building.x - 1;
                for (int y = 0; y < size.y; y++)
                {
                    road = new Vector2Int(x, building.y + y);
                    if (IsRoad(road))
                    {
                        return true;
                    }
                }
            }
            else
            {
                int y = front.y > 0
                    ? building.y + size.y
                    : building.y - 1;
                for (int x = 0; x < size.x; x++)
                {
                    road = new Vector2Int(building.x + x, y);
                    if (IsRoad(road))
                    {
                        return true;
                    }
                }
            }

            road = default;
            return false;
        }

        private void CollectRoadsAlongFront(
            Vector2Int building,
            Vector2Int size,
            PlacementDirection direction,
            List<Vector2Int> buffer)
        {
            Vector2Int front = TileFootprint.GetFrontOffset(direction);
            if (front.x != 0)
            {
                int x = front.x > 0
                    ? building.x + size.x
                    : building.x - 1;
                for (int y = 0; y < size.y; y++)
                {
                    AddRoadIfPresent(
                        new Vector2Int(x, building.y + y),
                        buffer);
                }
                return;
            }

            int frontageY = front.y > 0
                ? building.y + size.y
                : building.y - 1;
            for (int x = 0; x < size.x; x++)
            {
                AddRoadIfPresent(
                    new Vector2Int(building.x + x, frontageY),
                    buffer);
            }
        }

        private int CountRoadsAlongFront(
            Vector2Int building,
            Vector2Int size,
            PlacementDirection direction)
        {
            Vector2Int front = TileFootprint.GetFrontOffset(direction);
            int count = 0;
            if (front.x != 0)
            {
                int x = front.x > 0
                    ? building.x + size.x
                    : building.x - 1;
                for (int y = 0; y < size.y; y++)
                {
                    if (IsRoad(new Vector2Int(x, building.y + y)))
                    {
                        count++;
                    }
                }
                return count;
            }

            int frontageY = front.y > 0
                ? building.y + size.y
                : building.y - 1;
            for (int x = 0; x < size.x; x++)
            {
                if (IsRoad(new Vector2Int(building.x + x, frontageY)))
                {
                    count++;
                }
            }
            return count;
        }

        private void AddRoadIfPresent(
            Vector2Int tile,
            List<Vector2Int> buffer)
        {
            if (IsRoad(tile) && !buffer.Contains(tile))
            {
                buffer.Add(tile);
            }
        }

        private void StablePrioritizeOrdinaryRoads(
            List<Vector2Int> buffer,
            int firstAdded)
        {
            // 새로 수집한 구간만 안정 분할한다. 같은 등급 안의 발견 순서와 호출자 소유
            // prefix는 그대로 두며, 전부 교차로면 아무 이동도 일어나지 않는다.
            int insertAt = firstAdded;
            for (int i = firstAdded; i < buffer.Count; i++)
            {
                Vector2Int candidate = buffer[i];
                if (_grid.IsIntersection(candidate)) continue;

                if (i != insertAt)
                {
                    buffer.RemoveAt(i);
                    buffer.Insert(insertAt, candidate);
                }
                insertAt++;
            }
        }

        bool InBounds(Vector2Int v) => v.x >= 0 && v.x < _w && v.y >= 0 && v.y < _h;
        bool IsRoad(Vector2Int v) => InBounds(v) && _grid.GetTile(v) == TileType.Road;
    }
}
