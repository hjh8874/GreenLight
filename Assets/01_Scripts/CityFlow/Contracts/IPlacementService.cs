using UnityEngine;

namespace CityFlow.Contracts
{
    public interface IPlacementService
    {
        bool CanPlace(Vector2Int tile, TileType type, PlacementDirection direction = PlacementDirection.North);

        bool Place(Vector2Int tile, TileType type, PlacementDirection direction = PlacementDirection.North);

        bool Remove(Vector2Int tile);
    }
}
