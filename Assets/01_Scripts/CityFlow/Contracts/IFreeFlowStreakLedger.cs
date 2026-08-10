using UnityEngine;

namespace CityFlow.Contracts
{
    public interface IFreeFlowStreakLedger
    {
        float GetBottleneckIntensity(Vector2Int tile);
    }
}
