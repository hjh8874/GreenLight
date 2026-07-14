using UnityEngine;

namespace CityFlow.Contracts
{
    public interface IRouteDistanceProvider
    {
        bool TryGetAverageRouteDistance(
            Vector2Int destination,
            out float distanceTiles);

        bool TryGetCityAverageRouteDistance(out float distanceTiles);
    }
}
