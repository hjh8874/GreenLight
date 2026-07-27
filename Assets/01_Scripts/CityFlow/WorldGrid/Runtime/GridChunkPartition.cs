using System;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.WorldGrid
{
    public sealed class GridChunkPartition
    {
        public GridChunkPartition(int worldWidth, int worldHeight, int chunkSize)
        {
            if (worldWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(worldWidth));
            }

            if (worldHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(worldHeight));
            }

            if (chunkSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize));
            }

            WorldWidth = worldWidth;
            WorldHeight = worldHeight;
            ChunkSize = chunkSize;
            ChunkColumns = Mathf.CeilToInt(worldWidth / (float)chunkSize);
            ChunkRows = Mathf.CeilToInt(worldHeight / (float)chunkSize);
        }

        public int WorldWidth { get; }
        public int WorldHeight { get; }
        public int ChunkSize { get; }
        public int ChunkColumns { get; }
        public int ChunkRows { get; }
        public int ChunkCount => ChunkColumns * ChunkRows;

        public bool IsInsideWorld(Vector2Int tile) =>
            tile.x >= 0 && tile.x < WorldWidth &&
            tile.y >= 0 && tile.y < WorldHeight;

        public bool IsValidChunk(GridChunkId chunk) =>
            chunk.X >= 0 && chunk.X < ChunkColumns &&
            chunk.Y >= 0 && chunk.Y < ChunkRows;

        public bool IsAreaInsideWorld(Vector2Int anchor, Vector2Int footprint)
        {
            if (footprint.x <= 0 || footprint.y <= 0)
            {
                return false;
            }

            long maxX = (long)anchor.x + footprint.x;
            long maxY = (long)anchor.y + footprint.y;
            return anchor.x >= 0 && anchor.y >= 0 &&
                   maxX <= WorldWidth && maxY <= WorldHeight;
        }

        public bool TryGetChunkId(Vector2Int tile, out GridChunkId chunk)
        {
            chunk = default;
            if (!IsInsideWorld(tile))
            {
                return false;
            }

            chunk = new GridChunkId(
                tile.x / ChunkSize,
                tile.y / ChunkSize);
            return true;
        }

        public bool TryGetChunkIndex(GridChunkId chunk, out int index)
        {
            index = -1;
            if (!IsValidChunk(chunk))
            {
                return false;
            }

            index = chunk.Y * ChunkColumns + chunk.X;
            return true;
        }

        public bool TryGetChunkId(int index, out GridChunkId chunk)
        {
            chunk = default;
            if (index < 0 || index >= ChunkCount)
            {
                return false;
            }

            chunk = new GridChunkId(
                index % ChunkColumns,
                index / ChunkColumns);
            return true;
        }
    }
}
