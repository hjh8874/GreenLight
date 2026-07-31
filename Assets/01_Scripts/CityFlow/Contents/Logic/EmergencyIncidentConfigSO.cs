using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Content
{
    [CreateAssetMenu(
        fileName = "EmergencyIncidentConfig",
        menuName =
            "CityFlow/Emergency/Incident Config")]
    public sealed class EmergencyIncidentConfigSO :
        ScriptableObject
    {
        [Header("Spawn")]
        [SerializeField, Min(0.1f)]
        private float minimumSpawnInterval = 10f;
        [SerializeField, Min(0.1f)]
        private float maximumSpawnInterval = 20f;
        [SerializeField, Min(1)]
        private int minimumDispatchIntervalDays = 1;
        [SerializeField, Min(1)]
        private int maximumDispatchIntervalDays = 3;
        [SerializeField, Min(1)]
        private int maximumActiveIncidents = 3;
        [SerializeField, Min(1)]
        private int maximumAutomaticIncidentsPerDay = 1;
        [SerializeField]
        private EmergencyIncidentDefinitionSO[] incidentDefinitions;
        [SerializeField, Range(0f, 1f)]
        private float houseWeight = 0.7f;
        [SerializeField, Range(0f, 1f)]
        private float officeWeight = 0.3f;
        [SerializeField, Range(0f, 1f)]
        private float schoolWeight = 0.2f;
        [SerializeField, Range(0f, 1f)]
        private float specialBuildingWeight = 0.1f;
        [SerializeField, Min(0)]
        private int recentTargetHistorySize = 3;

        [Header("Ambulance Flow")]
        [SerializeField, Min(0.01f)]
        private float travelSecondsPerTile = 0.45f;
        [SerializeField, Min(0.01f)]
        private float treatmentSeconds = 2f;
        [SerializeField, Min(1)]
        private int ambulancesPerHospital = 2;
        [SerializeField, Min(0.1f)]
        private float routeRetrySeconds = 2f;
        [SerializeField, Min(1)]
        private int maximumOutboundRouteRetries = 3;
        [SerializeField, Min(1)]
        private int maximumReturnRouteRetries = 5;

        [Header("Ambulance Presentation")]
        [SerializeField]
        private GameObject vehicleVisualPrefab;
        [SerializeField, Min(0.01f)]
        private float visualScale = 0.085f;
        [SerializeField]
        private float visualDepth = -0.38f;
        [SerializeField, Min(0.05f)]
        private float vehicleLengthTiles = 0.56f;
        [SerializeField, Min(0.05f)]
        private float vehicleWidthTiles = 0.24f;

        public float MinimumSpawnInterval =>
            Mathf.Max(0.1f, minimumSpawnInterval);
        public float MaximumSpawnInterval =>
            Mathf.Max(
                MinimumSpawnInterval,
                maximumSpawnInterval);
        public int MinimumDispatchIntervalDays =>
            Mathf.Max(1, minimumDispatchIntervalDays);
        public int MaximumDispatchIntervalDays =>
            Mathf.Max(
                MinimumDispatchIntervalDays,
                maximumDispatchIntervalDays);
        public int MaximumActiveIncidents =>
            Mathf.Max(1, maximumActiveIncidents);
        public int MaximumAutomaticIncidentsPerDay =>
            Mathf.Max(1, maximumAutomaticIncidentsPerDay);
        public IReadOnlyList<EmergencyIncidentDefinitionSO>
            IncidentDefinitions =>
                incidentDefinitions ??
                System.Array.Empty<
                    EmergencyIncidentDefinitionSO>();
        public float HouseWeight =>
            Mathf.Clamp01(houseWeight);
        public float OfficeWeight =>
            Mathf.Clamp01(officeWeight);
        public float SchoolWeight =>
            Mathf.Clamp01(schoolWeight);
        public float SpecialBuildingWeight =>
            Mathf.Clamp01(specialBuildingWeight);
        public int RecentTargetHistorySize =>
            Mathf.Max(0, recentTargetHistorySize);
        public float TravelSecondsPerTile =>
            Mathf.Max(0.01f, travelSecondsPerTile);
        public float TreatmentSeconds =>
            Mathf.Max(0.01f, treatmentSeconds);
        public int AmbulancesPerHospital =>
            Mathf.Max(1, ambulancesPerHospital);
        public float RouteRetrySeconds =>
            Mathf.Max(0.1f, routeRetrySeconds);
        public int MaximumOutboundRouteRetries =>
            Mathf.Max(1, maximumOutboundRouteRetries);
        public int MaximumReturnRouteRetries =>
            Mathf.Max(1, maximumReturnRouteRetries);
        public GameObject VehicleVisualPrefab =>
            vehicleVisualPrefab;
        public float VisualScale =>
            Mathf.Max(0.01f, visualScale);
        public float VisualDepth => visualDepth;
        public float VehicleLengthTiles =>
            Mathf.Max(0.05f, vehicleLengthTiles);
        public float VehicleWidthTiles =>
            Mathf.Max(0.05f, vehicleWidthTiles);

#if UNITY_EDITOR
        private void OnValidate()
        {
            minimumSpawnInterval = Mathf.Max(
                0.1f,
                minimumSpawnInterval);
            maximumSpawnInterval = Mathf.Max(
                minimumSpawnInterval,
                maximumSpawnInterval);
            minimumDispatchIntervalDays = Mathf.Max(
                1,
                minimumDispatchIntervalDays);
            maximumDispatchIntervalDays = Mathf.Max(
                minimumDispatchIntervalDays,
                maximumDispatchIntervalDays);
            maximumActiveIncidents = Mathf.Max(
                1,
                maximumActiveIncidents);
            maximumAutomaticIncidentsPerDay =
                Mathf.Max(
                    1,
                    maximumAutomaticIncidentsPerDay);
            houseWeight = Mathf.Clamp01(houseWeight);
            officeWeight = Mathf.Clamp01(officeWeight);
            schoolWeight = Mathf.Clamp01(schoolWeight);
            specialBuildingWeight =
                Mathf.Clamp01(specialBuildingWeight);
            recentTargetHistorySize = Mathf.Max(
                0,
                recentTargetHistorySize);
            travelSecondsPerTile = Mathf.Max(
                0.01f,
                travelSecondsPerTile);
            treatmentSeconds = Mathf.Max(
                0.01f,
                treatmentSeconds);
            ambulancesPerHospital = Mathf.Max(
                1,
                ambulancesPerHospital);
            routeRetrySeconds = Mathf.Max(
                0.1f,
                routeRetrySeconds);
            maximumOutboundRouteRetries = Mathf.Max(
                1,
                maximumOutboundRouteRetries);
            maximumReturnRouteRetries = Mathf.Max(
                1,
                maximumReturnRouteRetries);
            visualScale = Mathf.Max(
                0.01f,
                visualScale);
            vehicleLengthTiles = Mathf.Max(
                0.05f,
                vehicleLengthTiles);
            vehicleWidthTiles = Mathf.Max(
                0.05f,
                vehicleWidthTiles);
        }
#endif
    }
}
