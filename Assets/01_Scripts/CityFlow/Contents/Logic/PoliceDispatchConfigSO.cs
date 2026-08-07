using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Content
{
    [CreateAssetMenu(
        fileName = "PoliceDispatchConfig",
        menuName = "CityFlow/Police/Dispatch Config")]
    public sealed class PoliceDispatchConfigSO :
        ScriptableObject,
        IResponseVehiclePresentationConfig
    {
        [Header("Fleet")]
        [SerializeField, Min(1)]
        private int vehiclesPerStation = 2;

        [SerializeField, Min(1)]
        private int maximumActiveCalls = 8;

        [Header("Response")]
        [SerializeField, Min(0.01f)]
        private float travelSecondsPerTile = 0.45f;

        [SerializeField, Min(0.01f)]
        private float defaultHandlingSeconds = 2f;

        [SerializeField, Min(0.1f)]
        private float routeRetrySeconds = 2f;

        [SerializeField, Min(1)]
        private int maximumOutboundRouteRetries = 3;

        [SerializeField, Min(1)]
        private int maximumReturnRouteRetries = 5;

        [Header("Traffic")]
        [SerializeField]
        private VehicleFootprintProfileSO vehicleFootprintProfile;

        [Header("Presentation")]
        [SerializeField]
        private GameObject vehicleVisualPrefab;

        [SerializeField, Min(0.01f)]
        private float visualScale = 1f;

        [SerializeField]
        private float visualDepth = -0.38f;

        public string VehicleDisplayName => "PoliceCar";
        public int VehiclesPerStation =>
            Mathf.Max(1, vehiclesPerStation);
        public int MaximumActiveCalls =>
            Mathf.Max(1, maximumActiveCalls);
        public float TravelSecondsPerTile =>
            Mathf.Max(0.01f, travelSecondsPerTile);
        public float DefaultHandlingSeconds =>
            Mathf.Max(0.01f, defaultHandlingSeconds);
        public float RouteRetrySeconds =>
            Mathf.Max(0.1f, routeRetrySeconds);
        public int MaximumOutboundRouteRetries =>
            Mathf.Max(1, maximumOutboundRouteRetries);
        public int MaximumReturnRouteRetries =>
            Mathf.Max(1, maximumReturnRouteRetries);
        public VehicleFootprint VehicleFootprint =>
            vehicleFootprintProfile != null
                ? vehicleFootprintProfile.Footprint
                : VehicleFootprint.StandardDefault;
        public VehicleFootprintProfileSO VehicleFootprintProfile =>
            vehicleFootprintProfile;
        public GameObject VehicleVisualPrefab =>
            vehicleVisualPrefab;
        public float VisualScale =>
            Mathf.Max(0.01f, visualScale);
        public float VisualDepth => visualDepth;

#if UNITY_EDITOR
        private void OnValidate()
        {
            vehiclesPerStation = Mathf.Max(1, vehiclesPerStation);
            maximumActiveCalls = Mathf.Max(1, maximumActiveCalls);
            travelSecondsPerTile = Mathf.Max(
                0.01f,
                travelSecondsPerTile);
            defaultHandlingSeconds = Mathf.Max(
                0.01f,
                defaultHandlingSeconds);
            routeRetrySeconds = Mathf.Max(0.1f, routeRetrySeconds);
            maximumOutboundRouteRetries = Mathf.Max(
                1,
                maximumOutboundRouteRetries);
            maximumReturnRouteRetries = Mathf.Max(
                1,
                maximumReturnRouteRetries);
            visualScale = Mathf.Max(0.01f, visualScale);
        }
#endif
    }
}
