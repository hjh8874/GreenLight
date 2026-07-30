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

        // 어제(마지막으로 완주한 하루)의 최종 도착 수. 오늘 누적치가 아니다 —
        // 하루 경계에서 확정되며, 시각 점프로 끊긴 날은 갱신하지 않는다.
        // 연구 해금의 통행량 조건과 연구 패널 계기판이 읽는다.
        int LastDayArrivalCount { get; }


        bool TryGetCompanyStaffing(
            UnityEngine.Vector2Int tile,
            out CompanyStaffing staffing
        );
    }
}
