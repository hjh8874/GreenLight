using System;
using CityFlow.Bootstrap;
using CityFlow.Content.Transit;
using CityFlow.Contracts;
using CityFlow.View;
using UnityEngine;

namespace CityFlow.Content
{
    [DisallowMultipleComponent]
    public sealed class CityBusVehicleAgent : MonoBehaviour
    {
        private CityFlowServices services;
        private BusDefinitionSO definition;
        private CityBusService owner;
        private BusDirectionalRoute directionalRoute;
        private BusRoute busRoute;
        private BusWorldView worldView;
        private BusRouteState observedRouteState;
        private bool prepared;
        private bool subscribed;
        private Vector2Int plannedInitialAccessRoad;
        private bool hasPlannedInitialAccessRoad;

        public int RouteId { get; private set; }
        public BusTravelDirection Direction { get; private set; }
        public BusRuntime Runtime { get; private set; }
        public BusRoute Route => busRoute;
        public bool IsOperating { get; private set; }
        public bool IsPrepared => prepared;

        public event Action<
            CityBusVehicleAgent,
            Vector2Int,
            int,
            int> StopServed;
        public event Action<CityBusVehicleAgent> RouteUnavailable;

        public bool Prepare(
            CityFlowServices cityServices,
            BusDefinitionSO busDefinition,
            CityBusService serviceOwner,
            BusDirectionalRoute route,
            BusRoute existingRoute = null)
        {
            Shutdown();

            if (cityServices == null ||
                busDefinition == null ||
                serviceOwner == null ||
                !route.IsValid)
            {
                return false;
            }

            services = cityServices;
            definition = busDefinition;
            owner = serviceOwner;
            directionalRoute = route;
            RouteId = route.RouteId;
            Direction = route.Direction;
            busRoute = existingRoute ??
                GetComponent<BusRoute>() ??
                gameObject.AddComponent<BusRoute>();

            busRoute.LoopRoute = true;
            busRoute.UseRoadsideStopApproach = true;
            busRoute.RoadsideStopsUsePairedPlatforms = true;
            busRoute.UseOppositePairedPlatformDirection =
                Direction == BusTravelDirection.Reverse;
            busRoute.AllowUnscheduledStopArrival = false;
            busRoute.AvoidImmediateUTurn = true;
            busRoute.SecondsPerTile = definition.SecondsPerTile;
            busRoute.StopWaitSeconds = definition.StopWaitSeconds;
            busRoute.ConfigureRoadTrafficAgent(
                RoadTrafficAgentKind.CityBus,
                definition.VehicleFootprint,
                true);
            busRoute.Initialize(services);

            Runtime = new BusRuntime(
                definition.PassengerCapacity);
            observedRouteState = busRoute.State;
            prepared = true;
            Subscribe();
            SynchronizeRuntime();
            return true;
        }

        public bool CanOperateLoop() =>
            TryBuildLoopRoadRoute(out _, out _);

        public bool TryBuildLoopRoadRoute(
            out RoadRoutePlan route,
            out RoadRoutePlan firstSegment)
        {
            route = default;
            firstSegment = default;
            bool built = prepared &&
                busRoute != null &&
                busRoute.TryBuildLoopRoadRoute(
                    directionalRoute.OrderedStops,
                    Direction == BusTravelDirection.Reverse,
                    out route,
                    out firstSegment);

            hasPlannedInitialAccessRoad = built;
            plannedInitialAccessRoad = built
                ? route.Origin
                : default;
            return built;
        }

        public bool StartOperating(
            BusWorldView presentationTemplate)
        {
            if (!prepared || !hasPlannedInitialAccessRoad)
            {
                return false;
            }

            if (!busRoute.ConfigureRoute(
                    directionalRoute.OrderedStops,
                    true))
            {
                return false;
            }

            if (hasPlannedInitialAccessRoad)
            {
                busRoute.SetInitialAccessRoad(
                    plannedInitialAccessRoad);
            }

            worldView = GetComponent<BusWorldView>() ??
                gameObject.AddComponent<BusWorldView>();
            worldView.ConfigureCityBusAgent(
                services,
                definition,
                busRoute,
                owner,
                presentationTemplate);

            if (!busRoute.StartRoute())
            {
                return false;
            }

            IsOperating = true;
            observedRouteState = busRoute.State;
            ApplyRouteState(observedRouteState);
            return true;
        }

        public void Shutdown()
        {
            Unsubscribe();

            if (busRoute != null)
            {
                busRoute.StopRoute();
            }

            Runtime?.ResetPassengers();
            Runtime?.SetState(
                BusOperatingState.OutOfService);
            IsOperating = false;
            prepared = false;
            hasPlannedInitialAccessRoad = false;
            plannedInitialAccessRoad = default;
        }

        private void Update()
        {
            if (!prepared || busRoute == null)
            {
                return;
            }

            if (observedRouteState != busRoute.State)
            {
                observedRouteState = busRoute.State;
                ApplyRouteState(observedRouteState);
            }

            if (Runtime.CurrentTile != busRoute.CurrentTile ||
                Runtime.NextStop != busRoute.NextStop)
            {
                SynchronizeRuntime();
            }
        }

        private void Subscribe()
        {
            if (subscribed || busRoute == null)
            {
                return;
            }

            busRoute.StopArrived += OnStopArrived;
            busRoute.RouteUnavailable +=
                OnRouteUnavailable;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || busRoute == null)
            {
                return;
            }

            busRoute.StopArrived -= OnStopArrived;
            busRoute.RouteUnavailable -=
                OnRouteUnavailable;
            subscribed = false;
        }

        private void OnStopArrived(
            Vector2Int tile,
            int _)
        {
            int left = Runtime.Leave(
                definition.LeavingDemandPerStop);
            int boarded = Runtime.Board(
                definition.BoardingDemandPerStop);
            Runtime.CompleteStop();
            SynchronizeRuntime();
            StopServed?.Invoke(
                this,
                tile,
                boarded,
                left);
        }

        private void OnRouteUnavailable()
        {
            IsOperating = false;
            Runtime.SetState(
                BusOperatingState.RouteUnavailable);
            SynchronizeRuntime();
            RouteUnavailable?.Invoke(this);
        }

        private void ApplyRouteState(
            BusRouteState routeState)
        {
            BusOperatingState state = routeState switch
            {
                BusRouteState.Moving =>
                    BusOperatingState.Moving,
                BusRouteState.WaitingAtStop =>
                    BusOperatingState.WaitingAtStop,
                BusRouteState.RouteUnavailable =>
                    BusOperatingState.RouteUnavailable,
                _ => BusOperatingState.Idle
            };

            Runtime.SetState(state);
            SynchronizeRuntime();
        }

        private void SynchronizeRuntime()
        {
            if (Runtime == null || busRoute == null)
            {
                return;
            }

            Runtime.SetRoutePosition(
                busRoute.CurrentTile,
                busRoute.NextStop);
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        // Unity setup: CityBusService creates these agents automatically from CityBusContent.prefab.
    }
}
