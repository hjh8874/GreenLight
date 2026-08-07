using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.Content
{
    public sealed class PoliceDispatchService :
        MonoBehaviour,
        ICityFlowServiceConsumer,
        IPolicePatrolService
    {
        private const string PoliceStationBuildingId =
            "police_station";

        [SerializeField]
        private PoliceCallSystem callSystem;

        [SerializeField]
        private PoliceDispatchConfigSO config;

        [SerializeField]
        private GameObject policeVehiclePrefab;

        [SerializeField]
        private PolicePatrolScheduler patrolScheduler;

        private readonly Dictionary<int, PoliceVehicleAgent>
            activeVehicles = new();
        private readonly Dictionary<Vector2Int, List<PoliceVehicleAgent>>
            stationVehicles = new();
        private readonly List<Vector2Int> stationReleaseBuffer = new();
        private readonly List<Vector2Int> orderedStationBuffer = new();
        private readonly PolicePatrolRoutePlanner patrolPlanner = new();

        private CityFlowServices services;
        private ISpecialBuildingService specialBuildings;
        private bool initialized;
        private bool subscribed;
        private bool fleetSyncPending;
        private bool callSyncPending;

        public int ActiveVehicleCount => activeVehicles.Count;
        public int TotalVehicleCount
        {
            get
            {
                int count = 0;
                foreach (List<PoliceVehicleAgent> vehicles
                         in stationVehicles.Values)
                {
                    count += vehicles.Count;
                }

                return count;
            }
        }

        public event Action<PolicePatrolEvent> PatrolStarted;
        public event Action<PolicePatrolEvent> PatrolFinished;

        public void Initialize(CityFlowServices cityServices)
        {
            if (!isActiveAndEnabled || initialized)
            {
                return;
            }

            ResolveReferences();
            if (cityServices == null ||
                callSystem == null ||
                config == null ||
                policeVehiclePrefab == null ||
                policeVehiclePrefab.GetComponent<PoliceVehicleAgent>() == null)
            {
                Debug.LogError(
                    "[PoliceDispatchService] Call system, config, services, and police vehicle prefab are required.",
                    this);
                return;
            }

            services = cityServices;
            if (!services.RegisterPolicePatrol(this))
            {
                Debug.LogError(
                    "[PoliceDispatchService] Another police patrol service is already registered.",
                    this);
                return;
            }

            initialized = true;
            Subscribe();
            BindSpecialBuildings(services.SpecialBuildings);
            SynchronizeStationFleet();
            SynchronizeActiveCalls();
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            if (!initialized)
            {
                return;
            }

            Subscribe();
            BindSpecialBuildings(services?.SpecialBuildings);
            SynchronizeStationFleet();
            SynchronizeActiveCalls();
        }

        private void OnDisable()
        {
            Unsubscribe();
            BindSpecialBuildings(null);
            ReleaseAllVehicles();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            BindSpecialBuildings(null);
        }

        private void LateUpdate()
        {
            if (fleetSyncPending)
            {
                fleetSyncPending = false;
                SynchronizeStationFleet();
            }

            if (callSyncPending)
            {
                callSyncPending = false;
                SynchronizeActiveCalls();
            }
        }

        private void ResolveReferences()
        {
            callSystem ??= GetComponent<PoliceCallSystem>();
            config ??= callSystem?.Config;
            patrolScheduler ??= GetComponent<PolicePatrolScheduler>();
        }

        private void Subscribe()
        {
            if (subscribed || callSystem == null || services == null)
            {
                return;
            }

            callSystem.CallCreated += HandleCallChanged;
            callSystem.CallChanged += HandleCallChanged;
            callSystem.CallRemoved += HandleCallRemoved;
            if (patrolScheduler != null)
            {
                patrolScheduler.PatrolDue += HandlePatrolDue;
            }
            services.SpecialBuildingsRegistered +=
                HandleSpecialBuildingsRegistered;
            if (services.Save != null)
            {
                services.Save.RestoreCompleted +=
                    HandleRestoreCompleted;
            }

            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || callSystem == null || services == null)
            {
                return;
            }

            callSystem.CallCreated -= HandleCallChanged;
            callSystem.CallChanged -= HandleCallChanged;
            callSystem.CallRemoved -= HandleCallRemoved;
            if (patrolScheduler != null)
            {
                patrolScheduler.PatrolDue -= HandlePatrolDue;
            }
            services.SpecialBuildingsRegistered -=
                HandleSpecialBuildingsRegistered;
            if (services.Save != null)
            {
                services.Save.RestoreCompleted -=
                    HandleRestoreCompleted;
            }

            subscribed = false;
        }

        private void BindSpecialBuildings(
            ISpecialBuildingService service)
        {
            if (ReferenceEquals(specialBuildings, service))
            {
                return;
            }

            if (specialBuildings != null)
            {
                specialBuildings.BuildingChanged -=
                    HandleBuildingChanged;
                specialBuildings.BuildingsRestored -=
                    HandleBuildingsRestored;
            }

            specialBuildings = service;
            if (specialBuildings != null)
            {
                specialBuildings.BuildingChanged +=
                    HandleBuildingChanged;
                specialBuildings.BuildingsRestored +=
                    HandleBuildingsRestored;
            }
        }

        private void HandleSpecialBuildingsRegistered(
            ISpecialBuildingService service)
        {
            BindSpecialBuildings(service);
            fleetSyncPending = true;
            callSyncPending = true;
        }

        private void HandleBuildingChanged(
            SpecialBuildingChangedEvent changed)
        {
            if (string.Equals(
                    changed.Building.BuildingId,
                    PoliceStationBuildingId,
                    StringComparison.Ordinal))
            {
                fleetSyncPending = true;
            }
        }

        private void HandleBuildingsRestored()
        {
            fleetSyncPending = true;
            callSyncPending = true;
        }

        private void HandleRestoreCompleted(RestoreCompletedEvent _)
        {
            fleetSyncPending = true;
            callSyncPending = true;
        }

        private void HandleCallChanged(PoliceCallSnapshot call)
        {
            if (call.State is PoliceCallState.VehicleOutbound
                or PoliceCallState.Handling)
            {
                EnsureVehicle(call);
                return;
            }

            if (call.State is PoliceCallState.VehicleReturning
                or PoliceCallState.VehicleReturningAfterFailure)
            {
                EnsureVehicle(call);
                if (activeVehicles.TryGetValue(
                        call.CallId,
                        out PoliceVehicleAgent returningVehicle))
                {
                    returningVehicle.BeginReturn(call);
                }
            }
        }

        private void HandleCallRemoved(PoliceCallSnapshot call)
        {
            ReleaseVehicle(call.CallId);
            fleetSyncPending = true;
        }

        public bool TryStartPatrol(Vector2Int station)
        {
            long totalDay = Math.Max(
                0L,
                services?.GameCalendar?.TotalDays ?? 0L);
            return TryStartPatrol(
                station,
                totalDay,
                out _);
        }

        private void HandlePatrolDue(long totalDay)
        {
            SynchronizeStationFleet();
            orderedStationBuffer.Clear();
            foreach (Vector2Int station in stationVehicles.Keys)
            {
                orderedStationBuffer.Add(station);
            }

            orderedStationBuffer.Sort(CompareCoordinates);
            for (int stationIndex = 0;
                 stationIndex < orderedStationBuffer.Count;
                 stationIndex++)
            {
                Vector2Int station =
                    orderedStationBuffer[stationIndex];
                bool startedAny = false;
                PolicePatrolOutcome lastOutcome =
                    PolicePatrolOutcome.SkippedNoVehicle;
                for (int patrolIndex = 0;
                     patrolIndex < config.PatrolVehiclesPerStation;
                     patrolIndex++)
                {
                    if (!TryStartPatrol(
                            station,
                            totalDay,
                            out lastOutcome))
                    {
                        break;
                    }

                    startedAny = true;
                }

                if (!startedAny)
                {
                    PatrolFinished?.Invoke(
                        new PolicePatrolEvent(
                            station,
                            totalDay,
                            false,
                            0,
                            lastOutcome));
                    Debug.LogWarning(
                        $"[PoliceDispatchService] Daily patrol skipped at {station}: {lastOutcome}.",
                        this);
                }
            }

            orderedStationBuffer.Clear();
        }

        private bool TryStartPatrol(
            Vector2Int station,
            long totalDay,
            out PolicePatrolOutcome failureOutcome)
        {
            failureOutcome = PolicePatrolOutcome.SkippedNoVehicle;
            SynchronizeStationFleet();
            if (!stationVehicles.TryGetValue(
                    station,
                    out List<PoliceVehicleAgent> vehicles))
            {
                return false;
            }

            PoliceVehicleAgent agent = null;
            for (int index = 0; index < vehicles.Count; index++)
            {
                if (vehicles[index] != null &&
                    !vehicles[index].IsBusy)
                {
                    agent = vehicles[index];
                    break;
                }
            }

            if (agent == null)
            {
                return false;
            }

            if (!agent.TryGetHomeAccessRoad(out Vector2Int accessRoad) ||
                !patrolPlanner.TryBuildPlan(
                    station,
                    accessRoad,
                    config.PatrolAreaSize,
                    services.TileData,
                    services.WorldGrid,
                    out PolicePatrolPlan plan))
            {
                failureOutcome = PolicePatrolOutcome.SkippedNoRoute;
                return false;
            }

            if (!agent.TryStartPatrol(plan, totalDay))
            {
                failureOutcome = PolicePatrolOutcome.RouteUnavailable;
                return false;
            }

            PolicePatrolEvent patrolEvent = new(
                station,
                totalDay,
                plan.UsedLoop,
                plan.Route.TileCount,
                PolicePatrolOutcome.Started);
            PatrolStarted?.Invoke(patrolEvent);
            Debug.Log(
                $"[PoliceDispatchService] Patrol started at {station}. " +
                $"loop={plan.UsedLoop}, routeTiles={plan.Route.TileCount}, " +
                $"chunks={plan.ScannedChunkCount}, scannedTiles={plan.ScannedTileCount}.",
                this);
            return true;
        }

        private void SynchronizeActiveCalls()
        {
            PoliceCallSnapshot[] calls =
                callSystem.CreateActiveCallSnapshot();
            for (int index = 0; index < calls.Length; index++)
            {
                HandleCallChanged(calls[index]);
            }
        }

        private void EnsureVehicle(PoliceCallSnapshot call)
        {
            if (activeVehicles.ContainsKey(call.CallId))
            {
                return;
            }

            SynchronizeStationFleet();
            PoliceVehicleAgent agent = FindVehicle(
                call.AssignedStation,
                call.AssignedVehicleSlot);
            if (agent == null || agent.IsAssigned)
            {
                Debug.LogError(
                    $"[PoliceDispatchService] Police car slot {call.AssignedVehicleSlot} is unavailable at {call.AssignedStation} for call #{call.CallId}.",
                    this);
                return;
            }

            activeVehicles.Add(call.CallId, agent);
            agent.Initialize(services);
            bool assigned = call.State == PoliceCallState.VehicleOutbound
                ? agent.Assign(call, callSystem)
                : agent.RestoreAssignment(call, callSystem);
            if (assigned)
            {
                return;
            }

            Debug.LogError(
                $"[PoliceDispatchService] Could not assign police car to call #{call.CallId}.",
                this);
            ReleaseVehicle(call.CallId);
        }

        private void SynchronizeStationFleet()
        {
            if (!initialized || specialBuildings == null)
            {
                return;
            }

            SpecialBuildingInstance[] buildings =
                specialBuildings.CreateBuildingSnapshot();
            var currentStations = new HashSet<Vector2Int>();
            for (int index = 0; index < buildings.Length; index++)
            {
                if (string.Equals(
                        buildings[index].BuildingId,
                        PoliceStationBuildingId,
                        StringComparison.Ordinal))
                {
                    currentStations.Add(buildings[index].Anchor);
                }
            }

            stationReleaseBuffer.Clear();
            foreach (Vector2Int station in stationVehicles.Keys)
            {
                if (!currentStations.Contains(station))
                {
                    stationReleaseBuffer.Add(station);
                }
            }

            for (int index = 0;
                 index < stationReleaseBuffer.Count;
                 index++)
            {
                RemoveStationFleet(stationReleaseBuffer[index]);
            }

            foreach (Vector2Int station in currentStations)
            {
                if (!stationVehicles.TryGetValue(
                        station,
                        out List<PoliceVehicleAgent> vehicles))
                {
                    vehicles = new List<PoliceVehicleAgent>(
                        config.VehiclesPerStation);
                    stationVehicles.Add(station, vehicles);
                }

                while (vehicles.Count < config.VehiclesPerStation)
                {
                    PoliceVehicleAgent agent = CreateVehicle(
                        station,
                        vehicles.Count);
                    if (agent == null)
                    {
                        break;
                    }

                    vehicles.Add(agent);
                }
            }

            stationReleaseBuffer.Clear();
        }

        private PoliceVehicleAgent CreateVehicle(
            Vector2Int station,
            int parkingSlot)
        {
            GameObject instance = Instantiate(
                policeVehiclePrefab,
                transform);
            instance.name =
                $"PoliceCar_{station.x}_{station.y}_{parkingSlot}";
            PoliceVehicleAgent agent =
                instance.GetComponent<PoliceVehicleAgent>();
            agent.Initialize(services);
            agent.PatrolEnded += HandlePatrolEnded;
            if (agent.PrepareAtStation(station, parkingSlot))
            {
                return agent;
            }

            DestroyVehicle(agent);
            return null;
        }

        private void HandlePatrolEnded(
            PoliceVehicleAgent _,
            PolicePatrolEvent patrolEvent)
        {
            PatrolFinished?.Invoke(patrolEvent);
            Debug.Log(
                $"[PoliceDispatchService] Patrol finished at {patrolEvent.Station}: " +
                $"{patrolEvent.Outcome}.",
                this);
        }

        private PoliceVehicleAgent FindVehicle(
            Vector2Int station,
            int parkingSlot)
        {
            if (!stationVehicles.TryGetValue(
                    station,
                    out List<PoliceVehicleAgent> vehicles) ||
                parkingSlot < 0 ||
                parkingSlot >= vehicles.Count)
            {
                return null;
            }

            return vehicles[parkingSlot];
        }

        private void ReleaseVehicle(int callId)
        {
            if (!activeVehicles.Remove(
                    callId,
                    out PoliceVehicleAgent agent) ||
                agent == null)
            {
                return;
            }

            agent.Release();
        }

        private void RemoveStationFleet(Vector2Int station)
        {
            if (!stationVehicles.TryGetValue(
                    station,
                    out List<PoliceVehicleAgent> vehicles))
            {
                return;
            }

            for (int index = 0; index < vehicles.Count; index++)
            {
                if (vehicles[index] != null &&
                    vehicles[index].IsAssigned)
                {
                    return;
                }
            }

            stationVehicles.Remove(station);
            for (int index = 0; index < vehicles.Count; index++)
            {
                DestroyVehicle(vehicles[index]);
            }
        }

        private void ReleaseAllVehicles()
        {
            foreach (List<PoliceVehicleAgent> vehicles
                     in stationVehicles.Values)
            {
                for (int index = 0; index < vehicles.Count; index++)
                {
                    DestroyVehicle(vehicles[index]);
                }
            }

            activeVehicles.Clear();
            stationVehicles.Clear();
            stationReleaseBuffer.Clear();
        }

        private void DestroyVehicle(PoliceVehicleAgent agent)
        {
            if (agent == null)
            {
                return;
            }

            agent.PatrolEnded -= HandlePatrolEnded;
            agent.Release();
            if (Application.isPlaying)
            {
                Destroy(agent.gameObject);
            }
            else
            {
                DestroyImmediate(agent.gameObject);
            }
        }


        [ContextMenu("Testing/Start Daily Patrol Now")]
        private void StartDailyPatrolFromContextMenu()
        {
            HandlePatrolDue(
                Math.Max(
                    0L,
                    services?.GameCalendar?.TotalDays ?? 0L));
        }

        private static int CompareCoordinates(
            Vector2Int left,
            Vector2Int right)
        {
            int yCompare = left.y.CompareTo(right.y);
            return yCompare != 0
                ? yCompare
                : left.x.CompareTo(right.x);
        }

        // Unity setup: this component is prewired in PoliceContent.prefab.
    }
}
