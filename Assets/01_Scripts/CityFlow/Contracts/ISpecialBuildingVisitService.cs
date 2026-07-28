using System;
using UnityEngine;

namespace CityFlow.Contracts
{
    public readonly struct SpecialBuildingVisitStatistics
    {
        public SpecialBuildingVisitStatistics(
            string buildingId,
            Vector2Int anchor,
            long day,
            int plannedToday,
            long totalPlannedVisits)
        {
            BuildingId = buildingId ?? string.Empty;
            Anchor = anchor;
            Day = day;
            PlannedToday = Mathf.Max(0, plannedToday);
            TotalPlannedVisits = Math.Max(0L, totalPlannedVisits);
        }

        public string BuildingId { get; }
        public Vector2Int Anchor { get; }
        public long Day { get; }
        public int PlannedToday { get; }
        public long TotalPlannedVisits { get; }
    }

    public readonly struct SpecialBuildingVisitDemandPlannedEvent
    {
        public SpecialBuildingVisitDemandPlannedEvent(
            SpecialBuildingVisitStatistics statistics)
        {
            Statistics = statistics;
        }

        public SpecialBuildingVisitStatistics Statistics { get; }
    }

    public interface ISpecialBuildingVisitService
    {
        long LastProcessedTotalDay { get; }

        event Action<SpecialBuildingVisitDemandPlannedEvent> DemandPlanned;

        bool TryGetStatistics(
            Vector2Int tile,
            out SpecialBuildingVisitStatistics statistics);
    }
}
