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

        public event Action<IEconomyService> EconomyRegistered;
        public event Action<IGameCalendarService> GameCalendarRegistered;

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
            GameCalendarRegistered?.Invoke(gameCalendar);
        }
    }
}
