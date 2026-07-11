namespace CityFlow.Sim
{
    // 모든 밸런스 숫자를 한 곳에 담는 값 묶음.
    // blueprint §0: const 금지(런타임·툴에서 튜닝 가능해야) → 전부 필드.
    // 실제로는 이진우의 EconomyConfig(ScriptableObject) → Bootstrap이 변환해 주입.
    // ponytail: 지금은 임시값. 밸런싱 전이라 틀려도 됨 — "그릇"만 확정.
    [System.Serializable]   // SimConfigAsset(SO)이 인스펙터에 노출하기 위해
    public struct SimConfig
    {
        // ── 고정 틱 ─────────────────────────────
        public float TickInterval;      // 시뮬 1스텝(초). blueprint §3 = 0.1
        public int   MaxStepsPerFrame;  // 누산기 while 폭주 방지 캡

        // ── 도시 크기 ───────────────────────────
        public int   GridWidth;         // blueprint 기준 20×20
        public int   GridHeight;

        // ── 흐름 rate (단위 통일: 대/초 — 도착 누산기가 rate×TickInterval 전제) ──
        public float RoadCapacity;      // 대/초, 도로 등급별(지금 단일) 🔓
        public float DemandPerHouse;    // 수요 1건(집→수요처)당 가상 차량 rate 🔓

        // ── 혼잡 임계 (ratio = flow/capacity) ────
        public float SlowRatio;         // <0.7 Free / 0.7~1.0 Slow / >1.0 Jam
        public float JamRatio;

        // ── 경로 효율 E(병목) ∈ [EfficiencyMin, 1] ─
        public float EfficiencyMin;      // 바닥값 = 0.2
        public float EfficiencyMinRatio; // E가 바닥에 닿는 ratio = 2.0 (Free 1.0 → 2.0서 0.2)

        // ── 신호 그린웨이브 ─────────────────────
        public float GreenWaveFloor;     // 오프셋 최악(반주기 어긋남) 시 효율 바닥 🔓

        // ── 수요처 용량 캡 (가구 수) ──
        // 확장: 수요처 종류 추가 시 여기 SchoolCapacity 등 한 줄 + DemandMap.CapacityFor.
        public int   OfficeCapacity;    // 회사(Office) 20
        public int   SchoolCapacity;    // 학교(School) 10

        // ── 수요 배정 다양성 ──
        // 집이 '가까운 K곳' 중 하나로 출근(좌표 해시로 결정론적 선택). 1 = 항상 최근접.
        // 3이면 통근 동선이 흩어져 도시가 살아 보임 🔓
        public int   DemandChoicePool;

        // ── 수요 맥동(러시아워) ──
        // 하루 두 봉우리(출근·퇴근)로 수요가 뭉쳐 나와 병목이 자연스럽게 '빌드업'된다(기획 §1).
        // 0 = 균일(기존 동작·테스트 그대로). 배율은 DemandPulse() 참조 🔓
        public float RushAmplitude;      // 봉우리에서 수요가 몇 배 더해지나 (0.6 = 최대 1.6배)
        public float DayLengthSeconds;   // 시뮬 하루 길이(초)

        // ── 신호 오버라이드 스킬(기획 §2-D) ──
        // 탭 = 양축 강제 초록으로 체증 세척. 쿨다운은 엔진이 강제(트러스트 경계) 🔓
        public float OverrideDurationSeconds;
        public float OverrideCooldownSeconds;
        public int   OverrideCorridorSignals;   // 코리도어 최대 신호 수(anchor 포함). 라인이 짧으면 그만큼만.

        // ── 무신호 교차로 간섭(신호 구매 피벗 1단계) ──
        // 교차 교통 1이 내 축을 λ만큼 방해(양보 협상 오버헤드) — MM식 자연 양보의 rate 근사.
        // λ=1이면 기존 합산과 동일(연속성). 자동생성 유지 중엔 라이브 미노출(모든 교차로에 신호) 🔓
        public float UnsignaledInterference;

        // ── 회전교차로(스펙 2026-07-11): 낮은 양보 간섭 + 전원 감속(용량 페널티) ──
        // 균형 교차로(s>2/3)=로터리, 편중(0.375~2/3)=신호, 극단(<0.375)=무신호가 로터리보다 나음(돈 낭비) — 3분할 전략.
        // 상수 λ만 쓰면 최적 신호를 항상 이겨 전략이 죽는다(스펙 §1) — cf<1이 균형추 🔓
        public float RoundaboutInterference;    // λr: 교차 교통의 방해 계수
        public float RoundaboutCapacityFactor;  // cf: 로터리 타일 유효 용량 배율

        // ── 유기적 라우팅(혼잡 회피 강도) ──
        // 증분 배정의 스텝 비용 = 물리거리 × (1 + w × 부하/용량). 0 = 순수 물리 최단.
        // 2면 부하율 1.5 타일이 4배 비쌈 → 몇 칸 우회가 이득 🔓
        public float RoutingCongestionWeight;

        // ── 신호 배치 모드(구매 피벗 2단계) ──
        // true = 현행 자동 감지(모든 교차로에 신호). false = 배치된 곳에만 존재(TryPlaceSignal).
        // 상점 UI(김건) 도입 시 asset에서 false 전환 — 그날 무신호 간섭 λ가 라이브 활성화 🔓
        public bool AutoDetectSignals;

        // ── 보상(코인) 원료 ────────────────────
        public float CoinBase;          // 🔓 공식 형태·가중치 잠정

        // ── Burst 감지 (히스테리시스 + 쿨다운) ──
        public float BurstJamEnterRatio;    // Jam 진입 1.0
        public float BurstFreeReturnRatio;  // Free 복귀 0.6 (경계 진동 방지)
        public float BurstCooldownSeconds;  // 타일당 10s (연사 방지)
        public float BurstRewardThreshold;  // pendingReward 이 값 넘어야 발행 🔓
        public float BurstRewardMultiplier; // 발행 시 pending × 배수 🔓 ⚠ 1 초과 금지 —
                                            // m>1이면 "고의 정체→해소" 파밍이 순이익(BurstGuardTests가 지킴)

        // ── 정산 ───────────────────────────────
        public float OfflineCapHours;   // 오프라인 상한 8h

        // 개발·테스트용 임시 한 벌. Bootstrap 주입 전까지 이걸로 굴림.
        // ponytail: Bootstrap/SO 붙으면 이 팩토리는 지워도 됨.
        public static SimConfig Default() => new SimConfig
        {
            TickInterval = 0.1f,
            MaxStepsPerFrame = 5,
            GridWidth = 20,
            GridHeight = 20,
            RoadCapacity = 10f,
            DemandPerHouse = 1f,
            SlowRatio = 0.7f,
            JamRatio = 1.0f,
            EfficiencyMin = 0.2f,
            EfficiencyMinRatio = 2.0f,
            GreenWaveFloor = 0.5f,
            OfficeCapacity = 20,
            SchoolCapacity = 10,
            DemandChoicePool = 3,
            RushAmplitude = 0f,        // 기본 오프 — SimDebug 씬은 SO 에셋으로 켠다
            DayLengthSeconds = 120f,
            // 코리도어 버스트: 3초 강제 초록(일자 라인 최대 3신호) + 60초 쿨다운 = 업타임 ~5%, 짧고 강한 스킬.
            OverrideDurationSeconds = 3f,
            OverrideCooldownSeconds = 60f,
            OverrideCorridorSignals = 3,
            UnsignaledInterference = 1.5f,
            RoundaboutInterference = 0.25f,
            RoundaboutCapacityFactor = 0.7f,
            RoutingCongestionWeight = 2f,
            AutoDetectSignals = true,
            CoinBase = 1f,
            BurstJamEnterRatio = 1.0f,
            BurstFreeReturnRatio = 0.6f,
            BurstCooldownSeconds = 10f,
            BurstRewardThreshold = 1f,
            BurstRewardMultiplier = 1f,   // 밀린 처리량 전액 회수(무이자 외상 정산) = 파밍 중립(환 2026-07-11)
            OfflineCapHours = 8f,
        };

        // 수요 맥동 배율(순수 함수 — 결정론·세이브 안전). sin(4πt/T)의 양수 구간만 취해
        // 하루에 두 번(출근·퇴근) 수요가 부풀었다 가라앉는다. 진폭 0이면 항상 1.
        public static float DemandPulse(double simTime, in SimConfig cfg)
        {
            if (cfg.RushAmplitude <= 0f || cfg.DayLengthSeconds <= 0f) return 1f;
            float t01 = (float)(simTime % cfg.DayLengthSeconds / cfg.DayLengthSeconds);
            float s = UnityEngine.Mathf.Sin(4f * UnityEngine.Mathf.PI * t01);
            return 1f + cfg.RushAmplitude * UnityEngine.Mathf.Max(0f, s);
        }
    }
}
