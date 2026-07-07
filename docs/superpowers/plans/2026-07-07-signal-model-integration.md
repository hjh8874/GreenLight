# 신호 모델 통합 (엔진 그린웨이브 ↔ 뷰 방향신호) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 엔진 처리량(`GreenWaveEfficiency`)과 뷰 신호등(`PhaseForAxis`)을 하나의 초록창 타이밍 모델에서 파생시켜 "보는 것 = 버는 것"을 구조적으로 보장한다.

**Architecture:** `SignalMath`에 공용 `GreenWindowFor` 프리미티브를 두고, 뷰 함수(`PhaseForAxis`/`IsGreen`)와 엔진 효율(`GreenWaveEfficiency`)이 **둘 다** 거기서 파생한다. 오프셋 부호를 직관 방향(오프셋↑=초록 늦게)으로 통일하고, 축(H/V)은 직선 경로에서 상쇄되므로 시그니처를 안 건드린다 → `FlowSolver`·`SimEngine`·뷰 렌더러 무변경. 변경은 `SignalMath.cs` 하나 + 테스트.

**Tech Stack:** Unity (C#), NUnit EditMode 테스트, MCP for Unity (`run_tests`).

## Global Constraints

- 대상 파일 **`Assets/01_Scripts/CityFlow/Sim/SignalMath.cs`만** 프로덕션 변경. `FlowSolver.cs`·`SimEngine.cs`·`Debug/SimTileRenderer.cs`·`ArrivalEmitter.cs`는 건드리지 않는다.
- 상수(SignalMath): `SlotSeconds = 0.5f`, `YellowFrac = 0.2f`, `ClearFrac = 0.15f`. 파생값: `CycleSlots=12` → cycle `6.0s`, half `3.0s`, greenLen `half·(1−0.2−0.15)=1.95s`.
- 통일 오프셋 부호: 초록창 열림 `open = mod(axisStart + OffsetSlots·SlotSeconds, cycle)`; 시간 판정은 `time − OffsetSlots·SlotSeconds`.
- 결정론 유지: per-car 랜덤·`Math.random`·시계 호출 금지. 순수 함수만.
- 효율 floor는 호출자가 넘김(테스트는 `0.5f`). `GreenWaveEfficiency` 시그니처 **불변**: `(Signal from, Signal to, int travelSlots, float floor)`.
- 테스트 어셈블리: `Assets/Tests/EditMode/` (namespace `CityFlow.Sim.Tests`).
- 브랜치 `feat/hwan-signal-model-merge` (이미 체크아웃됨). 커밋 메시지 `feat:`/`test:`/`refactor:` 콜론 스타일.

---

### Task 1: `GreenWindowFor` 공용 프리미티브

한 축의 초록창(열림 시각·길이)을 주는 순수 함수. 뷰·엔진이 공유할 단일 진실.

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Sim/SignalMath.cs`
- Test: `Assets/Tests/EditMode/SignalMathTests.cs`

**Interfaces:**
- Produces: `public static (double open, double greenLen) GreenWindowFor(Signal s, bool horizontal)` — `open` = 주기 내 초록 열림 시각(초, [0,cycle)), `greenLen` = 초록 길이(초). Task 2·3·5가 소비.

- [ ] **Step 1: 실패 테스트 작성** — `SignalMathTests.cs`에 추가

```csharp
[Test]
public void GreenWindowFor_AxesSeparatedByHalfCycle_SameLength()
{
    var s = new Signal { CycleSlots = 12 };   // cycle 6s, half 3s
    var (openH, lenH) = SignalMath.GreenWindowFor(s, true);
    var (openV, lenV) = SignalMath.GreenWindowFor(s, false);
    Assert.AreEqual(0.0, openH, 1e-9);
    Assert.AreEqual(3.0, openV, 1e-9);          // 세로는 반주기 뒤에 열림
    Assert.AreEqual(1.95, lenH, 1e-6);          // half(3)·0.65
    Assert.AreEqual(lenH, lenV, 1e-9);
}

[Test]
public void GreenWindowFor_OffsetDelaysOpen()   // 부호 통일: 오프셋↑ = 늦게 열림(직관)
{
    var s = new Signal { CycleSlots = 12, OffsetSlots = 2 };   // +1.0s
    Assert.AreEqual(1.0, SignalMath.GreenWindowFor(s, true).open, 1e-9);
}
```

- [ ] **Step 2: 컴파일 실패 확인**

Run: Unity Test Runner EditMode (MCP `run_tests`, filter `CityFlow.Sim.Tests`)
Expected: FAIL — `GreenWindowFor` 정의 없음(컴파일 에러)

- [ ] **Step 3: 최소 구현** — `SignalMath` 클래스에 추가 (기존 상수 아래)

```csharp
// 한 축(가로/세로)의 초록창: 열리는 시각(주기 내, 초)과 길이. 뷰·엔진이 공유하는 단일 타이밍.
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
```

- [ ] **Step 4: 통과 확인**

Run: Unity Test Runner EditMode (filter `CityFlow.Sim.Tests`)
Expected: PASS (신규 2개). 단, 기존 `IsGreen`/`PhaseForAxis`/`GreenWave*` 테스트는 아직 옛 사인이라 통과 유지(이 태스크에선 이들 안 건드림).

- [ ] **Step 5: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Sim/SignalMath.cs Assets/Tests/EditMode/SignalMathTests.cs
git commit -m "feat: add GreenWindowFor shared signal-timing primitive"
```

---

### Task 2: 뷰 함수를 프리미티브로 통일 (`PhaseForAxis`·`IsGreen`)

두 뷰 함수를 `GreenWindowFor`와 같은 부호로 맞추고, `PhaseForAxis`는 프리미티브 경유로 재구현. 동작은 오프셋 0·6에서 불변(기존 테스트 유지) + 부호 방향 테스트 추가.

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Sim/SignalMath.cs:21` (`IsGreen`), `:36` (`PhaseForAxis`)
- Test: `Assets/Tests/EditMode/SignalMathTests.cs`

**Interfaces:**
- Consumes: `GreenWindowFor` (Task 1)
- Produces: `PhaseForAxis(Signal,double,bool)`·`IsGreen(Signal,double)` — 시그니처 불변, 통일 부호. Task 5가 소비.

- [ ] **Step 1: 실패 테스트 작성** — 부호 방향 못박기(회귀 방지). `SignalMathTests.cs`에 추가

```csharp
[Test]
public void PhaseForAxis_OffsetDelaysGreen_UnifiedSign()
{
    // 오프셋 2슬롯(+1.0s) → 가로 초록창이 [1.0, 2.95)로 밀림: t=0.5 아직 빨강, t=1.5 초록
    var s = new Signal { CycleSlots = 12, OffsetSlots = 2 };
    Assert.AreEqual(SignalPhase.Red,   SignalMath.PhaseForAxis(s, 0.5, true));
    Assert.AreEqual(SignalPhase.Green, SignalMath.PhaseForAxis(s, 1.5, true));
}
```

- [ ] **Step 2: 실패 확인**

Run: Unity Test Runner EditMode (filter `CityFlow.Sim.Tests`)
Expected: FAIL — 현재 `+offset` 부호라 t=0.5가 Green으로 나옴(AreEqual Red 실패)

- [ ] **Step 3: `IsGreen` 부호 통일** — `SignalMath.IsGreen` 교체

```csharp
public static bool IsGreen(Signal s, double time)
{
    double cycle = s.CycleSlots * SlotSeconds;
    if (cycle <= 0) return true;                       // 주기 0 → 항상 통과
    double t = (time - s.OffsetSlots * SlotSeconds) % cycle;   // 부호 통일(오프셋↑=늦게)
    if (t < 0) t += cycle;
    return t < s.GreenSlots * SlotSeconds;
}
```

- [ ] **Step 4: `PhaseForAxis` 프리미티브 경유 재구현** — `SignalMath.PhaseForAxis` 교체

```csharp
// 방향 교대 3상태. 뷰·엔진이 같은 GreenWindowFor를 봄 → 처리량과 화면이 못 갈라짐.
public static SignalPhase PhaseForAxis(Signal s, double time, bool horizontal)
{
    double cycle = s.CycleSlots * SlotSeconds;
    if (cycle <= 0) return SignalPhase.Green;
    double half = cycle * 0.5;
    var (open, greenLen) = GreenWindowFor(s, horizontal);
    double yellowLen = half * YellowFrac;
    double local = (time - open) % cycle;              // 이 축 창 열림 기준 경과
    if (local < 0) local += cycle;
    if (local >= half) return SignalPhase.Red;         // 반대 축 창 = 빨강
    if (local < greenLen) return SignalPhase.Green;
    if (local < greenLen + yellowLen) return SignalPhase.Yellow;
    return SignalPhase.Red;                             // 전적색(정리)
}
```

- [ ] **Step 5: 통과 + 기존 뷰 테스트 회귀 확인**

Run: Unity Test Runner EditMode (filter `CityFlow.Sim.Tests`)
Expected: PASS — 신규 부호 테스트 통과 + 기존 `IsGreen_*`·`PhaseForAxis_GreenYellowRed_*`(오프셋 0·6, 부호 불변점) 그대로 통과. (`GreenWave*` 값 테스트는 아직 실패 상태일 수 있음 — Task 3에서 처리.)

- [ ] **Step 6: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Sim/SignalMath.cs Assets/Tests/EditMode/SignalMathTests.cs
git commit -m "refactor: unify view signals (PhaseForAxis/IsGreen) onto GreenWindowFor"
```

---

### Task 3: `GreenWaveEfficiency` 재구현 (같은 프리미티브·시그니처 불변)

엔진 효율을 `GreenWindowFor`에서 파생. 초록창 안착=1.0(플래토), 밖은 원형거리로 floor까지 선형. 기존 값 테스트를 새 모델로 갱신.

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Sim/SignalMath.cs:66` (`GreenWaveEfficiency`)
- Test: `Assets/Tests/EditMode/SignalMathTests.cs`

**Interfaces:**
- Consumes: `GreenWindowFor` (Task 1)
- Produces: `GreenWaveEfficiency(Signal from, Signal to, int travelSlots, float floor)` — 시그니처 불변(FlowSolver 무변경). Task 4·5가 소비.

- [ ] **Step 1: 기존 값 테스트 갱신 + 신규 작성** — `SignalMathTests.cs`에서 아래 3개를 **교체/추가**

기존 `GreenWave_HalfCycleOff_HitsFloor`와 `GreenWave_OffsetIsTheLever_MonotonicWithMisalignment`를 **삭제**하고 아래로 대체(`GreenWave_PerfectOffset_FullEfficiency`는 그대로 두면 통과):

```csharp
[Test]
public void GreenWave_ArrivalInsideGreen_IsPlateauOne()
{
    // 도착이 하류 초록창 안이면(정렬 포함) 1.0 — 초록 어디 잡아도 완전 통과(플래토)
    var a = new Signal { CycleSlots = 12, OffsetSlots = 0 };
    Assert.AreEqual(1f, SignalMath.GreenWaveEfficiency(a, new Signal { CycleSlots = 12, OffsetSlots = 4 }, 4, 0.5f), 1e-4f); // δ=0
    Assert.AreEqual(1f, SignalMath.GreenWaveEfficiency(a, new Signal { CycleSlots = 12, OffsetSlots = 2 }, 4, 0.5f), 1e-4f); // δ=1.0 < 1.95
}

[Test]
public void GreenWave_ArrivalPastGreen_FallsMonotonicToFloor()
{
    // 도착이 초록창 뒤로 점점 깊어질수록 효율 감소, 반대편 한복판 ≈ floor.
    var a = new Signal { CycleSlots = 12, OffsetSlots = 0 };
    float d2 = SignalMath.GreenWaveEfficiency(a, new Signal { CycleSlots = 12, OffsetSlots = 0  }, 4, 0.5f); // δ=2.0
    float d3 = SignalMath.GreenWaveEfficiency(a, new Signal { CycleSlots = 12, OffsetSlots = 10 }, 4, 0.5f); // δ=3.0
    float d4 = SignalMath.GreenWaveEfficiency(a, new Signal { CycleSlots = 12, OffsetSlots = 8  }, 4, 0.5f); // δ=4.0(≈최악)
    Assert.Greater(d2, d3);
    Assert.Greater(d3, d4);
    Assert.AreEqual(0.5f, d4, 0.02f);   // 최악 ≈ floor
}
```

- [ ] **Step 2: 실패 확인**

Run: Unity Test Runner EditMode (filter `CityFlow.Sim.Tests`)
Expected: FAIL — 옛 `GreenWaveEfficiency`(축 없는 선형·반대 부호)는 δ 기반 플래토 값과 안 맞음

- [ ] **Step 3: 재구현** — `SignalMath.GreenWaveEfficiency` 교체

```csharp
// 그린웨이브 효율 ∈ [floor,1]. 상류 초록 선두에 출발한 흐름이 travelSlots 뒤 하류 초록창에
// 안착하면 1(플래토), 반대편 한복판이면 floor. 뷰(PhaseForAxis)와 같은 GreenWindowFor에서 파생.
// ponytail: 인접 교차로 사이 회전(축 바뀜)은 도착 축으로 근사 — 정밀 회전 위상은 2차.
public static float GreenWaveEfficiency(Signal from, Signal to, int travelSlots, float floor)
{
    double cycle = from.CycleSlots * SlotSeconds;
    if (cycle <= 0) return 1f;                                     // 주기 이상 → 페널티 없음
    var (openFrom, _)      = GreenWindowFor(from, true);           // 축 무관(상쇄) → true 대표
    var (openTo, greenLen) = GreenWindowFor(to, true);
    double arrive = (openFrom + travelSlots * SlotSeconds) % cycle;
    double delta  = (arrive - openTo) % cycle;
    if (delta < 0) delta += cycle;
    if (delta < greenLen) return 1f;                               // 하류 초록 안착 = 완전 통과
    double gap    = System.Math.Min(delta - greenLen, cycle - delta);  // 초록창까지 원형 최단
    double maxGap = (cycle - greenLen) * 0.5;                      // 최악(반대편 한복판)
    double norm   = maxGap <= 0 ? 0.0 : gap / maxGap;
    if (norm < 0) norm = 0; else if (norm > 1) norm = 1;
    return (float)(1.0 - norm * (1.0 - floor));                    // 1 → floor 선형
}
```

- [ ] **Step 4: 통과 확인**

Run: Unity Test Runner EditMode (filter `CityFlow.Sim.Tests`)
Expected: PASS — `GreenWave_PerfectOffset_FullEfficiency`(δ=0→1.0) + 신규 플래토·단조 테스트. `SignalFlowTests`는 아직 옛 기대값이라 실패할 수 있음 → Task 4.

- [ ] **Step 5: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Sim/SignalMath.cs Assets/Tests/EditMode/SignalMathTests.cs
git commit -m "feat: derive GreenWaveEfficiency from shared GreenWindowFor (plateau model)"
```

---

### Task 4: `SignalFlowTests` 값 갱신 (엔진 통합 검증)

`FlowSolver`는 코드 변경 없지만 새 효율 모델로 delivered 값이 달라진다. 어긋남 케이스·e2e 초기값을 갱신. 정렬 케이스는 그대로 1.0.

**Files:**
- Test: `Assets/Tests/EditMode/SignalFlowTests.cs`

**Interfaces:**
- Consumes: `GreenWaveEfficiency`(Task 3), `FlowSolver.Resolve`, `SimEngine` (기존)

- [ ] **Step 1: `MisalignedSignals_ReduceThroughput` 갱신** — 명확히 나쁜 오프셋으로 교체

```csharp
[Test]
public void MisalignedSignals_ReduceThroughput()
{
    // 하류 오프셋 8 = 도착이 하류 초록창 반대편 → 효율 ≈ floor(0.5) → delivered 감소
    var (solver, _) = Solve(TwoSignalCity(), Cfg(), offsetAtSecond: 8);
    Assert.Less(solver.DeliveredTotal, 1f);
    Assert.AreEqual(0.506f, solver.DeliveredTotal, 0.02f);
}
```

- [ ] **Step 2: `EndToEnd_OffsetLever_ChangesThroughput` 갱신** — 나쁜 오프셋 → 조율 회복

기존 본문의 `e.Tick` 이후 단언 블록을 아래로 교체(도시 배치·`SignalTiles.Count` 확인은 유지):

```csharp
    e.Tick(0.25f);
    Assert.AreEqual(2, e.SignalTiles.Count);              // 교차로 2개 자동 감지

    Assert.IsTrue(e.TrySetSignalOffsetSlots(V(6, 0), 8)); // 일부러 어긋나게(반대편 착지)
    e.Tick(0.25f);
    Assert.Less(e.Stability01, 0.6f);                     // 처리량 저하

    Assert.IsTrue(e.TrySetSignalOffsetSlots(V(6, 0), 4)); // 그린웨이브 조율(이동시간 4)
    e.Tick(0.25f);
    Assert.AreEqual(1f, e.Stability01, 1e-3f);            // 회복 — 레버가 살아있음
```

- [ ] **Step 3: 통과 확인**

Run: Unity Test Runner EditMode (filter `CityFlow.Sim.Tests`)
Expected: PASS — `AlignedSignals_NoThroughputLoss`(offset 4 → 1.0) 무변경 통과 + 갱신 2개 통과 + `SingleSignal_NoEffect` 무변경.

- [ ] **Step 4: 커밋**

```bash
git add Assets/Tests/EditMode/SignalFlowTests.cs
git commit -m "test: update SignalFlow expectations for unified signal model"
```

---

### Task 5: 일치(anti-drift) 회귀 테스트 — 통합의 핵심 자산

두 모델이 다시 갈라지면 빨개지는 못. 엔진 효율과 뷰 페이즈를 같은 입력에서 교차 검증.

**Files:**
- Test: `Assets/Tests/EditMode/SignalMathTests.cs`

**Interfaces:**
- Consumes: `GreenWindowFor`·`GreenWaveEfficiency`·`PhaseForAxis`·`SlotSeconds` (모두 public)

- [ ] **Step 1: 일치 테스트 작성** — `SignalMathTests.cs`에 추가

```csharp
[Test]
public void SignalModels_Agree_EfficiencyMatchesViewGreen()
{
    // 통합 불변식: 엔진이 "완전 통과(1.0)"면 뷰도 그 도착 순간 초록. 어긋나면 둘 다 손실/빨강.
    var from = new Signal { CycleSlots = 12, OffsetSlots = 0 };
    var to   = new Signal { CycleSlots = 12, OffsetSlots = 4 };   // 정렬(offsetTo-offsetFrom=travel)
    const int travel = 4;
    double open = SignalMath.GreenWindowFor(from, true).open;
    double arrive = open + travel * SignalMath.SlotSeconds;       // 상류 초록 선두 출발 → 도착

    Assert.AreEqual(1f, SignalMath.GreenWaveEfficiency(from, to, travel, 0.5f), 1e-4f);   // 엔진: 통과
    Assert.AreEqual(SignalPhase.Green, SignalMath.PhaseForAxis(to, arrive, true));         // 뷰: 초록 ← 일치

    to.OffsetSlots = 8;                                            // 어긋남
    Assert.Less(SignalMath.GreenWaveEfficiency(from, to, travel, 0.5f), 1f);               // 엔진: 손실
    Assert.AreNotEqual(SignalPhase.Green, SignalMath.PhaseForAxis(to, arrive, true));      // 뷰: 초록 아님 ← 일치
}
```

- [ ] **Step 2: 통과 확인**

Run: Unity Test Runner EditMode (filter `CityFlow.Sim.Tests`)
Expected: PASS — 두 모델이 같은 프리미티브에서 파생하므로 일치.

- [ ] **Step 3: 전체 스위트 그린 확인** — 회귀 없음

Run: Unity Test Runner EditMode **전체**(필터 없이) + `read_console`로 컴파일 에러 0 확인
Expected: PASS — 신호 관련 신규/갱신 포함 전체 그린. `SignalMapTests`·결정론 테스트 무변경 통과.

- [ ] **Step 4: 커밋**

```bash
git add Assets/Tests/EditMode/SignalMathTests.cs
git commit -m "test: add anti-drift guard pinning engine efficiency to view green"
```

---

## Self-Review

**Spec coverage:**
- §3.1 GreenWindowFor → Task 1 ✓
- §3.2 뷰 부호 통일/리팩터 → Task 2 ✓
- §3.3 효율 재구현(축 상쇄, 시그니처 불변) → Task 3 ✓
- §3.4 소비자 무변경(FlowSolver/SimEngine/뷰/ArrivalEmitter) → Task 3·4에서 값만 검증, 프로덕션 무변경 ✓
- §7 테스트 1~9 → 창(T1), 부호/회귀(T2), peak·플래토·단조(T3), 신호0~1·e2e(T4·기존 유지), 일치(T5), 결정론(T5 전체 스위트) ✓
- §2 재밸런싱 나중 → 값은 관계/끝점 위주, 리밸런싱 2차 ✓

**Placeholder scan:** 모든 스텝에 실제 코드/명령/기대값. TBD 없음 ✓

**Type consistency:** `GreenWindowFor` 반환 `(double open, double greenLen)`을 T3·T5에서 동일 사용. `GreenWaveEfficiency` 4-인수 시그니처 T3 정의 = T4·T5 호출 일치. `PhaseForAxis(Signal,double,bool)`·`SlotSeconds`(public const) 일치 ✓

**Note:** Unity 테스트 실행은 MCP `run_tests`(EditMode) 또는 에디터 Test Runner. 스크립트 수정 후 `read_console`로 컴파일 통과 확인 뒤 테스트(도메인 리로드 대기).
