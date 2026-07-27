using System;
using System.Collections.Generic;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Sim
{
    internal readonly struct RouteSearchDiagnostics
    {
        public RouteSearchDiagnostics(
            bool usedChunkedSearch,
            int regionCount,
            int portalCount,
            int visitedRegionCount,
            int localSearchCount,
            int maxLocalTileCount)
        {
            UsedChunkedSearch = usedChunkedSearch;
            RegionCount = regionCount;
            PortalCount = portalCount;
            VisitedRegionCount = visitedRegionCount;
            LocalSearchCount = localSearchCount;
            MaxLocalTileCount = maxLocalTileCount;
        }

        public bool UsedChunkedSearch { get; }
        public int RegionCount { get; }
        public int PortalCount { get; }
        public int VisitedRegionCount { get; }
        public int LocalSearchCount { get; }
        public int MaxLocalTileCount { get; }
    }

    internal sealed class ChunkedRouteSearch
    {
        private readonly RouteRegionLayout layout;
        private readonly BoundedRouteSearch localSearch;
        private readonly SortedSet<PortalOpenNode> open =
            new SortedSet<PortalOpenNode>();

        private CityGrid cachedGrid;
        private int cachedTopologyVersion = -1;
        private int cachedHighwayHash;
        private RoutePortalIndex cachedPortalIndex;
        private int localSearchCount;
        private int maxLocalTileCount;

        public ChunkedRouteSearch(int width, int height, int regionSize)
        {
            layout = new RouteRegionLayout(width, height, regionSize);
            localSearch = new BoundedRouteSearch(
                width,
                regionSize * regionSize);
        }

        public bool IsRequired => layout.RequiresChunkedSearch;
        public RouteSearchDiagnostics LastDiagnostics { get; private set; }

        public List<Vector2Int> Search(
            CityGrid grid,
            Vector2Int from,
            Vector2Int to,
            in SimConfig config,
            IReadOnlyDictionary<Vector2Int, Vector2Int> oneways,
            IReadOnlyDictionary<Vector2Int, TurnMode> turnSigns,
            IReadOnlyList<HighwayLink> highways,
            int[] rampPartner,
            float[] load)
        {
            localSearchCount = 0;
            maxLocalTileCount = 0;

            int startRegion = layout.GetRegionIndex(from);
            int goalRegion = layout.GetRegionIndex(to);
            if (startRegion < 0 || goalRegion < 0)
            {
                SetDiagnostics(0, 0, 0);
                return null;
            }

            RoutePortalIndex portalIndex = GetPortalIndex(grid, highways);
            IReadOnlyList<RoutePortal> portals = portalIndex.Portals;
            var visitedRegions = new bool[layout.RegionCount];
            visitedRegions[startRegion] = true;

            float bestGoalCost = float.MaxValue;
            int goalPreviousPortal = -1;
            List<Vector2Int> goalSegment = null;

            BoundedPathResult direct = FindLocalPath(
                grid,
                startRegion,
                from,
                to,
                null,
                null,
                false,
                config,
                oneways,
                turnSigns,
                rampPartner,
                load);
            if (direct != null)
            {
                bestGoalCost = direct.Cost;
                goalSegment = direct.Path;
            }

            int portalCount = portals.Count;
            var portalCosts = new float[portalCount];
            var completed = new bool[portalCount];
            var previousPortal = new int[portalCount];
            var segmentToPortal = new List<Vector2Int>[portalCount];
            for (int i = 0; i < portalCount; i++)
            {
                portalCosts[i] = float.MaxValue;
                previousPortal[i] = -1;
            }

            open.Clear();
            IReadOnlyList<int> startPortals =
                portalIndex.GetOutgoing(startRegion);
            for (int i = 0; i < startPortals.Count; i++)
            {
                int portalId = startPortals[i];
                RoutePortal portal = portals[portalId];
                BoundedPathResult segment = FindLocalPath(
                    grid,
                    startRegion,
                    from,
                    portal.From,
                    null,
                    portal.To,
                    portal.IsHighway,
                    config,
                    oneways,
                    turnSigns,
                    rampPartner,
                    load);
                TryRelaxPortal(
                    portalId,
                    -1,
                    segment,
                    0f,
                    portalCosts,
                    previousPortal,
                    segmentToPortal);
            }

            while (open.Count > 0)
            {
                PortalOpenNode currentOpen = open.Min;
                open.Remove(currentOpen);
                int currentPortalId = currentOpen.PortalId;
                if (completed[currentPortalId] ||
                    currentOpen.Cost != portalCosts[currentPortalId])
                {
                    continue;
                }

                if (currentOpen.Cost >= bestGoalCost)
                {
                    break;
                }

                completed[currentPortalId] = true;
                RoutePortal arrival = portals[currentPortalId];
                int currentRegion = arrival.TargetRegion;
                visitedRegions[currentRegion] = true;
                int incomingDirection =
                    BoundedRouteSearch.DirectionFromDelta(
                        arrival.To - arrival.From);

                if (currentRegion == goalRegion)
                {
                    BoundedPathResult finish = FindLocalPath(
                        grid,
                        currentRegion,
                        arrival.To,
                        to,
                        incomingDirection,
                        null,
                        false,
                        config,
                        oneways,
                        turnSigns,
                        rampPartner,
                        load);
                    if (finish != null &&
                        currentOpen.Cost + finish.Cost < bestGoalCost)
                    {
                        bestGoalCost = currentOpen.Cost + finish.Cost;
                        goalPreviousPortal = currentPortalId;
                        goalSegment = finish.Path;
                    }
                }

                IReadOnlyList<int> outgoing =
                    portalIndex.GetOutgoing(currentRegion);
                for (int i = 0; i < outgoing.Count; i++)
                {
                    int nextPortalId = outgoing[i];
                    if (completed[nextPortalId])
                    {
                        continue;
                    }

                    RoutePortal nextPortal = portals[nextPortalId];
                    BoundedPathResult segment = FindLocalPath(
                        grid,
                        currentRegion,
                        arrival.To,
                        nextPortal.From,
                        incomingDirection,
                        nextPortal.To,
                        nextPortal.IsHighway,
                        config,
                        oneways,
                        turnSigns,
                        rampPartner,
                        load);
                    TryRelaxPortal(
                        nextPortalId,
                        currentPortalId,
                        segment,
                        currentOpen.Cost,
                        portalCosts,
                        previousPortal,
                        segmentToPortal);
                }
            }

            SetDiagnostics(
                portalCount,
                CountVisitedRegions(visitedRegions),
                localSearchCount);
            return goalSegment == null
                ? null
                : ComposePath(
                    goalPreviousPortal,
                    goalSegment,
                    previousPortal,
                    segmentToPortal);
        }

        private BoundedPathResult FindLocalPath(
            CityGrid grid,
            int regionIndex,
            Vector2Int from,
            Vector2Int to,
            int? incomingDirection,
            Vector2Int? requiredNext,
            bool requiredNextIsHighway,
            in SimConfig config,
            IReadOnlyDictionary<Vector2Int, Vector2Int> oneways,
            IReadOnlyDictionary<Vector2Int, TurnMode> turnSigns,
            int[] rampPartner,
            float[] load)
        {
            RouteSearchBounds bounds = layout.GetBounds(regionIndex);
            localSearchCount++;
            maxLocalTileCount = Mathf.Max(
                maxLocalTileCount,
                bounds.TileCount);
            return localSearch.FindPath(
                grid,
                bounds,
                from,
                to,
                incomingDirection,
                requiredNext,
                requiredNextIsHighway,
                config,
                oneways,
                turnSigns,
                rampPartner,
                load);
        }

        private void TryRelaxPortal(
            int portalId,
            int sourcePortalId,
            BoundedPathResult segment,
            float sourceCost,
            float[] portalCosts,
            int[] previousPortal,
            List<Vector2Int>[] segmentToPortal)
        {
            if (segment == null)
            {
                return;
            }

            float candidate = sourceCost + segment.Cost;
            if (candidate >= portalCosts[portalId])
            {
                return;
            }

            portalCosts[portalId] = candidate;
            previousPortal[portalId] = sourcePortalId;
            segmentToPortal[portalId] = segment.Path;
            open.Add(new PortalOpenNode(candidate, portalId));
        }

        private RoutePortalIndex GetPortalIndex(
            CityGrid grid,
            IReadOnlyList<HighwayLink> highways)
        {
            int highwayHash = CalculateHighwayHash(highways);
            if (ReferenceEquals(cachedGrid, grid) &&
                cachedTopologyVersion == grid.TopologyVersion &&
                cachedHighwayHash == highwayHash &&
                cachedPortalIndex != null)
            {
                return cachedPortalIndex;
            }

            cachedGrid = grid;
            cachedTopologyVersion = grid.TopologyVersion;
            cachedHighwayHash = highwayHash;
            cachedPortalIndex = RoutePortalIndex.Build(
                grid,
                layout,
                highways);
            return cachedPortalIndex;
        }

        private void SetDiagnostics(
            int portalCount,
            int visitedRegionCount,
            int searchCount)
        {
            LastDiagnostics = new RouteSearchDiagnostics(
                true,
                layout.RegionCount,
                portalCount,
                visitedRegionCount,
                searchCount,
                maxLocalTileCount);
        }

        private static List<Vector2Int> ComposePath(
            int goalPreviousPortal,
            List<Vector2Int> goalSegment,
            int[] previousPortal,
            List<Vector2Int>[] segmentToPortal)
        {
            var segments = new List<List<Vector2Int>>();
            segments.Add(goalSegment);
            for (int portal = goalPreviousPortal;
                 portal != -1;
                 portal = previousPortal[portal])
            {
                segments.Add(segmentToPortal[portal]);
            }

            segments.Reverse();
            var path = new List<Vector2Int>();
            for (int segmentIndex = 0;
                 segmentIndex < segments.Count;
                 segmentIndex++)
            {
                List<Vector2Int> segment = segments[segmentIndex];
                if (segment == null || segment.Count == 0)
                {
                    continue;
                }

                int start = path.Count > 0 && path[path.Count - 1] == segment[0]
                    ? 1
                    : 0;
                for (int tileIndex = start;
                     tileIndex < segment.Count;
                     tileIndex++)
                {
                    path.Add(segment[tileIndex]);
                }
            }

            return path;
        }

        private static int CountVisitedRegions(bool[] visited)
        {
            int count = 0;
            for (int i = 0; i < visited.Length; i++)
            {
                if (visited[i])
                {
                    count++;
                }
            }

            return count;
        }

        private static int CalculateHighwayHash(
            IReadOnlyList<HighwayLink> highways)
        {
            unchecked
            {
                int hash = 17;
                if (highways == null)
                {
                    return hash;
                }

                for (int i = 0; i < highways.Count; i++)
                {
                    HighwayLink link = highways[i];
                    hash = hash * 31 + link.A.x;
                    hash = hash * 31 + link.A.y;
                    hash = hash * 31 + link.B.x;
                    hash = hash * 31 + link.B.y;
                }

                return hash;
            }
        }

        private readonly struct PortalOpenNode : IComparable<PortalOpenNode>
        {
            public PortalOpenNode(float cost, int portalId)
            {
                Cost = cost;
                PortalId = portalId;
            }

            public float Cost { get; }
            public int PortalId { get; }

            public int CompareTo(PortalOpenNode other)
            {
                int costComparison = Cost.CompareTo(other.Cost);
                return costComparison != 0
                    ? costComparison
                    : PortalId.CompareTo(other.PortalId);
            }
        }
    }
}
