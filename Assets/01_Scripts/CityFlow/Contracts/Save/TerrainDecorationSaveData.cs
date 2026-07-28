using System;

namespace CityFlow.Contracts.Save
{
    [Serializable]
    public sealed class TerrainDecorationSaveData
    {
        public int GridWidth;
        public int GridHeight;
        public int OriginX;
        public int OriginY;
        public int[] ClearedTileIndices = Array.Empty<int>();
    }
}
