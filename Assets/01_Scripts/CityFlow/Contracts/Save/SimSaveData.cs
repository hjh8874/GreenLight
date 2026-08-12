using System;

namespace CityFlow.Contracts.Save
{
    [Serializable]
    public sealed class SimSaveData
    {
        public int GridWidth;
        public int GridHeight;
        public TileSaveData[] PlacedTiles;
        public SignalSaveData[] SignalOffsets;
        public RoundaboutSaveData[] Roundabouts;   // 구세이브 = null(로터리 0개) — 마이그레이션 공짜
        public OverpassSaveData[] Overpasses;      // 구세이브 = null(입체 0개) — 마이그레이션 공짜
        public OnewaySaveData[] Oneways;           // 구세이브 = null(일방통행 0개) — 마이그레이션 공짜
        public TurnSignSaveData[] TurnSigns;       // 구세이브 = null(턴 제한 표지판 0개) — 마이그레이션 공짜
        public PriorityRoadSaveData[] PriorityRoads;   // 구세이브 = null — 마이그레이션 공짜
        public HighwaySaveData[] Highways;
        public BusStopSaveData[] BusStops;
        public ConstructionSaveData[] Constructions;   // 구세이브 = null(공사 0건) — 마이그레이션 공짜
        public bool HasCarSimStats;
        public float CarTripSuccessRate;
        public int CarDayArrivalCount;
        // 구 세이브는 false다. 오늘 누적치를 어제 확정치로 오인하지 않게 구분한다.
        public bool HasCarLastDayArrivalCount;
        public int CarLastDayArrivalCount;
        public bool CarSkipCurrentDay;
    }
}
