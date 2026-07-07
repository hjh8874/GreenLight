namespace CityFlow.Sim
{
    // 교차로 신호 하나. 유저가 조작하는 노브 = OffsetSlots(그린웨이브의 핵심).
    // ponytail: NodeId·차선 등은 FlowSolver 통합 결정 후. 지금은 순수 수학용 최소 필드.
    internal sealed class Signal
    {
        public int CycleSlots = 12;   // 주기(슬롯). 12슬롯 = 6초
        public int GreenSlots = 6;    // 초록 길이(슬롯)
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
            double t = (time + s.OffsetSlots * SlotSeconds) % cycle;
            if (t < 0) t += cycle;   // 음수 시간 방어(정산 역산 등)
            return t < s.GreenSlots * SlotSeconds;
        }

        // 초록 시간 비율(duty cycle) ∈ [0,1]. 유효 용량 = RoadCapacity × 이 값.
        // 오프셋과 무관(비율은 오프셋에 안 변함) — 오프셋은 그린웨이브 조율에서만 의미.
        public static float GreenRatio(Signal s)
        {
            if (s.CycleSlots <= 0) return 0f;               // 주기 0 방어
            float r = (float)s.GreenSlots / s.CycleSlots;
            return r > 1f ? 1f : r < 0f ? 0f : r;           // [0,1] 클램프(오설정 방어)
        }
    }
}
