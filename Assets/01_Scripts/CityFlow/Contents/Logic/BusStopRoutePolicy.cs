using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Content
{
    /// <summary>
    /// Selects the station served when a bus reaches a station access road.
    /// The scheduled stop wins when multiple stations share the same road;
    /// otherwise any pass-by station is served instead of being skipped.
    /// </summary>
    public static class BusStopRoutePolicy
    {
        public static int FindStopIndexAtRoad(
            IReadOnlyList<Vector2Int> accessRoads,
            int currentStopIndex,
            int scheduledStopIndex,
            Vector2Int roadTile)
        {
            if (accessRoads == null || accessRoads.Count == 0)
            {
                return -1;
            }

            if (IsEligibleStop(
                    accessRoads,
                    scheduledStopIndex,
                    currentStopIndex,
                    roadTile))
            {
                return scheduledStopIndex;
            }

            for (int i = 0; i < accessRoads.Count; i++)
            {
                if (i == scheduledStopIndex)
                {
                    continue;
                }

                if (IsEligibleStop(
                        accessRoads,
                        i,
                        currentStopIndex,
                        roadTile))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsEligibleStop(
            IReadOnlyList<Vector2Int> accessRoads,
            int candidateIndex,
            int currentStopIndex,
            Vector2Int roadTile)
        {
            return candidateIndex >= 0 &&
                   candidateIndex < accessRoads.Count &&
                   candidateIndex != currentStopIndex &&
                   accessRoads[candidateIndex] == roadTile;
        }
    }
}
