namespace CityFlow.Sim
{
    // 신호 3상태(뷰가 차 진입 판정에 씀). SimEngine이 public로 노출하므로 public.
    public enum SignalPhase { Green, Yellow, Red }

    // 교차로 신호 하나. 유저가 조작하는 노브 = OffsetSlots(그린웨이브의 핵심).
    // ponytail: NodeId·차선 등은 FlowSolver 통합 결정 후. 지금은 순수 수학용 최소 필드.
    internal sealed class Signal
    {
        public int CycleSlots = 16;   // 주기(슬롯). 16슬롯 = 8초 (관찰·QA용으로 길게, 🔓 밸런스)
        public int GreenSlots = 8;    // 초록 길이(슬롯, 레거시 IsGreen용) — 주기의 절반 유지
        public int OffsetSlots = 0;   // 주기 시작을 미는 양 = 유저 조작 대상
    }

    // 상태 없는 순수 함수: 신호는 "시간의 함수"일 뿐. 프로토(TrafficSpirit)에서 검증된 개념 이식.
    internal static class SignalMath
    {
        public const float SlotSeconds = 0.5f;   // 이산 조작 1슬롯 = 0.5초

        // 신호가 time(초)에 초록인가. 오프셋만큼 시간축을 밀고 주기로 나눈 나머지가 초록창 안이면 true.
        public static bool IsGreen(Signal s, double time)
        {
            double cycle = s.CycleSlots * SlotSeconds;
            if (cycle <= 0) return true;                         // 주기 0 → 항상 통과
            double t = (time - s.OffsetSlots * SlotSeconds) % cycle;   // 부호 통일(오프셋↑=늦게)
            if (t < 0) t += cycle;   // 음수 시간 방어(정산 역산 등)
            return t < s.GreenSlots * SlotSeconds;
        }

        // 노란불·전적색 비율(각 방향 창의 절반 기준). 🔓 튜닝값.
        public const float YellowFrac = 0.2f;   // 초록 뒤 노란불(진입 금지, 정리 준비)
        public const float ClearFrac  = 0.15f;  // 전적색(양방향 빨강 = 교차로 비우기)

        // 한 축(가로/세로)의 초록창: 열리는 시각(주기 내, 초)과 길이. 뷰(PhaseForAxis)와
        // 엔진(GreenWaveEfficiency)이 공유하는 단일 타이밍 — 여기서 파생하면 못 갈라짐.
        // 통일 부호: 오프셋↑ = 초록 늦게 열림(하류를 이동시간만큼 미뤄 그린웨이브 = 직관).
        public static (double open, double greenLen) GreenWindowFor(Signal s, bool horizontal)
        {
            double cycle = s.CycleSlots * SlotSeconds;
            if (cycle <= 0) return (0.0, 0.0);                 // 주기 0 방어
            double half = cycle * 0.5;
            double greenLen = half * (1f - YellowFrac - ClearFrac);
            double axisStart = horizontal ? 0.0 : half;        // 세로는 반주기 뒤
            double open = (axisStart + s.OffsetSlots * SlotSeconds) % cycle;
            if (open < 0) open += cycle;
            return (open, greenLen);
        }

        // 방향 교대 신호의 3상태: 가로는 주기 전반, 세로는 후반이 활성(오프셋만큼 이동).
        // 각 방향 창 = 초록 → 노랑 → 전적색(정리). 두 방향이 동시에 초록/노랑인 적 없음 →
        // 전환 순간 교차로가 비워진 뒤에야 반대 방향 초록 → 낀 차 없음, 예측 가능.
        public static SignalPhase PhaseForAxis(Signal s, double time, bool horizontal)
        {
            double cycle = s.CycleSlots * SlotSeconds;
            if (cycle <= 0) return SignalPhase.Green;
            double half = cycle * 0.5;
            var (open, greenLen) = GreenWindowFor(s, horizontal);   // 뷰·엔진 공용 창
            double yellowLen = half * YellowFrac;

            double local = (time - open) % cycle;         // 이 축 창 열림 기준 경과
            if (local < 0) local += cycle;
            if (local >= half) return SignalPhase.Red;    // 반대 축 창 = 빨강
            if (local < greenLen) return SignalPhase.Green;
            if (local < greenLen + yellowLen) return SignalPhase.Yellow;
            return SignalPhase.Red;                        // 전적색(정리)
        }

        // 초록 시간 비율(duty cycle) ∈ [0,1]. 유효 용량 = RoadCapacity × 이 값.
        // 오프셋과 무관(비율은 오프셋에 안 변함) — 오프셋은 그린웨이브 조율에서만 의미.
        public static float GreenRatio(Signal s)
        {
            if (s.CycleSlots <= 0) return 0f;               // 주기 0 방어
            float r = (float)s.GreenSlots / s.CycleSlots;
            return r > 1f ? 1f : r < 0f ? 0f : r;           // [0,1] 클램프(오설정 방어)
        }

        // 그린웨이브 효율 ∈ [floor, 1]. 상류 초록 선두에 출발한 흐름이 travelSlots 뒤 하류
        // 초록창에 안착하면 1(플래토 — 초록 어디든 완전 통과), 반대편 한복판이면 floor.
        // 뷰(PhaseForAxis)와 같은 GreenWindowFor에서 파생 → 화면과 처리량이 못 갈라짐.
        // 🔓 floor·곡선은 팀 튜닝. ponytail: 같은 주기 가정 + 인접 교차로 회전(축 바뀜)은 도착
        // 축으로 근사 — 직선 경로에선 축이 상쇄돼 무해, 정밀 회전 위상은 2차.
        public static float GreenWaveEfficiency(Signal from, Signal to, int travelSlots, float floor)
        {
            double cycle = from.CycleSlots * SlotSeconds;
            if (cycle <= 0) return 1f;                                     // 주기 이상 → 페널티 없음
            var (openFrom, _)      = GreenWindowFor(from, true);           // 축 무관(상쇄) → true 대표
            var (openTo, greenLen) = GreenWindowFor(to, true);
            double arrive = (openFrom + travelSlots * SlotSeconds) % cycle;
            double delta  = (arrive - openTo) % cycle;
            if (delta < 0) delta += cycle;
            if (delta < greenLen) return 1f;                              // 하류 초록 안착 = 완전 통과
            double gap    = System.Math.Min(delta - greenLen, cycle - delta);  // 초록창까지 원형 최단
            double maxGap = (cycle - greenLen) * 0.5;                     // 최악(반대편 한복판)
            double norm   = maxGap <= 0 ? 0.0 : gap / maxGap;
            if (norm < 0) norm = 0; else if (norm > 1) norm = 1;
            return (float)(1.0 - norm * (1.0 - floor));                   // 1 → floor 선형
        }
    }
}
