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
            int rewardCoins,
            int visitorParkingSlotStart = 0,
            int visitorParkingSlotCount = 1,
            float visitDwellHours = 0f)
        {
            BuildingId = buildingId ?? string.Empty;
            Destination = destination;
            Day = day < 0L ? 0L : day;
            VisitIndex = Mathf.Max(0, visitIndex);
            ScheduledHour = Mathf.Repeat(scheduledHour, 24f);
            RewardCoins = Mathf.Max(0, rewardCoins);
            VisitorParkingSlotStart = Mathf.Max(
                0,
                visitorParkingSlotStart);
            VisitorParkingSlotCount = Mathf.Max(
                1,
                visitorParkingSlotCount);
            VisitDwellHours = Mathf.Max(0f, visitDwellHours);
        }

        public string BuildingId { get; }
        public Vector2Int Destination { get; }
        public long Day { get; }
        public int VisitIndex { get; }
        public float ScheduledHour { get; }
        public int RewardCoins { get; }    // 최종 목적지(특수건물) 도착 시 지급. 귀가 leg 는 0
        public int VisitorParkingSlotStart { get; }
        public int VisitorParkingSlotCount { get; }
        public float VisitDwellHours { get; }
    }

    public interface IVehicleTripService
    {
        int PendingTripCount { get; }
        int ActiveTripCount { get; }

        bool TryScheduleSpecialBuildingVisit(
            SpecialBuildingVisitTripRequest request);
    }
}
