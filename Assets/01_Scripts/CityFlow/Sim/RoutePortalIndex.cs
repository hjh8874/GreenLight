using System;
using System.Collections.Generic;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Sim
{
    internal sealed class RoutePortalIndex
    {
        private static readonly IReadOnlyList<int> EmptyPortalList =
            Array.Empty<int>();

        private readonly List<RoutePortal> portals = new List<RoutePortal>();
        private readonly List<int>[] outgoingByRegion;

        private RoutePortalIndex(int regionCount)
        {
            outgoingByRegion = new List<int>[regionCount];
        }

        public IReadOnlyList<RoutePortal> Portals => portals;

        public static RoutePortalIndex Build(
            CityGrid grid,
            RouteRegionLayout layout,
            IReadOnlyList<HighwayLink> highways)
        {
            var index = new RoutePortalIndex(layout.RegionCount);
            index.AddVerticalBoundaries(grid, layout);
            index.AddHorizontalBoundaries(grid, layout);
            index.AddHighways(grid, layout, highways);
            return index;
        }

        public IReadOnlyList<int> GetOutgoing(int regionIndex)
        {
            if (regionIndex < 0 || regionIndex >= outgoingByRegion.Length)
            {
                return EmptyPortalList;
            }

            return outgoingByRegion[regionIndex] ?? EmptyPortalList;
        }

        private void AddVerticalBoundaries(
            CityGrid grid,
            RouteRegionLayout layout)
        {
            for (int boundaryX = layout.RegionSize;
                 boundaryX < layout.WorldWidth;
                 boundaryX += layout.RegionSize)
            {
                for (int y = 0; y < layout.WorldHeight; y++)
                {
                    Vector2Int left = new Vector2Int(boundaryX - 1, y);
                    Vector2Int right = new Vector2Int(boundaryX, y);
                    AddRoadCrossing(grid, layout, left, right);
                }
            }
        }

        private void AddHorizontalBoundaries(
            CityGrid grid,
            RouteRegionLayout layout)
        {
            for (int boundaryY = layout.RegionSize;
                 boundaryY < layout.WorldHeight;
                 boundaryY += layout.RegionSize)
            {
                for (int x = 0; x < layout.WorldWidth; x++)
                {
                    Vector2Int bottom = new Vector2Int(x, boundaryY - 1);
                    Vector2Int top = new Vector2Int(x, boundaryY);
                    AddRoadCrossing(grid, layout, bottom, top);
                }
            }
        }

        private void AddRoadCrossing(
            CityGrid grid,
            RouteRegionLayout layout,
            Vector2Int first,
            Vector2Int second)
        {
            if (!IsRoad(grid, first) || !IsRoad(grid, second))
            {
                return;
            }

            int firstRegion = layout.GetRegionIndex(first);
            int secondRegion = layout.GetRegionIndex(second);
            AddPortal(first, second, firstRegion, secondRegion, false);
            AddPortal(second, first, secondRegion, firstRegion, false);
        }

        private void AddHighways(
            CityGrid grid,
            RouteRegionLayout layout,
            IReadOnlyList<HighwayLink> highways)
        {
            if (highways == null)
            {
                return;
            }

            for (int i = 0; i < highways.Count; i++)
            {
                HighwayLink link = highways[i];
                if (!IsRoad(grid, link.A) || !IsRoad(grid, link.B))
                {
                    continue;
                }

                int regionA = layout.GetRegionIndex(link.A);
                int regionB = layout.GetRegionIndex(link.B);
                if (regionA == regionB)
                {
                    continue;
                }

                AddPortal(link.A, link.B, regionA, regionB, true);
                AddPortal(link.B, link.A, regionB, regionA, true);
            }
        }

        private void AddPortal(
            Vector2Int from,
            Vector2Int to,
            int sourceRegion,
            int targetRegion,
            bool isHighway)
        {
            int portalIndex = portals.Count;
            portals.Add(new RoutePortal(
                from,
                to,
                sourceRegion,
                targetRegion,
                isHighway));

            List<int> outgoing = outgoingByRegion[sourceRegion];
            if (outgoing == null)
            {
                outgoing = new List<int>();
                outgoingByRegion[sourceRegion] = outgoing;
            }

            outgoing.Add(portalIndex);
        }

        private static bool IsRoad(CityGrid grid, Vector2Int tile) =>
            tile.x >= 0 && tile.x < grid.Width &&
            tile.y >= 0 && tile.y < grid.Height &&
            grid.GetTile(tile) == TileType.Road;
    }
}
