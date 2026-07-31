using System.Collections.Generic;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Content
{
    public static class BusDirectionalRouteBuilder
    {
        public static bool TryBuild(
            BusLineData line,
            BusTravelDirection direction,
            out BusDirectionalRoute route)
        {
            route = default;

            if (line == null ||
                line.RouteId <= 0 ||
                line.StopCount < 2 ||
                !IsSupportedDirection(direction))
            {
                return false;
            }

            IReadOnlyList<Vector2Int> source = line.OrderedStops;
            var orderedStops = new Vector2Int[source.Count];

            for (int index = 0; index < source.Count; index++)
            {
                int sourceIndex = direction ==
                    BusTravelDirection.Forward
                        ? index
                        : source.Count - 1 - index;
                orderedStops[index] = source[sourceIndex];
            }

            route = new BusDirectionalRoute(
                line.RouteId,
                direction,
                orderedStops);
            return route.IsValid;
        }

        private static bool IsSupportedDirection(
            BusTravelDirection direction) =>
            direction is BusTravelDirection.Forward
                or BusTravelDirection.Reverse;

        // Unity integration: no component is required; IBusLineService uses this builder.
    }
}
