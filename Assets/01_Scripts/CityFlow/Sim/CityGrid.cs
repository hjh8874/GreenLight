using System.Collections.Generic;
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
        readonly PlacementDirection[] _directions;
        // Flat indices stay sorted to preserve the previous y-then-x simulation order.
        readonly List<int> _roadTileIndices = new();
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
            _directions = new PlacementDirection[width * height];
            for (int i = 0; i < _footprintAnchors.Length; i++)
            {
                _footprintAnchors[i] = InvalidAnchor;
                _directions[i] = PlacementDirection.North;
            }
        }

        // flat 인덱스. 주석님 GridUtil엔 Index가 없어 여기서 직접(index = y*W+x).
        int Index(Vector2Int t) => t.y * _width + t.x;

        public int RoadTileCount => _roadTileIndices.Count;

        internal int GetRoadTileIndex(int position) =>
            _roadTileIndices[position];

        // internal(private→승격): SimEngine의 IReadOnlyTileData OOB 가드가 같은 어셈블리에서 재사용(감사 2026-07-12).
        internal bool InBounds(Vector2Int t) =>
            t.x >= 0 && t.x < _width && t.y >= 0 && t.y < _height;

        public TileType GetTile(Vector2Int t) => InBounds(t) ? _tiles[Index(t)] : TileType.Empty;

        public PlacementDirection GetDirection(Vector2Int t) => InBounds(t) ? _directions[Index(t)] : PlacementDirection.North;

        // 범위 안 + 진짜 타일 + 빈 칸일 때만 배치 가능.
        public bool CanPlace(Vector2Int t, TileType type, PlacementDirection direction = PlacementDirection.North)
        {
            if (type == TileType.Empty) return false;   // 비우기는 Remove가 담당

            Vector2Int size = TileFootprint.GetRotatedSize(type, direction);
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

        public bool Place(Vector2Int t, TileType type, PlacementDirection direction = PlacementDirection.North)
        {
            if (!CanPlace(t, type, direction)) return false;

            Vector2Int size = TileFootprint.GetRotatedSize(type, direction);
            for (int y = 0; y < size.y; y++)
            {
                for (int x = 0; x < size.x; x++)
                {
                    Vector2Int occupied = t + new Vector2Int(x, y);
                    int index = Index(occupied);
                    _tiles[index] = type;
                    _footprintAnchors[index] = t;
                    _directions[index] = direction;
                    if (type == TileType.Road)
                    {
                        AddRoadTileIndex(index);
                    }
                }
            }

            MarkDirty();
            return true;
        }

        // 공사 완성 승격. Place()와 달리 CanPlace() 검사를 하지 않는다 —
        // 이미 UnderConstruction이 점유한 풋프린트의 타입만 제자리에서 교체하기 때문이다.
        // anchor/direction은 보존한다(재배치가 아니라 타입 변경).
        internal bool Promote(Vector2Int anchor, TileType targetType)
        {
            if (!InBounds(anchor)) return false;
            int anchorIndex = Index(anchor);
            if (_tiles[anchorIndex] == TileType.Empty) return false;
            if (_footprintAnchors[anchorIndex] != anchor) return false;   // 앵커에서만 승격

            // 승격은 건물 전용이다. Road 가 원본이나 대상이면 거부한다 —
            // Place/TryRemove/Clear 만 _roadTileIndices 를 유지하는데 Promote 는 _tiles 를
            // 직접 쓰므로, 도로가 끼면 인덱스가 조용히 어긋나 RoadTileCount 가 틀어진다.
            // (현재 호출자는 AdvanceConstruction 하나이고 대상이 항상 건물이라 도달하지 않는다.)
            if (_tiles[anchorIndex] == TileType.Road || targetType == TileType.Road) return false;

            PlacementDirection direction = _directions[anchorIndex];
            TileType sourceType = _tiles[anchorIndex];
            Vector2Int targetSize = TileFootprint.GetRotatedSize(targetType, direction);
            Vector2Int sourceSize = sourceType == TileType.UnderConstruction
                ? targetSize
                : TileFootprint.GetRotatedSize(sourceType, direction);
            if (targetType != TileType.UnderConstruction &&
                sourceSize != targetSize)
            {
                return false;
            }

            if (sourceType == TileType.UnderConstruction)
            {
                int occupiedCount = 0;
                for (int i = 0; i < _footprintAnchors.Length; i++)
                {
                    if (_footprintAnchors[i] == anchor)
                    {
                        occupiedCount++;
                    }
                }

                if (occupiedCount != targetSize.x * targetSize.y)
                {
                    return false;
                }
            }

            Vector2Int promotedSize = targetType == TileType.UnderConstruction
                ? sourceSize
                : targetSize;

            // 승격은 재배치가 아니므로 원본 풋프린트를 벗어날 수 없다.
            // 검증과 쓰기를 분리해 실패 시 일부 타일만 바뀌는 비원자 경로를 막는다.
            for (int y = 0; y < promotedSize.y; y++)
            {
                for (int x = 0; x < promotedSize.x; x++)
                {
                    Vector2Int occupied = anchor + new Vector2Int(x, y);
                    if (!InBounds(occupied)) return false;
                    if (_footprintAnchors[Index(occupied)] != anchor) return false;
                }
            }

            for (int y = 0; y < promotedSize.y; y++)
            {
                for (int x = 0; x < promotedSize.x; x++)
                {
                    Vector2Int occupied = anchor + new Vector2Int(x, y);
                    _tiles[Index(occupied)] = targetType;
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

            Vector2Int size = TileFootprint.GetRotatedSize(removed, _directions[Index(anchor)]);
            for (int y = 0; y < size.y; y++)
            {
                for (int x = 0; x < size.x; x++)
                {
                    Vector2Int occupied = anchor + new Vector2Int(x, y);
                    if (!InBounds(occupied)) continue;

                    int index = Index(occupied);
                    if (_footprintAnchors[index] != anchor) continue;

                    if (removed == TileType.Road)
                    {
                        RemoveRoadTileIndex(index);
                    }

                    _tiles[index] = TileType.Empty;
                    _footprintAnchors[index] = InvalidAnchor;
                    _directions[index] = PlacementDirection.North;
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
            _roadTileIndices.Clear();
            for (int i = 0; i < _footprintAnchors.Length; i++)
            {
                _footprintAnchors[i] = InvalidAnchor;
                _directions[i] = PlacementDirection.North;
            }
            MarkDirty();
        }

        private void AddRoadTileIndex(int index)
        {
            int result = _roadTileIndices.BinarySearch(index);
            if (result < 0)
            {
                _roadTileIndices.Insert(~result, index);
            }
        }

        private void RemoveRoadTileIndex(int index)
        {
            int result = _roadTileIndices.BinarySearch(index);
            if (result >= 0)
            {
                _roadTileIndices.RemoveAt(result);
            }
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
