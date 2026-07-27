using System;
using UnityEngine;

namespace CityFlow.Sim
{
    internal sealed class RouteRegionLayout
    {
        public RouteRegionLayout(int worldWidth, int worldHeight, int regionSize)
        {
            if (worldWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(worldWidth));
            }

            if (worldHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(worldHeight));
            }

            if (regionSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(regionSize));
            }

            WorldWidth = worldWidth;
            WorldHeight = worldHeight;
            RegionSize = regionSize;
            RegionColumns = Mathf.CeilToInt(worldWidth / (float)regionSize);
            RegionRows = Mathf.CeilToInt(worldHeight / (float)regionSize);
        }

        public int WorldWidth { get; }
        public int WorldHeight { get; }
        public int RegionSize { get; }
        public int RegionColumns { get; }
        public int RegionRows { get; }
        public int RegionCount => RegionColumns * RegionRows;
        public bool RequiresChunkedSearch => RegionCount > 1;

        public int GetRegionIndex(Vector2Int tile)
        {
            if (!IsInsideWorld(tile))
            {
                return -1;
            }

            int regionX = tile.x / RegionSize;
            int regionY = tile.y / RegionSize;
            return regionY * RegionColumns + regionX;
        }

        public RouteSearchBounds GetBounds(int regionIndex)
        {
            if (regionIndex < 0 || regionIndex >= RegionCount)
            {
                throw new ArgumentOutOfRangeException(nameof(regionIndex));
            }

            int regionX = regionIndex % RegionColumns;
            int regionY = regionIndex / RegionColumns;
            int minX = regionX * RegionSize;
            int minY = regionY * RegionSize;
            return new RouteSearchBounds(
                minX,
                minY,
                Mathf.Min(RegionSize, WorldWidth - minX),
                Mathf.Min(RegionSize, WorldHeight - minY));
        }

        private bool IsInsideWorld(Vector2Int tile) =>
            tile.x >= 0 && tile.x < WorldWidth &&
            tile.y >= 0 && tile.y < WorldHeight;
    }
}
