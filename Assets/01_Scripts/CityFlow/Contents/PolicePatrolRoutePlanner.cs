using System;
using System.Collections.Generic;
using CityFlow.Contracts;
using CityFlow.WorldGrid;
using UnityEngine;

namespace CityFlow.Content
{
    internal readonly struct PolicePatrolPlan
    {
        public PolicePatrolPlan(
            RoadRoutePlan route,
            bool usedLoop,
            int scannedChunkCount,
            int scannedTileCount)
        {
            Route = route;
            UsedLoop = usedLoop;
            ScannedChunkCount = Mathf.Max(0, scannedChunkCount);
            ScannedTileCount = Mathf.Max(0, scannedTileCount);
        }

        public RoadRoutePlan Route { get; }
        public bool UsedLoop { get; }
        public int ScannedChunkCount { get; }
        public int ScannedTileCount { get; }
        public bool IsValid => Route.IsValid && Route.TileCount > 1;
    }

    internal sealed class PolicePatrolRoutePlanner
    {
        private static readonly Vector2Int[] Directions =
        {
            Vector2Int.up,
            Vector2Int.right,
            Vector2Int.down,
            Vector2Int.left
        };

        private readonly HashSet<Vector2Int> roads = new();
        private readonly HashSet<Vector2Int> reachable = new();
        private readonly Dictionary<Vector2Int, Vector2Int> parents = new();
        private readonly Dictionary<Vector2Int, int> distances = new();
        private readonly Queue<Vector2Int> searchQueue = new();
        private readonly List<Vector2Int> orderedReachable = new();
        private readonly List<Vector2Int> pathBuffer = new();

        public bool TryBuildPlan(
            Vector2Int station,
            Vector2Int accessRoad,
            int patrolAreaSize,
            IReadOnlyTileData tileData,
            IWorldGridAccess worldGrid,
            out PolicePatrolPlan plan)
        {
            plan = default;
            if (tileData == null ||
                tileData.GetTileType(accessRoad) != TileType.Road)
            {
                return false;
            }

            roads.Clear();
            int scannedChunks = 0;
            int scannedTiles = CollectRoads(
                station,
                Mathf.Max(1, patrolAreaSize),
                tileData,
                worldGrid,
                ref scannedChunks);
            if (!roads.Contains(accessRoad))
            {
                return false;
            }

            BuildReachableTree(accessRoad);
            if (reachable.Count < 2)
            {
                return false;
            }

            pathBuffer.Clear();
            bool usedLoop = TryBuildNearestCycle(
                accessRoad,
                pathBuffer);
            if (!usedLoop)
            {
                BuildOutAndBackPath(accessRoad, pathBuffer);
            }

            if (!IsValidPath(pathBuffer))
            {
                return false;
            }

            plan = new PolicePatrolPlan(
                new RoadRoutePlan(pathBuffer),
                usedLoop,
                scannedChunks,
                scannedTiles);
            return plan.IsValid;
        }

        private int CollectRoads(
            Vector2Int station,
            int patrolAreaSize,
            IReadOnlyTileData tileData,
            IWorldGridAccess worldGrid,
            ref int scannedChunkCount)
        {
            int scannedTileCount = 0;
            if (worldGrid == null || worldGrid.ChunkSize <= 0)
            {
                int half = patrolAreaSize / 2;
                int minX = station.x - half;
                int minY = station.y - half;
                for (int y = minY; y < minY + patrolAreaSize; y++)
                {
                    for (int x = minX; x < minX + patrolAreaSize; x++)
                    {
                        CollectRoad(
                            new Vector2Int(x, y),
                            tileData);
                        scannedTileCount++;
                    }
                }

                return scannedTileCount;
            }

            int chunkSize = Mathf.Max(1, worldGrid.ChunkSize);
            int chunksPerAxis = Mathf.Max(
                1,
                Mathf.CeilToInt(patrolAreaSize / (float)chunkSize));
            int minChunkX = ResolveWindowStart(
                station.x,
                chunkSize,
                chunksPerAxis,
                worldGrid.ChunkColumns);
            int minChunkY = ResolveWindowStart(
                station.y,
                chunkSize,
                chunksPerAxis,
                worldGrid.ChunkRows);

            for (int chunkY = 0; chunkY < chunksPerAxis; chunkY++)
            {
                for (int chunkX = 0; chunkX < chunksPerAxis; chunkX++)
                {
                    var chunk = new GridChunkId(
                        minChunkX + chunkX,
                        minChunkY + chunkY);
                    if (!worldGrid.IsChunkUnlocked(chunk))
                    {
                        continue;
                    }

                    scannedChunkCount++;
                    scannedTileCount +=
                        UnlockedGridTileScanner.VisitChunk(
                            worldGrid,
                            chunk,
                            tile => CollectRoad(tile, tileData));
                }
            }

            return scannedTileCount;
        }

        private void CollectRoad(
            Vector2Int tile,
            IReadOnlyTileData tileData)
        {
            if (tileData.GetTileType(tile) == TileType.Road)
            {
                roads.Add(tile);
            }
        }

        private static int ResolveWindowStart(
            int coordinate,
            int chunkSize,
            int chunksPerAxis,
            int availableChunks)
        {
            int desiredMinimum = coordinate -
                                 chunksPerAxis * chunkSize / 2;
            int start = Mathf.FloorToInt(
                desiredMinimum / (float)chunkSize);
            return Mathf.Clamp(
                start,
                0,
                Mathf.Max(0, availableChunks - chunksPerAxis));
        }

        private void BuildReachableTree(Vector2Int start)
        {
            reachable.Clear();
            parents.Clear();
            distances.Clear();
            searchQueue.Clear();
            orderedReachable.Clear();

            reachable.Add(start);
            distances[start] = 0;
            searchQueue.Enqueue(start);

            while (searchQueue.Count > 0)
            {
                Vector2Int current = searchQueue.Dequeue();
                orderedReachable.Add(current);
                int distance = distances[current];
                for (int index = 0; index < Directions.Length; index++)
                {
                    Vector2Int next = current + Directions[index];
                    if (!roads.Contains(next) ||
                        !reachable.Add(next))
                    {
                        continue;
                    }

                    parents[next] = current;
                    distances[next] = distance + 1;
                    searchQueue.Enqueue(next);
                }
            }

            orderedReachable.Sort(CompareReachableNodes);
        }

        private int CompareReachableNodes(
            Vector2Int left,
            Vector2Int right)
        {
            int distanceCompare =
                distances[left].CompareTo(distances[right]);
            return distanceCompare != 0
                ? distanceCompare
                : CompareCoordinates(left, right);
        }

        private bool TryBuildNearestCycle(
            Vector2Int start,
            List<Vector2Int> result)
        {
            bool found = false;
            Vector2Int edgeStart = default;
            Vector2Int edgeEnd = default;
            int bestScore = int.MaxValue;

            for (int nodeIndex = 0;
                 nodeIndex < orderedReachable.Count;
                 nodeIndex++)
            {
                Vector2Int node = orderedReachable[nodeIndex];
                for (int directionIndex = 0;
                     directionIndex < Directions.Length;
                     directionIndex++)
                {
                    Vector2Int neighbor =
                        node + Directions[directionIndex];
                    if (!reachable.Contains(neighbor) ||
                        CompareCoordinates(node, neighbor) >= 0 ||
                        IsTreeEdge(node, neighbor))
                    {
                        continue;
                    }

                    int score = distances[node] + distances[neighbor];
                    bool earlier = score < bestScore ||
                                   score == bestScore &&
                                   CompareEdge(
                                       node,
                                       neighbor,
                                       edgeStart,
                                       edgeEnd) < 0;
                    if (!earlier)
                    {
                        continue;
                    }

                    found = true;
                    bestScore = score;
                    edgeStart = node;
                    edgeEnd = neighbor;
                }
            }

            if (!found)
            {
                return false;
            }

            List<Vector2Int> startAncestors = BuildPathToRoot(edgeStart);
            List<Vector2Int> endAncestors = BuildPathToRoot(edgeEnd);
            int startIndex = startAncestors.Count - 1;
            int endIndex = endAncestors.Count - 1;
            Vector2Int common = start;
            while (startIndex >= 0 && endIndex >= 0 &&
                   startAncestors[startIndex] == endAncestors[endIndex])
            {
                common = startAncestors[startIndex];
                startIndex--;
                endIndex--;
            }

            List<Vector2Int> approach = BuildPathFromRoot(common);
            AppendPath(result, approach, skipFirst: false);

            for (int index = startIndex + 1; index >= 0; index--)
            {
                AppendTile(result, startAncestors[index]);
            }

            AppendTile(result, edgeEnd);
            for (int index = 1; index <= endIndex + 1; index++)
            {
                AppendTile(result, endAncestors[index]);
            }

            for (int index = approach.Count - 2; index >= 0; index--)
            {
                AppendTile(result, approach[index]);
            }

            return result.Count > 2 && result[0] == start &&
                   result[result.Count - 1] == start;
        }

        private void BuildOutAndBackPath(
            Vector2Int start,
            List<Vector2Int> result)
        {
            Vector2Int farthest = start;
            for (int index = 0; index < orderedReachable.Count; index++)
            {
                Vector2Int candidate = orderedReachable[index];
                if (distances[candidate] > distances[farthest] ||
                    distances[candidate] == distances[farthest] &&
                    CompareCoordinates(candidate, farthest) < 0)
                {
                    farthest = candidate;
                }
            }

            List<Vector2Int> outbound = BuildPathFromRoot(farthest);
            AppendPath(result, outbound, skipFirst: false);
            for (int index = outbound.Count - 2; index >= 0; index--)
            {
                AppendTile(result, outbound[index]);
            }
        }

        private bool IsTreeEdge(
            Vector2Int left,
            Vector2Int right)
        {
            return parents.TryGetValue(left, out Vector2Int leftParent) &&
                   leftParent == right ||
                   parents.TryGetValue(right, out Vector2Int rightParent) &&
                   rightParent == left;
        }

        private List<Vector2Int> BuildPathToRoot(Vector2Int node)
        {
            var path = new List<Vector2Int> { node };
            while (parents.TryGetValue(node, out Vector2Int parent))
            {
                node = parent;
                path.Add(node);
            }

            return path;
        }

        private List<Vector2Int> BuildPathFromRoot(Vector2Int node)
        {
            List<Vector2Int> path = BuildPathToRoot(node);
            path.Reverse();
            return path;
        }

        private static bool IsValidPath(
            IReadOnlyList<Vector2Int> path)
        {
            if (path == null || path.Count < 2 ||
                path[0] != path[path.Count - 1])
            {
                return false;
            }

            for (int index = 1; index < path.Count; index++)
            {
                Vector2Int delta = path[index] - path[index - 1];
                if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) != 1)
                {
                    return false;
                }
            }

            return true;
        }

        private static void AppendPath(
            List<Vector2Int> destination,
            IReadOnlyList<Vector2Int> source,
            bool skipFirst)
        {
            int start = skipFirst ? 1 : 0;
            for (int index = start; index < source.Count; index++)
            {
                AppendTile(destination, source[index]);
            }
        }

        private static void AppendTile(
            List<Vector2Int> destination,
            Vector2Int tile)
        {
            if (destination.Count == 0 ||
                destination[destination.Count - 1] != tile)
            {
                destination.Add(tile);
            }
        }

        private static int CompareEdge(
            Vector2Int leftStart,
            Vector2Int leftEnd,
            Vector2Int rightStart,
            Vector2Int rightEnd)
        {
            int startCompare = CompareCoordinates(
                leftStart,
                rightStart);
            return startCompare != 0
                ? startCompare
                : CompareCoordinates(leftEnd, rightEnd);
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

        // Unity setup: no component is required. PoliceDispatchService owns
        // one planner and supplies CityFlow's tile and world-grid contracts.
    }
}
