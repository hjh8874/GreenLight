using UnityEngine;

namespace CityFlow.Contracts
{
    public readonly struct SpecialBuildingVisitTripRequest
    {
        public SpecialBuildingVisitTripRequest(
            string buildingId,
            Vector2Int destination,
            long day,
            int visitIndex,
            float scheduledHour,
            int rewardCoins)
        {
            BuildingId = buildingId ?? string.Empty;
            Destination = destination;
            Day = day < 0L ? 0L : day;
            VisitIndex = Mathf.Max(0, visitIndex);
            ScheduledHour = Mathf.Repeat(scheduledHour, 24f);
            RewardCoins = Mathf.Max(0, rewardCoins);
        }

        public string BuildingId { get; }
        public Vector2Int Destination { get; }
        public long Day { get; }
        public int VisitIndex { get; }
        public float ScheduledHour { get; }
        public int RewardCoins { get; }    // 최종 목적지(특수건물) 도착 시 지급. 귀가 leg 는 0
    }

    public interface IVehicleTripService
    {
        int PendingTripCount { get; }
        int ActiveTripCount { get; }

        bool TryScheduleSpecialBuildingVisit(
            SpecialBuildingVisitTripRequest request);
    }
}
