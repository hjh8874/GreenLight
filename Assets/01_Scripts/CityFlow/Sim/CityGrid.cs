using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim
{
    // 도시 타일의 유일한 상태 저장소. flat 배열(index = y*W+x).
    // 주석님 IPlacementService 구현 — 배치/제거의 실제 창구.
    internal sealed class CityGrid : IPlacementService
    {
        readonly TileType[] _tiles;   // new 시 전부 0 = TileType.Empty → "빈 도시" 공짜
        readonly Vector2Int[] _footprintAnchors;
        readonly int _width;
        readonly int _height;
        static readonly Vector2Int InvalidAnchor = new Vector2Int(-1, -1);

        public int Width => _width;
        public int Height => _height;

        // 배치/제거가 성공할 때마다 ++. RoadNetwork 경로 캐시 무효화 키로 쓰임.
        public int TopologyVersion { get; private set; }
        // 마지막 소비 이후 배치도가 바뀌었나. SimEngine.Step이 읽고 재구축 후 Clear.
        public bool TopologyDirty { get; private set; }

        public CityGrid(int width, int height)
        {
            _width = width;
            _height = height;
            _tiles = new TileType[width * height];
            _footprintAnchors = new Vector2Int[width * height];
            for (int i = 0; i < _footprintAnchors.Length; i++)
            {
                _footprintAnchors[i] = InvalidAnchor;
            }
        }

        // flat 인덱스. 주석님 GridUtil엔 Index가 없어 여기서 직접(index = y*W+x).
        int Index(Vector2Int t) => t.y * _width + t.x;

        int _roadCount = -1;
        int _roadCountVersion = -1;
        public int RoadTileCount
        {
            get
            {
                if (_roadCountVersion == TopologyVersion) return _roadCount;
                int n = 0;
                for (int i = 0; i < _tiles.Length; i++)
                {
                    if (_tiles[i] == TileType.Road)
                    {
                        n++;
                    }
                }

                _roadCount = n;
                _roadCountVersion = TopologyVersion;
                return n;
            }
        }

        // internal(private→승격): SimEngine의 IReadOnlyTileData OOB 가드가 같은 어셈블리에서 재사용(감사 2026-07-12).
        internal bool InBounds(Vector2Int t) =>
            t.x >= 0 && t.x < _width && t.y >= 0 && t.y < _height;

        public TileType GetTile(Vector2Int t) => _tiles[Index(t)];

        // 범위 안 + 진짜 타일 + 빈 칸일 때만 배치 가능.
        public bool CanPlace(Vector2Int t, TileType type)
        {
            if (type == TileType.Empty) return false;   // 비우기는 Remove가 담당

            Vector2Int size = TileFootprint.GetSize(type);
            for (int y = 0; y < size.y; y++)
            {
                for (int x = 0; x < size.x; x++)
                {
                    Vector2Int occupied = t + new Vector2Int(x, y);
                    if (!InBounds(occupied) || GetTile(occupied) != TileType.Empty)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public bool Place(Vector2Int t, TileType type)
        {
            if (!CanPlace(t, type)) return false;

            Vector2Int size = TileFootprint.GetSize(type);
            for (int y = 0; y < size.y; y++)
            {
                for (int x = 0; x < size.x; x++)
                {
                    Vector2Int occupied = t + new Vector2Int(x, y);
                    int index = Index(occupied);
                    _tiles[index] = type;
                    _footprintAnchors[index] = t;
                }
            }

            MarkDirty();
            return true;
        }

        public bool Remove(Vector2Int t) => TryRemove(t, out _);

        // 범위 검사 + "뭘 지웠나"를 한 곳에서 — 호출자가 GetTile을 따로 부르다 범위 밖에서 터지는 일 방지.
        public bool TryRemove(Vector2Int t, out TileType removed)
        {
            return TryRemove(t, out removed, out _);
        }

        public bool TryRemove(Vector2Int t, out TileType removed, out Vector2Int anchor)
        {
            removed = TileType.Empty;
            anchor = default;
            if (!InBounds(t)) return false;
            removed = GetTile(t);
            if (removed == TileType.Empty) return false;   // 지울 게 없음

            anchor = _footprintAnchors[Index(t)];
            if (anchor == InvalidAnchor)
            {
                anchor = t;
            }

            Vector2Int size = TileFootprint.GetSize(removed);
            for (int y = 0; y < size.y; y++)
            {
                for (int x = 0; x < size.x; x++)
                {
                    Vector2Int occupied = anchor + new Vector2Int(x, y);
                    if (!InBounds(occupied)) continue;

                    int index = Index(occupied);
                    if (_footprintAnchors[index] != anchor) continue;

                    _tiles[index] = TileType.Empty;
                    _footprintAnchors[index] = InvalidAnchor;
                }
            }

            MarkDirty();
            return true;
        }

        public bool TryGetFootprintAnchor(Vector2Int t, out Vector2Int anchor)
        {
            anchor = default;
            if (!InBounds(t) || GetTile(t) == TileType.Empty) return false;

            anchor = _footprintAnchors[Index(t)];
            if (anchor == InvalidAnchor)
            {
                anchor = t;
            }

            return true;
        }

        public bool IsFootprintAnchor(Vector2Int t) =>
            TryGetFootprintAnchor(t, out Vector2Int anchor) && anchor == t;

        // 세이브 복원용 seam: 도시를 통째로 비운다(복원 = Clear → 저장 타일 재배치).
        public void Clear()
        {
            System.Array.Clear(_tiles, 0, _tiles.Length);
            for (int i = 0; i < _footprintAnchors.Length; i++)
            {
                _footprintAnchors[i] = InvalidAnchor;
            }
            MarkDirty();
        }

        // ── 교차로 판정(직각 도로 이웃 ≥3)의 단일 출처 — SignalMap·RoadQueueNetwork가 공유.
        // TopologyVersion 키 lazy 캐시(RoadNetwork.EnsureRegions와 같은 패턴). 구매 피벗 2단계에서
        // "교차로 ≠ 신호"가 되므로 신호와 무관한 여기(grid)가 오너.
        static readonly Vector2Int[] OrthoDirs =
            { new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0) };

        bool[] _intersection;
        int _intersectionVersion = -1;

        public bool IsIntersection(Vector2Int t)
        {
            if (!InBounds(t) || GetTile(t) != TileType.Road) return false;
            EnsureIntersections();
            return _intersection[Index(t)];
        }

        void EnsureIntersections()
        {
            if (_intersectionVersion == TopologyVersion) return;
            _intersectionVersion = TopologyVersion;
            _intersection ??= new bool[_tiles.Length];

            for (int y = 0; y < _height; y++)
                for (int x = 0; x < _width; x++)
                {
                    var t = new Vector2Int(x, y);
                    int i = Index(t);
                    if (GetTile(t) != TileType.Road) { _intersection[i] = false; continue; }
                    int n = 0;
                    foreach (var d in OrthoDirs)
                    {
                        var v = t + d;
                        if (InBounds(v) && GetTile(v) == TileType.Road) n++;
                    }
                    _intersection[i] = n >= 3;
                }
        }

        public void ClearTopologyDirty() => TopologyDirty = false;

        // SimEngine.ApplyConfig(스펙 2026-07-12) 전용 seam: 타일은 안 바뀌므로 TopologyVersion
        // (교차로·Region 캐시 키)은 그대로 두고, 다음 Step의 재계획(Reassign+RebuildSignals+Plan)만
        // 강제한다. Place/Remove의 MarkDirty와 달리 캐시 무효화가 필요 없어 별도 진입점.
        internal void MarkTopologyDirty() => TopologyDirty = true;

        void MarkDirty()
        {
            TopologyVersion++;
            TopologyDirty = true;
        }
    }
}
