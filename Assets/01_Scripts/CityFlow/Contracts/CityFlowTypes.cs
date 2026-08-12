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
        SpecialBuilding,
        // 공사 중. 완성 시 CityGrid.Promote()가 실제 타입으로 교체한다.
        // 실제 점유 크기는 공사 대상 건물의 풋프린트를 그대로 유지한다.
        UnderConstruction,
        // 약국·커피숍처럼 뒤 1x1 건물과 앞 1x1 주차장을 사용하는 특수건물.
        // 기존 저장의 enum 값을 보존하기 위해 항상 마지막에 추가한다.
        CompactSpecialBuilding
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
        private static readonly Vector2Int ResidentialBuilding =
            new Vector2Int(1, 2);
        private static readonly Vector2Int StandardBuilding = new Vector2Int(2, 2);

        public static bool IsBuilding(TileType type) =>
            type != TileType.Empty && type != TileType.Road;

        public static bool IsSpecialBuilding(TileType type) =>
            type == TileType.SpecialBuilding ||
            type == TileType.CompactSpecialBuilding;

        public static Vector2Int GetSize(TileType type) =>
            type == TileType.House ||
            type == TileType.CompactSpecialBuilding
                ? ResidentialBuilding
                : IsBuilding(type)
                    ? StandardBuilding
                    : SingleTile;

        public static bool TryGetSpecialBuildingType(
            Vector2Int footprint,
            out TileType type)
        {
            if (footprint == ResidentialBuilding)
            {
                type = TileType.CompactSpecialBuilding;
                return true;
            }

            if (footprint == StandardBuilding)
            {
                type = TileType.SpecialBuilding;
                return true;
            }

            type = TileType.Empty;
            return false;
        }

        /// <summary>
        /// 회전 방향을 고려한 풋프린트 크기를 반환합니다.
        /// 90도/270도일 경우 W↔H를 교환(Swap)합니다.
        /// </summary>
        public static Vector2Int GetRotatedSize(TileType type, PlacementDirection direction)
        {
            return GetRotatedSize(GetSize(type), direction);
        }

        public static Vector2Int GetRotatedSize(
            Vector2Int size,
            PlacementDirection direction)
        {
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

        /// <summary>
        /// 마우스로 가리킨 전면 주차장 타일을 기준으로 건물 풋프린트 앵커를 구합니다.
        /// 전면이 여러 칸이면 커서 타일을 전면 행/열의 첫 칸으로 사용합니다.
        /// </summary>
        public static Vector2Int GetAnchorFromFrontTile(
            Vector2Int frontTile,
            TileType type,
            PlacementDirection direction)
        {
            Vector2Int size = GetRotatedSize(type, direction);
            return direction switch
            {
                PlacementDirection.East =>
                    frontTile - new Vector2Int(size.x - 1, 0),
                PlacementDirection.South =>
                    frontTile - new Vector2Int(0, size.y - 1),
                _ => frontTile
            };
        }
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
