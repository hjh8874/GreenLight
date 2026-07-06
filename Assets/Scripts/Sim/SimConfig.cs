namespace CityFlow.Sim
{
    // 모든 밸런스 숫자를 한 곳에 담는 값 묶음.
    // blueprint §0: const 금지(런타임·툴에서 튜닝 가능해야) → 전부 필드.
    // 실제로는 이진우의 EconomyConfig(ScriptableObject) → Bootstrap이 변환해 주입.
    // ponytail: 지금은 임시값. 밸런싱 전이라 틀려도 됨 — "그릇"만 확정.
    public struct SimConfig
    {
        // ── 고정 틱 ─────────────────────────────
        public float TickInterval;      // 시뮬 1스텝(초). blueprint §3 = 0.1
        public int   MaxStepsPerFrame;  // 누산기 while 폭주 방지 캡

        // ── 도로 처리량 ─────────────────────────
        public float RoadCapacity;      // 대/틱, 도로 등급별(지금 단일) 🔓

        // ── 혼잡 임계 (ratio = flow/capacity) ────
        public float SlowRatio;         // <0.7 Free / 0.7~1.0 Slow / >1.0 Jam
        public float JamRatio;

        // ── 경로 효율 E(병목) ∈ [EfficiencyMin, 1] ─
        public float EfficiencyMin;      // 바닥값 = 0.2
        public float EfficiencyMinRatio; // E가 바닥에 닿는 ratio = 2.0 (Free 1.0 → 2.0서 0.2)

        // ── 수요처 용량 캡 (가구 수) ──
        // 확장: 수요처 종류 추가 시 여기 SchoolCapacity 등 한 줄 + DemandMap.CapacityFor.
        public int   OfficeCapacity;    // 회사(Office) 20

        // ── 보상(코인) 원료 ────────────────────
        public float CoinBase;          // 🔓 공식 형태·가중치 잠정

        // ── Burst 감지 (히스테리시스 + 쿨다운) ──
        public float BurstJamEnterRatio;    // Jam 진입 1.0
        public float BurstFreeReturnRatio;  // Free 복귀 0.6 (경계 진동 방지)
        public float BurstCooldownSeconds;  // 타일당 10s (연사 방지)
        public float BurstRewardThreshold;  // pendingReward 이 값 넘어야 발행 🔓
        public float BurstRewardMultiplier; // 발행 시 pending × 배수 🔓

        // ── 정산 ───────────────────────────────
        public float OfflineCapHours;   // 오프라인 상한 8h

        // 개발·테스트용 임시 한 벌. Bootstrap 주입 전까지 이걸로 굴림.
        // ponytail: Bootstrap/SO 붙으면 이 팩토리는 지워도 됨.
        public static SimConfig Default() => new SimConfig
        {
            TickInterval = 0.1f,
            MaxStepsPerFrame = 5,
            RoadCapacity = 10f,
            SlowRatio = 0.7f,
            JamRatio = 1.0f,
            EfficiencyMin = 0.2f,
            EfficiencyMinRatio = 2.0f,
            OfficeCapacity = 20,
            CoinBase = 1f,
            BurstJamEnterRatio = 1.0f,
            BurstFreeReturnRatio = 0.6f,
            BurstCooldownSeconds = 10f,
            BurstRewardThreshold = 1f,
            BurstRewardMultiplier = 2f,
            OfflineCapHours = 8f,
        };
    }
}
