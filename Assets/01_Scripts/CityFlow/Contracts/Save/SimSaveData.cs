using System;

namespace CityFlow.Contracts.Save
{
    [Serializable]
    public sealed class SimSaveData
    {
        public TileSaveData[] PlacedTiles;
        public SignalSaveData[] SignalOffsets;
        public RoundaboutSaveData[] Roundabouts;   // 구세이브 = null(로터리 0개) — 마이그레이션 공짜
    }
}
