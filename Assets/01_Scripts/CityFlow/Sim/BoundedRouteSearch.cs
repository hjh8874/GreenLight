using System;
using System.Collections.Generic;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Sim
{
    internal sealed class BoundedPathResult
    {
        public BoundedPathResult(List<Vector2Int> path, float cost)
        {
            Path = path;
            Cost = cost;
        }

        public List<Vector2Int> Path { get; }
        public float Cost { get; }
    }

    internal sealed class BoundedRouteSearch
    {
        private const int DirectionCount = 5;
        private const int NoIncomingDirection = 4;

        private static readonly int[] DeltaX = { 0, 1, 0, -1 };
        private static readonly int[] DeltaY = { 1, 0, -1, 0 };
        private static readonly int[] CardinalToDirection = { 3, 0, 1, 2 };

        private readonly int worldWidth;
        private readonly float[] costs;
        private readonly bool[] completed;
        private readonly int[] cameFrom;
        private readonly SortedSet<OpenNode> open = new SortedSet<OpenNode>();

        public BoundedRouteSearch(int worldWidth, int maximumRegionTileCount)
        {
            this.worldWidth = worldWidth;
            int stateCapacity = maximumRegionTileCount * DirectionCount;
            costs = new float[stateCapacity];
            completed = new bool[stateCapacity];
            cameFrom = new int[stateCapacity];
        }

        public BoundedPathResult FindPath(
            CityGrid grid,
            RouteSearchBounds bounds,
            Vector2Int from,
            Vector2Int to,
            int? incomingDirection,
            Vector2Int? requiredNext,
            bool requiredNextIsHighway,
            int? requiredFirstDirection,
            int? requiredArrivalDirection,
            bool forbidImmediateReverse,
            Vector2Int? forbiddenReentryTile,
            Vector2Int? forbiddenTransitTile,
            in SimConfig config,
            IReadOnlyDictionary<Vector2Int, Vector2Int> oneways,
            IReadOnlyDictionary<Vector2Int, TurnMode> turnSigns,
            int[] rampPartner,
            float[] load)
        {
            if (!bounds.Contains(from) || !bounds.Contains(to) ||
                !IsRoad(grid, from) || !IsRoad(grid, to))
            {
                return null;
            }

            // Route consumers use tile identity as their cursor. Do not let a
            // portal land on the destination with the wrong heading and then
            // revisit that same tile, because the first visit would be treated
            // as the actual arrival.
            if (from == to && incomingDirection.HasValue &&
                requiredArrivalDirection.HasValue &&
                incomingDirection.Value != requiredArrivalDirection.Value)
            {
                return null;
            }

            int stateCount = bounds.TileCount * DirectionCount;
            for (int i = 0; i < stateCount; i++)
            {
                costs[i] = float.MaxValue;
                completed[i] = false;
                cameFrom[i] = -1;
            }

            open.Clear();
            int startDirection = incomingDirection ?? NoIncomingDirection;
            int startState = ToState(bounds, from, startDirection);
            costs[startState] = 0f;
            open.Add(new OpenNode(0f, startState));

            float capacityInverse = config.RoadCapacity > 0f
                ? 1f / config.RoadCapacity
                : 0f;

            while (open.Count > 0)
            {
                OpenNode currentOpen = open.Min;
                open.Remove(currentOpen);
                int currentState = currentOpen.State;
                if (completed[currentState] ||
                    currentOpen.Cost != costs[currentState])
                {
                    continue;
                }

                Vector2Int current = ToTile(bounds, currentState);
                int currentDirection = currentState % DirectionCount;
                bool isSearchStart = currentState == startState;
                if (current == to &&
                    (!requiredArrivalDirection.HasValue ||
                     currentDirection == requiredArrivalDirection.Value) &&
                    TryComplete(
                        grid,
                        current,
                        currentDirection,
                        requiredNext,
                        requiredNextIsHighway,
                        requiredFirstDirection,
                        isSearchStart,
                        forbidImmediateReverse,
                        forbiddenReentryTile,
                        oneways,
                        turnSigns,
                        load,
                        capacityInverse,
                        config.RoutingCongestionWeight,
                        costs[currentState],
                        out float completedCost))
                {
                    List<Vector2Int> path = BuildPath(
                        bounds,
                        currentState,
                        requiredNext);
                    return new BoundedPathResult(path, completedCost);
                }

                completed[currentState] = true;
                ExpandRoadNeighbors(
                    grid,
                    bounds,
                    current,
                    currentDirection,
                    currentState,
                    from,
                    to,
                    requiredFirstDirection,
                    requiredArrivalDirection,
                    isSearchStart,
                    forbidImmediateReverse,
                    forbiddenReentryTile,
                    forbiddenTransitTile,
                    config,
                    oneways,
                    turnSigns,
                    load,
                    capacityInverse);
                ExpandHighwayNeighbor(
                    grid,
                    bounds,
                    current,
                    currentDirection,
                    currentState,
                    from,
                    to,
                    requiredFirstDirection,
                    requiredArrivalDirection,
                    isSearchStart,
                    forbidImmediateReverse,
                    forbiddenReentryTile,
                    forbiddenTransitTile,
                    turnSigns,
                    rampPartner);
            }

            return null;
        }

        public static int DirectionFromDelta(Vector2Int delta)
        {
            if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
            {
                return delta.x >= 0 ? 0 : 2;
            }

            return delta.y >= 0 ? 3 : 1;
        }

        private void ExpandRoadNeighbors(
            CityGrid grid,
            RouteSearchBounds bounds,
            Vector2Int current,
            int currentDirection,
            int currentState,
            Vector2Int routeStart,
            Vector2Int routeGoal,
            int? requiredFirstDirection,
            int? requiredArrivalDirection,
            bool isSearchStart,
            bool forbidImmediateReverse,
            Vector2Int? forbiddenReentryTile,
            Vector2Int? forbiddenTransitTile,
            in SimConfig config,
            IReadOnlyDictionary<Vector2Int, Vector2Int> oneways,
            IReadOnlyDictionary<Vector2Int, TurnMode> turnSigns,
            float[] load,
            float capacityInverse)
        {
            for (int cardinal = 0; cardinal < DeltaX.Length; cardinal++)
            {
                Vector2Int next = new Vector2Int(
                    current.x + DeltaX[cardinal],
                    current.y + DeltaY[cardinal]);
                if (!bounds.Contains(next) || !IsRoad(grid, next))
                {
                    continue;
                }

                int nextDirection = CardinalToDirection[cardinal];
                if (forbiddenReentryTile.HasValue &&
                    current != forbiddenReentryTile.Value &&
                    next == forbiddenReentryTile.Value)
                {
                    continue;
                }

                if (forbiddenTransitTile.HasValue &&
                    next == forbiddenTransitTile.Value &&
                    routeGoal != forbiddenTransitTile.Value)
                {
                    continue;
                }

                if (isSearchStart &&
                    requiredFirstDirection.HasValue &&
                    nextDirection != requiredFirstDirection.Value)
                {
                    continue;
                }

                if (requiredFirstDirection.HasValue &&
                    current != routeStart &&
                    next == routeStart)
                {
                    continue;
                }

                if (requiredArrivalDirection.HasValue &&
                    next == routeGoal &&
                    nextDirection != requiredArrivalDirection.Value)
                {
                    continue;
                }

                if (forbidImmediateReverse &&
                    currentDirection != NoIncomingDirection &&
                    IsOppositeDirection(currentDirection, nextDirection))
                {
                    continue;
                }

                if (!CanTraverseRoad(
                        current,
                        next,
                        currentDirection,
                        nextDirection,
                        oneways,
                        turnSigns))
                {
                    continue;
                }

                int nextState = ToState(bounds, next, nextDirection);
                float stepCost = GetRoadStepCost(
                    next,
                    load,
                    capacityInverse,
                    config.RoutingCongestionWeight);
                Relax(currentState, nextState, stepCost);
            }
        }

        private void ExpandHighwayNeighbor(
            CityGrid grid,
            RouteSearchBounds bounds,
            Vector2Int current,
            int currentDirection,
            int currentState,
            Vector2Int routeStart,
            Vector2Int routeGoal,
            int? requiredFirstDirection,
            int? requiredArrivalDirection,
            bool isSearchStart,
            bool forbidImmediateReverse,
            Vector2Int? forbiddenReentryTile,
            Vector2Int? forbiddenTransitTile,
            IReadOnlyDictionary<Vector2Int, TurnMode> turnSigns,
            int[] rampPartner)
        {
            int currentFlat = ToWorldIndex(current);
            if (rampPartner == null || currentFlat < 0 ||
                currentFlat >= rampPartner.Length)
            {
                return;
            }

            int partnerFlat = rampPartner[currentFlat];
            if (partnerFlat < 0)
            {
                return;
            }

            Vector2Int partner = new Vector2Int(
                partnerFlat % worldWidth,
                partnerFlat / worldWidth);
            if (!bounds.Contains(partner) || !IsRoad(grid, partner))
            {
                return;
            }

            int nextDirection = DirectionFromDelta(partner - current);
            if (forbiddenReentryTile.HasValue &&
                current != forbiddenReentryTile.Value &&
                partner == forbiddenReentryTile.Value)
            {
                return;
            }

            if (forbiddenTransitTile.HasValue &&
                partner == forbiddenTransitTile.Value &&
                routeGoal != forbiddenTransitTile.Value)
            {
                return;
            }

            if (isSearchStart &&
                requiredFirstDirection.HasValue &&
                partner - current !=
                DeltaFromDirection(requiredFirstDirection.Value))
            {
                return;
            }

            if (requiredFirstDirection.HasValue &&
                current != routeStart &&
                partner == routeStart)
            {
                return;
            }

            if (requiredArrivalDirection.HasValue &&
                partner == routeGoal &&
                partner - current !=
                DeltaFromDirection(requiredArrivalDirection.Value))
            {
                return;
            }

            if (forbidImmediateReverse &&
                currentDirection != NoIncomingDirection &&
                IsOppositeDirection(currentDirection, nextDirection))
            {
                return;
            }

            if (!CanLeaveTurnSign(
                    current,
                    currentDirection,
                    nextDirection,
                    turnSigns))
            {
                return;
            }

            int nextState = ToState(bounds, partner, nextDirection);
            Relax(
                currentState,
                nextState,
                GetHighwayCost(current, partner));
        }

        private bool TryComplete(
            CityGrid grid,
            Vector2Int current,
            int currentDirection,
            Vector2Int? requiredNext,
            bool requiredNextIsHighway,
            int? requiredFirstDirection,
            bool isSearchStart,
            bool forbidImmediateReverse,
            Vector2Int? forbiddenReentryTile,
            IReadOnlyDictionary<Vector2Int, Vector2Int> oneways,
            IReadOnlyDictionary<Vector2Int, TurnMode> turnSigns,
            float[] load,
            float capacityInverse,
            float congestionWeight,
            float currentCost,
            out float completedCost)
        {
            completedCost = currentCost;
            if (!requiredNext.HasValue)
            {
                return true;
            }

            Vector2Int next = requiredNext.Value;
            if (!IsRoad(grid, next))
            {
                return false;
            }

            if (forbiddenReentryTile.HasValue &&
                current != forbiddenReentryTile.Value &&
                next == forbiddenReentryTile.Value)
            {
                return false;
            }

            int nextDirection = DirectionFromDelta(next - current);
            if (isSearchStart &&
                requiredFirstDirection.HasValue &&
                (nextDirection != requiredFirstDirection.Value ||
                 (requiredNextIsHighway &&
                  next - current !=
                  DeltaFromDirection(requiredFirstDirection.Value))))
            {
                return false;
            }

            if (forbidImmediateReverse &&
                currentDirection != NoIncomingDirection &&
                IsOppositeDirection(currentDirection, nextDirection))
            {
                return false;
            }

            if (requiredNextIsHighway)
            {
                if (!CanLeaveTurnSign(
                        current,
                        currentDirection,
                        nextDirection,
                        turnSigns))
                {
                    return false;
                }

                completedCost += GetHighwayCost(current, next);
                return true;
            }

            if (Mathf.Abs(next.x - current.x) +
                Mathf.Abs(next.y - current.y) != 1 ||
                !CanTraverseRoad(
                    current,
                    next,
                    currentDirection,
                    nextDirection,
                    oneways,
                    turnSigns))
            {
                return false;
            }

            completedCost += GetRoadStepCost(
                next,
                load,
                capacityInverse,
                congestionWeight);
            return true;
        }

        private static bool CanTraverseRoad(
            Vector2Int current,
            Vector2Int next,
            int currentDirection,
            int nextDirection,
            IReadOnlyDictionary<Vector2Int, Vector2Int> oneways,
            IReadOnlyDictionary<Vector2Int, TurnMode> turnSigns)
        {
            Vector2Int step = next - current;
            if (oneways != null && oneways.Count > 0)
            {
                if (oneways.TryGetValue(current, out Vector2Int currentOneway) &&
                    step != currentOneway)
                {
                    return false;
                }

                if (oneways.TryGetValue(next, out Vector2Int nextOneway) &&
                    step == -nextOneway)
                {
                    return false;
                }
            }

            return CanLeaveTurnSign(
                current,
                currentDirection,
                nextDirection,
                turnSigns);
        }

        private static bool CanLeaveTurnSign(
            Vector2Int current,
            int currentDirection,
            int nextDirection,
            IReadOnlyDictionary<Vector2Int, TurnMode> turnSigns)
        {
            if (currentDirection == NoIncomingDirection ||
                turnSigns == null ||
                !turnSigns.TryGetValue(current, out TurnMode mode))
            {
                return true;
            }

            int expected = mode == TurnMode.LeftOnly
                ? (currentDirection + 3) % 4
                : (currentDirection + 1) % 4;
            return nextDirection == expected;
        }

        private static bool IsOppositeDirection(
            int currentDirection,
            int nextDirection) =>
            nextDirection == (currentDirection + 2) % 4;

        private static Vector2Int DeltaFromDirection(int direction) =>
            direction switch
            {
                0 => Vector2Int.right,
                1 => Vector2Int.down,
                2 => Vector2Int.left,
                _ => Vector2Int.up
            };

        private void Relax(int currentState, int nextState, float stepCost)
        {
            if (completed[nextState])
            {
                return;
            }

            float candidate = costs[currentState] + stepCost;
            if (candidate >= costs[nextState])
            {
                return;
            }

            costs[nextState] = candidate;
            cameFrom[nextState] = currentState;
            open.Add(new OpenNode(candidate, nextState));
        }

        private List<Vector2Int> BuildPath(
            RouteSearchBounds bounds,
            int goalState,
            Vector2Int? requiredNext)
        {
            var path = new List<Vector2Int>();
            for (int state = goalState;
                 state != -1;
                 state = cameFrom[state])
            {
                path.Add(ToTile(bounds, state));
            }

            path.Reverse();
            if (requiredNext.HasValue)
            {
                path.Add(requiredNext.Value);
            }

            return path;
        }

        private int ToState(
            RouteSearchBounds bounds,
            Vector2Int tile,
            int direction) =>
            bounds.ToLocalIndex(tile) * DirectionCount + direction;

        private static Vector2Int ToTile(
            RouteSearchBounds bounds,
            int state) =>
            bounds.FromLocalIndex(state / DirectionCount);

        private int ToWorldIndex(Vector2Int tile) =>
            tile.y * worldWidth + tile.x;

        private float GetRoadStepCost(
            Vector2Int destination,
            float[] load,
            float capacityInverse,
            float congestionWeight)
        {
            int index = ToWorldIndex(destination);
            float tileLoad = load != null && index >= 0 && index < load.Length
                ? load[index]
                : 0f;
            return 1f + congestionWeight * tileLoad * capacityInverse;
        }

        private static float GetHighwayCost(
            Vector2Int from,
            Vector2Int to) =>
            (Mathf.Abs(to.x - from.x) + Mathf.Abs(to.y - from.y)) / 2f;

        private static bool IsRoad(CityGrid grid, Vector2Int tile) =>
            tile.x >= 0 && tile.x < grid.Width &&
            tile.y >= 0 && tile.y < grid.Height &&
            grid.GetTile(tile) == TileType.Road;

        private readonly struct OpenNode : IComparable<OpenNode>
        {
            public OpenNode(float cost, int state)
            {
                Cost = cost;
                State = state;
            }

            public float Cost { get; }
            public int State { get; }

            public int CompareTo(OpenNode other)
            {
                int costComparison = Cost.CompareTo(other.Cost);
                return costComparison != 0
                    ? costComparison
                    : State.CompareTo(other.State);
            }
        }
    }
}
