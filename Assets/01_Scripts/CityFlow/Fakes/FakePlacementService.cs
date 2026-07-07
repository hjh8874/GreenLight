using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Fakes
{
    public sealed class FakePlacementService : IPlacementService
    {
        private readonly int width;
        private readonly int height;
        private readonly Dictionary<Vector2Int, TileType> placedTypes = new Dictionary<Vector2Int, TileType>();
        private CityFlowServices services;

        public FakePlacementService(int width, int height)
        {
            this.width = Mathf.Max(1, width);
            this.height = Mathf.Max(1, height);
        }

        public void Initialize(CityFlowServices services)
        {
            this.services = services;
        }

        public bool CanPlace(Vector2Int tile, TileType type)
        {
            return GridUtil.IsInside(tile, width, height) && type != TileType.Empty;
        }

        public bool Place(Vector2Int tile, TileType type)
        {
            if (!CanPlace(tile, type))
            {
                return false;
            }

            services?.Events.Publish(new PlacedEvent(tile, type, false));
            placedTypes[tile] = type;
            return true;
        }

        public bool Remove(Vector2Int tile)
        {
            if (!GridUtil.IsInside(tile, width, height))
            {
                return false;
            }

            placedTypes.TryGetValue(tile, out TileType removed);
            placedTypes.Remove(tile);
            services?.Events.Publish(new PlacedEvent(tile, removed, true));
            return true;
        }
    }
}
