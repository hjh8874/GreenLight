using System;

namespace CityFlow.Contracts.Save
{
    [Serializable]
    public sealed class TerrainDecorationSaveData
    {
        public int[] ClearedTileIndices = Array.Empty<int>();
    }
}
