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
        Hospital
    }

    public static class TileFootprint
    {
        private static readonly Vector2Int SingleTile = Vector2Int.one;
        private static readonly Vector2Int StandardBuilding = new Vector2Int(2, 2);

        public static bool IsBuilding(TileType type) =>
            type != TileType.Empty && type != TileType.Road;

        public static Vector2Int GetSize(TileType type) =>
            IsBuilding(type) ? StandardBuilding : SingleTile;
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