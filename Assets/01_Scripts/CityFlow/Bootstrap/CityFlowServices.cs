using CityFlow.Contracts;
using CityFlow.Save;

namespace CityFlow.Bootstrap
{
    public sealed class CityFlowServices
    {
        public SimEventHub Events { get; }
        public IReadOnlyTileData TileData { get; }
        public IPlacementService Placement { get; }
        public SaveService Save { get; }

        public CityFlowServices(
            SimEventHub events,
            IReadOnlyTileData tileData,
            IPlacementService placement,
            SaveService save = null)
        {
            Events = events;
            TileData = tileData;
            Placement = placement;
            Save = save;
        }
    }
}
