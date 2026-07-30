# 회사 3종 + 유형별 출퇴근 시간대 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 회사를 사무실·공장·물류창고 3종으로 나누고 종류마다 출퇴근 시간대와 정원을 다르게 해서, 같은 도로에 시간대별로 다른 교통 패턴이 나타나게 한다.

**Architecture:** `TileType`은 확장하지 않는다. `Office` 하나에 인스턴스 데이터(유형 id)를 붙이고, 유형별 출퇴근 창을 SO 카탈로그에 둔다. `CommuteScheduler`는 전역 창 4개 인자 대신 목적지별 창을 돌려주는 콜백을 받고, 출퇴근 판정을 전역 필드가 아니라 **차 개별 값** 기준으로 바꿔 자정을 넘는 근무를 지원한다.

**Tech Stack:** Unity 6000.5.2f1 · C# · NUnit EditMode (`CityFlow.Sim.Tests`)

**설계 문서:** `docs/superpowers/specs/2026-07-29-company-types-commute-windows-design.md`

## Global Constraints

- 기준 브랜치: **`develop 74bec26`에서 직접 분기.** 스택 금지 — Squash 머지라 브랜치를 쌓으면 diff가 중복된다.
- 회귀 기준선: **EditMode `CityFlow.Sim.Tests` 423/423 green** (`develop 74bec26` 실측, 2026-07-30). **부분 실패 허용 없음.**
- 검증 순서 (매 태스크 끝): `refresh_unity`(compile=request, mode=force) → `read_console`(types=["error"]) → `run_tests`(EditMode, `CityFlow.Sim.Tests`).
- **무시해도 되는 콘솔 에러 2건** — 환경/도구 로그이지 코드 문제가 아니다:
  1. `Required external font asset is missing: 'Assets/99_Download/Fonts/NanumGothic SDF.asset'`
  2. `MCP-FOR-UNITY: Connection verification failed: Bridge not running`
  **판단 기준: 메시지에 `error CS`가 들어 있으면 진짜 컴파일 에러다.** 그 외 에러가 있으면 멈추고 보고한다.
- **테스트가 돌았다는 것이 컴파일 성공을 뜻하지 않는다.** Unity는 컴파일 실패 시 직전 성공 DLL로 테스트를 돌린다. 반드시 `read_console`을 먼저 본다.
- **작업은 본 체크아웃 `/Users/hwan/Gamemaker/GreenLight`에서만.** `git worktree`·격리 사본 금지 — `Library/`가 없으면 전체 재임포트(10~30분)가 필요하고 Unity 에디터·unityMCP가 본 체크아웃에만 붙어 있어 컴파일 검증이 불가능하다.
- **통합 씬을 커밋하지 않는다.** 라이브 확인은 `Assets/00_Scenes/Debug/CityFlowIntegrated_hwan.unity`에서만 하고 씬 diff는 커밋에서 제외한다.
- 신규 `.cs`는 **`.cs.meta`를 함께 커밋한다.** 빠뜨리면 다른 사람 환경에서 GUID가 달라져 참조가 깨진다.
- `SimConfig`에 필드를 추가하면 **`.asset` 3개를 반드시 함께 채운다**(2026-07-22 팀 규칙). 순서가 아니라 **누락**이 위험 — 빠뜨리면 조용히 0이 들어간다.
- 커밋 메시지 접두 `[Feat]`/`[Fix]`. 커밋 전 `git status`로 **코드 파일만** 스테이징됐는지 확인한다.
- **시각 값은 게임시간 `[0,24)` 단위**다. 하루 길이(`DayLengthSeconds`)와 무관하므로 "하루 12분 전환"(별도 담당자) 작업을 기다리지 않는다.

## File Structure

| 파일 | 책임 | 변경 |
|---|---|---|
| `Assets/01_Scripts/CityFlow/Sim/CommuteWindow.cs` | 출퇴근 창 값 + 자정 순환 판정 순수 함수 | **신규** |
| `Assets/01_Scripts/CityFlow/Sim/CommuteScheduler.cs` | 통근 상태기 | 수정 — 판정을 차 개별 값으로, `Rebuild` 시그니처 |
| `Assets/01_Scripts/CityFlow/Sim/CommuteWindow.cs` | `CompanyTypeInfo` — Sim 쪽 유형 표현 | 수정 (Task 3) |
| `Assets/01_Scripts/CityFlow/Configs/Buildings/CompanyTypeSO.cs` | 회사 유형 1종 정의(오서링, `Assembly-CSharp`) | **신규** (Task 7) |
| `Assets/01_Scripts/CityFlow/Configs/Buildings/CompanyTypeCatalogSO.cs` | 유형 카탈로그(id 조회, 오서링) | **신규** (Task 7) |
| `Assets/01_Scripts/CityFlow/Bootstrap/CityBootstrap.cs` | SO → `CompanyTypeInfo` 배선 | 수정 (Task 7) |
| `Assets/01_Scripts/CityFlow/Sim/DemandMap.cs` | 회사 인스턴스 상태 | 수정 — `CompanyTypeId` 보관 |
| `Assets/01_Scripts/CityFlow/Sim/SimEngine.cs` | 파사드 | 수정 — `Place` 유형 인자·거부, 창 조회 제공 |
| `Assets/01_Scripts/CityFlow/Sim/CarSim.cs` | `Rebuild` 호출부 | 수정 — 콜백 전달 |
| `Assets/Tests/EditMode/CommuteWindowTests.cs` | 순수 함수 테스트 | **신규** |
| `Assets/Tests/EditMode/CompanyTypeTests.cs` | 유형·창 통합 테스트 | **신규** |
| `Assets/Tests/EditMode/CommuteSchedulerTests.cs` | 기존 통근 테스트 | 수정 — 시그니처 변경 반영 |

## 단계와 PR 분할

```
Phase ②  자정 넘김 지원         Task 1~2        → PR 1   (동작 무변경, 단독 머지 안전)
Phase ③  회사 3종 + 유형별 창    Task 3~5, 7     → PR 2
Phase ④  공사 중 유형 보존       Task 6          → PR 3   ← #171 머지 완료(0d313da), 조건 충족
```

Phase ②는 **동작 무변경**으로 끝난다(모든 차가 여전히 같은 전역 창을 받는다). 자정 넘김 능력만 생기고 쓰는 데가 없다. 그래서 단독 머지가 안전하다.

---

### Task 1: `CommuteWindow` + 자정 순환 판정

**Files:**
- Create: `Assets/01_Scripts/CityFlow/Sim/CommuteWindow.cs`
- Test: `Assets/Tests/EditMode/CommuteWindowTests.cs` (신규)

**Interfaces:**
- Produces:
  - `internal readonly struct CommuteWindow { string CompanyTypeId; float StartHour; float StartWindow; float EndHour; float EndWindow; }`
  - `internal static bool CommuteWindow.InWindow(float hour, float start, float end)`
  - Task 2·5가 둘 다 쓴다.

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/EditMode/CommuteWindowTests.cs`:

```csharp
using NUnit.Framework;

namespace CityFlow.Sim.Tests
{
    public class CommuteWindowTests
    {
        [Test]
        public void InWindow_NormalRange_IsHalfOpen()
        {
            // [6, 15) — 시작 포함, 끝 배타
            Assert.IsTrue(CommuteWindow.InWindow(6f, 6f, 15f), "시작 시각 포함");
            Assert.IsTrue(CommuteWindow.InWindow(10f, 6f, 15f));
            Assert.IsTrue(CommuteWindow.InWindow(14.99f, 6f, 15f));
            Assert.IsFalse(CommuteWindow.InWindow(5.99f, 6f, 15f));
            Assert.IsFalse(CommuteWindow.InWindow(15f, 6f, 15f), "끝 시각은 배타");
            Assert.IsFalse(CommuteWindow.InWindow(20f, 6f, 15f));
        }

        [Test]
        public void InWindow_WrapsMidnight_WhenStartGreaterThanEnd()
        {
            // [20, 5) — 자정을 넘는 구간
            Assert.IsTrue(CommuteWindow.InWindow(20f, 20f, 5f), "시작 시각 포함");
            Assert.IsTrue(CommuteWindow.InWindow(23f, 20f, 5f));
            Assert.IsTrue(CommuteWindow.InWindow(0f, 20f, 5f), "자정 통과");
            Assert.IsTrue(CommuteWindow.InWindow(4.99f, 20f, 5f));
            Assert.IsFalse(CommuteWindow.InWindow(5f, 20f, 5f), "끝 시각은 배타");
            Assert.IsFalse(CommuteWindow.InWindow(10f, 20f, 5f));
            Assert.IsFalse(CommuteWindow.InWindow(19.99f, 20f, 5f));
        }

        [Test]
        public void InWindow_ZeroLength_IsAlwaysFalse()
        {
            // start == end 는 빈 구간으로 해석한다(통상 구간의 반개 규칙을 따름)
            Assert.IsFalse(CommuteWindow.InWindow(8f, 8f, 8f));
            Assert.IsFalse(CommuteWindow.InWindow(0f, 8f, 8f));
        }
    }
}
```

- [ ] **Step 2: 실패 확인**

`run_tests`(EditMode, `CityFlow.Sim.Tests`, test_names=`CityFlow.Sim.Tests.CommuteWindowTests`)
Expected: 컴파일 에러 — `CommuteWindow` 미정의

- [ ] **Step 3: 구현**

`Assets/01_Scripts/CityFlow/Sim/CommuteWindow.cs`:

```csharp
namespace CityFlow.Sim
{
    // 유형별 출퇴근 창. 시각은 게임시간 [0,24) 단위이며 하루 길이(DayLengthSeconds)와 무관하다.
    internal readonly struct CommuteWindow
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
```

- [ ] **Step 4: 통과 확인**

`run_tests`(test_names=`CityFlow.Sim.Tests.CommuteWindowTests`) → 3/3 PASS
그다음 전체: `run_tests`(EditMode, `CityFlow.Sim.Tests`) → **426/426 PASS** (423 + 신규 3)

- [ ] **Step 5: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Sim/CommuteWindow.cs \
        Assets/01_Scripts/CityFlow/Sim/CommuteWindow.cs.meta \
        Assets/Tests/EditMode/CommuteWindowTests.cs \
        Assets/Tests/EditMode/CommuteWindowTests.cs.meta
git commit -m "[Feat] CommuteWindow — 자정 순환 구간 판정 순수 함수

start > end 면 자정을 넘는 구간으로 해석한다. 아직 소비자 없음."
```

---

### Task 2: 출퇴근 판정을 차 개별 창 기준으로 (동작 무변경)

전역 필드(`_morningEnd`·`_eveningStart`·`_eveningEnd`) 대신 **차 자신의 창 값**으로 판정하게 바꾼다. 이 태스크가 끝나도 모든 차가 같은 전역 값을 복사받으므로 **동작은 변하지 않는다.** 자정 넘김 능력만 생긴다.

> **계획 정정 (2026-07-30, 실제 코드 확인 후).** 초안은 `WorkStartHour`/`WorkEndHour`(근무 구간)를 추가해
> 퇴근을 `!InWindow(hour, WorkStart, WorkEnd)`로 판정하려 했다. **그건 동작 무변경이 아니다.**
> 현재 퇴근 조건은 `hour >= car.DepartWorkHour`(집마다 흩뿌려진 **개인** 퇴근 시각)인데, 구간 이탈 판정으로
> 바꾸면 전 차가 퇴근창 끝에 **동시에** 퇴근한다 — 스태거가 사라지고 퇴근 러시가 한 틱에 몰린다.
> 게다가 Task 5는 `WorkEndHour = w.EndHour`(퇴근창 **시작**)를 넣어 Task 2의 의미(`eveningEnd`)와 어긋났다.
>
> **정정안:** 트리거는 개인 시각(`DepartHomeHour`/`DepartWorkHour`)을 그대로 쓰고 **비교만 순환식**으로 바꾼다.
> 새 필드는 퇴근창 2개(`EveningStartHour`/`EveningEndHour`)뿐이고, 근무 구간 필드는 만들지 않는다.
> `SnapCar`의 2026-07-17 기획 결정("퇴근창 안만 ParkedWork, 낮 로드는 전원 지각 출근")도 그대로 보존된다.

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Sim/CommuteScheduler.cs`
- Test: `Assets/Tests/EditMode/CommuteSchedulerTests.cs` (기존 파일에 추가)

**Interfaces:**
- Consumes: `CommuteWindow.InWindow` (Task 1)
- Produces: `CommuteCar.EveningStartHour` / `CommuteCar.EveningEndHour` (퇴근창 `[시작, 끝)`).
  Task 5가 유형별 값으로 채운다.

**판정 3곳의 새 규칙** (모두 `InWindow` 하나로):

| 전이 | 현재 | 바뀐 뒤 | 주간조에서 동일한가 |
|---|---|---|---|
| ParkedHome → Outbound | `hour >= DepartHomeHour && hour < _eveningEnd` | `InWindow(hour, DepartHomeHour, EveningEndHour)` | 동일 |
| ParkedWork → Inbound | `hour >= DepartWorkHour` | `InWindow(hour, DepartWorkHour, DepartHomeHour)` | 동일 (예외: 근무 중 새벽 로드 같은 창 밖 상태는 이제 귀가) |
| `SnapCar` ParkedWork | `hour >= _eveningStart && hour < _eveningEnd` | `InWindow(hour, EveningStartHour, EveningEndHour)` | 동일 |

- [ ] **Step 1: 실패하는 테스트 작성**

기존 `Assets/Tests/EditMode/CommuteSchedulerTests.cs`의 `Build(homes, officeSlots)` 헬퍼를 재사용하고,
차의 창 값만 야간조로 덮어쓴다. 새 헬퍼를 만들지 않는다.

```csharp
        // 자정을 넘는 근무(20시 출근 / 5시 퇴근). 전역 창이 아니라 차 개별 값으로 판정하므로 성립한다.
        [Test]
        public void NightShift_StaysAtWorkPastMidnight_AndLeavesAtDawn()
        {
            var s = Build(homes: 1, officeSlots: 4);
            var car = s.Cars[0];
            car.DepartHomeHour = 20f;     // 출근창 [20, 24)
            car.DepartWorkHour = 5f;      // 퇴근창 [5, 9)
            car.EveningStartHour = 5f;
            car.EveningEndHour = 9f;
            car.State = CarState.ParkedHome;

            s.UpdateDepartures(19f);
            Assert.AreEqual(CarState.ParkedHome, car.State, "19시엔 아직 집");
            s.UpdateDepartures(20f);
            Assert.AreEqual(CarState.Outbound, car.State, "20시에 출근");
            s.NotifyArrived(car);
            Assert.AreEqual(CarState.ParkedWork, car.State);

            s.UpdateDepartures(23f);
            Assert.AreEqual(CarState.ParkedWork, car.State, "23시엔 근무 중");
            s.UpdateDepartures(2f);
            Assert.AreEqual(CarState.ParkedWork, car.State, "새벽 2시에도 근무 중 — 자정을 넘겼다");

            s.UpdateDepartures(5f);
            Assert.AreEqual(CarState.Inbound, car.State, "5시에 퇴근");
        }

        // 퇴근창 자체가 자정을 넘는 경우(23시~2시)의 스냅. 2026-07-17 정책은 유지 —
        // 퇴근창 안만 ParkedWork이고 그 밖(근무 중인 낮 포함)은 전부 ParkedHome이다.
        [Test]
        public void SnapCar_EveningWindowWrapsMidnight()
        {
            var s = Build(homes: 1, officeSlots: 4);
            var car = s.Cars[0];
            car.DepartHomeHour = 14f;
            car.DepartWorkHour = 23.5f;
            car.EveningStartHour = 23f;   // 퇴근창 [23, 2) — 자정 넘김
            car.EveningEndHour = 2f;

            s.SnapCar(car, 23.5f);
            Assert.AreEqual(CarState.ParkedWork, car.State, "퇴근창 안(자정 전)");
            s.SnapCar(car, 1f);
            Assert.AreEqual(CarState.ParkedWork, car.State, "퇴근창 안(자정 후)");
            s.SnapCar(car, 2f);
            Assert.AreEqual(CarState.ParkedHome, car.State, "끝 시각은 배타");
            s.SnapCar(car, 18f);
            Assert.AreEqual(CarState.ParkedHome, car.State, "근무 중인 낮도 ParkedHome — 첫 움직임은 출근");
        }
```

- [ ] **Step 2: 실패 확인**

`run_tests`(test_names=`CityFlow.Sim.Tests.CommuteSchedulerTests`)
Expected: 컴파일 에러 — `CommuteCar`에 `EveningStartHour`/`EveningEndHour` 없음

- [ ] **Step 3: `CommuteCar`에 퇴근창 추가**

`CommuteScheduler.cs`의 `CommuteCar`(현재 `:22`)에 필드 2개를 추가한다.

```csharp
        public float DepartHomeHour, DepartWorkHour;
        // 이 차의 퇴근창 [EveningStartHour, EveningEndHour). Start > End 면 자정을 넘는다.
        // 판정 기준을 전역 창이 아니라 차 개별 값으로 두는 이유 = 유형별 근무시간(야간조) 대비.
        public float EveningStartHour, EveningEndHour;
```

- [ ] **Step 4: 차 생성 시 퇴근창 채우기**

`Rebuild` 안 `new CommuteCar { ... }`(현재 `:226-234`)에 두 줄을 추가한다. **이 태스크에서는 전역 창을
그대로 복사한다** — 그래서 동작이 안 바뀐다.

```csharp
                    DepartHomeHour = StaggerHour(sources[i], morningStart, morningEnd),
                    DepartWorkHour = StaggerHour(sources[i], eveningStart, eveningEnd),
                    EveningStartHour = eveningStart,
                    EveningEndHour = eveningEnd,
```

- [ ] **Step 5: 판정을 차 개별 값으로 교체**

`UpdateDepartures`(현재 `:290-313`)의 세 조건을 바꾼다. `AwaitingNextWave` 해제 조건도 순환식으로 바꿔야
한다 — 야간조는 "출발 시각 이전"이 자정을 걸쳐 있어서 `hour < DepartHomeHour`가 근무 시간대를 포함해버린다.

```csharp
                if (car.State == CarState.ParkedHome && car.AwaitingNextWave)
                {
                    // 창 밖(=다음 파도 이전)을 한 번 관측하면 해제. 자정을 넘는 창도 성립한다.
                    if (!CommuteWindow.InWindow(hour, car.DepartHomeHour, car.EveningEndHour))
                        car.AwaitingNextWave = false;
                    else
                        continue;
                }
                if (car.State == CarState.ParkedHome
                    && CommuteWindow.InWindow(hour, car.DepartHomeHour, car.EveningEndHour))
                { car.State = CarState.Outbound; car.Distance = 0f; }
                else if (car.State == CarState.ParkedWork
                    && (CommuteWindow.InWindow(hour, car.DepartWorkHour, car.DepartHomeHour)
                        || car.RetireReason == RetireReason.WorkLost))
                { car.State = CarState.Inbound; car.Distance = 0f; }
```

> 퇴근 구간이 **`[개인 퇴근 시각, 다음 개인 출근 시각)`** 이다. 개인 시각을 트리거로 유지하므로
> 스태거가 살아 있고, 자정을 넘는 근무에서도 단순 `>=` 비교가 필요 없다.

`SnapCar`(현재 `:355-367`)의 창 판정을 바꾼다.

```csharp
                bool inEveningWindow = CommuteWindow.InWindow(
                    hour, car.EveningStartHour, car.EveningEndHour);
                car.State = inEveningWindow ? CarState.ParkedWork : CarState.ParkedHome;
```

`_morningEnd`·`_eveningStart`·`_eveningEnd` 필드와 `Rebuild`의 대입(현재 `:132`)을 **이 태스크에서 제거한다**
(초안은 Task 5로 미뤘지만 남길 이유가 없다). `Rebuild`의 시각 인자 4개는 스태거·창 복사에 아직 쓰이므로
Task 5까지 남는다.

- [ ] **Step 6: 통과 확인**

`refresh_unity` → `read_console`(위 화이트리스트 2건 외 `error CS` 없음) → `run_tests`(EditMode, `CityFlow.Sim.Tests`)
Expected: **428/428 PASS** (426 + 신규 2)

**기존 통근 테스트가 깨지면 수치만 갱신하고 실패를 덮는 완화는 하지 마라.** 주간조 값에서는 위 표대로
동작이 같아야 하므로, 깨졌다면 정정안 자체를 다시 봐야 한다. 멈추고 보고한다.

- [ ] **Step 7: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Sim/CommuteScheduler.cs \
        Assets/Tests/EditMode/CommuteSchedulerTests.cs
git commit -m "[Feat] 출퇴근 판정을 차 개별 퇴근창 기준으로 — 자정 넘김 지원

전역 _eveningStart/_eveningEnd 대신 CommuteCar 의 퇴근창으로 판정한다.
트리거는 개인 출퇴근 시각을 그대로 쓰므로 스태거가 유지되고,
이 커밋 시점에는 전 차가 같은 전역 값을 복사받으므로 동작은 변하지 않는다."
```

> 여기까지가 **PR 1**이다. 동작 무변경이므로 단독 머지가 안전하다.

---

### Task 3: Sim 쪽 유형 표 — `CompanyTypeInfo` + `SimEngine.SetCompanyTypes`

> **계획 정정 (2026-07-30, 어셈블리 그래프 확인 후).** 초안은 `SimEngine`이 `CompanyTypeCatalogSO`를 직접
> 들고 `SetCompanyTypeCatalog(catalog)`로 주입받게 했다. **컴파일이 불가능하다.**
> `CityFlow.Sim.asmdef`의 `references`는 `["CityFlow.Contracts"]` 하나뿐이다. SO를 둘 후보 둘 다 막힌다 —
> `Configs/Buildings/`(`BuildingCatalogSO`가 사는 곳)는 **asmdef 밖 = `Assembly-CSharp`**이고 asmdef
> 어셈블리는 `Assembly-CSharp`를 참조할 수 없다. `Contents/Logic/` = `CityFlow.Content`는 Sim이 참조하지
> 않으며 **진우 소유 영역**(`Contents/` 전체)이다.
> 선례도 같은 방향이다 — `SimEngine`은 SO를 하나도 받지 않고 `SimConfig`(평범한 struct)만 받으며,
> SO를 쓰는 기능(`SchoolBusService`·`SpecialBuildingService`·`EmergencyIncidentSystem`)은 전부 Sim **밖**에 있다.
>
> **정정안:** Sim은 평범한 구조체 표만 받는다(`CompanyTypeInfo`). SO 정의·3종 에셋·SO→구조체 변환 배선은
> **Task 7로 이동**한다(배선 계층 = `Bootstrap/CityBootstrap.cs`, `Assembly-CSharp`).
> 부수 효과로 Task 3~5가 전부 `CityFlow.Sim.Tests`에서 검증 가능해진다 — 초안 배치에서는 SO가
> `Assembly-CSharp`에 있어 테스트 어셈블리가 볼 수 없었다(테스트 자체가 불가능했다).

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Sim/CommuteWindow.cs` (`CompanyTypeInfo` 추가)
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimEngine.cs` (유형 표 — **새 구역만 추가**, 남의 메서드는 손대지 않는다)
- Test: `Assets/Tests/EditMode/CompanyTypeTests.cs` (신규)

**Interfaces:**
- Produces:
  - `CompanyTypeInfo { CommuteWindow Window; int Capacity; }` — 정원 `<= 0`이면 유형 정원 미지정(기존 정원 규칙)
  - `SimEngine.SetCompanyTypes(IReadOnlyList<CompanyTypeInfo> types)` — 배선 계층이 주입, 재주입은 표 교체
  - `internal bool SimEngine.TryGetCompanyType(string id, out CompanyTypeInfo info)`
  - `internal CommuteWindow SimEngine.FallbackCommuteWindow()` — 유형 없는 목적지(School 등)·미배선 상황
  - `internal int SimEngine.CompanyTypeCountForTest`
  - Task 4·5가 쓴다.

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/EditMode/CompanyTypeTests.cs`:

```csharp
using NUnit.Framework;

namespace CityFlow.Sim.Tests
{
    public class CompanyTypeTests
    {
        static CompanyTypeInfo NewType(string id, float start, float end, int capacity = 6) =>
            new CompanyTypeInfo(new CommuteWindow(id, start, 4f, end, 4f), capacity);

        [Test]
        public void CompanyTypes_LookUpById_AndRejectUnknown()
        {
            var engine = new SimEngine(SimConfig.Default(), new SimEventHub());
            engine.SetCompanyTypes(new[] { NewType("office", 6f, 17f), NewType("factory", 20f, 5f) });

            Assert.IsTrue(engine.TryGetCompanyType("office", out CompanyTypeInfo office));
            Assert.AreEqual(6f, office.Window.StartHour);
            Assert.IsTrue(engine.TryGetCompanyType("factory", out CompanyTypeInfo factory));
            Assert.AreEqual(20f, factory.Window.StartHour, "공장은 야간 출근");
            Assert.AreEqual(5f, factory.Window.EndHour, "퇴근이 출근보다 이르다 = 자정을 넘는다");

            Assert.IsFalse(engine.TryGetCompanyType("warehouse", out _), "없는 id는 false");
            Assert.IsFalse(engine.TryGetCompanyType(null, out _), "null도 false");
            Assert.IsFalse(engine.TryGetCompanyType("", out _), "빈 문자열도 false");
        }

        [Test]
        public void FallbackWindow_ComesFromSimConfig()
        {
            SimConfig cfg = SimConfig.Default();
            var engine = new SimEngine(cfg, new SimEventHub());

            CommuteWindow w = engine.FallbackCommuteWindow();
            Assert.AreEqual(string.Empty, w.CompanyTypeId, "폴백은 무명 유형");
            Assert.AreEqual(cfg.MorningStartHour, w.StartHour);
            Assert.AreEqual(cfg.MorningEndHour - cfg.MorningStartHour, w.StartWindow);
            Assert.AreEqual(cfg.EveningStartHour, w.EndHour);
            Assert.AreEqual(cfg.EveningEndHour - cfg.EveningStartHour, w.EndWindow);
        }

        [Test]
        public void SetCompanyTypes_ReplacesTable_AndSkipsNamelessEntries()
        {
            var engine = new SimEngine(SimConfig.Default(), new SimEventHub());
            engine.SetCompanyTypes(new[] { NewType("office", 6f, 17f), NewType("  ", 6f, 17f) });
            Assert.AreEqual(1, engine.CompanyTypeCountForTest, "무명 유형은 표에 들어가지 않는다");

            engine.SetCompanyTypes(new[] { NewType("factory", 20f, 5f) });
            Assert.IsFalse(engine.TryGetCompanyType("office", out _), "재주입은 표를 교체한다");
            Assert.IsTrue(engine.TryGetCompanyType("factory", out _));

            engine.SetCompanyTypes(null);
            Assert.AreEqual(0, engine.CompanyTypeCountForTest, "null 은 표를 비운다");
        }
    }
}
```

- [ ] **Step 2: 실패 확인**

Expected: 컴파일 에러 — `CompanyTypeInfo` / `SetCompanyTypes` 미정의

- [ ] **Step 3: `CompanyTypeInfo` 추가**

`CommuteWindow.cs`에 함께 둔다(같은 개념 쌍 — `CommuteScheduler.cs`가 `CarState`·`CommuteCar`를 함께 두는 선례).

```csharp
    // 회사 유형 1종의 Sim 쪽 표현. SO(CompanyTypeSO)는 Assembly-CSharp 소속이고 CityFlow.Sim은
    // 그 어셈블리를 참조할 수 없다 — 배선 계층이 이 구조체로 옮겨 넣는다(Task 7).
    public readonly struct CompanyTypeInfo
    {
        public readonly CommuteWindow Window;
        public readonly int Capacity;   // 유형별 정원. <= 0 이면 유형 정원 미지정(기존 규칙을 쓴다)

        public CompanyTypeInfo(CommuteWindow window, int capacity)
        {
            Window = window;
            Capacity = capacity;
        }
    }
```

- [ ] **Step 4: `SimEngine`에 유형 표 구역 추가**

기존 메서드를 고치지 않는다. 파일 끝의 자기 구역에 붙인다(소유권 규칙 — `SimEngine.cs`는 공유 파일이다).

```csharp
        // ── 회사 유형 표 (환) ─────────────────────────────────────────────
        // SO 카탈로그는 Assembly-CSharp 에 있고 CityFlow.Sim 은 그 어셈블리를 참조할 수 없다.
        // 배선 계층(CityBootstrap)이 SO → CompanyTypeInfo 로 옮겨 여기에 주입한다.
        readonly Dictionary<string, CompanyTypeInfo> _companyTypes = new(StringComparer.Ordinal);

        public void SetCompanyTypes(IReadOnlyList<CompanyTypeInfo> types)
        {
            _companyTypes.Clear();
            if (types == null) return;
            for (int i = 0; i < types.Count; i++)
            {
                string id = types[i].Window.CompanyTypeId;
                if (string.IsNullOrWhiteSpace(id)) continue;   // 무명 유형은 조회할 수 없다
                _companyTypes[id.Trim()] = types[i];
            }
        }

        internal bool TryGetCompanyType(string companyTypeId, out CompanyTypeInfo info)
        {
            info = default;
            if (string.IsNullOrWhiteSpace(companyTypeId)) return false;
            return _companyTypes.TryGetValue(companyTypeId.Trim(), out info);
        }

        // 유형이 없는 목적지(School 등)와 표 미주입 상황의 폴백 — 종전 전역 창 그대로.
        internal CommuteWindow FallbackCommuteWindow() => new CommuteWindow(
            string.Empty,
            _config.MorningStartHour, _config.MorningEndHour - _config.MorningStartHour,
            _config.EveningStartHour, _config.EveningEndHour - _config.EveningStartHour);

        internal int CompanyTypeCountForTest => _companyTypes.Count;
```

- [ ] **Step 5: 통과 확인**

`refresh_unity` → `read_console` → `run_tests`(EditMode, `CityFlow.Sim.Tests`)
Expected: **431/431 PASS** (428 + 신규 3)

- [ ] **Step 6: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Sim/CommuteWindow.cs \
        Assets/01_Scripts/CityFlow/Sim/SimEngine.cs \
        Assets/Tests/EditMode/CompanyTypeTests.cs \
        Assets/Tests/EditMode/CompanyTypeTests.cs.meta
git commit -m "[Feat] Sim 쪽 회사 유형 표 — CompanyTypeInfo + SetCompanyTypes

SO 대신 평범한 구조체를 주입받는다(CityFlow.Sim 은 Assembly-CSharp 를 참조할 수 없다)."
```
### Task 4: 유형 보관 + 배치 시 유형 전달 + 유형별 정원

> **결정 갱신 (환, 2026-07-30).** 설계 §0.1 ⑥은 "유형 미지정 Office 배치 = 거부"였다. 실제 코드에서
> 그 대가를 측정해 보니 기존 EditMode 테스트 **57곳(18파일)**이 깨지고, 진우 UI 경로
> (`PlacementActionDispatcher` → `Placement.Place`)가 유형을 넘기지 않으므로 **게임에서 사무실 배치가 막힌다.**
> 환의 판단: **"UI 상점 쪽에서 회사를 3종으로 나누면 미지정 경로 자체가 없으니 거부할 것도 없다."**
> → `Place`는 유형 미지정을 **거부하지 않고 폴백 창으로 배치**한다. 대신 **등록되지 않은 id는 경고**를 남긴다
> (오타가 조용히 묻히는 것만 막는다 — ⑥의 의도는 여기서 지켜진다).
> **인계 사항:** 건설 패널이 사무실/공장/물류창고 3종으로 갈려야 기능이 실제로 켜진다(진우 영역).
>
> **정원 차등은 이번 PR에 포함한다(환 결정).** 단순히 `capacityOverride`로 넘기면 안 된다 —
> `RegisterCompany`가 `SimConfig.OfficeCapacity`(6)로 clamp하고 `ApplyConfig`가 다시 `Min`을 걸어
> **공장 10이 조용히 6으로 깎인다.** 상한 규칙 자체를 유형 정원 기준으로 고친다.

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Sim/DemandMap.cs` (`CompanyCapacityState`, `RegisterCompany`, `RegisterRestoredCompany`, `ApplyConfig`, `SetCompanyCapacity`)
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimEngine.cs` (`Place` 오버로드 · 유형 표 구역)
- Test: `Assets/Tests/EditMode/CompanyTypeTests.cs` (추가)

**Interfaces:**
- Consumes: `SimEngine.TryGetCompanyType` (Task 3)
- Produces:
  - `SimEngine.Place(tile, type, direction, string companyTypeId)` — **3인자 계약은 그대로 두고 오버로드로 추가**
  - `DemandMap.TryGetCompanyTypeId(tile, out string id)` · `internal SimEngine.TryGetCompanyTypeIdForTest`
  - `RegisterCompany(..., string companyTypeId = null, int companyTypeCapacity = 0)`
  - Task 5·6이 쓴다.

- [ ] **Step 1: 실패하는 테스트 작성** — `CompanyTypeTests.cs`에 4건 추가

1. `PlaceOffice_StoresCompanyTypeId_AndTolerantOfMissingType` — 유형을 넘기면 저장, 미지정도 배치 성공
2. `PlaceOffice_UnknownTypeId_WarnsAndFallsBack` — `LogAssert.Expect(Warning)` + 유형 미저장
3. `CompanyCapacity_FollowsTypeDefinition` — 공장 10 / 물류창고 4 / 유형 없음 = `SimConfig.OfficeCapacity`
4. `ApplyConfig_KeepsTypeCapacity` — 재적용 후에도 공장 10 (조용한 축소 방지)

- [ ] **Step 2: 실패 확인** — `CS1501: No overload for method 'Place' takes 4 arguments` · `TryGetCompanyTypeIdForTest` 미정의

- [ ] **Step 3: `DemandMap`에 유형·유형정원 보관**

`CompanyCapacityState`에 `CompanyTypeId`·`CompanyTypeCapacity`를 더하고, 정원 상한을 한 곳으로 모은다.

```csharp
        // 회사 하나의 정원 상한. 유형 정원이 있으면 그것이 상한, 없으면 SimConfig 값.
        // SetCompanyCapacity·ApplyConfig 도 이 상한을 쓴다 — 유형 정원이 조용히 깎이지 않게.
        int CapacityCeilingFor(CompanyCapacityState company) =>
            company.CompanyTypeCapacity > 0
                ? company.CompanyTypeCapacity
                : CapacityForType(company.Type);
```

`RegisterCompany`·`RegisterRestoredCompany`에 `companyTypeId`·`companyTypeCapacity`(둘 다 기본값)를 더하고,
`ApplyConfig`(`:77`)와 `SetCompanyCapacity`(`:160`)의 `CapacityForType(...)`을 `CapacityCeilingFor(company)`로 바꾼다.
`TryGetCompanyTypeId(tile, out id)`를 추가한다.

- [ ] **Step 4: `SimEngine.Place` 오버로드**

**계약(`IPlacementService.Place`)에 인자를 더하지 않는다.** 옵셔널 인자를 더하면 시그니처가 달라져
`CS0535`(인터페이스 미구현)가 난다 — 실제로 겪었다. 3인자는 그대로 두고 4인자 오버로드로 위임한다.

```csharp
        public bool Place(Vector2Int tile, TileType type,
                         PlacementDirection direction = PlacementDirection.North)
            => Place(tile, type, direction, null);

        public bool Place(Vector2Int tile, TileType type,
                         PlacementDirection direction, string companyTypeId)
        { /* 기존 본문 + RegisterCompanyOfType(tile, type, companyTypeId) */ }
```

유형 표 구역에 등록 헬퍼를 둔다 — 미지정은 종전대로, 미등록 id는 경고 후 폴백.

```csharp
        void RegisterCompanyOfType(Vector2Int tile, TileType type, string companyTypeId)
        {
            if (string.IsNullOrWhiteSpace(companyTypeId)) { _demand.RegisterCompany(tile, type, _simTime); return; }
            if (!TryGetCompanyType(companyTypeId, out CompanyTypeInfo info))
            {
                Debug.LogWarning($"[SimEngine] 등록되지 않은 회사 유형 id '{companyTypeId}' — 폴백 창으로 배치한다.");
                _demand.RegisterCompany(tile, type, _simTime);
                return;
            }
            _demand.RegisterCompany(tile, type, _simTime,
                capacityOverride: null,
                companyTypeId: info.Window.CompanyTypeId,
                companyTypeCapacity: info.Capacity);
        }
```

- [ ] **Step 5: 통과 확인**

`refresh_unity` → `read_console` → `run_tests`(EditMode, `CityFlow.Sim.Tests`)
Expected: **435/435 PASS** (431 + 신규 4). **기존 테스트는 한 줄도 고치지 않는다** — 거부를 없앴으므로 회귀가 없다.

- [ ] **Step 6: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Sim/DemandMap.cs \
        Assets/01_Scripts/CityFlow/Sim/SimEngine.cs \
        Assets/Tests/EditMode/CompanyTypeTests.cs
git commit -m "[Feat] 회사 유형 보관 + 유형별 정원

Place 오버로드로 companyTypeId 를 받아 DemandMap 에 싣는다. 미지정은 폴백,
미등록 id 는 경고. 정원 상한을 CapacityCeilingFor 로 모아 유형 정원이 깎이지 않게 한다."
```

---

### Task 5: `Rebuild` 콜백 전환 + 유형별 창 적용

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Sim/CommuteScheduler.cs` (`Rebuild` `:122`, 차 생성 `:226`)
- Modify: `Assets/01_Scripts/CityFlow/Sim/CarSim.cs` (`:444` 호출부)
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimEngine.cs` (창 조회 제공)
- Test: `Assets/Tests/EditMode/CompanyTypeTests.cs` · `CommuteSchedulerTests.cs`

**Interfaces:**
- Consumes: `CommuteWindow` (Task 1), `CommuteCar.EveningStartHour/EveningEndHour` (Task 2), 카탈로그(Task 3), `DemandMap.TryGetCompanyTypeId` (Task 4)
- Produces: `Rebuild(..., Func<Vector2Int, CommuteWindow> windowFor, ...)` — 시각 인자 4개 제거

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
        [Test]
        public void DifferentCompanyTypes_ProduceDifferentDepartureHours()
        {
            SimConfig cfg = SimConfig.Default();
            cfg.GridWidth = 12; cfg.GridHeight = 5;
            cfg.CarsPerHouse = 1;
            var engine = new SimEngine(cfg, new SimEventHub());

            engine.SetCompanyTypes(new[] {
                NewType("office",  6f, 17f),   // 오전 출근
                NewType("factory", 20f, 5f),   // 야간 출근 — 자정 넘김
            });

            for (int x = 0; x <= 11; x++) Assert.IsTrue(engine.Place(V(x, 2), TileType.Road));
            Assert.IsTrue(engine.Place(V(0, 0), TileType.House));
            Assert.IsTrue(engine.Place(V(6, 0), TileType.House));
            Assert.IsTrue(engine.Place(V(3, 4), TileType.Office, PlacementDirection.North, "office"));
            Assert.IsTrue(engine.Place(V(9, 4), TileType.Office, PlacementDirection.North, "factory"));

            engine.SetGameHour(7f);
            engine.Tick(0.25f);

            // 두 차의 출근 시각이 서로 다른 창에서 나온다
            float a = engine.GetCarSnapshot(0).DepartHomeHourForTest;
            float b = engine.GetCarSnapshot(1).DepartHomeHourForTest;
            bool oneMorning = (a >= 6f && a < 10f) || (b >= 6f && b < 10f);
            bool oneNight   = (a >= 20f && a < 24f) || (b >= 20f && b < 24f);
            Assert.IsTrue(oneMorning, "하나는 오전 창에서 나와야 한다");
            Assert.IsTrue(oneNight,   "하나는 야간 창에서 나와야 한다");
        }
```

> `DepartHomeHourForTest`는 스냅샷에 없다. **관찰 seam이 필요하다.** `CarSnapshot`에 필드를 더하는 대신 `SimEngine`에 `internal float DepartHomeHourForTest(int carIndex)`를 추가해 `CarSim`을 거쳐 조회하는 편이 스냅샷 계약을 안 건드린다. 실제 배선은 `CarSim`이 `_scheduler`의 차 목록에 접근하는 방식을 읽고 맞춘다.

- [ ] **Step 2: 실패 확인**

Expected: 컴파일 에러 — `DepartHomeHourForTest` 미정의

- [ ] **Step 3: `Rebuild` 시그니처 교체**

```csharp
        public void Rebuild(IReadOnlyList<Vector2Int> sources, IReadOnlyList<Vector2Int> sinks,
            Func<Vector2Int, int> workCapacityFor,
            Func<Vector2Int, CommuteWindow> windowFor,
            int homeSlots, int maxCars,
            bool deferNewAssignments = false)
        {
            if (workCapacityFor == null) throw new ArgumentNullException(nameof(workCapacityFor));
            if (windowFor == null) throw new ArgumentNullException(nameof(windowFor));
            // _morningEnd / _eveningStart / _eveningEnd 저장은 제거한다 — 판정이 차 개별 값으로 옮겨졌다
```

차 생성부(`:226`)를 바꾼다.

```csharp
                CommuteWindow w = windowFor(sinks[i]);
                var fresh = new CommuteCar
                {
                    Home = sources[i], Work = sinks[i], RouteIndex = i,
                    WorkSlot = workSlot, HomeSlot = homeSlot,
                    DepartHomeHour = StaggerHour(sources[i], w.StartHour, w.StartHour + w.StartWindow),
                    DepartWorkHour = StaggerHour(sources[i], w.EndHour,   w.EndHour   + w.EndWindow),
                    EveningStartHour = w.EndHour,
                    EveningEndHour   = w.EndHour + w.EndWindow,
                    State = CarState.ParkedHome,
                    AwaitingNextWave = deferNewAssignments,
                };
```

> `CommuteCar`에 `CompanyTypeId` 필드도 더한다(설계 결정 ①). `w.CompanyTypeId`를 그대로 싣는다.
> 콜백이 출처이고 필드는 캐시다 — 역할이 겹치지 않는다.

`_morningEnd`·`_eveningStart`·`_eveningEnd`는 **Task 2에서 이미 제거됐다.** 여기서는 `Rebuild`의 시각 인자 4개(`morningStart`~`eveningEnd`)를 없애고 콜백으로 대체한다. 남은 참조가 있으면 그 지점을 보고하라.

- [ ] **Step 4: `SimEngine`이 창을 제공**

```csharp
        // 목적지 타일 → 그 회사 유형의 출퇴근 창. 카탈로그·유형이 없으면 SimConfig 폴백.
        internal CommuteWindow CommuteWindowAt(Vector2Int sink)
        {
            if (_demand.TryGetCompanyTypeId(sink, out string id)
                && TryGetCompanyType(id, out CompanyTypeInfo info))
            {
                return info.Window;
            }

            return FallbackCommuteWindow();   // School 등 유형 없는 목적지·표 미주입
        }
```

- [ ] **Step 5: `CarSim` 호출부 갱신**

`CarSim.cs:444`의 `_scheduler.Rebuild(...)`에서 시각 인자 4개를 빼고 콜백을 넘긴다. `CarSim`이 `SimEngine`을 참조하지 않으므로 **`CarSim.Rebuild`에 콜백을 인자로 받아 전달**한다. `SimEngine`이 `_carSim.Rebuild(..., CommuteWindowAt, ...)` 형태로 넘기는 배선이 필요하다. 실제 시그니처는 `CarSim.Rebuild`를 읽고 맞춘다.

- [ ] **Step 6: 통과 확인**

`refresh_unity` → `read_console` → `run_tests`(EditMode, `CityFlow.Sim.Tests`)
Expected: **436/436 PASS** (435 + 신규 1)

기존 `CommuteSchedulerTests`가 옛 시그니처로 `Rebuild`를 부르면 컴파일이 깨진다. **콜백을 넘기도록 갱신한다** — 폴백 창을 돌려주는 람다 한 줄이면 기존 동작이 그대로 재현된다.

```csharp
sink => new CommuteWindow(string.Empty, 6f, 4f, 17f, 4f)
```

- [ ] **Step 7: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Sim/CommuteScheduler.cs \
        Assets/01_Scripts/CityFlow/Sim/CarSim.cs \
        Assets/01_Scripts/CityFlow/Sim/SimEngine.cs \
        Assets/Tests/EditMode/
git commit -m "[Feat] Rebuild 시각 인자 4개를 창 콜백으로 대체 — 유형별 출퇴근 창 적용

창의 출처를 콜백 하나로 통일한다(이중 권한 회피). CommuteCar 는 유형 id 와
근무 구간을 캐시한다."
```

---

### Task 6: 공사 중 유형 보존

`ConstructionSite`는 PR #171(건물 건설시간)이 도입한 타입이다. **#171은 `0d313da`로 머지 완료**(후속 #178·#179까지 반영) — 초안의 선행 조건은 충족됐고 이 태스크는 조건부가 아니다.

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Sim/ConstructionSites.cs`
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimEngine.cs` (`Place` 공사 분기 · `AdvanceConstruction`)
- Modify: `Assets/01_Scripts/CityFlow/Contracts/Save/ConstructionSaveData.cs`
- Test: `Assets/Tests/EditMode/CompanyTypeTests.cs`

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
        [Test]
        public void CompanyType_SurvivesConstruction_AndSaveRoundTrip()
        {
            SimConfig cfg = SimConfig.Default();
            cfg.GridWidth = 8; cfg.GridHeight = 4;
            cfg.DayLengthSeconds = 24f;
            cfg.TickInterval = 0.25f;
            cfg.ConstructionHoursOffice = 2f;      // 2 게임시간 = 2 시뮬초 = 8틱
            var engine = new SimEngine(cfg, new SimEventHub());
            engine.SetCompanyTypes(new[] { NewType("office", 6f, 17f), NewType("factory", 20f, 5f) });

            Assert.IsTrue(engine.Place(V(4, 0), TileType.Office, PlacementDirection.North, "factory"));
            Assert.AreEqual(TileType.UnderConstruction, engine.GetTileType(V(4, 0)));

            // 공사 중 저장 → 로드
            var snap = engine.CreateSnapshot();
            var restored = new SimEngine(cfg, new SimEventHub());
            restored.SetCompanyTypes(new[] { NewType("factory", 20f, 5f) });
            restored.RestoreSnapshot(snap);

            for (int i = 0; i < 8; i++) restored.Tick(0.25f);

            Assert.AreEqual(TileType.Office, restored.GetTileType(V(4, 0)), "완성됨");
            Assert.IsTrue(restored.TryGetCompanyTypeIdForTest(V(4, 0), out string id));
            Assert.AreEqual("factory", id, "공사·세이브를 거쳐도 유형이 사무실로 되돌아가지 않는다");
        }
```

- [ ] **Step 2: 실패 확인** — 유형이 `office`이거나 null로 나온다

- [ ] **Step 3: `ConstructionSite`에 유형 적재**

`ConstructionSite` struct에 `public readonly string CompanyTypeId;`를 더하고 생성자에 인자를 추가한다. `ConstructionSites.Register`에도 파라미터를 더한다.

`SimEngine.Place`의 공사 분기에서 `companyTypeId`를 넘기고, `AdvanceConstruction`의 완성 처리에서 `_demand.RegisterCompany(site.Anchor, site.TargetType, _simTime, site.CompanyTypeId)`로 복원한다.

- [ ] **Step 4: 세이브에 유형 싣기**

`ConstructionSaveData`에 `public string CompanyTypeId;`를 더하고 `CreateSnapshot`/`RestoreSnapshot` 양쪽에 배선한다. **구세이브는 이 필드가 null이므로 폴백 경로가 살아 있어야 한다.**

- [ ] **Step 5: 통과 확인** — `run_tests` **437/437 PASS**

- [ ] **Step 6: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Sim/ConstructionSites.cs \
        Assets/01_Scripts/CityFlow/Sim/SimEngine.cs \
        Assets/01_Scripts/CityFlow/Contracts/Save/ConstructionSaveData.cs \
        Assets/Tests/EditMode/
git commit -m "[Fix] 공사 중 회사 유형 보존 — 완성 시 사무실로 되돌아가지 않게

ConstructionSite 와 세이브에 CompanyTypeId 를 싣는다. 구세이브는 null 폴백."
```

---

### Task 7: 3종 에셋 생성 + 값 기입

코드가 아니라 **데이터**다. 카탈로그가 비어 있으면 전부 폴백 창으로 돌아 3종이 갈리지 않는다.

> **Task 3에서 이동됨.** SO 정의는 오서링 계층이고 Sim이 볼 수 없으므로(Task 3 정정 참고) 여기서 만든다.

**Files:**
- Create: `Assets/01_Scripts/CityFlow/Configs/Buildings/CompanyTypeSO.cs` (+ `.cs.meta`)
- Create: `Assets/01_Scripts/CityFlow/Configs/Buildings/CompanyTypeCatalogSO.cs` (+ `.cs.meta`)
- Modify: `Assets/01_Scripts/CityFlow/Bootstrap/CityBootstrap.cs` (SO → `CompanyTypeInfo` 배선)
- Create: `Assets/05_ScriptableObjects/Companies/CompanyType_Office.asset`
- Create: `Assets/05_ScriptableObjects/Companies/CompanyType_Factory.asset`
- Create: `Assets/05_ScriptableObjects/Companies/CompanyType_Warehouse.asset`
- Create: `Assets/05_ScriptableObjects/Companies/CompanyTypeCatalog.asset`

- [ ] **Step 0: SO 정의 2개 작성**

`Configs/Buildings/`에 둔다 — `BuildingCatalogSO`가 사는 곳이고 `Assembly-CSharp`이라 배선 계층에서 보인다.
`Contents/`는 **진우 소유**이므로 쓰지 않는다. 필드는 `CompanyTypeSO`: `companyTypeId`·`displayName`·
`capacity`·`workStartHour`·`workStartWindow`·`workEndHour`·`workEndWindow`. 카탈로그는 `BuildingCatalogSO.cs`를
먼저 읽고 그 형태를 따른다(id 인덱스, 중복·빈 id 경고, `OnValidate` 재색인).

**이 두 클래스에는 EditMode 테스트를 붙이지 않는다** — `CityFlow.Sim.Tests`는 `Assembly-CSharp`를 참조할 수
없다(asmdef 제약). 검증 대상 로직은 Task 3~5의 Sim 쪽 표에 이미 다 있고, SO는 값 전달자일 뿐이다.

- [ ] **Step 1: 에셋 4개 생성**

Unity 메뉴 `Assets > Create > CityFlow > Content > Company Type` 으로 3개, `Company Type Catalog` 로 1개를 만든다. 폴더가 없으면 `Assets/05_ScriptableObjects/Companies/`를 만든다.

- [ ] **Step 2: 값 기입 — 겹치되 어긋나게**

시각은 게임시간 `[0,24)`. **완전히 분리하지 않는다** — 세 유형이 도로를 공유해야 조정 플레이가 산다.

| 에셋 | `companyTypeId` | `displayName` | `capacity` | `workStartHour` | `workStartWindow` | `workEndHour` | `workEndWindow` |
|---|---|---|---|---|---|---|---|
| `CompanyType_Office` | `office` | 사무실 | 6 | 6 | 4 | 15 | 4 |
| `CompanyType_Warehouse` | `warehouse` | 물류창고 | 4 | 4 | 4 | 13 | 4 |
| `CompanyType_Factory` | `factory` | 공장 | 10 | 20 | 4 | 5 | 4 |

의도한 겹침:

```
물류창고 출근 04~08 ┐
사무실   출근 06~10 ┘ 06~08 겹침 — 새벽조와 오전조가 같은 도로에서 만난다

공장     퇴근 05~09 ┐
물류창고 출근 04~08 ┘ 05~08 겹침 — 야간조 퇴근과 새벽조 출근이 교차한다

공장 근무 구간 [20, 5) → 자정을 넘는다. Task 1의 InWindow 순환 판정이 여기서 처음 실제로 쓰인다.
```

- [ ] **Step 3: 카탈로그에 3종 등록**

`CompanyTypeCatalog.asset`의 `types` 리스트에 위 3개를 끌어다 넣는다. 인스펙터에서 `OnValidate` 경고(중복 id·빈 id)가 없는지 확인한다.

- [ ] **Step 4: 부트스트랩 배선 확인**

`CityBootstrap`이 엔진 생성 직후 SO 카탈로그를 `CompanyTypeInfo` 목록으로 옮겨 `engine.SetCompanyTypes(...)`로 주입한다. **표를 주입하지 않으면 종전 동작 그대로**(전역 창 폴백 + 유형 미지정 Office 허용)이므로, 배선이 없는 씬은 영향을 받지 않는다.

> ⚠️ 이건 **통합 씬 직렬화**와 얽힌다. `CityBootstrap`에 `[SerializeField]`를 더하면 씬에서 값을 물려야 하고, 씬 커밋 금지 규칙에 걸린다. **씬을 건드리지 않으려면** `Resources.Load` 또는 카탈로그를 `SimConfigAsset` 옆에 두고 코드에서 경로로 읽는 방식을 쓴다. 어느 쪽이든 **씬 저장이 필요 없어야 한다.**

- [ ] **Step 5: 검증 — 에셋만 바뀌므로 테스트는 무변경**

`refresh_unity` → `read_console` → `run_tests`(EditMode, `CityFlow.Sim.Tests`)
Expected: **437/437 PASS** (테스트는 유형 표를 코드로 주입하므로 에셋 추가에 영향받지 않는다)

라이브 확인은 하루 12분 전환 이후다 — 지금은 창 4시간이 실시간 4초라 세 유형이 갈리는 게 눈에 안 보인다.

- [ ] **Step 6: 커밋**

```bash
git add Assets/05_ScriptableObjects/Companies/
# CityBootstrap 을 고쳤다면 함께. 씬 파일(.unity)은 절대 add 하지 마라.
git commit -m "[Feat] 회사 유형 3종 에셋 — 사무실/물류창고/공장

겹치되 어긋나는 창으로 기입. 공장은 [20,5) 자정 넘김.
값은 게임시간 단위라 하루 길이 전환과 무관하다."
```

---

## 완료 기준

- EditMode `CityFlow.Sim.Tests` **437/437 green** (기준선 423 + 신규 14)
- 컴파일 `error CS` 0
- 통합 씬 파일이 커밋에 **없음**
- 신규 `.cs`의 `.cs.meta` 전부 커밋됨
- **PR 1** = Task 1~2 (동작 무변경) · **PR 2** = Task 3~5 + Task 7 · **PR 3** = Task 6
- PR은 **15:00~16:00에만** 제출 (팀 규칙)

## 범위 밖 (하지 않을 것)

| 항목 | 사유 |
|---|---|
| 하루 12분 전환 | 별도 담당자. 시각 값이 게임시간 단위라 이 작업은 그것을 기다리지 않는다 |
| 건설 패널 카테고리 재편 | 별도 담당자. 다만 `Place`가 유형을 요구하므로 **인계 시 반드시 알린다** |
| 유형별 건물 비주얼 | 리스킨 작업과 병렬 |
| 교대근무(개인 로테이션) | 공장은 "야간에 출퇴근하는 근무지"까지 |
| 화물차·방문객 트래픽 | 특수차/멀티스톱 기능군 |
| 진짜 수요 곡선 | 지금은 균등분포 창. 창만 유형별로 나눠도 목표 달성 |
| 시간대 값 라이브 튜닝 | 값은 정하되 체감 확인은 하루 12분 전환 이후 |
