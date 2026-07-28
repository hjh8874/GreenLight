namespace CityFlow.Contracts
{
    public readonly struct CompanyStaffing
    {
        public readonly int Filled;
        public readonly int Capacity;

        public CompanyStaffing(
            int filled,
            int capacity
        )
        {
            Filled = filled;
            Capacity = capacity;
        }
    }

    public interface IReadOnlyCityStats
    {
        int ActiveVehicleCount { get; }

        // 도로 예산제: UI 카운터("도로 N/M")용 읽기 창구.
        // N = 일반도로 타일 수 + 고속도로 링크 길이 합, M = 상한.
        int RoadTileCount { get; }

        bool TryGetCompanyStaffing(
            UnityEngine.Vector2Int tile,
            out CompanyStaffing staffing
        );
    }
}
