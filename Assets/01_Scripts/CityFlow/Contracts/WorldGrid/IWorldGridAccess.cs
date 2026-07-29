using System;
using UnityEngine;

namespace CityFlow.Contracts
{
    public interface IWorldGridAccess
    {
        int WorldWidth { get; }
        int WorldHeight { get; }
        int ChunkSize { get; }
        int ChunkColumns { get; }
        int ChunkRows { get; }
        Vector2Int InitialPlayableOrigin { get; }
        Vector2Int InitialPlayableSize { get; }

        event Action<GridChunkId> ChunkUnlocked;
        event Action AccessRestored;

        bool IsInsideWorld(Vector2Int tile);
        bool IsTileUnlocked(Vector2Int tile);
        bool IsChunkUnlocked(GridChunkId chunk);
        bool IsAreaUnlocked(Vector2Int anchor, Vector2Int footprint);
        bool TryGetChunkId(Vector2Int tile, out GridChunkId chunk);
    }
}
