using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.Content
{
    public sealed class PoliceCallSystem :
        MonoBehaviour,
        ICityFlowServiceConsumer,
        IPoliceDispatchService,
        IPoliceDispatchSaveSource
    {
        private const string PoliceStationBuildingId =
            "police_station";

        [SerializeField]
        private PoliceDispatchConfigSO config;

        [SerializeField]
        private PolicePatrolScheduler patrolScheduler;

        [Header("Play Mode Test")]
        [SerializeField]
        private Vector2Int testTarget;

        [SerializeField]
        private string testExternalRequestId = "police_test";

        private readonly List<PoliceCall> calls = new();
        private readonly List<Vector2Int> policeStations = new();

        private CityFlowServices services;
        private IReadOnlyTileData tileData;
        private ISpecialBuildingService specialBuildings;
        private int nextCallId = 1;
        private bool initialized;
        private bool subscribed;

        public int ActiveCallCount => calls.Count;
        public PoliceDispatchConfigSO Config => config;

        public event Action<PoliceCallSnapshot> CallCreated;
        public event Action<PoliceCallSnapshot> CallChanged;
        public event Action<PoliceCallSnapshot> CallRemoved;

        public void Initialize(CityFlowServices cityServices)
        {
            if (!isActiveAndEnabled || initialized)
            {
                return;
            }

            if (cityServices?.Events == null ||
                cityServices.TileData == null ||
                config == null)
            {
                Debug.LogError(
                    "[PoliceCallSystem] Services, events, tile data, and config are required.",
                    this);
                return;
            }

            services = cityServices;
            tileData = cityServices.TileData;
            patrolScheduler ??= GetComponent<PolicePatrolScheduler>();
            if (!services.RegisterPoliceDispatch(this))
            {
                Debug.LogError(
                    "[PoliceCallSystem] Another police dispatch service is already registered.",
                    this);
                return;
            }

            initialized = true;
            Subscribe();
            BindSpecialBuildings(services.SpecialBuildings);
            TryAssignWaitingCalls();
            Debug.Log(
                "[PoliceCallSystem] Police dispatch API registered.",
                this);
        }

        private void Update()
        {
            for (int index = calls.Count - 1;
                 index >= 0;
                 index--)
            {
                PoliceCall call = calls[index];
                if (call.State != PoliceCallState.Handling ||
                    !call.AdvanceHandling(Time.deltaTime))
                {
                    continue;
                }

                call.BeginReturn();
                PublishChanged(call);
            }

            TryAssignWaitingCalls();
        }

        private void OnEnable()
        {
            if (initialized)
            {
                Subscribe();
                BindSpecialBuildings(services?.SpecialBuildings);
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
            BindSpecialBuildings(null);
        }

        private void OnDestroy()
        {
            Unsubscribe();
            BindSpecialBuildings(null);
        }

        public bool TryRequestDispatch(
            PoliceDispatchRequest request,
            out int callId)
        {
            callId = -1;
            if (!initialized ||
                calls.Count >= config.MaximumActiveCalls ||
                !IsValidTarget(request.Target))
            {
                return false;
            }

            Vector2Int target = ResolveAnchor(request.Target);
            var call = new PoliceCall(
                nextCallId++,
                new PoliceDispatchRequest(
                    target,
                    request.ExternalRequestId,
                    request.HandlingSeconds),
                config.DefaultHandlingSeconds);
            calls.Add(call);
            callId = call.CallId;
            PoliceCallSnapshot snapshot = call.CreateSnapshot();
            CallCreated?.Invoke(snapshot);
            services.Events.Publish(
                new PoliceDispatchAlertEvent(snapshot));
            TryAssignCall(call);
            return true;
        }

        public bool TryCancelDispatch(int callId)
        {
            if (!TryFindCall(callId, out PoliceCall call, out int index))
            {
                return false;
            }

            if (call.State == PoliceCallState.WaitingForVehicle)
            {
                call.CompleteFailure(
                    PoliceCallFailureReason.Cancelled);
                PublishChanged(call);
                PublishOutcomeAndRemove(index);
                return true;
            }

            if (call.State is PoliceCallState.VehicleOutbound
                or PoliceCallState.Handling)
            {
                call.BeginFailedReturn(
                    PoliceCallFailureReason.Cancelled);
                PublishChanged(call);
                return true;
            }

            return false;
        }

        public bool TryGetCall(
            int callId,
            out PoliceCallSnapshot call)
        {
            if (TryFindCall(callId, out PoliceCall found, out _))
            {
                call = found.CreateSnapshot();
                return true;
            }

            call = default;
            return false;
        }

        public PoliceCallSnapshot[] CreateActiveCallSnapshot()
        {
            var snapshot = new PoliceCallSnapshot[calls.Count];
            for (int index = 0; index < calls.Count; index++)
            {
                snapshot[index] = calls[index].CreateSnapshot();
            }

            return snapshot;
        }

        public bool TryMarkVehicleArrived(int callId)
        {
            if (!TryFindCall(callId, out PoliceCall call, out _) ||
                call.State != PoliceCallState.VehicleOutbound)
            {
                return false;
            }

            call.BeginHandling();
            PublishChanged(call);
            return true;
        }

        public bool TryMarkVehicleReturned(int callId)
        {
            if (!TryFindCall(callId, out PoliceCall call, out int index) ||
                call.State is not (
                    PoliceCallState.VehicleReturning
                    or PoliceCallState.VehicleReturningAfterFailure))
            {
                return false;
            }

            if (call.State == PoliceCallState.VehicleReturning)
            {
                call.Complete();
            }
            else
            {
                call.CompleteFailure(call.FailureReason);
            }

            PublishChanged(call);
            PublishOutcomeAndRemove(index);
            TryAssignWaitingCalls();
            return true;
        }

        public bool TryFailRouteUnavailable(
            int callId,
            bool vehicleLeftStation)
        {
            if (!TryFindCall(callId, out PoliceCall call, out int index))
            {
                return false;
            }

            if (vehicleLeftStation)
            {
                call.BeginFailedReturn(
                    PoliceCallFailureReason.DestinationUnreachable);
                PublishChanged(call);
            }
            else
            {
                call.CompleteFailure(
                    PoliceCallFailureReason.DestinationUnreachable);
                PublishChanged(call);
                PublishOutcomeAndRemove(index);
            }

            return true;
        }

        public PoliceDispatchSaveData CreateSnapshot()
        {
            var entries = new PoliceCallEntrySaveData[calls.Count];
            for (int index = 0; index < calls.Count; index++)
            {
                PoliceCall call = calls[index];
                entries[index] = new PoliceCallEntrySaveData
                {
                    CallId = call.CallId,
                    ExternalRequestId = call.ExternalRequestId,
                    TargetX = call.Target.x,
                    TargetY = call.Target.y,
                    StationX = call.AssignedStation.x,
                    StationY = call.AssignedStation.y,
                    AssignedVehicleSlot = call.AssignedVehicleSlot,
                    State = (int)call.State,
                    HandlingSeconds = call.HandlingSeconds,
                    RemainingHandlingSeconds =
                        call.RemainingHandlingSeconds,
                    FailureReason = (int)call.FailureReason
                };
            }

            return new PoliceDispatchSaveData
            {
                NextCallId = nextCallId,
                HasLastPatrolTotalDay =
                    patrolScheduler != null &&
                    patrolScheduler.LastScheduledTotalDay >= 0L,
                LastPatrolTotalDay =
                    patrolScheduler?.LastScheduledTotalDay ?? 0L,
                ActiveCalls = entries
            };
        }

        public void RestoreSnapshot(PoliceDispatchSaveData snapshot)
        {
            calls.Clear();
            nextCallId = Mathf.Max(1, snapshot?.NextCallId ?? 1);
            patrolScheduler ??= GetComponent<PolicePatrolScheduler>();
            patrolScheduler?.RestoreLastScheduledDay(
                snapshot?.HasLastPatrolTotalDay == true,
                snapshot?.LastPatrolTotalDay ?? 0L);
            PoliceCallEntrySaveData[] entries = snapshot?.ActiveCalls;
            if (entries == null)
            {
                return;
            }

            for (int index = 0; index < entries.Length; index++)
            {
                PoliceCallEntrySaveData saved = entries[index];
                if (saved == null ||
                    !Enum.IsDefined(
                        typeof(PoliceCallState),
                        saved.State))
                {
                    continue;
                }

                PoliceCallState state =
                    (PoliceCallState)saved.State;
                if (state is PoliceCallState.Completed
                    or PoliceCallState.Failed)
                {
                    continue;
                }

                calls.Add(PoliceCall.Restore(
                    saved.CallId,
                    saved.ExternalRequestId,
                    new Vector2Int(saved.TargetX, saved.TargetY),
                    new Vector2Int(saved.StationX, saved.StationY),
                    saved.AssignedVehicleSlot,
                    state,
                    saved.HandlingSeconds > 0f
                        ? saved.HandlingSeconds
                        : config.DefaultHandlingSeconds,
                    saved.RemainingHandlingSeconds,
                    Enum.IsDefined(
                        typeof(PoliceCallFailureReason),
                        saved.FailureReason)
                        ? (PoliceCallFailureReason)saved.FailureReason
                        : PoliceCallFailureReason.None));
            }

            RebuildPoliceStations();
            TryAssignWaitingCalls();
        }

        public bool TryRequestTestDispatchNow()
        {
            bool requested = TryRequestDispatch(
                new PoliceDispatchRequest(
                    testTarget,
                    testExternalRequestId),
                out int callId);
            Debug.Log(
                requested
                    ? $"[PoliceCallSystem] Created test call #{callId} at {testTarget}."
                    : $"[PoliceCallSystem] Could not create a test call at {testTarget}.",
                this);
            return requested;
        }

        [ContextMenu("Testing/Dispatch Police Car Now")]
        private void DispatchTestCallFromContextMenu()
        {
            TryRequestTestDispatchNow();
        }

        private void Subscribe()
        {
            if (subscribed || services == null)
            {
                return;
            }

            services.SpecialBuildingsRegistered +=
                HandleSpecialBuildingsRegistered;
            services.Events.Placed += HandlePlaced;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || services == null)
            {
                return;
            }

            services.SpecialBuildingsRegistered -=
                HandleSpecialBuildingsRegistered;
            services.Events.Placed -= HandlePlaced;
            subscribed = false;
        }

        private void HandleSpecialBuildingsRegistered(
            ISpecialBuildingService service)
        {
            BindSpecialBuildings(service);
            TryAssignWaitingCalls();
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

            RebuildPoliceStations();
        }

        private void HandleBuildingChanged(
            SpecialBuildingChangedEvent changed)
        {
            if (!string.Equals(
                    changed.Building.BuildingId,
                    PoliceStationBuildingId,
                    StringComparison.Ordinal))
            {
                return;
            }

            RebuildPoliceStations();
            if (changed.IsRemove)
            {
                HandlePoliceStationRemoved(
                    changed.Building.Anchor);
            }
            else
            {
                TryAssignWaitingCalls();
            }
        }

        private void HandleBuildingsRestored()
        {
            RebuildPoliceStations();
            ValidateAssignedStations();
            TryAssignWaitingCalls();
        }

        private void HandlePlaced(PlacedEvent placed)
        {
            if (!placed.IsRemove)
            {
                return;
            }

            Vector2Int removedAnchor = ResolveAnchor(placed.Tile);
            for (int index = calls.Count - 1;
                 index >= 0;
                 index--)
            {
                PoliceCall call = calls[index];
                if (call.Target != removedAnchor)
                {
                    continue;
                }

                call.BeginFailedReturn(
                    PoliceCallFailureReason.TargetRemoved);
                PublishChanged(call);
            }
        }

        private void RebuildPoliceStations()
        {
            policeStations.Clear();
            if (specialBuildings == null)
            {
                return;
            }

            SpecialBuildingInstance[] buildings =
                specialBuildings.CreateBuildingSnapshot();
            for (int index = 0; index < buildings.Length; index++)
            {
                if (string.Equals(
                        buildings[index].BuildingId,
                        PoliceStationBuildingId,
                        StringComparison.Ordinal))
                {
                    policeStations.Add(buildings[index].Anchor);
                }
            }

            policeStations.Sort(CompareTiles);
        }

        private void ValidateAssignedStations()
        {
            for (int index = calls.Count - 1;
                 index >= 0;
                 index--)
            {
                PoliceCall call = calls[index];
                if (call.State == PoliceCallState.WaitingForVehicle ||
                    policeStations.Contains(call.AssignedStation))
                {
                    continue;
                }

                call.BeginFailedReturn(
                    PoliceCallFailureReason.PoliceStationRemoved);
                PublishChanged(call);
            }
        }

        private void HandlePoliceStationRemoved(Vector2Int station)
        {
            for (int index = calls.Count - 1;
                 index >= 0;
                 index--)
            {
                PoliceCall call = calls[index];
                if (call.AssignedStation != station || call.IsFinished)
                {
                    continue;
                }

                call.BeginFailedReturn(
                    PoliceCallFailureReason.PoliceStationRemoved);
                PublishChanged(call);
            }
        }

        private void TryAssignWaitingCalls()
        {
            for (int index = 0; index < calls.Count; index++)
            {
                if (calls[index].State ==
                    PoliceCallState.WaitingForVehicle)
                {
                    TryAssignCall(calls[index]);
                }
            }
        }

        private bool TryAssignCall(PoliceCall call)
        {
            if (call == null ||
                call.State != PoliceCallState.WaitingForVehicle ||
                !TryFindAvailableVehicle(
                    call.Target,
                    out Vector2Int station,
                    out int vehicleSlot))
            {
                return false;
            }

            call.Dispatch(station, vehicleSlot);
            PublishChanged(call);
            return true;
        }

        private bool TryFindAvailableVehicle(
            Vector2Int target,
            out Vector2Int station,
            out int vehicleSlot)
        {
            station = default;
            vehicleSlot = -1;
            int bestDistance = int.MaxValue;

            for (int stationIndex = 0;
                 stationIndex < policeStations.Count;
                 stationIndex++)
            {
                Vector2Int candidate = policeStations[stationIndex];
                int freeSlot = FindFreeVehicleSlot(candidate);
                if (freeSlot < 0)
                {
                    continue;
                }

                int distance = Mathf.Abs(candidate.x - target.x) +
                               Mathf.Abs(candidate.y - target.y);
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                station = candidate;
                vehicleSlot = freeSlot;
            }

            return vehicleSlot >= 0;
        }

        private int FindFreeVehicleSlot(Vector2Int station)
        {
            for (int slot = 0;
                 slot < config.VehiclesPerStation;
                 slot++)
            {
                bool occupied = false;
                for (int callIndex = 0;
                     callIndex < calls.Count;
                     callIndex++)
                {
                    PoliceCall call = calls[callIndex];
                    if (!call.IsFinished &&
                        call.State != PoliceCallState.WaitingForVehicle &&
                        call.AssignedStation == station &&
                        call.AssignedVehicleSlot == slot)
                    {
                        occupied = true;
                        break;
                    }
                }

                if (!occupied)
                {
                    return slot;
                }
            }

            return -1;
        }

        private bool IsValidTarget(Vector2Int tile)
        {
            if (services?.WorldGrid != null &&
                (!services.WorldGrid.IsInsideWorld(tile) ||
                 !services.WorldGrid.IsTileUnlocked(tile)))
            {
                return false;
            }

            TileType type = tileData.GetTileType(tile);
            if (type == TileType.Empty)
            {
                return false;
            }

            if (type != TileType.SpecialBuilding)
            {
                return true;
            }

            return specialBuildings != null &&
                   specialBuildings.TryGetBuilding(
                       tile,
                       out SpecialBuildingInstance special) &&
                   !string.Equals(
                       special.BuildingId,
                       PoliceStationBuildingId,
                       StringComparison.Ordinal);
        }

        private Vector2Int ResolveAnchor(Vector2Int tile)
        {
            return tileData != null &&
                   tileData.TryGetFootprintAnchor(
                       tile,
                       out Vector2Int anchor)
                ? anchor
                : tile;
        }

        private bool TryFindCall(
            int callId,
            out PoliceCall call,
            out int index)
        {
            for (index = 0; index < calls.Count; index++)
            {
                if (calls[index].CallId == callId)
                {
                    call = calls[index];
                    return true;
                }
            }

            call = null;
            index = -1;
            return false;
        }

        private void PublishChanged(PoliceCall call)
        {
            CallChanged?.Invoke(call.CreateSnapshot());
        }

        private void PublishOutcomeAndRemove(int index)
        {
            PoliceCall call = calls[index];
            PoliceCallSnapshot snapshot = call.CreateSnapshot();
            services.Events.Publish(
                new PoliceDispatchOutcomeEvent(snapshot));
            calls.RemoveAt(index);
            CallRemoved?.Invoke(snapshot);
        }

        private static int CompareTiles(
            Vector2Int left,
            Vector2Int right)
        {
            int y = left.y.CompareTo(right.y);
            return y != 0 ? y : left.x.CompareTo(right.x);
        }

        // Unity setup: this component is prewired in PoliceContent.prefab.
    }
}
