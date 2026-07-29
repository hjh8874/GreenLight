using System;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.WorldGrid
{
    public static class UnlockedGridTileScanner
    {
        public static int VisitUnlockedTiles(
            IWorldGridAccess worldGrid,
            int fallbackWidth,
            int fallbackHeight,
            Action<Vector2Int> visitor)
        {
            if (visitor == null)
            {
                throw new ArgumentNullException(nameof(visitor));
            }

            if (worldGrid == null)
            {
                return VisitBounds(
                    0,
                    0,
                    Math.Max(1, fallbackWidth),
                    Math.Max(1, fallbackHeight),
                    visitor);
            }

            int visitedTileCount = 0;
            for (int chunkY = 0; chunkY < worldGrid.ChunkRows; chunkY++)
            {
                for (int chunkX = 0;
                     chunkX < worldGrid.ChunkColumns;
                     chunkX++)
                {
                    var chunk = new GridChunkId(chunkX, chunkY);
                    if (!worldGrid.IsChunkUnlocked(chunk))
                    {
                        continue;
                    }

                    visitedTileCount += VisitChunk(
                        worldGrid,
                        chunk,
                        visitor);
                }
            }

            return visitedTileCount;
        }

        public static int VisitChunk(
            IWorldGridAccess worldGrid,
            GridChunkId chunk,
            Action<Vector2Int> visitor)
        {
            if (worldGrid == null)
            {
                throw new ArgumentNullException(nameof(worldGrid));
            }

            if (visitor == null)
            {
                throw new ArgumentNullException(nameof(visitor));
            }

            if (chunk.X < 0 || chunk.X >= worldGrid.ChunkColumns ||
                chunk.Y < 0 || chunk.Y >= worldGrid.ChunkRows ||
                !worldGrid.IsChunkUnlocked(chunk))
            {
                return 0;
            }

            int chunkSize = Math.Max(1, worldGrid.ChunkSize);
            int minX = chunk.X * chunkSize;
            int minY = chunk.Y * chunkSize;
            int maxX = Math.Min(worldGrid.WorldWidth, minX + chunkSize);
            int maxY = Math.Min(worldGrid.WorldHeight, minY + chunkSize);
            return VisitBounds(minX, minY, maxX, maxY, visitor);
        }

        private static int VisitBounds(
            int minX,
            int minY,
            int maxX,
            int maxY,
            Action<Vector2Int> visitor)
        {
            int visitedTileCount = 0;
            for (int y = minY; y < maxY; y++)
            {
                for (int x = minX; x < maxX; x++)
                {
                    visitor(new Vector2Int(x, y));
                    visitedTileCount++;
                }
            }

            return visitedTileCount;
        }

        // Unity setup: no component is required; runtime consumers call this
        // helper with the WorldGrid service registered in CityFlowServices.
    }
}
