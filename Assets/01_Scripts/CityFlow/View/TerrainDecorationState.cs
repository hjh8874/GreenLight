using System;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.View
{
    public sealed class TerrainDecorationState
    {
        private readonly int width;
        private readonly int height;
        private readonly Vector2Int origin;
        private readonly bool[] clearedTiles;

        public TerrainDecorationState(
            int width,
            int height,
            Vector2Int origin = default)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            this.width = width;
            this.height = height;
            this.origin = origin;
            clearedTiles = new bool[width * height];
        }

        public bool IsCleared(Vector2Int tile)
        {
            return IsInsideGrid(tile) && clearedTiles[ToIndex(tile)];
        }

        public void ApplyPlacement(
            Vector2Int anchor,
            Vector2Int footprint,
            bool isRemove)
        {
            if (isRemove)
            {
                return;
            }

            footprint.x = Mathf.Max(1, footprint.x);
            footprint.y = Mathf.Max(1, footprint.y);

            for (int y = 0; y < footprint.y; y++)
            {
                for (int x = 0; x < footprint.x; x++)
                {
                    Vector2Int tile = anchor + new Vector2Int(x, y);
                    if (IsInsideGrid(tile))
                    {
                        clearedTiles[ToIndex(tile)] = true;
                    }
                }
            }
        }

        public TerrainDecorationSaveData CreateSnapshot()
        {
            int clearedCount = 0;
            for (int i = 0; i < clearedTiles.Length; i++)
            {
                if (clearedTiles[i])
                {
                    clearedCount++;
                }
            }

            int[] clearedTileIndices = new int[clearedCount];
            int destinationIndex = 0;
            for (int i = 0; i < clearedTiles.Length; i++)
            {
                if (clearedTiles[i])
                {
                    clearedTileIndices[destinationIndex++] = i;
                }
            }

            return new TerrainDecorationSaveData
            {
                GridWidth = width,
                GridHeight = height,
                OriginX = origin.x,
                OriginY = origin.y,
                ClearedTileIndices = clearedTileIndices
            };
        }

        public void RestoreSnapshot(
            TerrainDecorationSaveData snapshot,
            int legacyWidth = 0,
            int legacyHeight = 0,
            Vector2Int legacyOrigin = default)
        {
            Array.Clear(clearedTiles, 0, clearedTiles.Length);

            int[] clearedTileIndices = snapshot?.ClearedTileIndices;
            if (clearedTileIndices == null)
            {
                return;
            }

            bool hasSavedGrid = snapshot.GridWidth > 0 &&
                                snapshot.GridHeight > 0;
            int sourceWidth = hasSavedGrid
                ? snapshot.GridWidth
                : legacyWidth > 0 ? legacyWidth : width;
            int sourceHeight = hasSavedGrid
                ? snapshot.GridHeight
                : legacyHeight > 0 ? legacyHeight : height;
            Vector2Int sourceOrigin = hasSavedGrid
                ? new Vector2Int(snapshot.OriginX, snapshot.OriginY)
                : legacyOrigin;
            int sourceTileCount = sourceWidth * sourceHeight;

            for (int i = 0; i < clearedTileIndices.Length; i++)
            {
                int tileIndex = clearedTileIndices[i];
                if (tileIndex < 0 || tileIndex >= sourceTileCount)
                {
                    continue;
                }

                Vector2Int tile = sourceOrigin + new Vector2Int(
                    tileIndex % sourceWidth,
                    tileIndex / sourceWidth);
                if (IsInsideGrid(tile))
                {
                    clearedTiles[ToIndex(tile)] = true;
                }
            }
        }

        private int ToIndex(Vector2Int tile)
        {
            Vector2Int localTile = tile - origin;
            return localTile.y * width + localTile.x;
        }

        private bool IsInsideGrid(Vector2Int tile)
        {
            return tile.x >= origin.x &&
                   tile.x < origin.x + width &&
                   tile.y >= origin.y &&
                   tile.y < origin.y + height;
        }
    }
}
