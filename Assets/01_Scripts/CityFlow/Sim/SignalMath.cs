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

        // 방향 교대 신호: 가로는 주기 전반, 세로는 후반이 초록(오프셋만큼 이동).
        // 둘이 동시에 초록인 적이 없어 → 교차로에서 두 방향이 만나는 충돌·데드락이 구조적으로 사라짐.
        public static bool IsGreenForAxis(Signal s, double time, bool horizontal)
        {
            double cycle = s.CycleSlots * SlotSeconds;
            if (cycle <= 0) return true;
            double t = (time + s.OffsetSlots * SlotSeconds) % cycle;
            if (t < 0) t += cycle;
            bool firstHalf = t < cycle * 0.5;
            return horizontal ? firstHalf : !firstHalf;
        }

        // 초록 시간 비율(duty cycle) ∈ [0,1]. 유효 용량 = RoadCapacity × 이 값.
        // 오프셋과 무관(비율은 오프셋에 안 변함) — 오프셋은 그린웨이브 조율에서만 의미.
        public static float GreenRatio(Signal s)
        {
            if (s.CycleSlots <= 0) return 0f;               // 주기 0 방어
            float r = (float)s.GreenSlots / s.CycleSlots;
            return r > 1f ? 1f : r < 0f ? 0f : r;           // [0,1] 클램프(오설정 방어)
        }

        // 그린웨이브 효율 ∈ [floor, 1]. 인접 신호쌍의 오프셋이 이동시간에 맞으면 1(연쇄 초록),
        // 반 주기 어긋나면 floor(흐름이 빨강에 도착 → 대기). 오프셋이 처리량을 바꾸는 유일한 지점.
        // 🔓 1차 제안 공식(형태는 제안, floor·곡선은 팀 튜닝). ponytail: 같은 주기 가정 — 다르면 확장.
        public static float GreenWaveEfficiency(Signal from, Signal to, int travelSlots, float floor)
        {
            int cycle = from.CycleSlots;
            if (cycle <= 0) return 1f;   // 주기 이상 → 페널티 없음(방어)

            // 이상적 오프셋차 = 이동시간. 실제와의 오차를 주기로 접어 [0, cycle)로.
            int actual = to.OffsetSlots - from.OffsetSlots;
            int misalign = (((actual - travelSlots) % cycle) + cycle) % cycle;
            // 주기는 원형이라 반대쪽으로 접어 [0, cycle/2]로: 0=완벽 정렬, cycle/2=최악(반주기 어긋남)
            int phaseErr = misalign < cycle - misalign ? misalign : cycle - misalign;
            float norm = phaseErr / (cycle / 2f);   // [0,1]
            return 1f - norm * (1f - floor);        // 완벽 1 → 최악 floor 선형
        }
    }
}
