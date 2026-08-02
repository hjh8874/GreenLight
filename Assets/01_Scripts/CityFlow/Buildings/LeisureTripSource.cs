using System;
using System.Collections.Generic;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Buildings
{
    public enum LeisureDestinationKind
    {
        SpecialBuilding = 0,
        NeighbourHome = 1,
        RoadLoop = 2,
        None = 3
    }

    public readonly struct LeisureDestination
    {
        public LeisureDestination(LeisureDestinationKind kind, Vector2Int tile)
        {
            Kind = kind;
            Tile = tile;
        }

        public LeisureDestinationKind Kind { get; }
        public Vector2Int Tile { get; }
    }

    /// <summary>
    /// 결정론적 시민 여가 외출 계획기. 실제 차량은 기존 transient/reservation/
    /// TripScheduler 경로가 소비하며, 이 타입은 일일 선정과 실행시 목적지 해소만 담당한다.
    /// </summary>
    public static class LeisureTripPlanner
    {
        public static IReadOnlyList<Vector2Int> SelectHouseholds(
            IReadOnlyList<Vector2Int> houses,
            long day,
            float ratio)
        {
            var selected = new List<Vector2Int>();
            if (houses == null || houses.Count == 0 || ratio <= 0f)
                return selected;

            float clamped = Mathf.Clamp01(ratio);
            clamped = EffectiveRatio(clamped, day);
            for (int i = 0; i < houses.Count; i++)
            {
                uint hash = StableHash(houses[i], day);
                // [0,1) without Random: same home/slot/day always agrees.
                float sample = (hash & 0x00ffffffu) / 16777216f;
                if (sample < clamped)
                    selected.Add(houses[i]);
            }
            return selected;
        }

        // School-bus precedent: totalDays % 7 < 5 is weekday. Weekend leisure
        // doubles the opportunity (capped at 100%) while commute remains unchanged.
        public static float EffectiveRatio(float ratio, long day)
        {
            float clamped = Mathf.Clamp01(ratio);
            return (day % 7L) < 5L
                ? clamped
                : Mathf.Min(1f, clamped * 2f);
        }

        public static float SampleEveningHour(int index, int count) =>
            VisitTimeProfileSampler.SampleHour(
                VisitTimeProfile.Evening, index, Math.Max(1, count));

        public static LeisureDestination ResolveDestination(
            Vector2Int home,
            IReadOnlyList<Vector2Int> specialBuildings,
            IReadOnlyList<Vector2Int> houses,
            IReadOnlyList<Vector2Int> reachableRoadPoints)
        {
            if (TryNearestOther(home, specialBuildings, out Vector2Int special))
                return new LeisureDestination(
                    LeisureDestinationKind.SpecialBuilding, special);
            if (TryNearestOther(home, houses, out Vector2Int neighbour))
                return new LeisureDestination(
                    LeisureDestinationKind.NeighbourHome, neighbour);
            if (reachableRoadPoints != null && reachableRoadPoints.Count > 0)
                return new LeisureDestination(
                    LeisureDestinationKind.RoadLoop, reachableRoadPoints[0]);
            return new LeisureDestination(LeisureDestinationKind.None, home);
        }

        private static bool TryNearestOther(
            Vector2Int home,
            IReadOnlyList<Vector2Int> candidates,
            out Vector2Int nearest)
        {
            nearest = default;
            if (candidates == null || candidates.Count == 0)
                return false;

            int bestDistance = int.MaxValue;
            bool found = false;
            for (int i = 0; i < candidates.Count; i++)
            {
                Vector2Int candidate = candidates[i];
                if (candidate == home)
                    continue;
                int distance = Math.Abs(candidate.x - home.x) +
                    Math.Abs(candidate.y - home.y);
                if (distance < bestDistance ||
                    (distance == bestDistance && Compare(candidate, nearest) < 0))
                {
                    bestDistance = distance;
                    nearest = candidate;
                    found = true;
                }
            }
            return found;
        }

        private static int Compare(Vector2Int left, Vector2Int right)
        {
            int y = left.y.CompareTo(right.y);
            return y != 0 ? y : left.x.CompareTo(right.x);
        }

        private static uint StableHash(Vector2Int home, long day)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)home.x) * 16777619u;
                h = (h ^ (uint)home.y) * 16777619u;
                h = (h ^ (uint)day) * 16777619u;
                h = (h ^ (uint)(day >> 32)) * 16777619u;
                h ^= h >> 13;
                h *= 0x5bd1e995u;
                h ^= h >> 15;
                return h;
            }
        }
    }

    // Scene integration point: add beside SpecialBuildingVisitService when the
    // SimConfig leisure ratio is enabled. Planning remains dependency-free for tests.
    [DisallowMultipleComponent]
    public sealed class LeisureTripSource : MonoBehaviour { }
}
