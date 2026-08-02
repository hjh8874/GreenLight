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

        // 유형이 없는 목적지(School 등)·유형 표 미주입 상황의 폴백 창. 정의를 한 곳에 둔다 —
        // SimEngine 과 DemandMap 이 각자 만들면 두 값이 갈린다.
        public static CommuteWindow FromConfig(in SimConfig config) => new CommuteWindow(
            string.Empty,
            config.MorningStartHour, config.MorningEndHour - config.MorningStartHour,
            config.EveningStartHour, config.EveningEndHour - config.EveningStartHour);

        // 반개 구간 [start, end) 판정. start > end 면 자정을 넘는 구간으로 해석한다.
        // 순수 함수 — 결정론적이고 테스트하기 쉽다.
        public static bool InWindow(float hour, float start, float end) =>
            start < end
                ? (hour >= start && hour < end)
                : start > end
                    ? (hour >= start || hour < end)
                    : false;   // start == end 는 빈 구간
    }

    // 회사 유형 1종의 Sim 쪽 표현. 오서링 SO(CompanyTypeSO)는 Assembly-CSharp 소속이고
    // CityFlow.Sim은 그 어셈블리를 참조할 수 없다 — 배선 계층이 이 구조체로 옮겨 넣는다.
    public readonly struct CompanyTypeInfo
    {
        public readonly CommuteWindow Window;
        public readonly int Capacity;   // 유형별 정원. <= 0 이면 유형 정원 미지정(기존 정원 규칙을 쓴다)

        public CompanyTypeInfo(CommuteWindow window, int capacity)
        {
            Window = window;
            Capacity = capacity;
        }
    }
}
