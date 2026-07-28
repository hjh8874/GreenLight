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
        public int RoadCapacityPurchases;              // 도로 확장권 구매횟수. 구세이브 = 0(미구매) — 마이그레이션 공짜
        public bool HasCarSimStats;
        public float CarTripSuccessRate;
        public int CarDayArrivalCount;
        public bool CarSkipCurrentDay;
    }
}
