using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Content.Transit;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using CityFlow.View;
using UnityEngine;

namespace CityFlow.Content
{
    [RequireComponent(typeof(BusRoute))]
    [RequireComponent(typeof(BusStopRegistry))]
    public sealed class CityBusService :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        private readonly struct DirectedRoadEdge :
            IEquatable<DirectedRoadEdge>
        {
            public Vector2Int Start { get; }
            public Vector2Int End { get; }

            public DirectedRoadEdge(
                Vector2Int start,
                Vector2Int end)
            {
                Start = start;
                End = end;
            }

            public bool Equals(DirectedRoadEdge other) =>
                Start == other.Start && End == other.End;

            public override bool Equals(object obj) =>
                obj is DirectedRoadEdge other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Start.GetHashCode() * 397) ^
                        End.GetHashCode();
                }
            }
        }

        private const string StopRevenueReason =
            "city bus stop";
        private const int LegacyDefaultRouteId = 1;
        private static readonly Vector2Int[] RoundaboutFootprintOffsets =
        {
            Vector2Int.zero,
            Vector2Int.up,
            Vector2Int.right,
            Vector2Int.down,
            Vector2Int.left
        };

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
        private readonly List<CityBusVehicleAgent> activeVehicles = new();
        private readonly List<CityBusVehicleAgent>
            vehiclesReadyToRetire = new();
        private readonly Dictionary<
            int,
            BusLineDirectionAvailability> availabilityByRoute = new();
        private readonly List<int> staleAvailabilityRouteIds = new();

        private CityFlowServices services;
        private IGameCalendarService calendar;
        private BusWorldView rootView;
        private CityBusVehicleAgent primaryAgent;
        private BusRuntime fallbackRuntime;
        private bool initialized;
        private bool subscribed;
        private bool calendarSubscribed;
        private bool serviceRequested;
        private bool rebuildPending;
        private bool recoveryPending;
        private bool rebuildInProgress;
        private bool topologyRefreshInProgress;
        private bool vehicleVisible;
        private bool serviceStopPending;
        private bool ownsLegacyDefaultLine;
        private bool synchronizingLegacyLine;

        public BusRuntime Runtime =>
            primaryAgent?.Runtime ?? fallbackRuntime;
        public IBusLineService BusLines { get; private set; }
        public IReadOnlyList<Vector2Int> RouteStops => routeStops;
        public IReadOnlyList<CityBusVehicleAgent> ActiveVehicles =>
            activeVehicles;
        public bool IsInitialized => initialized;
        public BusDefinitionSO Definition => definition;
        public CityBusScheduleSO Schedule => schedule;
        public bool IsVehicleVisible => vehicleVisible;
        public bool IsStoppingAtNextStop => serviceStopPending;

        public event Action ServiceStarted;
        public event Action ServiceStopped;
        public event Action<Vector2Int, int, int> StopServed;
        public event Action ServiceUnavailable;
        public event Action<bool> VehicleVisibilityChanged;
        public event Action<
            int,
            BusLineDirectionAvailability>
            DirectionAvailabilityChanged;

        private void Reset()
        {
            busRoute = GetComponent<BusRoute>();
            stopRegistry = GetComponent<BusStopRegistry>();
        }

        private void Awake()
        {
            busRoute ??= GetComponent<BusRoute>();
            stopRegistry ??= GetComponent<BusStopRegistry>();
            rootView = GetComponent<BusWorldView>();
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
            BindBusLineService(cityServices);
            ConfigureRootRoute();
            fallbackRuntime = new BusRuntime(
                definition.PassengerCapacity);
            initialized = true;
            Subscribe();
            BindCalendar(cityServices.GameCalendar);
            SetVehicleVisible(false);

            if (autoStart)
            {
                EvaluateOperatingWindow();
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
            rootView ??= GetComponent<BusWorldView>();

            if (busRoute == null || stopRegistry == null)
            {
                Debug.LogError(
                    "[CityBusService] BusRoute and BusStopRegistry are required.",
                    this);
                return false;
            }

            return true;
        }

        private void ConfigureRootRoute()
        {
            busRoute.LoopRoute = true;
            busRoute.UseRoadsideStopApproach = true;
            busRoute.RoadsideStopsUsePairedPlatforms = true;
            busRoute.AllowUnscheduledStopArrival = false;
            busRoute.AvoidImmediateUTurn = true;
            busRoute.SecondsPerTile = definition.SecondsPerTile;
            busRoute.StopWaitSeconds = definition.StopWaitSeconds;
            busRoute.ConfigureRoadTrafficAgent(
                RoadTrafficAgentKind.CityBus,
                definition.VehicleFootprint,
                true);
            busRoute.Initialize(services);
        }

        private void Update()
        {
            if (!initialized || rebuildInProgress)
            {
                return;
            }

            if (serviceStopPending)
            {
                ProcessVehiclesReadyToRetire();
                return;
            }

            if (recoveryPending && IsInsideOperatingWindow())
            {
                recoveryPending = false;
                RebuildVehicles(false);
                return;
            }

            if (rebuildPending &&
                IsInsideOperatingWindow() &&
                CanApplyPendingRebuild())
            {
                rebuildPending = false;
                RebuildVehicles(false);
            }
        }

        private bool CanApplyPendingRebuild()
        {
            if (primaryAgent?.Route == null)
            {
                return true;
            }

            return !primaryAgent.Route
                    .IsStopPresentationPending &&
                (primaryAgent.Route.State is
                    BusRouteState.Idle or
                    BusRouteState.WaitingAtStop or
                    BusRouteState.RouteUnavailable or
                    BusRouteState.Completed);
        }

        private void OnEnable()
        {
            if (!initialized)
            {
                return;
            }

            Subscribe();
            EvaluateOperatingWindow();
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
            ClearVehicleAgents();
        }

        public bool StartService()
        {
            if (!initialized || !IsInsideOperatingWindow())
            {
                return false;
            }

            if (serviceStopPending)
            {
                StopService();
            }

            if (vehicleVisible && activeVehicles.Count > 0)
            {
                return true;
            }

            serviceRequested = true;
            serviceStopPending = false;
            vehiclesReadyToRetire.Clear();
            rebuildPending = false;
            recoveryPending = false;
            return RebuildVehicles(true);
        }

        public bool RequestServiceStopAtNextStop()
        {
            if (!initialized)
            {
                return false;
            }

            serviceRequested = false;
            rebuildPending = false;
            recoveryPending = false;

            if (!vehicleVisible || activeVehicles.Count == 0)
            {
                StopService();
                return false;
            }

            if (serviceStopPending)
            {
                return true;
            }

            serviceStopPending = true;
            vehiclesReadyToRetire.Clear();

            for (int index = 0;
                 index < activeVehicles.Count;
                 index++)
            {
                CityBusVehicleAgent agent =
                    activeVehicles[index];
                BusRouteState routeState =
                    agent?.Route != null
                        ? agent.Route.State
                        : BusRouteState.Idle;

                if (agent == null ||
                    !agent.IsOperating ||
                    (routeState != BusRouteState.Moving &&
                     !agent.Route.IsStopPresentationPending))
                {
                    QueueVehicleRetirement(agent);
                }
            }

            if (verboseLogging)
            {
                Debug.Log(
                    "[CityBusService] Service end requested. " +
                    "Each bus will retire at its next stop.",
                    this);
            }

            return true;
        }

        public void StopService()
        {
            bool wasOperating = vehicleVisible;
            serviceRequested = false;
            serviceStopPending = false;
            vehiclesReadyToRetire.Clear();
            rebuildPending = false;
            recoveryPending = false;
            SetVehicleVisible(false);
            ClearVehicleAgents();
            primaryAgent = null;
            fallbackRuntime?.ResetPassengers();
            fallbackRuntime?.SetState(
                BusOperatingState.OutOfService);

            if (wasOperating)
            {
                ServiceStopped?.Invoke();
            }
        }

        public bool TryGetDirectionAvailability(
            int routeId,
            out BusLineDirectionAvailability availability)
        {
            return availabilityByRoute.TryGetValue(
                routeId,
                out availability);
        }

        private bool RebuildVehicles(bool raiseStartedEvent)
        {
            if (rebuildInProgress)
            {
                return false;
            }

            rebuildInProgress = true;
            bool wasOperating = vehicleVisible;

            try
            {
                serviceStopPending = false;
                vehiclesReadyToRetire.Clear();
                SetVehicleVisible(false);
                ClearVehicleAgents();
                primaryAgent = null;
                RefreshLegacyRouteStops();
                PrepareAvailabilityRefresh();

                bool rootAssigned = false;
                IReadOnlyList<BusLineData> lines =
                    BusLines?.Lines;
                if (lines != null)
                {
                    for (int lineIndex = 0;
                         lineIndex < lines.Count;
                         lineIndex++)
                    {
                        BusLineData line = lines[lineIndex];
                        BusLineDirectionAvailability availability =
                            BusLineDirectionAvailability.None;

                        bool forwardStarted = TryStartDirection(
                            line,
                            BusTravelDirection.Forward,
                            ref rootAssigned,
                            ref availability,
                            default,
                            false,
                            out RoadRoutePlan forwardRoadRoute);
                        TryStartDirection(
                            line,
                            BusTravelDirection.Reverse,
                            ref rootAssigned,
                            ref availability,
                            forwardRoadRoute,
                            forwardStarted,
                            out _);

                        SetDirectionAvailability(
                            line.RouteId,
                            availability);
                    }
                }

                CompleteAvailabilityRefresh();

                if (activeVehicles.Count == 0)
                {
                    fallbackRuntime?.SetState(
                        BusOperatingState.RouteUnavailable);
                    ServiceUnavailable?.Invoke();
                    return false;
                }

                primaryAgent = activeVehicles[0];
                SetVehicleVisible(true);

                if (raiseStartedEvent || !wasOperating)
                {
                    ServiceStarted?.Invoke();
                }

                if (verboseLogging)
                {
                    Debug.Log(
                        $"[CityBusService] Started {activeVehicles.Count} " +
                        $"vehicles across {BusLines.LineCount} lines.",
                        this);
                }

                return true;
            }
            finally
            {
                rebuildInProgress = false;
            }
        }

        private bool TryStartDirection(
            BusLineData line,
            BusTravelDirection direction,
            ref bool rootAssigned,
            ref BusLineDirectionAvailability availability,
            RoadRoutePlan comparisonRoute,
            bool requireOppositeDirection,
            out RoadRoutePlan roadRoute)
        {
            roadRoute = default;
            if (!BusLines.TryBuildDirectionalRoute(
                    line.RouteId,
                    direction,
                    out BusDirectionalRoute directionalRoute))
            {
                return false;
            }

            bool usesRoot = !rootAssigned;
            CityBusVehicleAgent agent =
                CreateVehicleAgent(
                    line.RouteId,
                    direction,
                    usesRoot);
            BusRoute candidateRoute = usesRoot
                ? busRoute
                : null;

            bool prepared = agent.Prepare(
                services,
                definition,
                this,
                directionalRoute,
                candidateRoute);
            bool routeBuilt = prepared &&
                agent.TryBuildLoopRoadRoute(
                    out roadRoute,
                    out _);
            bool directionAccepted = routeBuilt &&
                (!requireOppositeDirection ||
                 UsesOppositeRoadDirection(
                     comparisonRoute,
                     roadRoute,
                     (services.Placement as
                         IIntersectionFacilityService)?.RoundaboutTiles));
            bool started = directionAccepted &&
                agent.StartOperating(rootView);

            if (!started)
            {
                Debug.LogWarning(
                    $"[CityBusService] Route {line.RouteId} " +
                    $"{direction} was not started: " +
                    GetDirectionStartFailure(
                        prepared,
                        routeBuilt,
                        directionAccepted) +
                    ".",
                    this);
                DiscardVehicleAgent(agent, usesRoot);
                roadRoute = default;
                return false;
            }

            rootAssigned |= usesRoot;
            SubscribeAgent(agent);
            activeVehicles.Add(agent);
            availability |= direction == BusTravelDirection.Forward
                ? BusLineDirectionAvailability.Forward
                : BusLineDirectionAvailability.Reverse;
            LogDirectionStarted(
                line.RouteId,
                direction,
                usesRoot,
                agent,
                roadRoute);
            return true;
        }

        private void LogDirectionStarted(
            int routeId,
            BusTravelDirection direction,
            bool usesRoot,
            CityBusVehicleAgent agent,
            RoadRoutePlan roadRoute)
        {
            string trafficState = "snapshot=missing";
            if (agent.Route.TryGetRoadTrafficSnapshot(
                    out RoadTrafficSnapshot snapshot))
            {
                trafficState =
                    $"state={snapshot.State}, " +
                    $"snapshotVisible={snapshot.IsVisible}, " +
                    $"tile={snapshot.CurrentTile}";
            }

            Debug.Log(
                $"[CityBusService] Route {routeId} {direction} " +
                $"started. agent={agent.gameObject.name}, " +
                $"agentEntity={agent.GetEntityId()}, " +
                $"usesRoot={usesRoot}, " +
                $"pathOrigin={roadRoute.Origin}, " +
                $"pathTiles={roadRoute.TileCount}, " +
                trafficState,
                agent);
        }

        private static bool UsesOppositeRoadDirection(
            RoadRoutePlan forward,
            RoadRoutePlan reverse,
            IReadOnlyList<Vector2Int> roundaboutCenters)
        {
            if (!forward.IsValid || !reverse.IsValid)
            {
                return false;
            }

            int oppositeDirectionEdges = 0;
            var forwardEdges = new HashSet<DirectedRoadEdge>();
            HashSet<Vector2Int> roundaboutFootprint =
                BuildRoundaboutFootprint(roundaboutCenters);

            for (int forwardIndex = 0;
                 forwardIndex < forward.TileCount - 1;
                 forwardIndex++)
            {
                var forwardEdge = new DirectedRoadEdge(
                    forward.Tiles[forwardIndex],
                    forward.Tiles[forwardIndex + 1]);
                if (!TouchesRoundabout(
                        forwardEdge,
                        roundaboutFootprint))
                {
                    forwardEdges.Add(forwardEdge);
                }
            }

            for (int reverseIndex = 0;
                 reverseIndex < reverse.TileCount - 1;
                 reverseIndex++)
            {
                var reverseEdge = new DirectedRoadEdge(
                    reverse.Tiles[reverseIndex],
                    reverse.Tiles[reverseIndex + 1]);
                // Both services must use the roundabout's legal circulation.
                if (TouchesRoundabout(
                        reverseEdge,
                        roundaboutFootprint))
                {
                    continue;
                }

                var oppositeEdge = new DirectedRoadEdge(
                    reverseEdge.End,
                    reverseEdge.Start);
                if (forwardEdges.Contains(oppositeEdge))
                {
                    oppositeDirectionEdges++;
                }
            }

            // Stop approaches and constrained corridors may be shared even
            // when the loop has a genuine opposite-running section.
            return oppositeDirectionEdges > 0;
        }

        private static string GetDirectionStartFailure(
            bool prepared,
            bool routeBuilt,
            bool directionAccepted)
        {
            if (!prepared)
            {
                return "agent preparation failed";
            }

            if (!routeBuilt)
            {
                return "loop route could not be built";
            }

            return !directionAccepted
                ? "no opposite road segment was found"
                : "route start failed";
        }

        private static HashSet<Vector2Int> BuildRoundaboutFootprint(
            IReadOnlyList<Vector2Int> roundaboutCenters)
        {
            var footprint = new HashSet<Vector2Int>();
            if (roundaboutCenters == null)
            {
                return footprint;
            }

            for (int centerIndex = 0;
                 centerIndex < roundaboutCenters.Count;
                 centerIndex++)
            {
                Vector2Int center = roundaboutCenters[centerIndex];
                for (int offsetIndex = 0;
                     offsetIndex < RoundaboutFootprintOffsets.Length;
                     offsetIndex++)
                {
                    footprint.Add(
                        center +
                        RoundaboutFootprintOffsets[offsetIndex]);
                }
            }

            return footprint;
        }

        private static bool TouchesRoundabout(
            DirectedRoadEdge edge,
            HashSet<Vector2Int> roundaboutFootprint) =>
            roundaboutFootprint.Contains(edge.Start) ||
            roundaboutFootprint.Contains(edge.End);

        private CityBusVehicleAgent CreateVehicleAgent(
            int routeId,
            BusTravelDirection direction,
            bool useRoot)
        {
            if (useRoot)
            {
                return GetComponent<CityBusVehicleAgent>() ??
                    gameObject.AddComponent<CityBusVehicleAgent>();
            }

            var vehicleObject = new GameObject(
                $"CityBus_{routeId}_{direction}");
            vehicleObject.transform.SetParent(
                transform,
                false);
            return vehicleObject.AddComponent<CityBusVehicleAgent>();
        }

        private void DiscardVehicleAgent(
            CityBusVehicleAgent agent,
            bool usesRoot)
        {
            if (agent == null)
            {
                return;
            }

            agent.Shutdown();
            if (!usesRoot)
            {
                DestroyVehicleObject(agent.gameObject);
            }
        }

        private void ClearVehicleAgents()
        {
            for (int index = activeVehicles.Count - 1;
                 index >= 0;
                 index--)
            {
                CityBusVehicleAgent agent =
                    activeVehicles[index];
                if (agent == null)
                {
                    continue;
                }

                UnsubscribeAgent(agent);
                agent.Shutdown();

                if (agent.gameObject != gameObject)
                {
                    DestroyVehicleObject(agent.gameObject);
                }
            }

            activeVehicles.Clear();

            CityBusVehicleAgent rootAgent =
                GetComponent<CityBusVehicleAgent>();
            if (rootAgent != null)
            {
                UnsubscribeAgent(rootAgent);
                rootAgent.Shutdown();
            }
        }

        private static void DestroyVehicleObject(
            GameObject vehicleObject)
        {
            if (vehicleObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(vehicleObject);
            }
            else
            {
                DestroyImmediate(vehicleObject);
            }
        }

        private void SubscribeAgent(CityBusVehicleAgent agent)
        {
            agent.StopServed += OnAgentStopServed;
            agent.RouteUnavailable +=
                OnAgentRouteUnavailable;
        }

        private void UnsubscribeAgent(CityBusVehicleAgent agent)
        {
            agent.StopServed -= OnAgentStopServed;
            agent.RouteUnavailable -=
                OnAgentRouteUnavailable;
        }

        private void OnAgentStopServed(
            CityBusVehicleAgent agent,
            Vector2Int tile,
            int boarded,
            int left)
        {
            GrantStopRevenue(tile);
            StopServed?.Invoke(
                tile,
                boarded,
                left);

            if (serviceStopPending)
            {
                QueueVehicleRetirement(agent);
            }
        }

        private void OnAgentRouteUnavailable(
            CityBusVehicleAgent failedAgent)
        {
            Debug.LogWarning(
                $"[CityBusService] Route {failedAgent.RouteId} " +
                $"{failedAgent.Direction} became unavailable " +
                $"after startup. agentEntity=" +
                $"{failedAgent.GetEntityId()}.",
                failedAgent);

            if (topologyRefreshInProgress)
            {
                return;
            }

            if (serviceStopPending)
            {
                QueueVehicleRetirement(failedAgent);
                return;
            }

            recoveryPending = serviceRequested &&
                IsInsideOperatingWindow();

            bool anotherVehicleOperating = false;
            for (int index = 0;
                 index < activeVehicles.Count;
                 index++)
            {
                CityBusVehicleAgent candidate =
                    activeVehicles[index];
                if (candidate != null &&
                    candidate != failedAgent &&
                    candidate.IsOperating)
                {
                    anotherVehicleOperating = true;
                    break;
                }
            }

            if (!anotherVehicleOperating)
            {
                ServiceUnavailable?.Invoke();
            }
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

        private void BindBusLineService(
            CityFlowServices cityServices)
        {
            BusLines = cityServices.BusLines;
            if (BusLines == null)
            {
                var localService = new BusLineService();
                cityServices.RegisterBusLines(localService);
                BusLines = cityServices.BusLines;
            }

            SynchronizeLegacyDefaultLine();
        }

        private void SynchronizeLegacyDefaultLine()
        {
            if (BusLines == null || stopRegistry == null)
            {
                return;
            }

            synchronizingLegacyLine = true;
            try
            {
                IReadOnlyList<Vector2Int> stops =
                    stopRegistry.BusStops;
                if (stops.Count < 2)
                {
                    if (ownsLegacyDefaultLine)
                    {
                        BusLines.TryRemoveLine(
                            LegacyDefaultRouteId);
                        ownsLegacyDefaultLine = false;
                    }

                    RefreshLegacyRouteStops();
                    return;
                }

                if (!BusLines.TryGetLine(
                        LegacyDefaultRouteId,
                        out _))
                {
                    ownsLegacyDefaultLine =
                        BusLines.TryCreateLine(
                            LegacyDefaultRouteId,
                            stops);
                }
                else if (ownsLegacyDefaultLine)
                {
                    BusLines.TryUpdateLine(
                        LegacyDefaultRouteId,
                        stops);
                }

                RefreshLegacyRouteStops();
            }
            finally
            {
                synchronizingLegacyLine = false;
            }
        }

        private void RefreshLegacyRouteStops()
        {
            routeStops.Clear();
            if (BusLines == null ||
                !BusLines.TryGetLine(
                    LegacyDefaultRouteId,
                    out BusLineData line))
            {
                return;
            }

            for (int index = 0;
                 index < line.StopCount;
                 index++)
            {
                routeStops.Add(line.OrderedStops[index]);
            }
        }

        private void PrepareAvailabilityRefresh()
        {
            staleAvailabilityRouteIds.Clear();
            foreach (int routeId in availabilityByRoute.Keys)
            {
                staleAvailabilityRouteIds.Add(routeId);
            }
        }

        private void SetDirectionAvailability(
            int routeId,
            BusLineDirectionAvailability availability)
        {
            staleAvailabilityRouteIds.Remove(routeId);
            if (availabilityByRoute.TryGetValue(
                    routeId,
                    out BusLineDirectionAvailability previous) &&
                previous == availability)
            {
                return;
            }

            availabilityByRoute[routeId] = availability;
            DirectionAvailabilityChanged?.Invoke(
                routeId,
                availability);
        }

        private void CompleteAvailabilityRefresh()
        {
            for (int index = 0;
                 index < staleAvailabilityRouteIds.Count;
                 index++)
            {
                int routeId = staleAvailabilityRouteIds[index];
                availabilityByRoute.Remove(routeId);
                DirectionAvailabilityChanged?.Invoke(
                    routeId,
                    BusLineDirectionAvailability.None);
            }

            staleAvailabilityRouteIds.Clear();
        }

        private void Subscribe()
        {
            if (subscribed)
            {
                return;
            }

            stopRegistry.RegistryChanged += OnRegistryChanged;
            if (BusLines != null)
            {
                BusLines.LineCreated += OnLineChanged;
                BusLines.LineUpdated += OnLineChanged;
                BusLines.LineRemoved += OnLineChanged;
            }

            if (services?.Save != null)
            {
                services.Save.RestoreCompleted +=
                    OnRestoreCompleted;
            }

            if (services != null)
            {
                services.Events.Placed += OnPlaced;
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

            if (stopRegistry != null)
            {
                stopRegistry.RegistryChanged -=
                    OnRegistryChanged;
            }

            if (BusLines != null)
            {
                BusLines.LineCreated -= OnLineChanged;
                BusLines.LineUpdated -= OnLineChanged;
                BusLines.LineRemoved -= OnLineChanged;
            }

            if (services?.Save != null)
            {
                services.Save.RestoreCompleted -=
                    OnRestoreCompleted;
            }

            if (services != null)
            {
                services.Events.Placed -= OnPlaced;
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

        private void OnRegistryChanged()
        {
            if (!initialized)
            {
                return;
            }

            SynchronizeLegacyDefaultLine();
            RequestServiceRebuild();
        }

        private void OnPlaced(PlacedEvent placedEvent)
        {
            if (placedEvent.Type != TileType.Road)
            {
                return;
            }

            RefreshRoutesAfterRoadTopologyChanged(
                placedEvent.Tile);
        }

        private void RefreshRoutesAfterRoadTopologyChanged(
            Vector2Int changedTile)
        {
            if (!initialized ||
                !serviceRequested ||
                !IsInsideOperatingWindow())
            {
                return;
            }

            topologyRefreshInProgress = true;
            try
            {
                bool replanned = activeVehicles.Count > 0;
                for (int index = 0;
                     index < activeVehicles.Count;
                     index++)
                {
                    if (!activeVehicles[index]
                            .TryReplanFromCurrentPosition())
                    {
                        replanned = false;
                        break;
                    }
                }

                rebuildPending = false;
                recoveryPending = false;

                if (!replanned)
                {
                    RebuildVehicles(false);
                }

                if (verboseLogging)
                {
                    Debug.Log(
                        "[CityBusService] Refreshed routes after " +
                        $"road topology changed at {changedTile}.",
                        this);
                }
            }
            finally
            {
                topologyRefreshInProgress = false;
            }
        }

        private void OnLineChanged(BusLineData _)
        {
            if (synchronizingLegacyLine)
            {
                return;
            }

            RefreshLegacyRouteStops();
            RequestServiceRebuild();
        }

        private void RequestServiceRebuild()
        {
            if (!serviceRequested || !IsInsideOperatingWindow())
            {
                return;
            }

            rebuildPending = true;
            if (activeVehicles.Count == 0)
            {
                rebuildPending = false;
                RebuildVehicles(false);
            }
        }

        private void EvaluateOperatingWindow()
        {
            if (!autoStart)
            {
                return;
            }

            if (!IsInsideOperatingWindow())
            {
                RequestServiceStopAtNextStop();
                return;
            }

            if (serviceStopPending)
            {
                StopService();
            }

            if (vehicleVisible && activeVehicles.Count > 0)
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

        private void OnRestoreCompleted(
            RestoreCompletedEvent _)
        {
            StopService();
            SynchronizeLegacyDefaultLine();
            if (autoStart)
            {
                EvaluateOperatingWindow();
            }
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

        private void QueueVehicleRetirement(
            CityBusVehicleAgent agent)
        {
            if (agent == null ||
                vehiclesReadyToRetire.Contains(agent))
            {
                return;
            }

            vehiclesReadyToRetire.Add(agent);
        }

        private void ProcessVehiclesReadyToRetire()
        {
            if (vehiclesReadyToRetire.Count == 0)
            {
                return;
            }

            for (int index =
                     vehiclesReadyToRetire.Count - 1;
                 index >= 0;
                 index--)
            {
                RetireVehicleAtStop(
                    vehiclesReadyToRetire[index]);
            }

            vehiclesReadyToRetire.Clear();

            if (activeVehicles.Count == 0)
            {
                CompletePendingServiceStop();
            }
        }

        private void RetireVehicleAtStop(
            CityBusVehicleAgent agent)
        {
            if (agent == null ||
                !activeVehicles.Remove(agent))
            {
                return;
            }

            UnsubscribeAgent(agent);
            agent.Shutdown();

            if (agent.gameObject != gameObject)
            {
                DestroyVehicleObject(agent.gameObject);
            }

            primaryAgent = activeVehicles.Count > 0
                ? activeVehicles[0]
                : null;
        }

        private void CompletePendingServiceStop()
        {
            bool wasOperating = vehicleVisible;
            serviceStopPending = false;
            primaryAgent = null;
            SetVehicleVisible(false);
            fallbackRuntime?.ResetPassengers();
            fallbackRuntime?.SetState(
                BusOperatingState.OutOfService);

            if (wasOperating)
            {
                ServiceStopped?.Invoke();
            }
        }

        // Unity setup: place CityBusContent.prefab; route agents are created automatically.
    }
}
