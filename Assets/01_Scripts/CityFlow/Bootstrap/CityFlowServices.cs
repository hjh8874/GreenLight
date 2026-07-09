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
        public SaveService Save { get; }
        public IEconomyService Economy { get; private set; }

        public event Action<IEconomyService> EconomyRegistered;

        public CityFlowServices(
            SimEventHub events,
            IReadOnlyTileData tileData,
            IPlacementService placement,
            SaveService save = null,
            IEconomyService economy = null)
        {
            Events = events;
            TileData = tileData;
            Placement = placement;
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
    }
}
