using System;
using UnityEngine;

namespace CityFlow.Contracts.Save
{
    public interface ITerrainDecorationSaveSource
    {
        event Action StateChanged;

        bool IsCleared(Vector2Int tile);

        TerrainDecorationSaveData CreateSnapshot();

        void RestoreSnapshot(TerrainDecorationSaveData snapshot);
    }
}
