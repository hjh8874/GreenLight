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

        // 공사 진행도 0..1. 공사 중이 아니면 false.
        // 풋프린트 어느 타일로 물어도 앵커로 환산해 답한다(뷰가 앵커를 몰라도 되게).
        bool TryGetConstructionProgress01(Vector2Int tile, out float progress01);
    }
}
