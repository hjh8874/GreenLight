namespace CityFlow.Sim
{
    // 모든 밸런스 숫자를 한 곳에 담는 값 묶음.
    // blueprint §0: const 금지(런타임·툴에서 튜닝 가능해야) → 전부 필드.
    // 실제로는 이진우의 EconomyConfig(ScriptableObject) → Bootstrap이 변환해 주입.
    // ponytail: 지금은 임시값. 밸런싱 전이라 틀려도 됨 — "그릇"만 확정.
    [System.Serializable]   // SimConfigAsset(SO)이 인스펙터에 노출하기 위해
    public struct SimConfig
    {
        // 시뮬레이션 물리량 헬퍼
        // 차 1대가 방해 없이 이동할 때 1초당 몇 타일을 가는가 (현재 엔진 규약 상 1틱당 1칸)
        public float GetFreeFlowSpeed() => 1f / TickInterval;
        public float GetTravelSeconds(int distTiles) => distTiles / GetFreeFlowSpeed();

        // ── 고정 틱 ─────────────────────────────
        public float TickInterval;      // 시뮬 1스텝(초). blueprint §3 = 0.1
        public int   MaxStepsPerFrame;  // 누산기 while 폭주 방지 캡

        // ── 도시 크기 ───────────────────────────
        // 생성 시 고정(구조 필드) — 같은 성격의 필드 추가 시 SimEngine.ApplyConfig 보존 목록 갱신.
        public int   GridWidth;         // blueprint 기준 20×20
        public int   GridHeight;

        // ── 도로/라우팅 튜닝 ───────────────────
        public float RoadCapacity;      // 대/초, 도로 등급별(지금 단일) 🔓

        // ── 차 단위 큐 Sim (3차 빌드) ──────────
        // 타일의 진입방향별 FIFO가 각각 가질 수 있는 차 토큰 수. SimConfigAsset.Value를
        // 통해 인스펙터에 자동 노출되며 기존 .asset은 0이므로 전환 전 수동 설정이 필요하다. 🔓
        public int QueueCapacityPerTile;
        public int QueueServicePerTick;   // 한 방향 큐가 틱당 서비스할 최대 차 수 🔓
        public int GridlockValveTicks;    // 머리 차 연속 막힘 후 강제 전이까지의 틱 수 🔓
        public int CoinPerTrip;           // 회사 도착 1회 보상 🔓
        public int CarsPerHouse;          // 집 주차 슬롯/차량 상한 🔓
        public float MorningStartHour;
        public float MorningEndHour;
        public float EveningStartHour;
        public float EveningEndHour;
        public int MaxSimCars;
        public float QueueSlowRatio;
        public float QueueJamRatio;

        // ── 혼잡 임계 (ratio = flow/capacity) ────
        public float SlowRatio;         // <0.7 Free / 0.7~1.0 Slow / >1.0 Jam
        public float JamRatio;

        // ── 경로 효율 E(병목) ∈ [EfficiencyMin, 1] ─
        public float EfficiencyMin;      // 바닥값 = 0.2
        public float EfficiencyMinRatio; // E가 바닥에 닿는 ratio = 2.0 (Free 1.0 → 2.0서 0.2)

        // ── 안정도 국지 감점 ─────────────────────
        public float StabilityJamWeight;  // jam 타일 비율이 안정도에 주는 가중치 🔓

        // ── 신호 그린웨이브 ─────────────────────
        public float GreenWaveFloor;     // 오프셋 최악(반주기 어긋남) 시 효율 바닥 🔓
        public int GreenWaveScanInterval; // 그린웨이브 판정 주기 (틱)
        public float GreenWaveThreshold; // 그린웨이브 연출 달성 임계 (기본 0.85f)
        public float GreenWaveMagnitudeOffset; // 강도 환산 기준 (기본 0.8f)
        public float GreenWaveMagnitudeScale; // 강도 환산 배율 (기본 10f)

        // ── 수요처 용량 캡 (가구 수) ──
        // 회사 정원은 배정과 주차가 함께 쓰는 단일 값이며 프리팹 주차 자리 수를 넘기지 않는다.
        public int   OfficeCapacity;    // 회사(Office) 6
        public float CompanyHiringSlotsPerGameHour; // 건설 후 게임 시간당 열리는 회사 자리 수 🔓
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

        // ── 우선도로(스펙 2026-07-13): 무신호 간섭의 비대칭 확장 ──
        // 메인축은 곁길로부터 거의 방해 안 받고(λ_main≈0), 곁길은 메인축에 크게 양보(λ_yield↑).
        public float PriorityMainInterference;    // λ_main
        public float PriorityYieldInterference;   // λ_yield

        // ── 유기적 라우팅(혼잡 회피 강도) ──
        // 증분 배정의 스텝 비용 = 물리거리 × (1 + w × 부하/용량). 0 = 순수 물리 최단.
        // 2면 부하율 1.5 타일이 4배 비쌈 → 몇 칸 우회가 이득 🔓
        public float RoutingCongestionWeight;

        // ── 신호 배치 모드(구매 피벗 2단계) ──
        // true = 현행 자동 감지(모든 교차로에 신호). false = 배치된 곳에만 존재(TryPlaceSignal).
        // 상점 UI(김건) 도입 시 asset에서 false 전환 — 그날 무신호 간섭 λ가 라이브 활성화 🔓
        // 생성 시 고정(구조 필드) — 같은 성격의 필드 추가 시 SimEngine.ApplyConfig 보존 목록 갱신.
        public bool AutoDetectSignals;

        // ── 도착 코인 환율 ─────────────────────
        public float CoinBase;          // 🔓 공식 형태·가중치 잠정

        // ── 도로 카운터코스트: 예산제(스펙 2026-07-17, 기획 결정 환) ──
        // 유지비(러닝코스트) → 도로 타일 스톡 상한. 도배 방어를 "손해"→"물리적 불가"로 전환.
        // 기존 RoadMaintPerSec 유지비 체인은 삭제됨(MaintenanceEvent dead chain 포함).


        // ── 도로 확장권(스펙 §2단계): "+10칸"을 코인으로 구매, 가격 = 기본가 × 성장률^구매횟수 ──
        // 에스컬레이션이 도배의 수학적 소프트 캡(맵 도배 총비용 >> 현실 지평) 🔓
        public int   RoadExpandBaseCost;     // 첫 확장권 가격(코인, 반올림 정수 산출의 기저)
        public float RoadExpandCostGrowth;   // 구매마다 곱해지는 성장률(1.5 = +50%)

        // ── Burst 감지 (히스테리시스 + 쿨다운) ──
        public float BurstJamEnterRatio;    // Jam 진입 1.0
        public float BurstFreeReturnRatio;  // Free 복귀 0.6 (경계 진동 방지)
        public float BurstCooldownSeconds;  // 타일당 10s (연사 방지)
        public float BurstRewardThreshold;  // pending magnitude가 이 값 넘어야 발행 🔓

        // 개발·테스트용 임시 한 벌. Bootstrap 주입 전까지 이걸로 굴림.
        // ponytail: Bootstrap/SO 붙으면 이 팩토리는 지워도 됨.
        public static SimConfig Default() => new SimConfig
        {
            TickInterval = 0.1f,
            MaxStepsPerFrame = 5,
            GridWidth = 20,
            GridHeight = 20,
            RoadCapacity = 10f,
            QueueCapacityPerTile = 4,
            QueueServicePerTick = 1,
            GridlockValveTicks = 8,
            CoinPerTrip = 10,
            CarsPerHouse = 2,
            MorningStartHour = 6f,
            MorningEndHour = 10f,
            EveningStartHour = 17f,
            EveningEndHour = 21f,
            MaxSimCars = 96,
            QueueSlowRatio = 0.5f,
            QueueJamRatio = 0.99f,
            SlowRatio = 0.7f,
            JamRatio = 1.0f,
            EfficiencyMin = 0.2f,
            EfficiencyMinRatio = 2.0f,
            StabilityJamWeight = 0.5f,
            GreenWaveFloor = 0.5f,
            GreenWaveScanInterval = 50,
            GreenWaveThreshold = 0.85f,
            GreenWaveMagnitudeOffset = 0.8f,
            GreenWaveMagnitudeScale = 10f,
            OfficeCapacity = 6,
            CompanyHiringSlotsPerGameHour = 2f,
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
            PriorityMainInterference = 0.1f,
            PriorityYieldInterference = 2.5f,
            RoutingCongestionWeight = 2f,
            AutoDetectSignals = true,
            CoinBase = 1f,

            RoadExpandBaseCost = 100,
            RoadExpandCostGrowth = 1.5f,
            BurstJamEnterRatio = 1.0f,
            BurstFreeReturnRatio = 0.6f,
            BurstCooldownSeconds = 10f,
            BurstRewardThreshold = 1f,
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
