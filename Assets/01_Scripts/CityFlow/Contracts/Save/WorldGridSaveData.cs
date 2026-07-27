using System;

namespace CityFlow.Contracts.Save
{
    [Serializable]
    public sealed class WorldGridSaveData
    {
        public int WorldWidth;
        public int WorldHeight;
        public int ChunkSize;
        public int[] UnlockedChunkIndices = Array.Empty<int>();
    }
}
