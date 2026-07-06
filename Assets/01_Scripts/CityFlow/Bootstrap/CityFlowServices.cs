using CityFlow.Contracts;

namespace CityFlow.Bootstrap
{
    public sealed class CityFlowServices
    {
        public SimEventHub Events { get; }
        public IReadOnlyTileData TileData { get; }
        public IPlacementService Placement { get; }

        public CityFlowServices(
            SimEventHub events,
            IReadOnlyTileData tileData,
            IPlacementService placement)
        {
            Events = events;
            TileData = tileData;
            Placement = placement;
        }
    }
}
