using UnityEngine;

namespace CityFlow.Sim
{
    // 도시 타일의 유일한 상태 저장소.
    // 2차원 배열 대신 flat 배열(index = y*W+x): 캐시 친화 + 직렬화 쉬움(blueprint §2).
    internal sealed class CityGrid
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

        bool InBounds(Vector2Int t) =>
            t.x >= 0 && t.x < _width && t.y >= 0 && t.y < _height;

        public TileType GetTile(Vector2Int t) => _tiles[GridUtil.Index(t.x, t.y, _width)];

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
            _tiles[GridUtil.Index(t.x, t.y, _width)] = type;
            MarkDirty();
            return true;
        }

        public bool Remove(Vector2Int t)
        {
            if (!InBounds(t)) return false;
            if (GetTile(t) == TileType.Empty) return false;   // 지울 게 없음
            _tiles[GridUtil.Index(t.x, t.y, _width)] = TileType.Empty;
            MarkDirty();
            return true;
        }

        public void ClearTopologyDirty() => TopologyDirty = false;

        void MarkDirty()
        {
            TopologyVersion++;
            TopologyDirty = true;
        }
    }
}
