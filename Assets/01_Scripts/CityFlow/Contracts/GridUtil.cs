using UnityEngine;

namespace CityFlow.Contracts
{
    public static class GridUtil
    {
        public const int DefaultWidth = 20;
        public const int DefaultHeight = 20;
        public const float TileSize = 1f;

        public static Vector2Int WorldToGrid(Vector3 world)
        {
            return new Vector2Int(
                Mathf.FloorToInt(world.x / TileSize),
                Mathf.FloorToInt(world.y / TileSize));
        }

        public static Vector3 GridToWorld(Vector2Int grid)
        {
            return new Vector3(
                (grid.x + 0.5f) * TileSize,
                (grid.y + 0.5f) * TileSize,
                0f);
        }

        public static bool IsInside(Vector2Int tile, int width = DefaultWidth, int height = DefaultHeight)
        {
            return tile.x >= 0 && tile.x < width && tile.y >= 0 && tile.y < height;
        }
    }
}
