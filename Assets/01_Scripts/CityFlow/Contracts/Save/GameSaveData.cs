using System;

namespace CityFlow.Contracts.Save
{
    [Serializable]
    public sealed class GameSaveData
    {
        public int SaveVersion;
        public long SavedAtUtcTicks;
        public int GridWidth;
        public int GridHeight;
        public SimSaveData Simulation;
        public EconomySaveData Economy;
        public WeeklySettlementSaveData WeeklySettlement;
        public ResearchSaveData Research;
        public ProgressionSaveData Progression;
        public GameCalendarSaveData Calendar;
        public RadioSaveData Radio;
        public TerrainDecorationSaveData TerrainDecorations;
        public WorldGridSaveData WorldGrid;
        public SpecialBuildingSaveData SpecialBuildings;
        public SpecialBuildingVisitSaveData SpecialBuildingVisits;
    }
}
