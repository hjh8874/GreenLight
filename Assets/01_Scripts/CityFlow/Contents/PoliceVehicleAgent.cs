using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Content.Transit;
using CityFlow.Contracts;
using CityFlow.View;
using UnityEngine;

namespace CityFlow.Content
{
    [RequireComponent(typeof(BusRoute))]
    public sealed class PoliceVehicleAgent :
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
        private PoliceDispatchConfigSO config;

        [SerializeField]
        private AmbulanceWorldView worldView;

        private readonly List<Vector2Int> routeStops = new(2);

        private CityFlowServices services;
        private PoliceCallSystem callSystem;
        private IReadOnlyTileData tileData;
        private PoliceCallSnapshot call;
        private TravelStage stage;
        private float retryRemainingSeconds;
        private int routeFailureCount;
        private bool hasDepartedStation;
        private bool initialized;
        private bool subscribed;
        private bool assigned;
        private Vector2Int homeStation;
        private int stationParkingSlot;
        private bool hasHomeStation;
        private bool patrolling;
        private bool patrolParkingRequested;
        private PolicePatrolPlan patrolPlan;
        private long patrolTotalDay;

        public int CallId => assigned ? call.CallId : -1;
        public bool IsAssigned => assigned;
        public bool IsPatrolling => patrolling;
        public bool IsBusy => assigned || patrolling;
        public bool HasHomeStation => hasHomeStation;
        public Vector2Int HomeStation => homeStation;
        public int StationParkingSlot => stationParkingSlot;
        public PoliceDispatchConfigSO Config => config;

        internal event Action<PoliceVehicleAgent, PolicePatrolEvent>
            PatrolEnded;

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
                    "[PoliceVehicleAgent] Services, route, config, and world view are required.",
                    this);
                return;
            }

            services = cityServices;
            tileData = services.TileData;
            route.ConfigureRoadTrafficAgent(
                RoadTrafficAgentKind.FeatureVehicle,
                config.VehicleFootprint,
                holdAtDestination: true);
            route.Initialize(services);
            worldView.ConfigurePresentation(config);
            worldView.Initialize(services);
            initialized = true;
            Subscribe();
        }

        public bool PrepareAtStation(
            Vector2Int station,
            int parkingSlot)
        {
            if (!initialized)
            {
                return false;
            }

            homeStation = station;
            stationParkingSlot = Mathf.Max(0, parkingSlot);
            hasHomeStation = true;
            assigned = false;
            patrolling = false;
            patrolParkingRequested = false;
            stage = TravelStage.None;
            retryRemainingSeconds = 0f;
            routeFailureCount = 0;
            hasDepartedStation = false;
            route.StopRoute();
            worldView.ShowParkedAtHome(
                homeStation,
                stationParkingSlot,
                config.VehiclesPerStation,
                immediate: true);
            return true;
        }

        internal bool TryStartPatrol(
            PolicePatrolPlan plan,
            long totalDay)
        {
            if (!initialized || !hasHomeStation || IsBusy ||
                !plan.IsValid)
            {
                return false;
            }

            patrolParkingRequested = false;
            patrolPlan = plan;
            patrolTotalDay = Math.Max(0L, totalDay);
            retryRemainingSeconds = 0f;
            routeFailureCount = 0;
            stage = TravelStage.None;
            ConfigurePatrolRouteDefaults();
            if (route.ConfigurePreplannedRoadRoute(
                    homeStation,
                    plan.Route) &&
                route.StartRoute())
            {
                patrolling = true;
                return true;
            }

            patrolParkingRequested = false;
            route.StopRoute();
            worldView.ShowParkedAtHome(
                homeStation,
                stationParkingSlot,
                config.VehiclesPerStation,
                immediate: true);
            return false;
        }

        internal bool TryGetHomeAccessRoad(
            out Vector2Int accessRoad)
        {
            accessRoad = default;
            return initialized && hasHomeStation &&
                   route.TryGetAccessRoadForStop(
                       homeStation,
                       out accessRoad);
        }

        public bool Assign(
            PoliceCallSnapshot targetCall,
            PoliceCallSystem owner)
        {
            if (!initialized ||
                owner == null ||
                !hasHomeStation ||
                targetCall.State !=
                    PoliceCallState.VehicleOutbound ||
                targetCall.AssignedStation != homeStation ||
                targetCall.AssignedVehicleSlot !=
                    stationParkingSlot)
            {
                return false;
            }

            bool dispatchingFromPatrolRoad =
                InterruptPatrolForDispatch();
            call = targetCall;
            callSystem = owner;
            assigned = true;
            stage = TravelStage.Outbound;
            retryRemainingSeconds = 0f;
            routeFailureCount = 0;
            hasDepartedStation = dispatchingFromPatrolRoad;
            ConfigureRouteDefaults();
            worldView.PrepareRoadsideTargetStop();
            StartCurrentStage(
                preferCurrentRoad: dispatchingFromPatrolRoad);
            return true;
        }

        public bool RestoreAssignment(
            PoliceCallSnapshot targetCall,
            PoliceCallSystem owner)
        {
            if (!initialized ||
                owner == null ||
                !hasHomeStation ||
                targetCall.AssignedStation != homeStation ||
                targetCall.AssignedVehicleSlot !=
                    stationParkingSlot ||
                targetCall.State is not (
                    PoliceCallState.Handling
                    or PoliceCallState.VehicleReturning
                    or PoliceCallState
                        .VehicleReturningAfterFailure))
            {
                return false;
            }

            call = targetCall;
            callSystem = owner;
            assigned = true;
            retryRemainingSeconds = 0f;
            routeFailureCount = 0;
            hasDepartedStation = true;
            ConfigureRouteDefaults();

            if (targetCall.State == PoliceCallState.Handling)
            {
                stage = TravelStage.None;
                route.StopRoute();
                worldView.ShowParkedAtTarget(
                    targetCall.Target,
                    parkingSlot: 0,
                    immediate: true);
                return true;
            }

            stage = TravelStage.Returning;
            worldView.PrepareHomewardDeparture();
            StartCurrentStage(preferCurrentRoad: false);
            return true;
        }

        public void BeginReturn(PoliceCallSnapshot updatedCall)
        {
            if (!assigned ||
                updatedCall.CallId != call.CallId ||
                updatedCall.State is not (
                    PoliceCallState.VehicleReturning
                    or PoliceCallState
                        .VehicleReturningAfterFailure))
            {
                return;
            }

            call = updatedCall;
            if (stage == TravelStage.Returning &&
                route.State is BusRouteState.Moving
                    or BusRouteState.WaitingAtStop)
            {
                return;
            }

            stage = TravelStage.Returning;
            retryRemainingSeconds = 0f;
            routeFailureCount = 0;
            hasDepartedStation = false;
            worldView.PrepareHomewardDeparture();
            ScheduleRetry(immediate: true);
        }

        public void Release()
        {
            assigned = false;
            patrolling = false;
            patrolParkingRequested = false;
            retryRemainingSeconds = 0f;
            routeFailureCount = 0;
            hasDepartedStation = false;
            stage = TravelStage.None;
            route?.StopRoute();
            call = default;
            callSystem = null;

            if (hasHomeStation)
            {
                worldView.ShowParkedAtHome(
                    homeStation,
                    stationParkingSlot,
                    config.VehiclesPerStation,
                    immediate: true);
            }
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
            if (!assigned || retryRemainingSeconds <= 0f)
            {
                return;
            }

            retryRemainingSeconds -=
                Mathf.Max(0f, Time.deltaTime);
            if (retryRemainingSeconds <= 0f)
            {
                StartCurrentStage(preferCurrentRoad: true);
            }
        }

        private void ResolveReferences()
        {
            route ??= GetComponent<BusRoute>();
            worldView ??= GetComponent<AmbulanceWorldView>();
        }

        private void Subscribe()
        {
            if (subscribed || route == null)
            {
                return;
            }

            route.StopArrived += HandleStopArrived;
            route.TileChanged += HandleTileChanged;
            route.RouteUnavailable += HandleRouteUnavailable;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || route == null)
            {
                return;
            }

            route.StopArrived -= HandleStopArrived;
            route.TileChanged -= HandleTileChanged;
            route.RouteUnavailable -= HandleRouteUnavailable;
            subscribed = false;
        }

        private void ConfigureRouteDefaults()
        {
            route.LoopRoute = false;
            route.SecondsPerTile = config.TravelSecondsPerTile;
            route.StopWaitSeconds = config.DefaultHandlingSeconds;
            route.UseRoadsideStopApproach = true;
            route.RoadsideStopSetbackTiles = 1;
            route.RoadsideStopFilter = IsRoadsideTarget;
            route.AvoidImmediateUTurn = true;
        }

        private void ConfigurePatrolRouteDefaults()
        {
            route.LoopRoute = false;
            route.SecondsPerTile = config.TravelSecondsPerTile;
            route.StopWaitSeconds = 0f;
            route.UseRoadsideStopApproach = false;
            route.RoadsideStopSetbackTiles = 0;
            route.RoadsideStopFilter = null;
            route.AvoidImmediateUTurn = false;
        }

        private bool StartCurrentStage(bool preferCurrentRoad)
        {
            if (!assigned || route == null)
            {
                return false;
            }

            Vector2Int destination =
                stage == TravelStage.Outbound
                    ? call.Target
                    : homeStation;
            bool configured = false;
            bool configuredFromCurrentRoad = false;

            if (preferCurrentRoad && IsRoad(route.CurrentTile))
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
                routeStops.Add(
                    stage == TravelStage.Outbound
                        ? homeStation
                        : call.Target);
                routeStops.Add(destination);
                configured = route.ConfigureRoute(
                    routeStops,
                    shouldLoop: false);
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
            routeFailureCount = 0;
            return true;
        }

        private bool IsRoadsideTarget(Vector2Int stop)
        {
            return assigned &&
                   stage == TravelStage.Outbound &&
                   stop == call.Target;
        }

        private bool IsRoad(Vector2Int tile)
        {
            return tileData != null &&
                   tileData.GetTileType(tile) == TileType.Road;
        }

        private void HandleStopArrived(Vector2Int stop, int _)
        {
            if (patrolling && stop == homeStation)
            {
                BeginPatrolParking();
                return;
            }

            if (!assigned || callSystem == null)
            {
                return;
            }

            if (stage == TravelStage.Outbound && stop == call.Target)
            {
                callSystem.TryMarkVehicleArrived(call.CallId);
                return;
            }

            if (stage == TravelStage.Returning && stop == homeStation)
            {
                int returningCallId = call.CallId;
                worldView.ShowParkedAtHome(
                    homeStation,
                    stationParkingSlot,
                    config.VehiclesPerStation,
                    immediate: false,
                    onParked: () =>
                    {
                        if (assigned && call.CallId == returningCallId)
                        {
                            callSystem?.TryMarkVehicleReturned(
                                returningCallId);
                        }
                    });
            }
        }

        private void HandleRouteUnavailable()
        {
            if (patrolling)
            {
                FinishPatrol(
                    PolicePatrolOutcome.RouteUnavailable,
                    parkImmediately: true);
                return;
            }

            if (!assigned || callSystem == null)
            {
                return;
            }

            routeFailureCount++;
            if (stage == TravelStage.Outbound &&
                routeFailureCount >=
                    config.MaximumOutboundRouteRetries)
            {
                retryRemainingSeconds = 0f;
                callSystem.TryFailRouteUnavailable(
                    call.CallId,
                    hasDepartedStation);
                return;
            }

            if (stage == TravelStage.Returning &&
                routeFailureCount >=
                    config.MaximumReturnRouteRetries)
            {
                RecoverAtStation();
                return;
            }

            ScheduleRetry();
        }

        private void HandleTileChanged(Vector2Int tile)
        {
            if (assigned &&
                stage == TravelStage.Outbound &&
                tile != homeStation)
            {
                hasDepartedStation = true;
            }
        }

        private void BeginPatrolParking()
        {
            if (!patrolling || patrolParkingRequested)
            {
                return;
            }

            patrolParkingRequested = true;
            worldView.ShowParkedAtHome(
                homeStation,
                stationParkingSlot,
                config.VehiclesPerStation,
                immediate: false,
                onParked: () =>
                {
                    if (patrolling)
                    {
                        FinishPatrol(
                            PolicePatrolOutcome.Completed,
                            parkImmediately: false);
                    }
                });
        }

        private bool InterruptPatrolForDispatch()
        {
            if (!patrolling)
            {
                return false;
            }

            bool isOnRoad = IsRoad(route.CurrentTile);
            PolicePatrolEvent patrolEvent = CreatePatrolEvent(
                PolicePatrolOutcome.InterruptedByDispatch);
            patrolling = false;
            patrolParkingRequested = false;
            route.StopRoute();
            PatrolEnded?.Invoke(this, patrolEvent);
            return isOnRoad;
        }

        private void FinishPatrol(
            PolicePatrolOutcome outcome,
            bool parkImmediately)
        {
            if (!patrolling)
            {
                return;
            }

            PolicePatrolEvent patrolEvent = CreatePatrolEvent(outcome);
            patrolling = false;
            patrolParkingRequested = false;
            route.StopRoute();
            if (parkImmediately)
            {
                worldView.ShowParkedAtHome(
                    homeStation,
                    stationParkingSlot,
                    config.VehiclesPerStation,
                    immediate: true);
            }

            PatrolEnded?.Invoke(this, patrolEvent);
        }

        private PolicePatrolEvent CreatePatrolEvent(
            PolicePatrolOutcome outcome)
        {
            return new PolicePatrolEvent(
                homeStation,
                patrolTotalDay,
                patrolPlan.UsedLoop,
                patrolPlan.Route.TileCount,
                outcome);
        }

        private void RecoverAtStation()
        {
            if (!assigned || callSystem == null)
            {
                return;
            }

            int returningCallId = call.CallId;
            retryRemainingSeconds = 0f;
            route.StopRoute();
            worldView.ShowParkedAtHome(
                homeStation,
                stationParkingSlot,
                config.VehiclesPerStation,
                immediate: true);
            callSystem.TryMarkVehicleReturned(returningCallId);
        }

        private void ScheduleRetry(bool immediate = false)
        {
            retryRemainingSeconds = immediate
                ? 0.01f
                : config != null
                    ? config.RouteRetrySeconds
                    : 2f;
        }

        // Unity setup: this component is prewired in PoliceVehicle.prefab.
    }
}
