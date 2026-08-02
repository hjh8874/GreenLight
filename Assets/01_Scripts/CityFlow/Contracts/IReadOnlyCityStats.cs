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

    // 회사 하나로 통근하는 집 하나의 (좌표, 인원) — 회사 카드/디버그 표시용
    public readonly struct CommuterHomeCount
    {
        public readonly UnityEngine.Vector2Int Home;
        public readonly int Count;

        public CommuterHomeCount(UnityEngine.Vector2Int home, int count)
        {
            Home = home;
            Count = count;
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

        // 이 회사의 유형 id (office/factory/…). 유형 미지정·회사 아님이면 false.
        bool TryGetCompanyTypeId(
            UnityEngine.Vector2Int tile,
            out string companyTypeId
        );

        // 이 회사로 통근하는 집 목록 (집 좌표, 인원). 회사가 아니면 빈 목록.
        // 호출마다 새 목록을 만든다 — 클릭 카드용, 매 프레임 폴링 금지.
        System.Collections.Generic.IReadOnlyList<CommuterHomeCount> GetCompanyCommuterHomes(
            UnityEngine.Vector2Int tile
        );
    }
}
