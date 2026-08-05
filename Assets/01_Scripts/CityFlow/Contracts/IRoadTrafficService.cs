using System;
using UnityEngine;

namespace CityFlow.Contracts
{
    public enum RoadTrafficAgentKind
    {
        Car = 0,
        CityBus = 1,
        SchoolBus = 2,
        FeatureVehicle = 3
    }

    public enum RoadTrafficAgentState
    {
        Registered = 0,
        Ready = 1,
        Queued = 2,
        Moving = 3,
        Paused = 4,
        Arrived = 5,
        RouteUnavailable = 6,
        HoldingAtDestination = 7
    }

    public enum RoadTrafficArrivalPolicy
    {
        ReleaseAtDestination = 0,
        HoldAtDestination = 1
    }

    public readonly struct RoadTrafficAgentId :
        IEquatable<RoadTrafficAgentId>
    {
        public RoadTrafficAgentId(int value)
        {
            Value = value;
        }

        public int Value { get; }
        public bool IsValid => Value > 0;
        public static RoadTrafficAgentId Invalid => default;

        public bool Equals(RoadTrafficAgentId other) =>
            Value == other.Value;

        public override bool Equals(object obj) =>
            obj is RoadTrafficAgentId other && Equals(other);

        public override int GetHashCode() => Value;
        public override string ToString() => IsValid
            ? Value.ToString()
            : "Invalid";

        public static bool operator ==(
            RoadTrafficAgentId left,
            RoadTrafficAgentId right) => left.Equals(right);

        public static bool operator !=(
            RoadTrafficAgentId left,
            RoadTrafficAgentId right) => !left.Equals(right);
    }

    public readonly struct RoadTrafficAgentRegistration
    {
        public RoadTrafficAgentRegistration(
            RoadTrafficAgentKind kind,
            VehicleFootprint footprint)
        {
            Kind = kind;
            Footprint = new VehicleFootprint(
                footprint.SizeClass,
                footprint.LengthTiles,
                footprint.WidthTiles,
                footprint.MinimumGapTiles);
        }

        public RoadTrafficAgentRegistration(
            RoadTrafficAgentKind kind,
            float lengthTiles,
            float widthTiles)
            : this(
                kind,
                new VehicleFootprint(
                    VehicleSizeClass.Standard,
                    lengthTiles,
                    widthTiles,
                    0f))
        {
        }

        public RoadTrafficAgentKind Kind { get; }
        public VehicleFootprint Footprint { get; }
        public float LengthTiles => Footprint.LengthTiles;
        public float WidthTiles => Footprint.WidthTiles;
        public float MinimumGapTiles => Footprint.MinimumGapTiles;
    }

    public readonly struct RoadTrafficRouteRequest
    {
        public RoadTrafficRouteRequest(
            RoadTrafficAgentId agentId,
            RoadRoutePlan route,
            bool loop,
            RoadTrafficArrivalPolicy arrivalPolicy =
                RoadTrafficArrivalPolicy.ReleaseAtDestination,
            bool pauseOnEntry = false)
        {
            AgentId = agentId;
            Route = route;
            Loop = loop;
            ArrivalPolicy = arrivalPolicy;
            PauseOnEntry = pauseOnEntry;
        }

        public RoadTrafficAgentId AgentId { get; }
        public RoadRoutePlan Route { get; }
        public bool Loop { get; }
        public RoadTrafficArrivalPolicy ArrivalPolicy { get; }
        public bool PauseOnEntry { get; }
    }

    public readonly struct RoadTrafficSnapshot
    {
        public RoadTrafficSnapshot(
            RoadTrafficAgentId agentId,
            RoadTrafficAgentKind kind,
            VehicleFootprint footprint,
            RoadTrafficAgentState state,
            Vector2Int currentTile,
            Vector2Int nextTile,
            int routeTileIndex,
            int queueSlot,
            float queueOffsetTiles,
            bool isVisible,
            float intersectionProgress01,
            float linkProgress01,
            float roundaboutProgress01)
        {
            AgentId = agentId;
            Kind = kind;
            Footprint = footprint;
            State = state;
            CurrentTile = currentTile;
            NextTile = nextTile;
            RouteTileIndex = Mathf.Max(-1, routeTileIndex);
            QueueSlot = Mathf.Max(-1, queueSlot);
            QueueOffsetTiles = Mathf.Max(0f, queueOffsetTiles);
            IsVisible = isVisible;
            IntersectionProgress01 = intersectionProgress01 < 0f
                ? -1f
                : Mathf.Clamp01(intersectionProgress01);
            LinkProgress01 = Mathf.Clamp01(linkProgress01);
            RoundaboutProgress01 = roundaboutProgress01 < 0f
                ? -1f
                : Mathf.Clamp01(roundaboutProgress01);
        }

        public RoadTrafficAgentId AgentId { get; }
        public RoadTrafficAgentKind Kind { get; }
        public VehicleFootprint Footprint { get; }
        public RoadTrafficAgentState State { get; }
        public Vector2Int CurrentTile { get; }
        public Vector2Int NextTile { get; }
        public int RouteTileIndex { get; }
        public int QueueSlot { get; }
        public float QueueOffsetTiles { get; }
        public bool IsVisible { get; }
        public float IntersectionProgress01 { get; }
        public float LinkProgress01 { get; }
        public float RoundaboutProgress01 { get; }
    }

    public readonly struct RoadTrafficArrivalEvent
    {
        public RoadTrafficArrivalEvent(
            RoadTrafficAgentId agentId,
            RoadTrafficAgentKind kind,
            Vector2Int destination)
        {
            AgentId = agentId;
            Kind = kind;
            Destination = destination;
        }

        public RoadTrafficAgentId AgentId { get; }
        public RoadTrafficAgentKind Kind { get; }
        public Vector2Int Destination { get; }
    }

    public readonly struct RoadTrafficRecoveryRequest
    {
        public RoadTrafficRecoveryRequest(
            RoadTrafficAgentId agentId,
            RoadTrafficAgentKind kind,
            Vector2Int currentTile,
            Vector2Int destination,
            int blockedTicks)
        {
            AgentId = agentId;
            Kind = kind;
            CurrentTile = currentTile;
            Destination = destination;
            BlockedTicks = Mathf.Max(0, blockedTicks);
        }

        public RoadTrafficAgentId AgentId { get; }
        public RoadTrafficAgentKind Kind { get; }
        public Vector2Int CurrentTile { get; }
        public Vector2Int Destination { get; }
        public int BlockedTicks { get; }
    }

    public interface IRoadTrafficService
    {
        int RegisteredAgentCount { get; }
        float StepIntervalSeconds { get; }
        float StepProgress01 { get; }

        event Action<RoadTrafficArrivalEvent> AgentArrived;
        event Action<RoadTrafficRecoveryRequest> RecoveryRequested;

        bool TryRegisterAgent(
            RoadTrafficAgentRegistration registration,
            out RoadTrafficAgentId agentId);

        bool TryAssignRoute(RoadTrafficRouteRequest request);
        bool TryStartAgent(RoadTrafficAgentId agentId);
        bool TrySetAgentPaused(RoadTrafficAgentId agentId, bool paused);

        bool IsSafeHoldTile(Vector2Int tile);

        bool TryGetSnapshot(
            RoadTrafficAgentId agentId,
            out RoadTrafficSnapshot snapshot);

        bool TryRemoveAgent(RoadTrafficAgentId agentId);
    }

    // Unity integration: register an implementation in CityFlowServices.
}
