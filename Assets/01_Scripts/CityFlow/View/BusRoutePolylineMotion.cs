using System.Collections.Generic;
using CityFlow.Content.Transit;
using CityFlow.Contracts;
using CityFlow.ViewKit;
using UnityEngine;

namespace CityFlow.View
{
    /// <summary>
    /// Shared adapter that maps a BusRoute segment to the same baked lane,
    /// corner, intersection, and roundabout geometry used by regular cars.
    /// </summary>
    internal sealed class BusRoutePolylineMotion
    {
        private readonly List<Vector2Int> roadTiles = new();
        private readonly List<int> pathToRoadIndex = new();

        private RoutePolyline polyline;
        private int cachedPathCount = -1;
        private int cachedPathHash;
        private int cachedSourcePathCount = -1;
        private int cachedSourcePathHash;

        public RoutePolyline Polyline => polyline;

        public void Invalidate()
        {
            polyline = null;
            cachedPathCount = -1;
            cachedPathHash = 0;
            cachedSourcePathCount = -1;
            cachedSourcePathHash = 0;
            roadTiles.Clear();
            pathToRoadIndex.Clear();
        }

        public bool MatchesSourcePath(BusRoute route)
        {
            IReadOnlyList<Vector2Int> sourcePath =
                route?.CurrentRoadPath;
            return polyline != null &&
                   sourcePath != null &&
                   sourcePath.Count == cachedSourcePathCount &&
                   ComputePathHash(sourcePath) ==
                   cachedSourcePathHash;
        }

        public bool TryRefresh(
            BusRoute route,
            IReadOnlyTileData tileData,
            MainCityView cityView,
            float visualDepth,
            out int roadIndex)
        {
            return TryRefresh(
                route,
                tileData,
                cityView,
                visualDepth,
                null,
                null,
                out roadIndex);
        }

        public bool TryRefresh(
            BusRoute route,
            IReadOnlyTileData tileData,
            MainCityView cityView,
            float visualDepth,
            Vector3? startAnchor,
            Vector3? endAnchor,
            out int roadIndex)
        {
            roadIndex = -1;
            IReadOnlyList<Vector2Int> sourcePath =
                route?.CurrentRoadPath;
            if (sourcePath == null ||
                sourcePath.Count == 0 ||
                tileData == null ||
                cityView == null)
            {
                return false;
            }

            int sourcePathHash = ComputePathHash(sourcePath);
            int pathHash = sourcePathHash;
            pathHash = CombineAnchorHash(
                pathHash,
                startAnchor);
            pathHash = CombineAnchorHash(
                pathHash,
                endAnchor);
            if (polyline == null ||
                cachedPathCount != sourcePath.Count ||
                cachedPathHash != pathHash)
            {
                roadTiles.Clear();
                pathToRoadIndex.Clear();

                for (int i = 0; i < sourcePath.Count; i++)
                {
                    Vector2Int tile = sourcePath[i];
                    if (tileData.GetTileType(tile) ==
                        TileType.Road)
                    {
                        pathToRoadIndex.Add(roadTiles.Count);
                        roadTiles.Add(tile);
                    }
                    else
                    {
                        pathToRoadIndex.Add(-1);
                    }
                }

                if (!IsContinuous(roadTiles))
                {
                    polyline = null;
                    return false;
                }

                polyline = cityView.BakeTrafficRoute(
                    roadTiles,
                    visualDepth,
                    startAnchor,
                    endAnchor,
                    clampAnchorSpurOvershoot:
                        startAnchor.HasValue ||
                        endAnchor.HasValue);
                if (polyline == null)
                {
                    return false;
                }

                cachedPathCount = sourcePath.Count;
                cachedPathHash = pathHash;
                cachedSourcePathCount = sourcePath.Count;
                cachedSourcePathHash = sourcePathHash;
            }

            int pathIndex = route.CurrentRoadPathIndex;
            if (pathIndex < 0 ||
                pathIndex >= pathToRoadIndex.Count)
            {
                return false;
            }

            roadIndex = pathToRoadIndex[pathIndex];
            return roadIndex >= 0 &&
                   roadIndex < polyline.TileCount;
        }

        private static bool IsContinuous(
            IReadOnlyList<Vector2Int> tiles)
        {
            if (tiles == null || tiles.Count == 0)
            {
                return false;
            }

            for (int i = 1; i < tiles.Count; i++)
            {
                Vector2Int delta = tiles[i] - tiles[i - 1];
                if (Mathf.Abs(delta.x) +
                    Mathf.Abs(delta.y) != 1)
                {
                    return false;
                }
            }

            return true;
        }

        private static int ComputePathHash(
            IReadOnlyList<Vector2Int> path)
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < path.Count; i++)
                {
                    hash = hash * 31 + path[i].x;
                    hash = hash * 31 + path[i].y;
                }

                return hash;
            }
        }

        private static int CombineAnchorHash(
            int hash,
            Vector3? anchor)
        {
            unchecked
            {
                if (!anchor.HasValue)
                {
                    return hash * 31;
                }

                Vector3 value = anchor.Value;
                hash = hash * 31 + 1;
                hash = hash * 31 + value.x.GetHashCode();
                hash = hash * 31 + value.y.GetHashCode();
                return hash * 31 + value.z.GetHashCode();
            }
        }
    }

    /// <summary>
    /// Keeps a feature vehicle slightly behind the latest authoritative
    /// queue position so consecutive tile grants become one continuous
    /// acceleration/braking motion instead of separate stop-start lerps.
    /// </summary>
    internal sealed class BufferedRouteFollower
    {
        private RoutePolyline path;
        private float currentDistance;
        private float targetDistance;
        private float speed;
        private bool hasDistance;
        private bool authorityTargetAdvancing;

        public RoutePolyline Path => path;
        public float CurrentDistance => currentDistance;
        public float TargetDistance => targetDistance;
        public float Speed => speed;
        public bool HasPath =>
            hasDistance && path != null;
        public bool IsAtTarget =>
            !HasPath ||
            Mathf.Abs(targetDistance - currentDistance) <=
            0.0001f;

        public void SetTarget(
            RoutePolyline nextPath,
            float startDistance,
            float nextTargetDistance,
            bool snapToTarget)
        {
            SetTargetInternal(
                nextPath,
                startDistance,
                nextTargetDistance,
                snapToTarget,
                preserveSpeedAtAuthorityLimit: false);
        }

        public bool SetAuthorizedTarget(
            RoutePolyline nextPath,
            float startDistance,
            float nextTargetDistance,
            bool snapToTarget)
        {
            return SetTargetInternal(
                nextPath,
                startDistance,
                nextTargetDistance,
                snapToTarget,
                preserveSpeedAtAuthorityLimit: true);
        }

        public void MarkAuthorityHeld()
        {
            authorityTargetAdvancing = false;
        }

        private bool SetTargetInternal(
            RoutePolyline nextPath,
            float startDistance,
            float nextTargetDistance,
            bool snapToTarget,
            bool preserveSpeedAtAuthorityLimit)
        {
            if (nextPath == null)
            {
                Reset();
                return false;
            }

            bool pathChanged =
                !ReferenceEquals(path, nextPath) ||
                !hasDistance;
            path = nextPath;
            startDistance = Mathf.Clamp(
                startDistance,
                0f,
                path.Length);
            nextTargetDistance = Mathf.Clamp(
                nextTargetDistance,
                0f,
                path.Length);
            bool targetExtended =
                pathChanged ||
                nextTargetDistance >
                targetDistance + 0.0001f;

            if (pathChanged)
            {
                currentDistance = startDistance;
                speed = 0f;
                hasDistance = true;
            }

            targetDistance = Mathf.Max(
                currentDistance,
                nextTargetDistance);
            if (preserveSpeedAtAuthorityLimit &&
                targetExtended)
            {
                authorityTargetAdvancing = true;
            }
            else if (!preserveSpeedAtAuthorityLimit)
            {
                authorityTargetAdvancing = false;
            }

            if (snapToTarget)
            {
                currentDistance = targetDistance;
                speed = 0f;
                authorityTargetAdvancing = false;
            }

            return targetExtended;
        }

        public float CalculateCandidateDistance(
            float deltaTime,
            float nominalSpeed)
        {
            if (!HasPath)
            {
                return currentDistance;
            }

            float remaining =
                targetDistance - currentDistance;
            if (remaining <= 0.0001f)
            {
                speed = 0f;
                return targetDistance;
            }

            float safeDeltaTime =
                Mathf.Max(0f, deltaTime);
            float safeNominalSpeed =
                Mathf.Max(0.01f, nominalSpeed);
            float acceleration =
                safeNominalSpeed * 5f;
            float braking =
                safeNominalSpeed * 7f;
            float terminalSpeed =
                authorityTargetAdvancing
                    ? safeNominalSpeed
                    : 0f;
            float brakingSpeed =
                Mathf.Sqrt(
                    terminalSpeed * terminalSpeed +
                    2f * braking * remaining);
            float desiredSpeed =
                Mathf.Min(
                    safeNominalSpeed,
                    brakingSpeed);
            float rate = desiredSpeed >= speed
                ? acceleration
                : braking;

            speed = Mathf.MoveTowards(
                speed,
                desiredSpeed,
                rate * safeDeltaTime);

            return Mathf.Min(
                targetDistance,
                currentDistance +
                speed * safeDeltaTime);
        }

        public void CommitCandidate(
            float candidateDistance,
            float allowedFraction)
        {
            if (!HasPath)
            {
                return;
            }

            float fraction =
                Mathf.Clamp01(allowedFraction);
            currentDistance = Mathf.Lerp(
                currentDistance,
                Mathf.Clamp(
                    candidateDistance,
                    currentDistance,
                    targetDistance),
                fraction);

            if (fraction <= 0.0001f)
            {
                speed = 0f;
            }

            if (IsAtTarget)
            {
                currentDistance = targetDistance;
                speed = 0f;
                authorityTargetAdvancing = false;
            }
        }

        public void Reset()
        {
            path = null;
            currentDistance = 0f;
            targetDistance = 0f;
            speed = 0f;
            hasDistance = false;
            authorityTargetAdvancing = false;
        }

        public void RecoverToAuthoritativeDistance(
            RoutePolyline nextPath,
            float authoritativeDistance)
        {
            if (nextPath == null)
            {
                Reset();
                return;
            }

            path = nextPath;
            currentDistance = Mathf.Clamp(
                authoritativeDistance,
                0f,
                path.Length);
            targetDistance = currentDistance;
            speed = 0f;
            hasDistance = true;
            authorityTargetAdvancing = false;
        }
    }
}
