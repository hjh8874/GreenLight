using System;

namespace CityFlow.Contracts.Save
{
    [Serializable]
    public sealed class SimSaveData
    {
        public TileSaveData[] PlacedTiles;
        public SignalSaveData[] SignalOffsets;
    }
}
