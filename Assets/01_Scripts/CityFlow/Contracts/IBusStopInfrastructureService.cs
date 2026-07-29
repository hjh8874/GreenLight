using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Contracts
{
    /// <summary>
    /// Authoritative placement state for purchasable city-bus stops.
    /// Stops occupy an empty roadside tile and are persisted with simulation data.
    /// General placement cannot overlap an installed stop. A road can be removed
    /// only when every adjacent stop keeps at least one other access road.
    /// </summary>
    public interface IBusStopInfrastructureService
    {
        IReadOnlyList<Vector2Int> BusStopTiles { get; }
        bool CanPlaceBusStop(Vector2Int tile);
        bool TryPlaceBusStop(Vector2Int tile);
        bool TryRemoveBusStop(Vector2Int tile);
    }

    /// <summary>
    /// Shared placement rule for a stop that a right-driving bus can approach
    /// without crossing into the opposite lane.
    /// </summary>
    public static class BusStopInfrastructurePolicy
    {
        private static readonly Vector2Int[] Directions =
        {
            Vector2Int.up,
            Vector2Int.right,
            Vector2Int.down,
            Vector2Int.left
        };

        public static bool HasRoadsideApproach(
            Vector2Int stopTile,
            Func<Vector2Int, bool> isRoad)
        {
            if (isRoad == null)
            {
                return false;
            }

            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2Int accessRoad =
                    stopTile + Directions[i];
                if (!isRoad(accessRoad))
                {
                    continue;
                }

                Vector2Int stopSide =
                    stopTile - accessRoad;
                Vector2Int arrivalDirection = new(
                    -stopSide.y,
                    stopSide.x);
                Vector2Int predecessor =
                    accessRoad - arrivalDirection;

                if (isRoad(predecessor))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
