using UnityEngine;

namespace CityFlow.Contracts
{
    public interface ICongestionHistory
    {
        float LastDayJamRatio01(Vector2Int tile);
    }
}
