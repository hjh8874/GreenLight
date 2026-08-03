using System;
using System.Collections.Generic;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Sim
{
    internal sealed class RoadTrafficCoordinator :
        IRoadTrafficService,
        ICarRouteProvider,
        IDestinationOccupancyPolicy
    {
        private const int NetworkIdBase = 1000000;

        private sealed class Agent
        {
            public Agent(
                RoadTrafficAgentId id,
                int networkId,
                RoadTrafficAgentRegistration registration)
            {
                Id = id;
                NetworkId = networkId;
                Registration = registration;
            }

            public RoadTrafficAgentId Id { get; }
            public int NetworkId { get; }
            public RoadTrafficAgentRegistration Registration { get; }
            public List<Vector2Int> Route { get; } = new();
            public RoadTrafficAgentState State { get; set; } =
                RoadTrafficAgentState.Registered;
            public Vector2Int CurrentTile { get; set; }
            public Vector2Int NextTile { get; set; }
            public int RouteTileIndex { get; set; } = -1;
            public int QueueSlot { get; set; } = -1;
            public float QueueOffsetTiles { get; set; }
            public float IntersectionProgress01 { get; set; }
            public float LinkProgress01 { get; set; }
            public float RoundaboutProgress01 { get; set; }
            public bool Loop { get; set; }
            public bool Started { get; set; }
            public bool IsEnqueued { get; set; }
            public bool IsPaused { get; set; }
            public bool IsVisible { get; set; }
            public RoadTrafficArrivalPolicy ArrivalPolicy { get; set; }
            public bool IsHoldingAtDestination { get; set; }
        }

        private readonly RoadQueueNetwork network;
        private readonly Func<float> stepIntervalProvider;
        private readonly Func<float> stepProgressProvider;
        private readonly Dictionary<int, Agent> agentsById = new();
        private readonly Dictionary<int, Agent> agentsByNetworkId = new();
        private ICarRouteProvider coreRoutes;
        private int nextAgentId = 1;

        public RoadTrafficCoordinator(
            RoadQueueNetwork network,
            Func<float> stepIntervalProvider = null,
            Func<float> stepProgressProvider = null)
        {
            this.network = network ??
                throw new ArgumentNullException(nameof(network));
            this.stepIntervalProvider = stepIntervalProvider;
            this.stepProgressProvider = stepProgressProvider;
        }

        public int RegisteredAgentCount => agentsById.Count;
        public float StepIntervalSeconds => Mathf.Max(
            0.0001f,
            stepIntervalProvider?.Invoke() ?? 0.1f);
        public float StepProgress01 => Mathf.Clamp01(
            stepProgressProvider?.Invoke() ?? 1f);

        public event Action<RoadTrafficArrivalEvent> AgentArrived;

        public bool TryRegisterAgent(
            RoadTrafficAgentRegistration registration,
            out RoadTrafficAgentId agentId)
        {
            int idValue = nextAgentId++;
            agentId = new RoadTrafficAgentId(idValue);
            int networkId = checked(NetworkIdBase + idValue);
            var agent = new Agent(agentId, networkId, registration);
            agentsById.Add(idValue, agent);
            agentsByNetworkId.Add(networkId, agent);
            return true;
        }

        public bool TryAssignRoute(RoadTrafficRouteRequest request)
        {
            if (!TryGetAgent(request.AgentId, out Agent agent)
                || !IsValidRoute(request.Route.Tiles))
            {
                return false;
            }

            bool continueFromHeldDestination =
                agent.IsEnqueued &&
                agent.IsHoldingAtDestination &&
                request.Route.Tiles[0] == agent.CurrentTile;

            if (agent.IsEnqueued && !continueFromHeldDestination)
            {
                network.TryRemoveCarForRescue(agent.NetworkId);
            }

            agent.Route.Clear();
            for (int index = 0; index < request.Route.TileCount; index++)
            {
                agent.Route.Add(request.Route.Tiles[index]);
            }

            agent.Loop = request.Loop;
            agent.ArrivalPolicy = request.ArrivalPolicy;
            agent.Started = false;
            agent.IsEnqueued = continueFromHeldDestination;
            agent.IsPaused = continueFromHeldDestination;
            agent.IsHoldingAtDestination = false;
            agent.CurrentTile = agent.Route[0];
            agent.NextTile = agent.Route.Count > 1
                ? agent.Route[1]
                : agent.Route[0];
            agent.RouteTileIndex = 0;
            if (!continueFromHeldDestination)
            {
                agent.QueueSlot = -1;
                agent.QueueOffsetTiles = 0f;
            }
            agent.IntersectionProgress01 = -1f;
            agent.LinkProgress01 = 0f;
            agent.RoundaboutProgress01 = -1f;
            agent.State = continueFromHeldDestination
                ? RoadTrafficAgentState.Paused
                : RoadTrafficAgentState.Ready;
            agent.IsVisible = continueFromHeldDestination;
            return true;
        }

        public bool TryStartAgent(RoadTrafficAgentId agentId)
        {
            if (!TryGetAgent(agentId, out Agent agent)
                || agent.Route.Count == 0)
            {
                return false;
            }

            agent.Started = true;
            agent.IsPaused = false;
            agent.State = agent.IsEnqueued
                ? RoadTrafficAgentState.Moving
                : RoadTrafficAgentState.Ready;
            return true;
        }

        public bool TrySetAgentPaused(
            RoadTrafficAgentId agentId,
            bool paused)
        {
            if (!TryGetAgent(agentId, out Agent agent))
            {
                return false;
            }

            agent.IsPaused = paused;
            if (paused)
            {
                agent.State = RoadTrafficAgentState.Paused;
            }
            else if (agent.Started)
            {
                agent.State = agent.IsEnqueued
                    ? RoadTrafficAgentState.Moving
                    : RoadTrafficAgentState.Ready;
            }

            return true;
        }

        public bool IsSafeHoldTile(Vector2Int tile) =>
            network.IsSafeResumeTile(tile);

        public bool TryGetSnapshot(
            RoadTrafficAgentId agentId,
            out RoadTrafficSnapshot snapshot)
        {
            if (!TryGetAgent(agentId, out Agent agent))
            {
                snapshot = default;
                return false;
            }

            snapshot = new RoadTrafficSnapshot(
                agent.Id,
                agent.Registration.Kind,
                agent.Registration.Footprint,
                agent.State,
                agent.CurrentTile,
                agent.NextTile,
                agent.RouteTileIndex,
                agent.QueueSlot,
                agent.QueueOffsetTiles,
                agent.IsVisible,
                agent.IntersectionProgress01,
                agent.LinkProgress01,
                agent.RoundaboutProgress01);
            return true;
        }

        public bool TryRemoveAgent(RoadTrafficAgentId agentId)
        {
            if (!TryGetAgent(agentId, out Agent agent))
            {
                return false;
            }

            if (agent.IsEnqueued)
            {
                network.TryRemoveCarForRescue(agent.NetworkId);
            }

            agentsById.Remove(agent.Id.Value);
            agentsByNetworkId.Remove(agent.NetworkId);
            return true;
        }

        internal void PrepareStep(ICarRouteProvider coreRouteProvider)
        {
            coreRoutes = coreRouteProvider;
            foreach (Agent agent in agentsById.Values)
            {
                if (!agent.Started || agent.IsPaused || agent.IsEnqueued
                    || agent.Route.Count == 0)
                {
                    continue;
                }

                int startIndex = FindSafeResumeIndex(agent);
                if (startIndex < 0
                    || !TryGetEntryDirection(agent.Route, startIndex, out Dir entry))
                {
                    agent.State = RoadTrafficAgentState.RouteUnavailable;
                    continue;
                }

                if (!network.TryEnqueue(
                        agent.Route[startIndex],
                        entry,
                        agent.NetworkId,
                        agent.Registration.Footprint))
                {
                    agent.State = RoadTrafficAgentState.Ready;
                    continue;
                }

                agent.RouteTileIndex = startIndex;
                agent.CurrentTile = agent.Route[startIndex];
                agent.NextTile = GetNextTile(agent, startIndex);
                agent.IsEnqueued = true;
                agent.IsVisible = true;
                agent.State = RoadTrafficAgentState.Queued;
            }
        }

        internal int ProcessArrivals()
        {
            int externalArrivalCount = 0;
            for (int index = 0; index < network.ArrivalCount; index++)
            {
                ArrivalRecord arrival = network.GetArrival(index);
                if (!agentsByNetworkId.TryGetValue(
                        arrival.CarId,
                        out Agent agent))
                {
                    continue;
                }

                externalArrivalCount++;
                agent.Started = false;
                agent.RouteTileIndex = agent.Route.Count - 1;
                agent.CurrentTile = arrival.Tile;
                agent.NextTile = arrival.Tile;
                agent.IntersectionProgress01 = -1f;
                agent.LinkProgress01 = 0f;
                agent.RoundaboutProgress01 = -1f;
                agent.IsVisible = true;

                if (agent.ArrivalPolicy ==
                    RoadTrafficArrivalPolicy.HoldAtDestination)
                {
                    agent.IsEnqueued = true;
                    agent.IsPaused = true;
                    agent.IsHoldingAtDestination = true;
                    agent.State =
                        RoadTrafficAgentState.HoldingAtDestination;
                }
                else
                {
                    agent.IsEnqueued = false;
                    agent.IsPaused = false;
                    agent.IsHoldingAtDestination = false;
                    agent.QueueSlot = -1;
                    agent.QueueOffsetTiles = 0f;
                    agent.State = RoadTrafficAgentState.Arrived;
                }

                AgentArrived?.Invoke(new RoadTrafficArrivalEvent(
                    agent.Id,
                    agent.Registration.Kind,
                    arrival.Tile));
            }

            return externalArrivalCount;
        }

        internal void SynchronizeSnapshots()
        {
            foreach (Agent agent in agentsById.Values)
            {
                if (!agent.IsEnqueued)
                {
                    continue;
                }

                if (!network.TryLocateCar(
                        agent.NetworkId,
                        out Vector2Int tile,
                        out _,
                        out int queueSlot,
                        out float intersectionProgress,
                        out float linkProgress,
                        out int roundaboutCell,
                        out float queueOffsetTiles))
                {
                    continue;
                }

                int routeIndex = FindRouteIndex(
                    agent.Route,
                    tile,
                    agent.RouteTileIndex);
                if (routeIndex >= 0)
                {
                    agent.RouteTileIndex = routeIndex;
                }

                agent.CurrentTile = tile;
                agent.NextTile = GetNextTile(agent, agent.RouteTileIndex);
                agent.QueueSlot = queueSlot;
                agent.QueueOffsetTiles = queueOffsetTiles;
                agent.IntersectionProgress01 = intersectionProgress;
                agent.LinkProgress01 = linkProgress;
                agent.RoundaboutProgress01 = roundaboutCell >= 0
                    ? (roundaboutCell + 1f) / 4f
                    : -1f;
                agent.State = agent.IsPaused
                    ? agent.IsHoldingAtDestination
                        ? RoadTrafficAgentState.HoldingAtDestination
                        : RoadTrafficAgentState.Paused
                    : RoadTrafficAgentState.Moving;
            }
        }

        internal void ResetNetworkOccupancy()
        {
            foreach (Agent agent in agentsById.Values)
            {
                if (!agent.IsEnqueued)
                {
                    continue;
                }

                agent.IsEnqueued = false;
                agent.IsHoldingAtDestination = false;
                agent.QueueSlot = -1;
                agent.QueueOffsetTiles = 0f;
                agent.IntersectionProgress01 = -1f;
                agent.LinkProgress01 = 0f;
                agent.RoundaboutProgress01 = -1f;
                if (agent.Started)
                {
                    agent.State = RoadTrafficAgentState.Ready;
                }
            }
        }

        public bool TryGetNextTile(
            int carId,
            Vector2Int current,
            out Vector2Int next,
            out Dir entryDirAtNext)
        {
            next = default;
            entryDirAtNext = default;
            if (!agentsByNetworkId.TryGetValue(carId, out Agent agent))
            {
                return coreRoutes != null
                    && coreRoutes.TryGetNextTile(
                        carId,
                        current,
                        out next,
                        out entryDirAtNext);
            }

            if (agent.IsPaused || agent.Route.Count == 0)
            {
                return false;
            }

            int routeIndex = FindRouteIndex(
                agent.Route,
                current,
                agent.RouteTileIndex);
            if (routeIndex < 0)
            {
                return false;
            }

            int nextIndex = routeIndex + 1;
            if (nextIndex >= agent.Route.Count)
            {
                if (!agent.Loop)
                {
                    return false;
                }

                nextIndex = 0;
            }

            next = agent.Route[nextIndex];
            return TryDirection(next - current, out entryDirAtNext);
        }

        // 라이브 경로에선 코디네이터가 net.Step의 provider다 — 크레딧 질의를 CarSim에
        // 위임하지 않으면 트럭 게이트가 라이브에서만 무력화된다(TryGetNextTile과 동일 패턴).
        public bool TryConsumeAdvanceCredit(int carId, int tick)
        {
            if (agentsByNetworkId.ContainsKey(carId))
            {
                return true; // 버스 등 외부 에이전트는 속도 크레딧 미사용
            }

            return coreRoutes == null
                || coreRoutes.TryConsumeAdvanceCredit(carId, tick);
        }

        public bool IsDestination(int carId, Vector2Int tile)
        {
            if (!agentsByNetworkId.TryGetValue(carId, out Agent agent))
            {
                return coreRoutes != null
                    && coreRoutes.IsDestination(carId, tile);
            }

            return !agent.IsHoldingAtDestination
                && !agent.Loop
                && agent.Route.Count > 0
                && agent.Route[agent.Route.Count - 1] == tile;
        }

        public bool IsTransient(int carId)
        {
            if (agentsByNetworkId.ContainsKey(carId)) return false;
            return coreRoutes != null && coreRoutes.IsTransient(carId);
        }

        public bool ShouldHoldAtDestination(
            int carId,
            Vector2Int tile) =>
            agentsByNetworkId.TryGetValue(carId, out Agent agent) &&
            agent.ArrivalPolicy ==
                RoadTrafficArrivalPolicy.HoldAtDestination &&
            !agent.IsHoldingAtDestination &&
            agent.Route.Count > 0 &&
            agent.Route[agent.Route.Count - 1] == tile;

        private bool TryGetAgent(
            RoadTrafficAgentId agentId,
            out Agent agent)
        {
            agent = null;
            return agentId.IsValid
                && agentsById.TryGetValue(agentId.Value, out agent);
        }

        private int FindSafeResumeIndex(Agent agent)
        {
            int start = Mathf.Clamp(
                agent.RouteTileIndex,
                0,
                agent.Route.Count - 1);
            for (int index = start; index >= 0; index--)
            {
                if (network.IsSafeResumeTile(agent.Route[index]))
                {
                    return index;
                }
            }

            return -1;
        }

        private static Vector2Int GetNextTile(Agent agent, int routeIndex)
        {
            int nextIndex = routeIndex + 1;
            if (nextIndex < agent.Route.Count)
            {
                return agent.Route[nextIndex];
            }

            return agent.Loop && agent.Route.Count > 0
                ? agent.Route[0]
                : agent.CurrentTile;
        }

        private static int FindRouteIndex(
            IReadOnlyList<Vector2Int> route,
            Vector2Int tile,
            int preferredIndex)
        {
            int start = Mathf.Clamp(preferredIndex, 0, route.Count - 1);
            for (int index = start; index < route.Count; index++)
            {
                if (route[index] == tile)
                {
                    return index;
                }
            }

            for (int index = 0; index < start; index++)
            {
                if (route[index] == tile)
                {
                    return index;
                }
            }

            return -1;
        }

        private static bool IsValidRoute(
            IReadOnlyList<Vector2Int> route)
        {
            if (route == null || route.Count == 0)
            {
                return false;
            }

            for (int index = 1; index < route.Count; index++)
            {
                Vector2Int delta = route[index] - route[index - 1];
                if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) != 1)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryGetEntryDirection(
            IReadOnlyList<Vector2Int> route,
            int index,
            out Dir direction)
        {
            if (route.Count <= 1)
            {
                direction = Dir.N;
                return true;
            }

            Vector2Int delta = index > 0
                ? route[index] - route[index - 1]
                : route[1] - route[0];
            return TryDirection(delta, out direction);
        }

        private static bool TryDirection(
            Vector2Int delta,
            out Dir direction)
        {
            if (delta == Vector2Int.up) direction = Dir.N;
            else if (delta == Vector2Int.right) direction = Dir.E;
            else if (delta == Vector2Int.down) direction = Dir.S;
            else if (delta == Vector2Int.left) direction = Dir.W;
            else
            {
                direction = default;
                return false;
            }

            return true;
        }

        // Unity integration: SimEngine owns this service and CityBootstrap
        // publishes it through CityFlowServices for traffic agents.
    }
}
