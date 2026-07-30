using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Contracts
{
    public interface IBusLineService
    {
        IReadOnlyList<BusLineData> Lines { get; }
        int LineCount { get; }

        event Action<BusLineData> LineCreated;
        event Action<BusLineData> LineUpdated;
        event Action<BusLineData> LineRemoved;

        bool TryCreateLine(
            int routeId,
            IReadOnlyList<Vector2Int> orderedStops);

        bool TryUpdateLine(
            int routeId,
            IReadOnlyList<Vector2Int> orderedStops);

        bool TryRemoveLine(int routeId);

        bool TryGetLine(
            int routeId,
            out BusLineData line);

        bool TryBuildDirectionalRoute(
            int routeId,
            BusTravelDirection direction,
            out BusDirectionalRoute route);
    }

    // Unity integration: register one implementation through CityFlowServices.
}
