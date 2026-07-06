using UnityEngine;

namespace CityFlow.Contracts
{
    public interface IReadOnlyTileData
    {
        float Stability01 { get; }

        CongestionLevel GetCongestion(Vector2Int tile);

        float GetDensity01(Vector2Int tile);

        TileType GetTileType(Vector2Int tile);
    }
}
