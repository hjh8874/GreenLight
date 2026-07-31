using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Contracts
{
    public readonly struct RoadRoutePlan
    {
        private static readonly IReadOnlyList<Vector2Int> EmptyTiles =
            Array.AsReadOnly(Array.Empty<Vector2Int>());

        private readonly IReadOnlyList<Vector2Int> tiles;

        public RoadRoutePlan(IReadOnlyList<Vector2Int> routeTiles)
        {
            if (routeTiles == null || routeTiles.Count == 0)
            {
                tiles = EmptyTiles;
                return;
            }

            var copy = new Vector2Int[routeTiles.Count];
            for (int i = 0; i < routeTiles.Count; i++)
            {
                copy[i] = routeTiles[i];
            }

            tiles = Array.AsReadOnly(copy);
        }

        public IReadOnlyList<Vector2Int> Tiles => tiles ?? EmptyTiles;
        public int TileCount => tiles?.Count ?? 0;
        public bool IsValid => TileCount > 0;
        public Vector2Int Origin => IsValid ? Tiles[0] : default;
        public Vector2Int Destination => IsValid
            ? Tiles[TileCount - 1]
            : default;
    }

    public interface IRoadRoutePlanningService
    {
        bool TryPlanRoadRoute(
            Vector2Int originRoad,
            Vector2Int destinationRoad,
            out RoadRoutePlan route);
    }

    // Unity integration: consume this service through CityFlowServices.
}
