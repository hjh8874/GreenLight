using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Content.Transit;
using CityFlow.Contracts;
using CityFlow.View;
using UnityEngine;

namespace CityFlow.Content
{
    [RequireComponent(typeof(BusRoute))]
    public sealed class AmbulanceVehicleAgent :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        private enum TravelStage
        {
            None = 0,
            Outbound = 1,
            Returning = 2
        }

        [SerializeField]
        private BusRoute route;
        [SerializeField]
        private EmergencyIncidentConfigSO config;
        [SerializeField]
        private AmbulanceWorldView worldView;

        private readonly List<Vector2Int> routeStops = new(2);

        private CityFlowServices services;
        private EmergencyIncidentSystem incidentSystem;
        private EmergencyIncident incident;
        private IReadOnlyTileData tileData;
        private TravelStage stage;
        private float retryRemainingSeconds;
        private bool initialized;
        private bool subscribed;
        private bool assigned;
        private Vector2Int homeHospital;
        private int hospitalParkingSlot;
        private bool hasHomeHospital;

        public int IncidentId =>
            incident?.IncidentId ?? -1;
        public bool IsAssigned => assigned;
        public bool HasHomeHospital => hasHomeHospital;
        public Vector2Int HomeHospital => homeHospital;
        public EmergencyIncidentConfigSO Config => config;

        public void Initialize(CityFlowServices cityServices)
        {
            if (initialized)
            {
                return;
            }

            ResolveReferences();

            if (cityServices?.TileData == null ||
                route == null ||
                config == null ||
                worldView == null)
            {
                Debug.LogError(
                    "[AmbulanceVehicleAgent] Services, route, config, and world view are required.",
                    this);
                return;
            }

            services = cityServices;
            tileData = services.TileData;
            route.ConfigureRoadTrafficAgent(
                RoadTrafficAgentKind.FeatureVehicle,
                new VehicleFootprint(
                    VehicleSizeClass.Standard,
                    config.VehicleLengthTiles,
                    config.VehicleWidthTiles,
                    0.11f),
                holdAtDestination: true);
            route.Initialize(services);
            worldView.Initialize(services);
            initialized = true;
            Subscribe();
        }

        public bool PrepareAtHospital(
            Vector2Int hospital,
            int parkingSlot)
        {
            if (!initialized)
            {
                return false;
            }

            homeHospital = hospital;
            hospitalParkingSlot =
                Mathf.Max(0, parkingSlot);
            hasHomeHospital = true;
            assigned = false;
            stage = TravelStage.None;
            retryRemainingSeconds = 0f;
            route.StopRoute();
            worldView.ShowParkedAtHospital(
                homeHospital,
                hospitalParkingSlot,
                immediate: true);
            return true;
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Update()
        {
            if (!assigned ||
                retryRemainingSeconds <= 0f)
            {
                return;
            }

            retryRemainingSeconds -=
                Mathf.Max(0f, Time.deltaTime);

            if (retryRemainingSeconds <= 0f)
            {
                StartCurrentStage(
                    preferCurrentRoad: true);
            }
        }

        public bool Assign(
            EmergencyIncident targetIncident,
            EmergencyIncidentSystem owner)
        {
            if (!initialized ||
                targetIncident == null ||
                owner == null ||
                !hasHomeHospital ||
                targetIncident.AssignedHospital !=
                    homeHospital ||
                targetIncident.State !=
                    EmergencyIncidentState.AmbulanceOutbound)
            {
                return false;
            }

            incident = targetIncident;
            incidentSystem = owner;
            assigned = true;
            stage = TravelStage.Outbound;
            retryRemainingSeconds = 0f;
            ConfigureRouteDefaults();
            worldView.PrepareRoadsideIncidentStop();
            StartCurrentStage(
                preferCurrentRoad: false);
            return true;
        }

        public void BeginReturn()
        {
            if (!assigned ||
                incident == null ||
                incident.State !=
                    EmergencyIncidentState.AmbulanceReturning ||
                (stage == TravelStage.Returning &&
                 (route.State == BusRouteState.Moving ||
                  route.State ==
                      BusRouteState.WaitingAtStop)))
            {
                return;
            }

            stage = TravelStage.Returning;
            retryRemainingSeconds = 0f;
            worldView.PrepareRoadsideDeparture();
            StartCurrentStage(
                preferCurrentRoad: true);
        }

        public void Release()
        {
            assigned = false;
            retryRemainingSeconds = 0f;
            stage = TravelStage.None;
            route?.StopRoute();
            incident = null;
            incidentSystem = null;

            if (hasHomeHospital)
            {
                worldView.ShowParkedAtHospital(
                    homeHospital,
                    hospitalParkingSlot,
                    immediate: true);
            }
        }

        private void ResolveReferences()
        {
            route ??= GetComponent<BusRoute>();
            worldView ??=
                GetComponent<AmbulanceWorldView>();
        }

        private void Subscribe()
        {
            if (subscribed || route == null)
            {
                return;
            }

            route.StopArrived += HandleStopArrived;
            route.RouteUnavailable +=
                HandleRouteUnavailable;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || route == null)
            {
                return;
            }

            route.StopArrived -= HandleStopArrived;
            route.RouteUnavailable -=
                HandleRouteUnavailable;
            subscribed = false;
        }

        private void ConfigureRouteDefaults()
        {
            route.LoopRoute = false;
            route.SecondsPerTile =
                config.TravelSecondsPerTile;
            route.StopWaitSeconds =
                config.TreatmentSeconds;
            // Emergency destinations use the same right-lane road geometry
            // as traffic, but stop before the building parking entrance so
            // the ambulance cannot cross parked or departing vehicles.
            route.UseRoadsideStopApproach = true;
            route.RoadsideStopSetbackTiles = 1;
            route.RoadsideStopFilter =
                IsRoadsideDestination;
        }

        private bool StartCurrentStage(
            bool preferCurrentRoad)
        {
            if (!assigned ||
                incident == null ||
                route == null)
            {
                return false;
            }

            Vector2Int destination =
                stage == TravelStage.Outbound
                    ? incident.Location
                    : incident.AssignedHospital;

            bool configured = false;
            bool configuredFromCurrentRoad = false;

            if (preferCurrentRoad &&
                IsRoad(route.CurrentTile))
            {
                routeStops.Clear();
                routeStops.Add(destination);
                configured =
                    route.ReconfigureLoopFromCurrentPosition(
                        route.CurrentTile,
                        routeStops);
                configuredFromCurrentRoad = configured;
            }

            if (!configured)
            {
                routeStops.Clear();

                if (stage == TravelStage.Outbound)
                {
                    routeStops.Add(
                        incident.AssignedHospital);
                    routeStops.Add(incident.Location);
                    configured =
                        route.ConfigureRoute(
                            routeStops,
                            shouldLoop: false);
                }
                else
                {
                    routeStops.Add(incident.Location);
                    routeStops.Add(
                        incident.AssignedHospital);
                    configured =
                        route.ConfigureRoute(
                            routeStops,
                            shouldLoop: false);
                }
            }

            if (!configured || !route.StartRoute())
            {
                ScheduleRetry();
                return false;
            }

            if (configuredFromCurrentRoad)
            {
                route.LoopRoute = false;
            }

            retryRemainingSeconds = 0f;
            return true;
        }

        private bool IsRoadsideDestination(
            Vector2Int stop)
        {
            return assigned &&
                   stage == TravelStage.Outbound &&
                   incident != null &&
                   stop == incident.Location;
        }

        private bool IsRoad(Vector2Int tile)
        {
            return tileData != null &&
                   tileData.GetTileType(tile) ==
                   TileType.Road;
        }

        private void HandleStopArrived(
            Vector2Int stop,
            int _)
        {
            if (!assigned ||
                incident == null ||
                incidentSystem == null)
            {
                return;
            }

            if (stage == TravelStage.Outbound &&
                stop == incident.Location)
            {
                incidentSystem.TryMarkAmbulanceArrived(
                    incident.IncidentId);
                return;
            }

            if (stage == TravelStage.Returning &&
                stop == incident.AssignedHospital)
            {
                int returningIncidentId =
                    incident.IncidentId;
                worldView.ShowParkedAtHospital(
                    homeHospital,
                    hospitalParkingSlot,
                    immediate: false,
                    onParked: () =>
                    {
                        if (assigned &&
                            incident != null &&
                            incident.IncidentId ==
                                returningIncidentId)
                        {
                            incidentSystem
                                ?.TryMarkAmbulanceReturned(
                                    returningIncidentId);
                        }
                    });
            }
        }

        private void HandleRouteUnavailable()
        {
            if (assigned)
            {
                ScheduleRetry();
            }
        }

        private void ScheduleRetry()
        {
            retryRemainingSeconds =
                config != null
                    ? config.RouteRetrySeconds
                    : 2f;
        }
    }
}
