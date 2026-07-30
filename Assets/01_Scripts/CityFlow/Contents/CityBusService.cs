using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Content.Transit;
using CityFlow.Contracts;
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
        private const string StopRevenueReason =
            "city bus stop";

        [Header("Configuration")]
        [SerializeField] private BusDefinitionSO definition;
        [SerializeField] private CityBusScheduleSO schedule;

        [Header("Local Components")]
        [SerializeField] private BusRoute busRoute;
        [SerializeField] private BusStopRegistry stopRegistry;

        [Header("Startup")]
        [SerializeField] private bool autoStart = true;
        [SerializeField] private bool verboseLogging;

        private readonly List<Vector2Int> routeStops = new();
        private CityFlowServices services;
        private IGameCalendarService calendar;
        private BusRouteState observedRouteState;
        private bool initialized;
        private bool subscribed;
        private bool calendarSubscribed;
        private bool routeRefreshPending;
        private bool routeRecoveryPending;
        private bool routeOperationInProgress;
        private bool vehicleVisible;
        private bool hasFailedDestination;
        private Vector2Int failedDestination;

        public BusRuntime Runtime { get; private set; }
        public IReadOnlyList<Vector2Int> RouteStops =>
            routeStops;
        public bool IsInitialized => initialized;
        public BusDefinitionSO Definition => definition;
        public CityBusScheduleSO Schedule => schedule;
        public bool IsVehicleVisible => vehicleVisible;

        public event Action ServiceStarted;
        public event Action ServiceStopped;
        public event Action<Vector2Int, int, int> StopServed;
        public event Action ServiceUnavailable;
        public event Action<bool> VehicleVisibilityChanged;

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
            busRoute.UseRoadsideStopApproach = true;
            busRoute.ConfigureRoadTrafficAgent(
                RoadTrafficAgentKind.CityBus,
                definition.VehicleFootprint,
                true);
            busRoute.Initialize(services);
            busRoute.SecondsPerTile =
                definition.SecondsPerTile;
            busRoute.StopWaitSeconds =
                definition.StopWaitSeconds;
            Runtime = new BusRuntime(
                definition.PassengerCapacity);
            observedRouteState = busRoute.State;
            initialized = true;
            Subscribe();
            BindCalendar(cityServices.GameCalendar);
            SynchronizeRuntime();
            SetVehicleVisible(false);

            if (autoStart)
            {
                EvaluateOperatingWindow();
            }
            else
            {
                Runtime.SetState(
                    BusOperatingState.OutOfService);
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

            if (schedule == null)
            {
                Debug.LogError(
                    "[CityBusService] A CityBusScheduleSO is required.",
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

            if (routeRecoveryPending && IsInsideOperatingWindow())
            {
                routeRecoveryPending = false;
                TryRecoverRoute();
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
                EvaluateOperatingWindow();
            }
        }

        private void OnDisable()
        {
            if (initialized)
            {
                StopService();
            }

            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        public bool StartService()
        {
            if (!initialized || !IsInsideOperatingWindow())
            {
                return false;
            }

            routeRefreshPending = false;
            routeRecoveryPending = false;
            hasFailedDestination = false;
            BuildRouteStops();

            routeOperationInProgress = true;
            bool started;
            try
            {
                started = stopRegistry.BusStopCount >= 1 &&
                    routeStops.Count >= 1 &&
                    busRoute.ConfigureRoute(
                        routeStops,
                        true) &&
                    busRoute.StartRoute();
            }
            finally
            {
                routeOperationInProgress = false;
            }

            if (!started)
            {
                RememberFailedDestination();
                SetServiceUnavailable(true);
                return false;
            }

            observedRouteState = busRoute.State;
            ApplyRouteState(observedRouteState);
            SetVehicleVisible(true);
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
            bool wasOperating = vehicleVisible;
            routeRefreshPending = false;
            routeRecoveryPending = false;
            hasFailedDestination = false;
            busRoute?.StopRoute();
            Runtime?.ResetPassengers();
            Runtime?.SetState(
                BusOperatingState.OutOfService);
            SetVehicleVisible(false);

            if (wasOperating)
            {
                ServiceStopped?.Invoke();
            }
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

        private bool BuildRouteStopsFrom(
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

            if (currentIndex >= 0)
            {
                routeStops.Add(currentStop);
            }

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

            return currentIndex >= 0;
        }

        private void BuildRouteStopsStartingAt(int startIndex)
        {
            routeStops.Clear();
            IReadOnlyList<Vector2Int> stops =
                stopRegistry.BusStops;

            for (int offset = 0; offset < stops.Count; offset++)
            {
                int index = (startIndex + offset) % stops.Count;
                routeStops.Add(stops[index]);
            }
        }

        private void RefreshRouteAtCurrentStop()
        {
            routeRefreshPending = false;
            Vector2Int currentStop = busRoute.CurrentStop;
            Vector2Int currentPosition = busRoute.CurrentTile;
            bool currentStopIsRegistered =
                BuildRouteStopsFrom(currentStop);
            routeOperationInProgress = true;
            bool routeConfigured;
            bool started;
            try
            {
                routeConfigured = currentStopIsRegistered
                    ? busRoute.ReconfigureLoopAtCurrentStop(
                        routeStops)
                    : busRoute.ReconfigureLoopFromCurrentPosition(
                        currentPosition,
                        routeStops);
                started = stopRegistry.BusStopCount >= 1 &&
                    routeStops.Count >= 1 &&
                    routeConfigured &&
                    busRoute.StartRoute();
            }
            finally
            {
                routeOperationInProgress = false;
            }

            if (!started)
            {
                RememberFailedDestination();
                SetServiceUnavailable(true);
                return;
            }

            observedRouteState = busRoute.State;
            ApplyRouteState(observedRouteState);
        }

        private void EvaluateOperatingWindow()
        {
            if (!autoStart)
            {
                return;
            }

            if (!IsInsideOperatingWindow())
            {
                StopService();
                return;
            }

            if (vehicleVisible &&
                busRoute.State is BusRouteState.Moving
                    or BusRouteState.WaitingAtStop)
            {
                return;
            }

            StartService();
        }

        private bool IsInsideOperatingWindow() =>
            calendar != null &&
            schedule != null &&
            schedule.IsOperatingHour(calendar.Hour);

        private void BindCalendar(
            IGameCalendarService gameCalendar)
        {
            if (ReferenceEquals(calendar, gameCalendar))
            {
                if (subscribed && calendar != null &&
                    !calendarSubscribed)
                {
                    calendar.HourChanged += OnHourChanged;
                    calendarSubscribed = true;
                }

                return;
            }

            if (calendarSubscribed && calendar != null)
            {
                calendar.HourChanged -= OnHourChanged;
            }

            calendarSubscribed = false;
            calendar = gameCalendar;
            if (subscribed && calendar != null)
            {
                calendar.HourChanged += OnHourChanged;
                calendarSubscribed = true;
            }
        }

        private void OnGameCalendarRegistered(
            IGameCalendarService gameCalendar)
        {
            BindCalendar(gameCalendar);
            EvaluateOperatingWindow();
        }

        private void OnHourChanged(int _)
        {
            EvaluateOperatingWindow();
        }

        private void TryRecoverRoute()
        {
            IReadOnlyList<Vector2Int> stops =
                stopRegistry.BusStops;
            if (stops.Count == 0)
            {
                SetServiceUnavailable(false);
                return;
            }

            int startIndex = 0;
            if (hasFailedDestination)
            {
                for (int index = 0; index < stops.Count; index++)
                {
                    if (stops[index] == failedDestination)
                    {
                        startIndex = (index + 1) % stops.Count;
                        break;
                    }
                }
            }

            Vector2Int currentPosition = busRoute.CurrentTile;
            for (int offset = 0; offset < stops.Count; offset++)
            {
                int candidateIndex =
                    (startIndex + offset) % stops.Count;
                BuildRouteStopsStartingAt(candidateIndex);

                routeOperationInProgress = true;
                bool started;
                try
                {
                    started = busRoute
                        .ReconfigureLoopFromCurrentPosition(
                            currentPosition,
                            routeStops) &&
                        busRoute.StartRoute();
                }
                finally
                {
                    routeOperationInProgress = false;
                }

                if (!started)
                {
                    continue;
                }

                hasFailedDestination = false;
                observedRouteState = busRoute.State;
                ApplyRouteState(observedRouteState);
                SetVehicleVisible(true);
                ServiceStarted?.Invoke();
                return;
            }

            SetServiceUnavailable(false);
        }

        private void RememberFailedDestination()
        {
            Vector2Int destination = busRoute.NextStop;
            hasFailedDestination =
                routeStops.Contains(destination);
            failedDestination = destination;
        }

        private void SetServiceUnavailable(bool requestRecovery)
        {
            routeRefreshPending = false;
            routeRecoveryPending = requestRecovery &&
                stopRegistry.BusStopCount > 0;
            busRoute.StopRoute();
            Runtime.SetState(
                BusOperatingState.RouteUnavailable);
            SetVehicleVisible(false);
            ServiceUnavailable?.Invoke();
        }

        private void SetVehicleVisible(bool visible)
        {
            if (vehicleVisible == visible)
            {
                return;
            }

            vehicleVisible = visible;
            VehicleVisibilityChanged?.Invoke(vehicleVisible);

            if (verboseLogging)
            {
                Debug.Log(
                    $"[CityBusService] Vehicle visibility: {vehicleVisible}.",
                    this);
            }
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

            if (services != null)
            {
                services.GameCalendarRegistered +=
                    OnGameCalendarRegistered;
            }

            subscribed = true;
            BindCalendar(services?.GameCalendar);
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

            if (services != null)
            {
                services.GameCalendarRegistered -=
                    OnGameCalendarRegistered;
            }

            if (calendarSubscribed && calendar != null)
            {
                calendar.HourChanged -= OnHourChanged;
            }

            calendarSubscribed = false;
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
            GrantStopRevenue(tile);
            Runtime.SetRoutePosition(
                tile,
                busRoute.NextStop);

            StopServed?.Invoke(
                tile,
                boarded,
                left);
        }

        private void GrantStopRevenue(Vector2Int tile)
        {
            int revenue = definition.StopRevenueCoins;

            if (revenue <= 0 ||
                services?.Economy == null ||
                services.Save?.IsRestoring == true)
            {
                return;
            }

            services.Economy.AddCoins(
                revenue,
                StopRevenueReason);

            if (verboseLogging)
            {
                Debug.Log(
                    $"[CityBusService] Earned {revenue} coins at stop {tile}.",
                    this);
            }
        }

        private void OnRouteUnavailable()
        {
            if (routeOperationInProgress)
            {
                return;
            }

            RememberFailedDestination();
            SetServiceUnavailable(
                IsInsideOperatingWindow());
        }

        private void OnRegistryChanged()
        {
            if (!initialized)
            {
                return;
            }

            if (!autoStart || !IsInsideOperatingWindow())
            {
                return;
            }

            if (stopRegistry.BusStopCount < 1)
            {
                SetServiceUnavailable(false);
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
            StopService();
            if (autoStart)
            {
                EvaluateOperatingWindow();
            }
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

        // Unity setup: place CityBusContent.prefab; its schedule and route references are prewired.
    }
}
