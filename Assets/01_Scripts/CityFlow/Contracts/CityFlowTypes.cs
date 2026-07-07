using UnityEngine;

namespace CityFlow.Contracts
{
    public enum TileType
    {
        Empty,
        Road,
        Origin,
        Destination
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

        public TileSnapshot(Vector2Int tile, TileType type, CongestionLevel congestion, float density01)
        {
            Tile = tile;
            Type = type;
            Congestion = congestion;
            Density01 = Mathf.Clamp01(density01);
        }
    }
}
