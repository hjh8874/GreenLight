using UnityEngine;

namespace CityFlow.Contracts
{
    public enum TileType
    {
        Empty,
        Road,
        House,
        Office,
        School,
        Hospital,
        SpecialBuilding
    }

    /// <summary>
    /// 건물 배치 방향(0°, 90°, 180°, 270°). 90도 단위 시계 방향 회전.
    /// </summary>
    public enum PlacementDirection
    {
        North = 0,
        East  = 1,
        South = 2,
        West  = 3
    }

    public static class TileFootprint
    {
        private static readonly Vector2Int SingleTile = Vector2Int.one;
        private static readonly Vector2Int StandardBuilding = new Vector2Int(2, 2);

        public static bool IsBuilding(TileType type) =>
            type != TileType.Empty && type != TileType.Road;

        public static Vector2Int GetSize(TileType type) =>
            IsBuilding(type) ? StandardBuilding : SingleTile;

        /// <summary>
        /// 회전 방향을 고려한 풋프린트 크기를 반환합니다.
        /// 90도/270도일 경우 W↔H를 교환(Swap)합니다.
        /// </summary>
        public static Vector2Int GetRotatedSize(TileType type, PlacementDirection direction)
        {
            Vector2Int size = GetSize(type);
            bool isSwapped = direction == PlacementDirection.East || direction == PlacementDirection.West;
            return isSwapped ? new Vector2Int(size.y, size.x) : size;
        }

        /// <summary>
        /// 현재 방향에서 90도 시계 방향으로 회전한 다음 방향을 반환합니다.
        /// </summary>
        public static PlacementDirection RotateClockwise(PlacementDirection current) =>
            (PlacementDirection)(((int)current + 1) % 4);

        /// <summary>
        /// PlacementDirection을 각도(0, 90, 180, 270)로 변환합니다.
        /// </summary>
        public static float ToAngle(PlacementDirection direction) =>
            (int)direction * 90f;

        /// <summary>
        /// 기본 모델에서 주차장·출입구가 놓인 로컬 앞면 방향을 반환합니다.
        /// North(0°) 모델의 앞면은 그리드 -Y이며 회전 방향과 함께 시계 방향으로 돕니다.
        /// </summary>
        public static Vector2Int GetFrontOffset(
            PlacementDirection direction) =>
            direction switch
            {
                PlacementDirection.East => Vector2Int.right,
                PlacementDirection.South => Vector2Int.up,
                PlacementDirection.West => Vector2Int.left,
                _ => Vector2Int.down
            };
    }

    public enum CongestionLevel
    {
        Free,
        Slow,
        Jam
    }

    public readonly struct TileSnapshot
    {
        public readonly Vector2Int Tile;
        public readonly TileType Type;
        public readonly CongestionLevel Congestion;
        public readonly float Density01;

        public TileSnapshot(
            Vector2Int tile,
            TileType type,
            CongestionLevel congestion,
            float density01
        )
        {
            Tile = tile;
            Type = type;
            Congestion = congestion;
            Density01 = Mathf.Clamp01(density01);
        }
    }
}
