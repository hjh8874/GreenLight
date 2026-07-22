using UnityEngine;

namespace CityFlow.ViewKit
{
    public static class SignalStopLineMath
    {
        public static float CalculateStopDistance(
            float approachCenterDistance,
            float signalCenterDistance,
            float vehicleLength,
            float safetyMargin)
        {
            float entryBoundary = Mathf.Lerp(
                approachCenterDistance,
                signalCenterDistance,
                0.5f);
            float frontClearance = Mathf.Max(0f, vehicleLength) * 0.5f
                + Mathf.Max(0f, safetyMargin);
            return Mathf.Max(0f, entryBoundary - frontClearance);
        }
    }
}
