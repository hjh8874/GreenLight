# 지각 출근(B) Implementation Plan

> **For agentic workers:** 이 계획을 스텝 그대로 실행한다. 설계: `docs/superpowers/specs/2026-07-30-hiring-feedback-design.md` §B (결정: **퇴근창 시작 전까지만** 그날 지각 출근).

**Goal:** 출근창이 지난 시각에 채용된 새 직원이 다음 날까지 잠들지 않고 그날 즉시 지각 출근한다 — 단 퇴근창이 시작되면 다음 날로.

**Architecture:** 신규 차 전용 훅 `CommuteScheduler.SnapNewToHour`에서만 `AwaitingNextWave`를 조건부 해제한다. 기존 차(리빌드 생존자)의 의미는 그대로. 5줄짜리 변경 + 테스트 3건.

## Global Constraints

- 브랜치: **`feat-late-hire-departure-hwan`** (develop `d50078e` 직분기). 브랜치 변경 금지.
- 회귀 기준선: `CityFlow.Sim.Tests` **423/423**. 부분 실패 허용 없음.
- 검증 순서(고정): `refresh_unity`(compile=request, mode=force) → `read_console`(types=["error"]) — `error CS` 포함만 진짜 에러(`Bridge not running`·`NanumGothic` 무시) → `run_tests`(assembly_names=["CityFlow.Sim.Tests"]).
- RED 먼저 증명. 씬 커밋 금지. **수정 파일은 `CommuteScheduler.cs` + `CommuteSchedulerTests.cs` 딱 2개** — 그 외가 필요하면 escalation.

## ⚠️ #182(회사 3종)와의 관계 — 리베이스 예정 해결법

이 브랜치는 develop(`d50078e`, #182 미머지) 기준이다. develop의 판정은 전역 창(`_eveningStart`)이고,
#182 머지 후에는 차 개별 값(`car.EveningStartHour`)과 `CommuteWindow.InWindow`로 바뀐다.
**#182 머지 후 리베이스 충돌 시 기계적 해결:**

```csharp
// develop(지금) 구현:
if (car.AwaitingNextWave && hour >= car.DepartHomeHour && hour < _eveningStart)
// #182 머지 후 대체:
if (car.AwaitingNextWave && CommuteWindow.InWindow(hour, car.DepartHomeHour, car.EveningStartHour))
```

의미는 동일("개인 출근 시각 이후 ~ 퇴근창 시작 전")이고, #182 판이 자정 넘김(야간조)도 공짜로 처리한다.

## Task: 지각 출근

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Sim/CommuteScheduler.cs` — `SnapNewToHour`(`:337-349`)
- Test: `Assets/Tests/EditMode/CommuteSchedulerTests.cs` (기존 파일에 추가)

**현재 동작 (근거 코드):**
- 신규 차는 `AwaitingNextWave = deferNewAssignments`(`:233`)로 생성된다
- `UpdateDepartures`(`:299-305`): `AwaitingNextWave`면 `hour < DepartHomeHour`를 관측해야만 해제 →
  **출근 시각이 이미 지난 시각에 채용되면 다음 날 아침까지 잠긴다** (실시간 최대 ~10분 무반응)
- `SnapCar`(`:357`): `AwaitingNextWave`면 무조건 `ParkedHome`

- [ ] **Step 1 (RED): 테스트 3건 추가** — 기존 `Build` 헬퍼 스타일을 따르되 `deferNewAssignments: true`로 리빌드하는 로컬 구성을 쓴다:

```csharp
        // 지각 출근(2026-07-30 환 결정): 출근 시각이 지난 낮 시간대에 채용된 신규 차는
        // 다음 날을 기다리지 않고 그날 즉시 출근한다. 퇴근창 이후 채용은 현행대로 다음 날.
        [Test]
        public void NewHire_DuringDay_DepartsSameDay()
        {
            var s = BuildDeferred(hour: 12f);   // 출근창(6~10)은 지났고 퇴근창(17~)은 전
            var car = s.Cars[0];
            Assert.IsFalse(car.AwaitingNextWave, "낮 채용은 대기 해제");
            s.UpdateDepartures(12f);
            Assert.AreEqual(CarState.Outbound, car.State, "그날 즉시 지각 출근");
        }

        [Test]
        public void NewHire_DuringEvening_WaitsForNextDay()
        {
            var s = BuildDeferred(hour: 18f);   // 퇴근창(17~21) 안
            var car = s.Cars[0];
            Assert.IsTrue(car.AwaitingNextWave, "퇴근창 채용은 다음 날");
            s.UpdateDepartures(18f);
            Assert.AreEqual(CarState.ParkedHome, car.State);
        }

        [Test]
        public void NewHire_BeforeMorning_KeepsNormalSameDayFlow()
        {
            var s = BuildDeferred(hour: 4f);    // 출근 시각 전 — 종전에도 그날 출근했다
            var car = s.Cars[0];
            s.UpdateDepartures(4f);
            Assert.AreEqual(CarState.ParkedHome, car.State, "아직 출근 시각 전");
            s.UpdateDepartures(car.DepartHomeHour + 0.05f);
            Assert.AreEqual(CarState.Outbound, car.State, "정상 출근 유지");
        }

        static CommuteScheduler BuildDeferred(float hour)
        {
            var sources = new List<Vector2Int> { V(0, 0) };
            var sinks = new List<Vector2Int> { V(50, 50) };
            var s = new CommuteScheduler();
            s.Rebuild(sources, sinks, _ => 4, homeSlots: 1, maxCars: 96,
                morningStart: 6f, morningEnd: 10f, eveningStart: 17f, eveningEnd: 21f,
                deferNewAssignments: true);
            s.SnapNewToHour(hour);
            return s;
        }
```

> `Rebuild` 실제 시그니처(인자명·순서)는 파일을 열어 맞춘다. RED 기대: 1번 테스트가
> `AwaitingNextWave == true`로 **단정 실패** (컴파일은 된다 — 신규 API 없음).

- [ ] **Step 2 (RED 확인):** `refresh_unity` → `read_console` 0 → `run_tests` — 1번 실패·2/3번 통과 확인.
- [ ] **Step 3 (구현):** `SnapNewToHour` 루프에서 `SnapCar` 호출 **직전**에 추가:

```csharp
                // 지각 출근(2026-07-30 환 결정): 채용 시각이 [개인 출근 시각, 퇴근창 시작) 안이면
                // 다음 날을 기다리지 않는다 — "낮 로드 = 지각 출근"(2026-07-17) 철학의 신규 채용 확장.
                // 퇴근 러시 직전 역방향 출근차를 피하려고 퇴근창 이후 채용은 현행 유지(다음 날).
                // 기존 차(리빌드 생존자)의 AwaitingNextWave 의미는 건드리지 않는다 — 신규 차 훅에서만.
                if (_newCars[i].AwaitingNextWave &&
                    hour >= _newCars[i].DepartHomeHour && hour < _eveningStart)
                {
                    _newCars[i].AwaitingNextWave = false;
                }
```

- [ ] **Step 4 (GREEN):** 게이트 3단 — 기준선 423 + 신규 3 = **426/426**.
- [ ] **Step 5 (커밋):**

```bash
git add Assets/01_Scripts/CityFlow/Sim/CommuteScheduler.cs Assets/Tests/EditMode/CommuteSchedulerTests.cs
git commit  # [Feat] 신규 채용 지각 출근 — 낮 채용은 그날 출발, 퇴근창 이후는 다음 날
```

커밋 본문에 #182 리베이스 해결법(위 코드 블록)을 그대로 실어라.

- [ ] **Step 6:** worker_done — RED 증거(단정 실패 메시지), 최종 426/426, 커밋 해시.
