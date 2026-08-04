using UnityEngine;

namespace CityFlow.Contracts
{
    public interface ICongestionHistory
    {
        float CityJamRatio01 { get; }
        float LastDayJamRatio01(Vector2Int tile);
    }
}
