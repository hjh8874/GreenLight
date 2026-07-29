using UnityEngine;

namespace CityFlow.ViewKit
{
    public static class VehicleSpacingMath
    {
        public static int CalculateLookaheadTiles(
            float speed,
            float brakeAcceleration,
            float reactionSeconds,
            float minimumHeadway,
            float tileSize,
            int minimumTiles = 2,
            int maximumTiles = 3)
        {
            float safeSpeed = Mathf.Max(0f, speed);
            float safeBrakeAcceleration = Mathf.Max(0.0001f, brakeAcceleration);
            float brakingDistance = safeSpeed * safeSpeed / (2f * safeBrakeAcceleration);
            float reactionDistance = safeSpeed * Mathf.Max(0f, reactionSeconds);
            float requiredDistance = brakingDistance + reactionDistance + Mathf.Max(0f, minimumHeadway);
            int requiredTiles = Mathf.CeilToInt(requiredDistance / Mathf.Max(0.0001f, tileSize));
            int lowerBound = Mathf.Max(0, minimumTiles);
            int upperBound = Mathf.Max(lowerBound, maximumTiles);
            return Mathf.Clamp(requiredTiles, lowerBound, upperBound);
        }

        public static float LimitAdvance(float proposedAdvance, float headway, float minimumHeadway)
        {
            float availableDistance = Mathf.Max(0f, headway - Mathf.Max(0f, minimumHeadway));
            return Mathf.Min(Mathf.Max(0f, proposedAdvance), availableDistance);
        }

        public static bool IsSameFlowDirection(
            Vector3 firstDirection,
            Vector3 secondDirection,
            float minimumDot = 0.5f)
        {
            if (firstDirection.sqrMagnitude <= 0.0001f ||
                secondDirection.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            return Vector3.Dot(
                       firstDirection.normalized,
                       secondDirection.normalized) >
                   Mathf.Clamp(minimumDot, -1f, 1f);
        }

        public static float ClampCorridorToForwardProgress(
            float currentDistance,
            float authorizedDistance)
        {
            return Mathf.Max(
                Mathf.Max(0f, currentDistance),
                Mathf.Max(0f, authorizedDistance));
        }
    }
}
