using System;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using CityFlow.Save;

namespace CityFlow.Bootstrap
{
    public sealed class CityFlowServices
    {
        public SimEventHub Events { get; }
        public IReadOnlyTileData TileData { get; }
        public IPlacementService Placement { get; }
        public IReadOnlyCityStats Stats { get; }
        public SaveService Save { get; }
        public IEconomyService Economy { get; private set; }
        public IGameCalendarService GameCalendar { get; private set; }
        public IWeeklyEconomyService WeeklyEconomy { get; private set; }

        public event Action<IEconomyService> EconomyRegistered;
        public event Action<IGameCalendarService> GameCalendarRegistered;
        public event Action<IWeeklyEconomyService> WeeklyEconomyRegistered;

        public CityFlowServices(
            SimEventHub events,
            IReadOnlyTileData tileData,
            IPlacementService placement,
            SaveService save = null,
            IEconomyService economy = null,
            IReadOnlyCityStats stats = null)
        {
            Events = events;
            TileData = tileData;
            Placement = placement;
            Stats = stats;
            Save = save;
            RegisterEconomy(economy);
        }

        public void RegisterEconomy(IEconomyService economy)
        {
            if (economy == null)
            {
                return;
            }

            Economy = economy;

            if (economy is IEconomySaveSource economySaveSource)
            {
                Save?.RegisterEconomySaveSource(economySaveSource);
            }

            EconomyRegistered?.Invoke(economy);
        }

        public void RegisterGameCalendar(IGameCalendarService gameCalendar)
        {
            if (gameCalendar == null)
            {
                return;
            }

            GameCalendar = gameCalendar;

            if (gameCalendar is IGameCalendarSaveSource gameCalendarSaveSource)
            {
                Save?.RegisterGameCalendarSaveSource(gameCalendarSaveSource);
            }

            GameCalendarRegistered?.Invoke(gameCalendar);
        }

        public void RegisterWeeklySettlementSaveSource(
            IWeeklySettlementSaveSource weeklySettlementSaveSource)
        {
            if (weeklySettlementSaveSource == null)
            {
                return;
            }

            Save?.RegisterWeeklySettlementSaveSource(weeklySettlementSaveSource);
        }

        public void RegisterWeeklyEconomy(IWeeklyEconomyService weeklyEconomy)
        {
            if (weeklyEconomy == null)
            {
                return;
            }

            WeeklyEconomy = weeklyEconomy;

            if (weeklyEconomy is IWeeklySettlementSaveSource saveSource)
            {
                Save?.RegisterWeeklySettlementSaveSource(saveSource);
            }

            WeeklyEconomyRegistered?.Invoke(weeklyEconomy);
        }

        public void RegisterResearchSaveSource(IResearchSaveSource researchSaveSource)
        {
            if (researchSaveSource == null)
            {
                return;
            }

            Save?.RegisterResearchSaveSource(researchSaveSource);
        }

        public void RegisterProgressionSaveSource(IProgressionSaveSource progressionSaveSource)
        {
            if (progressionSaveSource == null)
            {
                return;
            }

            Save?.RegisterProgressionSaveSource(progressionSaveSource);
        }

        public void RegisterRadioSaveSource(IRadioSaveSource radioSaveSource)
        {
            if (radioSaveSource == null)
            {
                return;
            }

            Save?.RegisterRadioSaveSource(radioSaveSource);
        }

        public void RegisterTerrainDecorationSaveSource(
            ITerrainDecorationSaveSource terrainDecorationSaveSource)
        {
            if (terrainDecorationSaveSource == null)
            {
                return;
            }

            Save?.RegisterTerrainDecorationSaveSource(
                terrainDecorationSaveSource);
        }
    }
}
