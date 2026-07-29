using UnityEngine;

namespace CityFlow.Contracts
{
    public interface IReadOnlyTileData
    {

        CongestionLevel GetCongestion(Vector2Int tile);

        float GetDensity01(Vector2Int tile);

        int GetQueueCount(Vector2Int tile, Dir entryDir);

        TileType GetTileType(Vector2Int tile);

        PlacementDirection GetDirection(Vector2Int tile);

        Vector2Int GetFootprintSize(TileType type);

        bool TryGetFootprintAnchor(Vector2Int tile, out Vector2Int anchor);

        bool IsFootprintAnchor(Vector2Int tile);
    }
}
