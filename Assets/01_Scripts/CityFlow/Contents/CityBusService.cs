using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Content.Transit;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.Content
{
    [RequireComponent(typeof(BusRoute))]
    [RequireComponent(typeof(BusStopRegistry))]
    public sealed class CityBusService :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        [Header("Configuration")]
        [SerializeField] private BusDefinitionSO definition;

        [Header("Local Components")]
        [SerializeField] private BusRoute busRoute;
        [SerializeField] private BusStopRegistry stopRegistry;

        [Header("Startup")]
        [SerializeField] private bool autoStart = true;
        [SerializeField] private bool verboseLogging;

        private readonly List<Vector2Int> routeStops = new();
        private CityFlowServices services;
        private BusRouteState observedRouteState;
        private bool initialized;
        private bool subscribed;
        private bool routeRefreshPending;

        public BusRuntime Runtime { get; private set; }
        public IReadOnlyList<Vector2Int> RouteStops =>
            routeStops;
        public bool IsInitialized => initialized;

        public event Action ServiceStarted;
        public event Action<Vector2Int, int, int> StopServed;
        public event Action ServiceUnavailable;

        private void Reset()
        {
            busRoute = GetComponent<BusRoute>();
            stopRegistry = GetComponent<BusStopRegistry>();
        }

        private void Awake()
        {
            busRoute ??= GetComponent<BusRoute>();
            stopRegistry ??= GetComponent<BusStopRegistry>();
        }

        public void Initialize(CityFlowServices cityServices)
        {
            if (!isActiveAndEnabled || initialized)
            {
                return;
            }

            if (!ValidateConfiguration(cityServices))
            {
                return;
            }

            services = cityServices;
            stopRegistry.Initialize(services);
            busRoute.Initialize(services);
            busRoute.StopWaitSeconds =
                definition.StopWaitSeconds;
            Runtime = new BusRuntime(
                definition.PassengerCapacity);
            observedRouteState = busRoute.State;
            initialized = true;
            Subscribe();
            SynchronizeRuntime();

            if (autoStart)
            {
                StartService();
            }
        }

        private bool ValidateConfiguration(
            CityFlowServices cityServices)
        {
            if (cityServices?.TileData == null)
            {
                Debug.LogError(
                    "[CityBusService] CityFlowServices or TileData is missing.",
                    this);
                return false;
            }

            if (definition == null ||
                definition.BusType != BusType.CityBus)
            {
                Debug.LogError(
                    "[CityBusService] A CityBus BusDefinitionSO is required.",
                    this);
                return false;
            }

            busRoute ??= GetComponent<BusRoute>();
            stopRegistry ??= GetComponent<BusStopRegistry>();

            if (busRoute == null || stopRegistry == null)
            {
                Debug.LogError(
                    "[CityBusService] BusRoute and BusStopRegistry are required.",
                    this);
                return false;
            }

            return true;
        }

        private void Update()
        {
            if (!initialized)
            {
                return;
            }

            if (observedRouteState != busRoute.State)
            {
                observedRouteState = busRoute.State;
                ApplyRouteState(observedRouteState);
            }

            if (routeRefreshPending &&
                busRoute.State == BusRouteState.WaitingAtStop)
            {
                RefreshRouteAtCurrentStop();
            }

            if (Runtime.CurrentTile != busRoute.CurrentTile ||
                Runtime.NextStop != busRoute.NextStop)
            {
                Runtime.SetRoutePosition(
                    busRoute.CurrentTile,
                    busRoute.NextStop);
            }
        }

        private void OnEnable()
        {
            if (initialized)
            {
                Subscribe();
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        public bool StartService()
        {
            if (!initialized)
            {
                return false;
            }

            routeRefreshPending = false;
            BuildRouteStops();

            if (stopRegistry.BusStopCount < 1 ||
                routeStops.Count < 1 ||
                !busRoute.ConfigureRoute(
                    routeStops,
                    true) ||
                !busRoute.StartRoute())
            {
                busRoute.StopRoute();
                Runtime.SetState(
                    BusOperatingState.RouteUnavailable);
                ServiceUnavailable?.Invoke();
                return false;
            }

            observedRouteState = busRoute.State;
            ApplyRouteState(observedRouteState);
            ServiceStarted?.Invoke();

            if (verboseLogging)
            {
                Debug.Log(
                    $"[CityBusService] Started with {routeStops.Count} stops.",
                    this);
            }

            return true;
        }

        public void StopService()
        {
            routeRefreshPending = false;
            busRoute?.StopRoute();
            Runtime?.ResetPassengers();
            Runtime?.SetState(
                BusOperatingState.OutOfService);
        }



        private void BuildRouteStops()
        {
            routeStops.Clear();

            IReadOnlyList<Vector2Int> stops =
                stopRegistry.BusStops;

            for (int i = 0; i < stops.Count; i++)
            {
                routeStops.Add(stops[i]);
            }
        }

        private void BuildRouteStopsFrom(
            Vector2Int currentStop)
        {
            routeStops.Clear();

            IReadOnlyList<Vector2Int> stops =
                stopRegistry.BusStops;
            int currentIndex = -1;

            for (int i = 0; i < stops.Count; i++)
            {
                if (stops[i] == currentStop)
                {
                    currentIndex = i;
                    break;
                }
            }

            routeStops.Add(currentStop);

            for (int offset = 1; offset <= stops.Count; offset++)
            {
                int index = currentIndex >= 0
                    ? (currentIndex + offset) % stops.Count
                    : offset - 1;
                Vector2Int stop = stops[index];

                if (stop != currentStop)
                {
                    routeStops.Add(stop);
                }
            }
        }

        private void RefreshRouteAtCurrentStop()
        {
            routeRefreshPending = false;
            Vector2Int currentStop = busRoute.CurrentStop;
            BuildRouteStopsFrom(currentStop);

            if (stopRegistry.BusStopCount < 1 ||
                routeStops.Count < 1 ||
                !busRoute.ReconfigureLoopAtCurrentStop(
                    routeStops) ||
                !busRoute.StartRoute())
            {
                busRoute.StopRoute();
                Runtime.SetState(
                    BusOperatingState.RouteUnavailable);
                ServiceUnavailable?.Invoke();
                return;
            }

            observedRouteState = busRoute.State;
            ApplyRouteState(observedRouteState);
        }

        private void Subscribe()
        {
            if (subscribed)
            {
                return;
            }

            busRoute.StopArrived += OnStopArrived;
            busRoute.RouteUnavailable +=
                OnRouteUnavailable;
            stopRegistry.RegistryChanged +=
                OnRegistryChanged;

            if (services?.Save != null)
            {
                services.Save.RestoreCompleted +=
                    OnRestoreCompleted;
            }

            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed)
            {
                return;
            }

            if (busRoute != null)
            {
                busRoute.StopArrived -= OnStopArrived;
                busRoute.RouteUnavailable -=
                    OnRouteUnavailable;
            }

            if (stopRegistry != null)
            {
                stopRegistry.RegistryChanged -=
                    OnRegistryChanged;
            }

            if (services?.Save != null)
            {
                services.Save.RestoreCompleted -=
                    OnRestoreCompleted;
            }

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
            Runtime.SetRoutePosition(
                tile,
                busRoute.NextStop);

            StopServed?.Invoke(
                tile,
                boarded,
                left);
        }

        private void OnRouteUnavailable()
        {
            Runtime.SetState(
                BusOperatingState.RouteUnavailable);
            ServiceUnavailable?.Invoke();
        }

        private void OnRegistryChanged()
        {
            if (!initialized)
            {
                return;
            }

            if (stopRegistry.BusStopCount < 1)
            {
                routeRefreshPending = false;
                busRoute.StopRoute();
                Runtime.SetState(
                    BusOperatingState.RouteUnavailable);
                ServiceUnavailable?.Invoke();
                return;
            }

            if (busRoute.State is BusRouteState.Idle
                or BusRouteState.RouteUnavailable)
            {
                StartService();
                return;
            }

            routeRefreshPending = true;
        }

        private void OnRestoreCompleted(
            RestoreCompletedEvent _)
        {
            Runtime.ResetPassengers();
            StartService();
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
            Runtime?.SetRoutePosition(
                busRoute.CurrentTile,
                busRoute.NextStop);
        }
    }
}
