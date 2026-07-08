using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim
{
    // 도시 타일의 유일한 상태 저장소. flat 배열(index = y*W+x).
    // 주석님 IPlacementService 구현 — 배치/제거의 실제 창구.
    internal sealed class CityGrid : IPlacementService
    {
        readonly TileType[] _tiles;   // new 시 전부 0 = TileType.Empty → "빈 도시" 공짜
        readonly int _width;
        readonly int _height;

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
        }

        // flat 인덱스. 주석님 GridUtil엔 Index가 없어 여기서 직접(index = y*W+x).
        int Index(Vector2Int t) => t.y * _width + t.x;

        bool InBounds(Vector2Int t) =>
            t.x >= 0 && t.x < _width && t.y >= 0 && t.y < _height;

        public TileType GetTile(Vector2Int t) => _tiles[Index(t)];

        // 범위 안 + 진짜 타일 + 빈 칸일 때만 배치 가능.
        public bool CanPlace(Vector2Int t, TileType type)
        {
            if (!InBounds(t)) return false;
            if (type == TileType.Empty) return false;   // 비우기는 Remove가 담당
            return GetTile(t) == TileType.Empty;
        }

        public bool Place(Vector2Int t, TileType type)
        {
            if (!CanPlace(t, type)) return false;
            _tiles[Index(t)] = type;
            MarkDirty();
            return true;
        }

        public bool Remove(Vector2Int t) => TryRemove(t, out _);

        // 범위 검사 + "뭘 지웠나"를 한 곳에서 — 호출자가 GetTile을 따로 부르다 범위 밖에서 터지는 일 방지.
        public bool TryRemove(Vector2Int t, out TileType removed)
        {
            removed = TileType.Empty;
            if (!InBounds(t)) return false;
            removed = GetTile(t);
            if (removed == TileType.Empty) return false;   // 지울 게 없음
            _tiles[Index(t)] = TileType.Empty;
            MarkDirty();
            return true;
        }

        // 세이브 복원용 seam: 도시를 통째로 비운다(복원 = Clear → 저장 타일 재배치).
        public void Clear()
        {
            System.Array.Clear(_tiles, 0, _tiles.Length);
            MarkDirty();
        }

        public void ClearTopologyDirty() => TopologyDirty = false;

        void MarkDirty()
        {
            TopologyVersion++;
            TopologyDirty = true;
        }
    }
}
