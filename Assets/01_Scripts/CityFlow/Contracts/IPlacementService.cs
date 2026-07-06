using UnityEngine;

namespace CityFlow.Contracts
{
    public interface IPlacementService
    {
        bool CanPlace(Vector2Int tile, TileType type);

        bool Place(Vector2Int tile, TileType type);

        bool Remove(Vector2Int tile);
    }
}
