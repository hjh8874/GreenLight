namespace CityFlow.Sim
{
    // 유형별 출퇴근 창. 시각은 게임시간 [0,24) 단위이며 하루 길이(DayLengthSeconds)와 무관하다.
    // public인 이유: Task 5에서 public CommuteScheduler.Rebuild 시그니처에 등장한다(internal이면 CS0051).
    public readonly struct CommuteWindow
    {
        public readonly string CompanyTypeId;
        public readonly float StartHour;    // 출근 창 시작
        public readonly float StartWindow;  // 출근 창 길이(시간)
        public readonly float EndHour;      // 퇴근 창 시작
        public readonly float EndWindow;    // 퇴근 창 길이(시간)

        public CommuteWindow(
            string companyTypeId,
            float startHour, float startWindow,
            float endHour, float endWindow)
        {
            CompanyTypeId = companyTypeId ?? string.Empty;
            StartHour = startHour;
            StartWindow = startWindow;
            EndHour = endHour;
            EndWindow = endWindow;
        }

        // 반개 구간 [start, end) 판정. start > end 면 자정을 넘는 구간으로 해석한다.
        // 순수 함수 — 결정론적이고 테스트하기 쉽다.
        public static bool InWindow(float hour, float start, float end) =>
            start < end
                ? (hour >= start && hour < end)
                : start > end
                    ? (hour >= start || hour < end)
                    : false;   // start == end 는 빈 구간
    }
}
