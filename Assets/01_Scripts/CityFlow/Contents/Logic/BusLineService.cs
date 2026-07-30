using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Content
{
    public sealed class BusLineService : IBusLineService
    {
        private readonly Dictionary<int, BusLineData> linesById = new();
        private readonly List<BusLineData> orderedLines = new();
        private readonly ReadOnlyCollection<BusLineData> readOnlyLines;

        public IReadOnlyList<BusLineData> Lines => readOnlyLines;
        public int LineCount => orderedLines.Count;

        public event Action<BusLineData> LineCreated;
        public event Action<BusLineData> LineUpdated;
        public event Action<BusLineData> LineRemoved;

        public BusLineService()
        {
            readOnlyLines = orderedLines.AsReadOnly();
        }

        public bool TryCreateLine(
            int routeId,
            IReadOnlyList<Vector2Int> orderedStops)
        {
            if (linesById.ContainsKey(routeId) ||
                !IsValidLine(routeId, orderedStops))
            {
                return false;
            }

            var line = new BusLineData(routeId, orderedStops);
            linesById.Add(routeId, line);
            orderedLines.Add(line);
            orderedLines.Sort(CompareByRouteId);
            LineCreated?.Invoke(line);
            return true;
        }

        public bool TryUpdateLine(
            int routeId,
            IReadOnlyList<Vector2Int> orderedStops)
        {
            if (!linesById.TryGetValue(routeId, out BusLineData current) ||
                !IsValidLine(routeId, orderedStops))
            {
                return false;
            }

            if (HasSameStops(current.OrderedStops, orderedStops))
            {
                return true;
            }

            var updated = new BusLineData(routeId, orderedStops);
            linesById[routeId] = updated;

            int index = orderedLines.FindIndex(
                candidate => candidate.RouteId == routeId);
            if (index >= 0)
            {
                orderedLines[index] = updated;
            }

            LineUpdated?.Invoke(updated);
            return true;
        }

        public bool TryRemoveLine(int routeId)
        {
            if (!linesById.TryGetValue(routeId, out BusLineData line))
            {
                return false;
            }

            linesById.Remove(routeId);
            orderedLines.Remove(line);
            LineRemoved?.Invoke(line);
            return true;
        }

        public bool TryGetLine(
            int routeId,
            out BusLineData line) =>
            linesById.TryGetValue(routeId, out line);

        public bool TryBuildDirectionalRoute(
            int routeId,
            BusTravelDirection direction,
            out BusDirectionalRoute route)
        {
            route = default;
            return TryGetLine(routeId, out BusLineData line) &&
                BusDirectionalRouteBuilder.TryBuild(
                    line,
                    direction,
                    out route);
        }

        private static bool IsValidLine(
            int routeId,
            IReadOnlyList<Vector2Int> orderedStops)
        {
            if (routeId <= 0 ||
                orderedStops == null ||
                orderedStops.Count < 2)
            {
                return false;
            }

            var uniqueStops = new HashSet<Vector2Int>();
            for (int index = 0; index < orderedStops.Count; index++)
            {
                if (!uniqueStops.Add(orderedStops[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasSameStops(
            IReadOnlyList<Vector2Int> left,
            IReadOnlyList<Vector2Int> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int index = 0; index < left.Count; index++)
            {
                if (left[index] != right[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static int CompareByRouteId(
            BusLineData left,
            BusLineData right) =>
            left.RouteId.CompareTo(right.RouteId);

        // Unity integration: CityBusService creates and registers this service automatically.
    }
}
