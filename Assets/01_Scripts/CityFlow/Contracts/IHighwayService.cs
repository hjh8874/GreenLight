using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Contracts
{
    public interface IHighwayService
    {
        IReadOnlyList<HighwayLink> HighwayLinks { get; }
        bool IsHighwayRamp(Vector2Int tile);
        bool CanSelectHighwayRamp(Vector2Int tile);
        bool CanPlaceHighway(Vector2Int a, Vector2Int b);
        bool TryPlaceHighway(Vector2Int a, Vector2Int b);
        bool TryRemoveHighway(Vector2Int ramp);
        int HighwayCost(Vector2Int a, Vector2Int b);
    }
}
