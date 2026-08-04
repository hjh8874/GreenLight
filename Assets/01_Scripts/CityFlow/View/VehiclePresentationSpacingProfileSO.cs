using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.View
{
    [CreateAssetMenu(
        fileName = "VehiclePresentationSpacingProfile",
        menuName = "CityFlow/Traffic/Vehicle Presentation Spacing Profile")]
    public sealed class VehiclePresentationSpacingProfileSO : ScriptableObject
    {
        public const float DefaultStandardExtraGapTiles = 0.04f;
        public const float DefaultLargeExtraGapTiles = 0.08f;
        public const float DefaultEmergencyExtraGapTiles = 0.06f;
        public const float DefaultLaneToleranceTiles = 0.02f;
        public const float DefaultHeightToleranceTiles = 0.3f;
        public const float DefaultMinimumDirectionDot = 0.6f;
        public const float DefaultMaximumCatchUpSpeedMultiplier = 1.15f;
        public const int DefaultStaleFrameTolerance = 2;

        [SerializeField, Min(0f)]
        private float standardExtraGapTiles =
            DefaultStandardExtraGapTiles;

        [SerializeField, Min(0f)]
        private float largeExtraGapTiles =
            DefaultLargeExtraGapTiles;

        [SerializeField, Min(0f)]
        private float emergencyExtraGapTiles =
            DefaultEmergencyExtraGapTiles;

        [SerializeField, Min(0f)]
        private float laneToleranceTiles =
            DefaultLaneToleranceTiles;

        [SerializeField, Min(0f)]
        private float heightToleranceTiles =
            DefaultHeightToleranceTiles;

        [SerializeField, Range(-1f, 1f)]
        private float minimumDirectionDot =
            DefaultMinimumDirectionDot;

        [SerializeField, Range(1f, 2f)]
        private float maximumCatchUpSpeedMultiplier =
            DefaultMaximumCatchUpSpeedMultiplier;

        [SerializeField, Min(1)]
        private int staleFrameTolerance =
            DefaultStaleFrameTolerance;

        public float StandardExtraGapTiles =>
            Mathf.Max(0f, standardExtraGapTiles);
        public float LargeExtraGapTiles =>
            Mathf.Max(0f, largeExtraGapTiles);
        public float EmergencyExtraGapTiles =>
            Mathf.Max(0f, emergencyExtraGapTiles);
        public float LaneToleranceTiles =>
            Mathf.Max(0f, laneToleranceTiles);
        public float HeightToleranceTiles =>
            Mathf.Max(0f, heightToleranceTiles);
        public float MinimumDirectionDot =>
            Mathf.Clamp(minimumDirectionDot, -1f, 1f);
        public float MaximumCatchUpSpeedMultiplier =>
            Mathf.Clamp(maximumCatchUpSpeedMultiplier, 1f, 2f);
        public int StaleFrameTolerance =>
            Mathf.Max(1, staleFrameTolerance);

        public float GetExtraGapTiles(
            RoadTrafficAgentKind kind,
            VehicleSizeClass sizeClass)
        {
            if (kind == RoadTrafficAgentKind.FeatureVehicle)
            {
                return EmergencyExtraGapTiles;
            }

            if (kind == RoadTrafficAgentKind.CityBus ||
                kind == RoadTrafficAgentKind.SchoolBus ||
                sizeClass == VehicleSizeClass.Large)
            {
                return LargeExtraGapTiles;
            }

            return StandardExtraGapTiles;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            standardExtraGapTiles = Mathf.Max(
                0f,
                standardExtraGapTiles);
            largeExtraGapTiles = Mathf.Max(
                0f,
                largeExtraGapTiles);
            emergencyExtraGapTiles = Mathf.Max(
                0f,
                emergencyExtraGapTiles);
            laneToleranceTiles = Mathf.Max(
                0f,
                laneToleranceTiles);
            heightToleranceTiles = Mathf.Max(
                0f,
                heightToleranceTiles);
            minimumDirectionDot = Mathf.Clamp(
                minimumDirectionDot,
                -1f,
                1f);
            maximumCatchUpSpeedMultiplier = Mathf.Clamp(
                maximumCatchUpSpeedMultiplier,
                1f,
                2f);
            staleFrameTolerance = Mathf.Max(
                1,
                staleFrameTolerance);
        }
#endif

        // Unity setup: place the configured asset at Resources/CityFlow/VehiclePresentationSpacingProfile.
    }
}
