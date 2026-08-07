using System;
using UnityEngine;

namespace CityFlow.Contracts
{
    public enum PolicePatrolOutcome
    {
        Started = 0,
        Completed = 1,
        SkippedNoVehicle = 2,
        SkippedNoRoute = 3,
        InterruptedByDispatch = 4,
        RouteUnavailable = 5
    }

    public readonly struct PolicePatrolEvent
    {
        public PolicePatrolEvent(
            Vector2Int station,
            long totalDay,
            bool usedLoop,
            int routeTileCount,
            PolicePatrolOutcome outcome)
        {
            Station = station;
            TotalDay = Math.Max(0L, totalDay);
            UsedLoop = usedLoop;
            RouteTileCount = Mathf.Max(0, routeTileCount);
            Outcome = outcome;
        }

        public Vector2Int Station { get; }
        public long TotalDay { get; }
        public bool UsedLoop { get; }
        public int RouteTileCount { get; }
        public PolicePatrolOutcome Outcome { get; }
    }

    public interface IPolicePatrolService
    {
        event Action<PolicePatrolEvent> PatrolStarted;
        event Action<PolicePatrolEvent> PatrolFinished;

        bool TryStartPatrol(Vector2Int station);
    }

    // Unity setup: consume this service through CityFlowServices.PolicePatrol.
}
