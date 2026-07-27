using System;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.WorldGrid
{
    public sealed class WorldGridState
    {
        private readonly GridChunkPartition partition;
        private readonly GridChunkState[] chunks;
        private readonly int initialUnlockedColumns;
        private readonly int initialUnlockedRows;

        public WorldGridState(
            GridChunkPartition partition,
            int initialUnlockedColumns,
            int initialUnlockedRows)
        {
            this.partition = partition ??
                throw new ArgumentNullException(nameof(partition));
            this.initialUnlockedColumns = Mathf.Clamp(
                initialUnlockedColumns,
                1,
                partition.ChunkColumns);
            this.initialUnlockedRows = Mathf.Clamp(
                initialUnlockedRows,
                1,
                partition.ChunkRows);

            chunks = new GridChunkState[partition.ChunkCount];
            for (int index = 0; index < chunks.Length; index++)
            {
                partition.TryGetChunkId(index, out GridChunkId id);
                chunks[index] = new GridChunkState(id);
            }

            ResetToInitialAccess();
        }

        public GridChunkPartition Partition => partition;

        public bool IsChunkUnlocked(GridChunkId chunk)
        {
            return partition.TryGetChunkIndex(chunk, out int index) &&
                   chunks[index].IsUnlocked;
        }

        public bool IsTileUnlocked(Vector2Int tile)
        {
            return partition.TryGetChunkId(tile, out GridChunkId chunk) &&
                   IsChunkUnlocked(chunk);
        }

        public bool IsAreaUnlocked(Vector2Int anchor, Vector2Int footprint)
        {
            if (!partition.IsAreaInsideWorld(anchor, footprint))
            {
                return false;
            }

            Vector2Int lastTile = anchor + footprint - Vector2Int.one;
            partition.TryGetChunkId(anchor, out GridChunkId firstChunk);
            partition.TryGetChunkId(lastTile, out GridChunkId lastChunk);

            for (int chunkY = firstChunk.Y; chunkY <= lastChunk.Y; chunkY++)
            {
                for (int chunkX = firstChunk.X; chunkX <= lastChunk.X; chunkX++)
                {
                    if (!IsChunkUnlocked(new GridChunkId(chunkX, chunkY)))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public bool TryUnlockChunk(GridChunkId chunk)
        {
            return partition.TryGetChunkIndex(chunk, out int index) &&
                   chunks[index].TryUnlock();
        }

        public WorldGridSaveData CreateSnapshot()
        {
            int unlockedCount = 0;
            for (int index = 0; index < chunks.Length; index++)
            {
                if (chunks[index].IsUnlocked)
                {
                    unlockedCount++;
                }
            }

            int[] unlockedIndices = new int[unlockedCount];
            int destination = 0;
            for (int index = 0; index < chunks.Length; index++)
            {
                if (chunks[index].IsUnlocked)
                {
                    unlockedIndices[destination++] = index;
                }
            }

            return new WorldGridSaveData
            {
                WorldWidth = partition.WorldWidth,
                WorldHeight = partition.WorldHeight,
                ChunkSize = partition.ChunkSize,
                UnlockedChunkIndices = unlockedIndices
            };
        }

        public bool RestoreSnapshot(WorldGridSaveData snapshot)
        {
            if (snapshot == null)
            {
                ResetToInitialAccess();
                return true;
            }

            if (snapshot.WorldWidth != partition.WorldWidth ||
                snapshot.WorldHeight != partition.WorldHeight ||
                snapshot.ChunkSize != partition.ChunkSize)
            {
                ResetToInitialAccess();
                return false;
            }

            ResetAll(isUnlocked: false);
            int[] unlockedIndices = snapshot.UnlockedChunkIndices;
            if (unlockedIndices == null)
            {
                return true;
            }

            for (int index = 0; index < unlockedIndices.Length; index++)
            {
                int chunkIndex = unlockedIndices[index];
                if (chunkIndex >= 0 && chunkIndex < chunks.Length)
                {
                    chunks[chunkIndex].TryUnlock();
                }
            }

            return true;
        }

        public void ResetToInitialAccess()
        {
            ResetAll(isUnlocked: false);
            int firstChunkX =
                (partition.ChunkColumns - initialUnlockedColumns) / 2;
            int firstChunkY =
                (partition.ChunkRows - initialUnlockedRows) / 2;

            for (int row = 0; row < initialUnlockedRows; row++)
            {
                for (int column = 0;
                     column < initialUnlockedColumns;
                     column++)
                {
                    TryUnlockChunk(new GridChunkId(
                        firstChunkX + column,
                        firstChunkY + row));
                }
            }
        }

        private void ResetAll(bool isUnlocked)
        {
            for (int index = 0; index < chunks.Length; index++)
            {
                chunks[index].Reset(isUnlocked);
            }
        }
    }
}
