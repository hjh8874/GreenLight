using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Contracts
{
    /// <summary>
    /// Authoritative placement state for purchasable city-bus stops.
    /// Stops occupy an empty roadside tile and are persisted with simulation data.
    /// </summary>
    public interface IBusStopInfrastructureService
    {
        IReadOnlyList<Vector2Int> BusStopTiles { get; }
        bool CanPlaceBusStop(Vector2Int tile);
        bool TryPlaceBusStop(Vector2Int tile);
        bool TryRemoveBusStop(Vector2Int tile);
    }
}
