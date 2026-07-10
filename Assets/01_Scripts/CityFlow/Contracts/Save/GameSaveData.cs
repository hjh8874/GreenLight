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
        public ResearchSaveData Research;
        public ProgressionSaveData Progression;
        public GameCalendarSaveData Calendar;
    }
}
