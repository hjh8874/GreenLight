using System;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.View
{
    public sealed class TerrainDecorationState
    {
        private readonly int width;
        private readonly int height;
        private readonly bool[] clearedTiles;

        public TerrainDecorationState(int width, int height)
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
                ClearedTileIndices = clearedTileIndices
            };
        }

        public void RestoreSnapshot(TerrainDecorationSaveData snapshot)
        {
            Array.Clear(clearedTiles, 0, clearedTiles.Length);

            int[] clearedTileIndices = snapshot?.ClearedTileIndices;
            if (clearedTileIndices == null)
            {
                return;
            }

            for (int i = 0; i < clearedTileIndices.Length; i++)
            {
                int tileIndex = clearedTileIndices[i];
                if (tileIndex >= 0 && tileIndex < clearedTiles.Length)
                {
                    clearedTiles[tileIndex] = true;
                }
            }
        }

        private int ToIndex(Vector2Int tile)
        {
            return tile.y * width + tile.x;
        }

        private bool IsInsideGrid(Vector2Int tile)
        {
            return tile.x >= 0 &&
                   tile.x < width &&
                   tile.y >= 0 &&
                   tile.y < height;
        }
    }
}
