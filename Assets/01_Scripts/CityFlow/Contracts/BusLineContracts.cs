using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Contracts
{
    public enum BusTravelDirection
    {
        Forward = 0,
        Reverse = 1
    }

    [Flags]
    public enum BusLineDirectionAvailability
    {
        None = 0,
        Forward = 1 << 0,
        Reverse = 1 << 1
    }

    public sealed class BusLineData
    {
        private static readonly IReadOnlyList<Vector2Int> EmptyStops =
            Array.AsReadOnly(Array.Empty<Vector2Int>());

        public int RouteId { get; private set; }
        public IReadOnlyList<Vector2Int> OrderedStops { get; private set; }
        public int StopCount => OrderedStops.Count;

        public BusLineData(
            int routeId,
            IReadOnlyList<Vector2Int> orderedStops)
        {
            RouteId = routeId;
            OrderedStops = CopyStops(orderedStops);
        }

        private static IReadOnlyList<Vector2Int> CopyStops(
            IReadOnlyList<Vector2Int> source)
        {
            if (source == null || source.Count == 0)
            {
                return EmptyStops;
            }

            var copy = new Vector2Int[source.Count];
            for (int index = 0; index < source.Count; index++)
            {
                copy[index] = source[index];
            }

            return Array.AsReadOnly(copy);
        }
    }

    public readonly struct BusDirectionalRoute
    {
        private static readonly IReadOnlyList<Vector2Int> EmptyStops =
            Array.AsReadOnly(Array.Empty<Vector2Int>());

        private readonly IReadOnlyList<Vector2Int> orderedStops;

        public int RouteId { get; }
        public BusTravelDirection Direction { get; }
        public IReadOnlyList<Vector2Int> OrderedStops =>
            orderedStops ?? EmptyStops;
        public int StopCount => OrderedStops.Count;
        public bool IsValid => RouteId > 0 && StopCount >= 2;

        public BusDirectionalRoute(
            int routeId,
            BusTravelDirection direction,
            IReadOnlyList<Vector2Int> stops)
        {
            RouteId = routeId;
            Direction = direction;
            orderedStops = CopyStops(stops);
        }

        private static IReadOnlyList<Vector2Int> CopyStops(
            IReadOnlyList<Vector2Int> source)
        {
            if (source == null || source.Count == 0)
            {
                return EmptyStops;
            }

            var copy = new Vector2Int[source.Count];
            for (int index = 0; index < source.Count; index++)
            {
                copy[index] = source[index];
            }

            return Array.AsReadOnly(copy);
        }
    }

    // Unity integration: consume these immutable contracts through IBusLineService.
}
