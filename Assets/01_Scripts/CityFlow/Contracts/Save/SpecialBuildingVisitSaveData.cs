using System;

namespace CityFlow.Contracts.Save
{
    [Serializable]
    public sealed class SpecialBuildingVisitSaveData
    {
        public bool HasState;
        public long LastProcessedTotalDay;
        public SpecialBuildingVisitStatisticsSaveData[] Statistics =
            Array.Empty<SpecialBuildingVisitStatisticsSaveData>();
    }

    [Serializable]
    public sealed class SpecialBuildingVisitStatisticsSaveData
    {
        public string BuildingId;
        public int X;
        public int Y;
        public long Day;
        public int PlannedToday;
        public long TotalPlannedVisits;
    }
}
