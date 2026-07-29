# 회사 3종 + 유형별 출퇴근 시간대 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 회사를 사무실·공장·물류창고 3종으로 나누고 종류마다 출퇴근 시간대와 정원을 다르게 해서, 같은 도로에 시간대별로 다른 교통 패턴이 나타나게 한다.

**Architecture:** `TileType`은 확장하지 않는다. `Office` 하나에 인스턴스 데이터(유형 id)를 붙이고, 유형별 출퇴근 창을 SO 카탈로그에 둔다. `CommuteScheduler`는 전역 창 4개 인자 대신 목적지별 창을 돌려주는 콜백을 받고, 출퇴근 판정을 전역 필드가 아니라 **차 개별 값** 기준으로 바꿔 자정을 넘는 근무를 지원한다.

**Tech Stack:** Unity 6000.5.2f1 · C# · NUnit EditMode (`CityFlow.Sim.Tests`)

**설계 문서:** `docs/superpowers/specs/2026-07-29-company-types-commute-windows-design.md`

## Global Constraints

- 기준 브랜치: **`develop 43c0d5f`에서 직접 분기.** 스택 금지 — Squash 머지라 브랜치를 쌓으면 diff가 중복된다.
- 회귀 기준선: **EditMode `CityFlow.Sim.Tests` 410/410 green** (`develop 43c0d5f` 실측, 2026-07-29). **부분 실패 허용 없음.**
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
| `Assets/01_Scripts/CityFlow/Configs/Buildings/CompanyTypeSO.cs` | 회사 유형 1종 정의 | **신규** |
| `Assets/01_Scripts/CityFlow/Configs/Buildings/CompanyTypeCatalogSO.cs` | 유형 카탈로그(id 조회) | **신규** |
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
Phase ④  공사 중 유형 보존       Task 6 (조건부)  → PR 3   ← PR #171 머지 후에만
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
그다음 전체: `run_tests`(EditMode, `CityFlow.Sim.Tests`) → **413/413 PASS** (410 + 신규 3)

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

### Task 2: 출퇴근 판정을 차 개별 값 기준으로 (동작 무변경)

전역 필드(`_eveningEnd` 등) 대신 **차 자신의 근무 구간**으로 판정하게 바꾼다. 이 태스크가 끝나도 모든 차가 여전히 같은 전역 창을 받으므로 **동작은 변하지 않는다.** 자정 넘김 능력만 생긴다.

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Sim/CommuteScheduler.cs`
- Test: `Assets/Tests/EditMode/CommuteSchedulerTests.cs` (기존 파일에 추가)

**Interfaces:**
- Consumes: `CommuteWindow.InWindow` (Task 1)
- Produces: `CommuteCar.WorkStartHour` / `CommuteCar.WorkEndHour` (근무 구간). Task 5가 유형별 값으로 채운다.

- [ ] **Step 1: 실패하는 테스트 작성**

기존 `Assets/Tests/EditMode/CommuteSchedulerTests.cs`를 먼저 읽고 **그 파일의 스케줄러 생성·호출 패턴을 그대로 따라** 아래 테스트를 추가한다. 새 헬퍼를 만들지 말고 기존 것을 재사용한다.

```csharp
        [Test]
        public void NightShift_DepartsAtNight_AndReturnsBeforeDawn()
        {
            // 출근 20시 / 퇴근 5시 — 자정을 넘는 근무
            var car = new CommuteCar
            {
                Home = V(0, 0), Work = V(4, 0),
                DepartHomeHour = 20f, DepartWorkHour = 5f,
                WorkStartHour = 20f, WorkEndHour = 5f,
                State = CarState.ParkedHome,
            };
            var scheduler = NewSchedulerWith(car);   // 기존 파일의 헬퍼를 쓰거나 같은 방식으로 구성

            scheduler.UpdateDepartures(19f);
            Assert.AreEqual(CarState.ParkedHome, car.State, "19시엔 아직 집");

            scheduler.UpdateDepartures(20f);
            Assert.AreEqual(CarState.Outbound, car.State, "20시에 출근 시작");

            scheduler.NotifyArrived(car);
            Assert.AreEqual(CarState.ParkedWork, car.State);

            scheduler.UpdateDepartures(23f);
            Assert.AreEqual(CarState.ParkedWork, car.State, "23시엔 아직 근무 중");
            scheduler.UpdateDepartures(2f);
            Assert.AreEqual(CarState.ParkedWork, car.State, "새벽 2시에도 근무 중 — 자정을 넘겼다");

            scheduler.UpdateDepartures(5f);
            Assert.AreEqual(CarState.Inbound, car.State, "5시에 퇴근");
        }

        [Test]
        public void SnapCar_NightShift_ParksAtWorkAcrossMidnight()
        {
            var car = new CommuteCar
            {
                Home = V(0, 0), Work = V(4, 0),
                DepartHomeHour = 20f, DepartWorkHour = 5f,
                WorkStartHour = 20f, WorkEndHour = 5f,
            };
            var scheduler = NewSchedulerWith(car);

            scheduler.SnapCar(car, 23f);
            Assert.AreEqual(CarState.ParkedWork, car.State, "23시 로드 → 근무 중");

            scheduler.SnapCar(car, 3f);
            Assert.AreEqual(CarState.ParkedWork, car.State, "새벽 3시 로드 → 근무 중");

            scheduler.SnapCar(car, 12f);
            Assert.AreEqual(CarState.ParkedHome, car.State, "정오 로드 → 집");
        }
```

> `NewSchedulerWith`는 예시 이름이다. 기존 파일에 스케줄러를 만드는 방식이 이미 있으면 **그것을 쓴다.** 없으면 기존 테스트가 하는 방식(대개 `Rebuild` 호출)으로 구성하고, 위 두 테스트가 요구하는 것은 "특정 차 하나를 원하는 시각 값으로 두고 `UpdateDepartures`/`SnapCar`를 부르는 것"뿐이다.

- [ ] **Step 2: 실패 확인**

Expected: 컴파일 에러 — `CommuteCar`에 `WorkStartHour`/`WorkEndHour` 없음

- [ ] **Step 3: `CommuteCar`에 근무 구간 추가**

`CommuteScheduler.cs`의 `CommuteCar` 클래스에 필드 2개를 추가한다.

```csharp
        public float DepartHomeHour, DepartWorkHour;
        // 근무 구간 [WorkStartHour, WorkEndHour). WorkStart > WorkEnd 면 자정을 넘는 근무다.
        // 출퇴근 판정의 기준 — 전역 창이 아니라 차 자신의 값을 쓴다(유형별 창 대비).
        public float WorkStartHour, WorkEndHour;
```

- [ ] **Step 4: 차 생성 시 근무 구간 채우기**

`Rebuild` 안 `new CommuteCar { ... }`(현재 `:119-127`)에 두 줄을 추가한다. **이 태스크에서는 전역 창을 그대로 쓴다** — 그래서 동작이 안 바뀐다.

```csharp
                    DepartHomeHour = StaggerHour(sources[i], morningStart, morningEnd),
                    DepartWorkHour = StaggerHour(sources[i], eveningStart, eveningEnd),
                    WorkStartHour = morningStart,
                    WorkEndHour = eveningEnd,
```

- [ ] **Step 5: 판정을 차 개별 값으로 교체**

`UpdateDepartures`(현재 `:159-178`)의 두 조건을 바꾼다.

```csharp
                if (car.State == CarState.ParkedHome
                    && hour >= car.DepartHomeHour
                    && CommuteWindow.InWindow(hour, car.WorkStartHour, car.WorkEndHour))
                { car.State = CarState.Outbound; car.Distance = 0f; }
                else if (car.State == CarState.ParkedWork
                    && (!CommuteWindow.InWindow(hour, car.WorkStartHour, car.WorkEndHour)
                        || car.RetireReason == RetireReason.WorkLost))
                { car.State = CarState.Inbound; car.Distance = 0f; }
```

> 퇴근 조건이 `hour >= DepartWorkHour`에서 **"근무 구간을 벗어났는가"**로 바뀐다.
> 자정을 넘는 근무에서는 단순 비교가 성립하지 않기 때문이다.

`SnapCar`(현재 `:210-224`)의 창 판정을 바꾼다.

```csharp
                bool inWorkWindow = CommuteWindow.InWindow(
                    hour, car.WorkStartHour, car.WorkEndHour);
                car.State = inWorkWindow ? CarState.ParkedWork : CarState.ParkedHome;
```

- [ ] **Step 6: 통과 확인**

`refresh_unity` → `read_console`(위 화이트리스트 2건 외 `error CS` 없음) → `run_tests`(EditMode, `CityFlow.Sim.Tests`)
Expected: **415/415 PASS** (413 + 신규 2)

**기존 통근 테스트가 깨지면 수치만 갱신하고 실패를 덮는 완화는 하지 마라.** 깨진 이유가 "전역 창 전제"였다면 갱신이 맞고, 그 외 이유면 멈추고 보고한다.

- [ ] **Step 7: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Sim/CommuteScheduler.cs \
        Assets/Tests/EditMode/CommuteSchedulerTests.cs
git commit -m "[Feat] 출퇴근 판정을 차 개별 근무 구간 기준으로 — 자정 넘김 지원

전역 _eveningEnd 대신 CommuteCar.WorkStartHour/WorkEndHour 로 판정한다.
이 커밋 시점에는 전 차가 같은 전역 창을 받으므로 동작은 변하지 않는다."
```

> 여기까지가 **PR 1**이다. 동작 무변경이므로 단독 머지가 안전하다.

---

### Task 3: `CompanyTypeSO` + 카탈로그

**Files:**
- Create: `Assets/01_Scripts/CityFlow/Configs/Buildings/CompanyTypeSO.cs`
- Create: `Assets/01_Scripts/CityFlow/Configs/Buildings/CompanyTypeCatalogSO.cs`
- Test: `Assets/Tests/EditMode/CompanyTypeTests.cs` (신규)

**Interfaces:**
- Produces:
  - `CompanyTypeSO` — public 필드 `companyTypeId`, `displayName`, `capacity`, `workStartHour`, `workStartWindow`, `workEndHour`, `workEndWindow`
  - `CompanyTypeCatalogSO.TryGet(string id, out CompanyTypeSO definition)` / `IReadOnlyList<CompanyTypeSO> Types` / `int Count`
  - Task 4·5가 쓴다.

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/EditMode/CompanyTypeTests.cs`:

```csharp
using CityFlow.Content;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Sim.Tests
{
    public class CompanyTypeTests
    {
        static CompanyTypeSO NewType(string id, float start, float end)
        {
            var so = ScriptableObject.CreateInstance<CompanyTypeSO>();
            so.companyTypeId = id;
            so.displayName = id;
            so.capacity = 6;
            so.workStartHour = start;
            so.workStartWindow = 4f;
            so.workEndHour = end;
            so.workEndWindow = 4f;
            return so;
        }

        [Test]
        public void Catalog_LooksUpById_AndRejectsUnknown()
        {
            var catalog = ScriptableObject.CreateInstance<CompanyTypeCatalogSO>();
            catalog.SetTypesForTest(new[] { NewType("office", 6f, 15f), NewType("factory", 20f, 5f) });

            Assert.AreEqual(2, catalog.Count);
            Assert.IsTrue(catalog.TryGet("office", out CompanyTypeSO office));
            Assert.AreEqual(6f, office.workStartHour);
            Assert.IsTrue(catalog.TryGet("factory", out CompanyTypeSO factory));
            Assert.AreEqual(20f, factory.workStartHour, "공장은 야간 출근");
            Assert.AreEqual(5f, factory.workEndHour, "퇴근이 출근보다 이르다 = 자정을 넘는다");

            Assert.IsFalse(catalog.TryGet("warehouse", out _), "없는 id는 false");
            Assert.IsFalse(catalog.TryGet(null, out _), "null도 false");
            Assert.IsFalse(catalog.TryGet("", out _), "빈 문자열도 false");
        }
    }
}
```

- [ ] **Step 2: 실패 확인**

Expected: 컴파일 에러 — `CompanyTypeSO` / `CompanyTypeCatalogSO` 미정의

- [ ] **Step 3: SO 구현**

`CompanyTypeSO.cs`:

```csharp
using UnityEngine;

namespace CityFlow.Content
{
    // 회사 유형 1종. 시각은 게임시간 [0,24) 단위 — 하루 길이와 무관하다.
    // workStartHour > workEndHour 면 자정을 넘는 근무다(예: 공장 20시 출근 5시 퇴근).
    [CreateAssetMenu(
        fileName = "CompanyType",
        menuName = "CityFlow/Content/Company Type")]
    public sealed class CompanyTypeSO : ScriptableObject
    {
        public string companyTypeId;
        public string displayName;
        public int    capacity;

        public float  workStartHour;
        public float  workStartWindow;   // 출근 창 길이(시간). StaggerHour가 이 안에 흩뿌린다
        public float  workEndHour;
        public float  workEndWindow;
    }
}
```

`CompanyTypeCatalogSO.cs` — **`BuildingCatalogSO.cs`를 먼저 읽고 그 형태를 따른다**(id 인덱스, 중복·누락 경고, `OnValidate`에서 재색인).

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Content
{
    [CreateAssetMenu(
        fileName = "CompanyTypeCatalog",
        menuName = "CityFlow/Content/Company Type Catalog")]
    public sealed class CompanyTypeCatalogSO : ScriptableObject
    {
        [SerializeField]
        private List<CompanyTypeSO> types = new();

        private readonly Dictionary<string, CompanyTypeSO> byId =
            new(StringComparer.Ordinal);
        private bool indexDirty = true;

        public IReadOnlyList<CompanyTypeSO> Types => types;
        public int Count => types?.Count ?? 0;

        public bool TryGet(string companyTypeId, out CompanyTypeSO definition)
        {
            EnsureIndex();
            definition = null;
            if (string.IsNullOrWhiteSpace(companyTypeId)) return false;
            return byId.TryGetValue(companyTypeId.Trim(), out definition);
        }

        // 테스트 전용 주입 — 에셋 없이 카탈로그를 구성한다.
        internal void SetTypesForTest(IReadOnlyList<CompanyTypeSO> list)
        {
            types = new List<CompanyTypeSO>(list);
            indexDirty = true;
        }

        private void OnEnable() => indexDirty = true;
        private void OnValidate() { indexDirty = true; EnsureIndex(logWarnings: true); }

        private void EnsureIndex(bool logWarnings = false)
        {
            if (!indexDirty) return;
            byId.Clear();
            indexDirty = false;
            if (types == null) return;

            for (int i = 0; i < types.Count; i++)
            {
                CompanyTypeSO definition = types[i];
                string id = definition?.companyTypeId?.Trim();
                if (definition == null || string.IsNullOrEmpty(id))
                {
                    if (logWarnings)
                        Debug.LogWarning($"[CompanyTypeCatalogSO] Entry {i} has no company type ID.", this);
                    continue;
                }
                if (!byId.TryAdd(id, definition) && logWarnings)
                    Debug.LogWarning($"[CompanyTypeCatalogSO] Duplicate company type ID: {id}", this);
            }
        }
    }
}
```

> `SetTypesForTest`가 `internal`이므로 `CityFlow.Content` 어셈블리에 `[assembly: InternalsVisibleTo("CityFlow.Sim.Tests")]`가 필요하다. `Assets/01_Scripts/CityFlow/Contents/Logic/` 아래 asmdef가 `CityFlow.Content`이니 그 어셈블리에 `AssemblyInfo.cs`가 있는지 확인하고, 없으면 만든다. **`CompanyTypeSO`/`CompanyTypeCatalogSO`가 어느 어셈블리에 들어가는지 먼저 확인하라** — `Configs/Buildings/`가 asmdef 밖이면 `Assembly-CSharp`이고 그 경우 테스트에서 접근이 안 될 수 있다. 접근이 안 되면 `SetTypesForTest`를 `public`으로 바꾸고 주석으로 테스트 전용임을 밝힌다.

- [ ] **Step 4: 통과 확인**

`refresh_unity` → `read_console` → `run_tests`(EditMode, `CityFlow.Sim.Tests`)
Expected: **416/416 PASS** (415 + 신규 1)

- [ ] **Step 5: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Configs/Buildings/CompanyTypeSO.cs \
        Assets/01_Scripts/CityFlow/Configs/Buildings/CompanyTypeSO.cs.meta \
        Assets/01_Scripts/CityFlow/Configs/Buildings/CompanyTypeCatalogSO.cs \
        Assets/01_Scripts/CityFlow/Configs/Buildings/CompanyTypeCatalogSO.cs.meta \
        Assets/Tests/EditMode/CompanyTypeTests.cs \
        Assets/Tests/EditMode/CompanyTypeTests.cs.meta
git commit -m "[Feat] CompanyTypeSO + 카탈로그 — 유형별 정원·출퇴근 창

유형 추가가 에셋 편집만으로 끝나도록 카탈로그 형태로 만든다."
```

---

### Task 4: 유형 보관 + 배치 시 유형 지정·거부

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Sim/DemandMap.cs` (`CompanyCapacityState` `:50`, `RegisterCompany` `:87`)
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimEngine.cs` (`Place` `:~520`, `RegisterCompany` 호출 `:536`)
- Test: `Assets/Tests/EditMode/CompanyTypeTests.cs` (추가)

**Interfaces:**
- Consumes: `CompanyTypeCatalogSO.TryGet` (Task 3)
- Produces:
  - `SimEngine.Place(tile, type, direction, string companyTypeId = null)`
  - `SimEngine.SetCompanyTypeCatalog(CompanyTypeCatalogSO catalog)` — 부트스트랩이 주입
  - `DemandMap.TryGetCompanyTypeId(Vector2Int anchor, out string id)`
  - Task 5가 쓴다.

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
        [Test]
        public void PlaceOffice_WithoutCompanyType_IsRejected()
        {
            SimConfig cfg = SimConfig.Default();
            cfg.GridWidth = 8; cfg.GridHeight = 4;
            var engine = new SimEngine(cfg, new SimEventHub());
            var catalog = ScriptableObject.CreateInstance<CompanyTypeCatalogSO>();
            catalog.SetTypesForTest(new[] { NewType("office", 6f, 15f) });
            engine.SetCompanyTypeCatalog(catalog);

            Assert.IsFalse(engine.Place(V(4, 0), TileType.Office),
                "유형 미지정 Office 배치는 거부된다 — 조용한 폴백을 만들지 않는다");
            Assert.AreEqual(TileType.Empty, engine.GetTileType(V(4, 0)),
                "거부되면 아무것도 놓이지 않는다");

            Assert.IsFalse(engine.Place(V(4, 0), TileType.Office, PlacementDirection.North, "nope"),
                "카탈로그에 없는 id도 거부");

            Assert.IsTrue(engine.Place(V(4, 0), TileType.Office, PlacementDirection.North, "office"),
                "유형을 넘기면 배치된다");
            Assert.IsTrue(engine.TryGetCompanyTypeIdForTest(V(4, 0), out string id));
            Assert.AreEqual("office", id);
        }

        [Test]
        public void PlaceNonOffice_IgnoresCompanyType()
        {
            SimConfig cfg = SimConfig.Default();
            cfg.GridWidth = 8; cfg.GridHeight = 4;
            var engine = new SimEngine(cfg, new SimEventHub());

            // 카탈로그가 없어도 도로·집은 그대로 놓인다 (기존 호출자 무영향)
            Assert.IsTrue(engine.Place(V(0, 2), TileType.Road));
            Assert.IsTrue(engine.Place(V(0, 0), TileType.House));
        }
```

관찰 seam이 필요하다. `SimEngine.cs:79-82`의 `*ForTest` 패턴을 따라 추가한다:

```csharp
        internal bool TryGetCompanyTypeIdForTest(Vector2Int anchor, out string id) =>
            _demand.TryGetCompanyTypeId(anchor, out id);
```

- [ ] **Step 2: 실패 확인**

Expected: 컴파일 에러 — `SetCompanyTypeCatalog` / `TryGetCompanyTypeIdForTest` 미정의

- [ ] **Step 3: `DemandMap`에 유형 보관**

`CompanyCapacityState`(`:50`)에 필드를 더한다.

```csharp
        sealed class CompanyCapacityState
        {
            public TileType Type;
            public string CompanyTypeId;      // 신규
            public int TotalCapacity;
            public double BuiltAtSimSeconds;
            public bool IsFullyOpen;
            public bool UsesTypeDefault;
        }
```

`RegisterCompany`(`:87`)에 파라미터를 더하고(기본값 `null`) 상태에 싣는다. 조회를 추가한다.

```csharp
        public bool TryGetCompanyTypeId(Vector2Int anchor, out string id)
        {
            id = null;
            if (!_companies.TryGetValue(anchor, out CompanyCapacityState state)) return false;
            id = state.CompanyTypeId;
            return !string.IsNullOrEmpty(id);
        }
```

> `_companies` 딕셔너리의 실제 이름은 파일을 읽고 맞춘다.

- [ ] **Step 4: `SimEngine.Place`에 유형 인자와 거부**

```csharp
        private CompanyTypeCatalogSO _companyTypes;
        public void SetCompanyTypeCatalog(CompanyTypeCatalogSO catalog) => _companyTypes = catalog;

        public bool Place(Vector2Int tile, TileType type,
                          PlacementDirection direction = PlacementDirection.North,
                          string companyTypeId = null)
        {
            // Office 는 유형 지정이 필수다. 조용히 사무실로 폴백하지 않는다 —
            // 유형을 빠뜨린 실수가 에러 없이 묻히는 것을 막는다.
            if (type == TileType.Office)
            {
                if (_companyTypes == null) return false;
                if (!_companyTypes.TryGet(companyTypeId, out _)) return false;
            }
            // ... 기존 본문 ...
```

`RegisterCompany` 호출(`:536`)에 id를 넘긴다.

```csharp
            if (type == TileType.Office)
                _demand.RegisterCompany(tile, type, _simTime, companyTypeId);
```

> **주의**: 기존 EditMode 테스트 상당수가 `engine.Place(tile, TileType.Office)`를 유형 없이 호출한다. 이 변경으로 전부 실패한다. **해결책: 카탈로그가 주입되지 않은 경우(`_companyTypes == null`)에도 거부**하면 테스트가 전부 깨진다. 그러므로 **기존 테스트를 카탈로그 주입 + 유형 지정으로 갱신한다.** 실패를 덮는 완화(예: null이면 통과)를 만들지 마라 — ⑥ 결정이 무의미해진다.
> 갱신 대상은 `run_tests` 실패 목록으로 확인한다. 대개 `CarSimEngineTests`·`DemandMapTests`·`CommuteEconomyProbeTests`의 헬퍼 한두 곳에 카탈로그 주입을 넣으면 일괄 해결된다.

- [ ] **Step 5: 유형별 정원 배선**

지금 정원은 `SimConfig.OfficeCapacity`(6) 공통이다. 유형이 있으면 그 값을 쓰고 없으면 폴백한다.

`DemandMap`이 회사 정원을 정하는 지점(`RegisterCompany` 또는 `EffectiveCapacity` 계산부)에서:

```csharp
        // 유형이 지정돼 있으면 유형 정원, 아니면 SimConfig 폴백.
        int totalCapacity = typeCapacity > 0 ? typeCapacity : _config.OfficeCapacity;
```

`typeCapacity`는 `SimEngine`이 `RegisterCompany` 호출 시 카탈로그에서 뽑아 넘긴다 — `DemandMap`이 SO를 직접 참조하지 않게 한다(어셈블리 방향: `Sim`은 `Content`를 몰라도 되게).

```csharp
        // SimEngine.Place 안
        int capacity = 0;
        if (_companyTypes != null && _companyTypes.TryGet(companyTypeId, out CompanyTypeSO def))
            capacity = def.capacity;
        _demand.RegisterCompany(tile, type, _simTime, companyTypeId, capacity);
```

테스트를 하나 더 추가한다:

```csharp
        [Test]
        public void CompanyCapacity_FollowsTypeDefinition()
        {
            SimConfig cfg = SimConfig.Default();
            cfg.GridWidth = 8; cfg.GridHeight = 4;
            var engine = new SimEngine(cfg, new SimEventHub());
            var big = NewType("factory", 20f, 5f); big.capacity = 10;
            var small = NewType("warehouse", 4f, 13f); small.capacity = 4;
            var catalog = ScriptableObject.CreateInstance<CompanyTypeCatalogSO>();
            catalog.SetTypesForTest(new[] { big, small });
            engine.SetCompanyTypeCatalog(catalog);

            Assert.IsTrue(engine.Place(V(0, 0), TileType.Office, PlacementDirection.North, "factory"));
            Assert.IsTrue(engine.Place(V(4, 0), TileType.Office, PlacementDirection.North, "warehouse"));

            Assert.IsTrue(engine.TryGetCompanyStaffing(V(0, 0), out CompanyStaffing f));
            Assert.IsTrue(engine.TryGetCompanyStaffing(V(4, 0), out CompanyStaffing w));
            Assert.AreEqual(10, f.Capacity, "공장은 유형 정원 10");
            Assert.AreEqual(4, w.Capacity, "물류창고는 유형 정원 4");
        }
```

- [ ] **Step 6: 통과 확인**

`refresh_unity` → `read_console` → `run_tests`(EditMode, `CityFlow.Sim.Tests`)
Expected: **419/419 PASS** (416 + 신규 3). 기존 테스트 갱신분이 있으면 숫자는 같고 내용만 바뀐다.

- [ ] **Step 7: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Sim/DemandMap.cs \
        Assets/01_Scripts/CityFlow/Sim/SimEngine.cs \
        Assets/Tests/EditMode/
git commit -m "[Feat] 회사 유형 보관 + 배치 시 유형 필수 + 유형별 정원

Office 배치에 companyTypeId 를 요구하고 미지정·미등록 id 는 거부한다.
조용한 사무실 폴백을 만들지 않는다(설계 결정 6)."
```

---

### Task 5: `Rebuild` 콜백 전환 + 유형별 창 적용

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Sim/CommuteScheduler.cs` (`Rebuild` `:45`, 차 생성 `:119`)
- Modify: `Assets/01_Scripts/CityFlow/Sim/CarSim.cs` (`:358` 호출부)
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimEngine.cs` (창 조회 제공)
- Test: `Assets/Tests/EditMode/CompanyTypeTests.cs` · `CommuteSchedulerTests.cs`

**Interfaces:**
- Consumes: `CommuteWindow` (Task 1), `CommuteCar.WorkStartHour/WorkEndHour` (Task 2), 카탈로그(Task 3), `DemandMap.TryGetCompanyTypeId` (Task 4)
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

            var catalog = ScriptableObject.CreateInstance<CompanyTypeCatalogSO>();
            catalog.SetTypesForTest(new[] {
                NewType("office",  6f, 15f),   // 오전 출근
                NewType("factory", 20f, 5f),   // 야간 출근 — 자정 넘김
            });
            engine.SetCompanyTypeCatalog(catalog);

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

차 생성부(`:119`)를 바꾼다.

```csharp
                CommuteWindow w = windowFor(sinks[i]);
                var fresh = new CommuteCar
                {
                    Home = sources[i], Work = sinks[i], RouteIndex = i,
                    WorkSlot = workSlot, HomeSlot = homeSlot,
                    DepartHomeHour = StaggerHour(sources[i], w.StartHour, w.StartHour + w.StartWindow),
                    DepartWorkHour = StaggerHour(sources[i], w.EndHour,   w.EndHour   + w.EndWindow),
                    WorkStartHour = w.StartHour,
                    WorkEndHour   = w.EndHour,
                    State = CarState.ParkedHome,
                    AwaitingNextWave = deferNewAssignments,
                };
```

> `CommuteCar`에 `CompanyTypeId` 필드도 더한다(설계 결정 ①). `w.CompanyTypeId`를 그대로 싣는다.
> 콜백이 출처이고 필드는 캐시다 — 역할이 겹치지 않는다.

`_morningEnd`·`_eveningStart`·`_eveningEnd` 필드와 그 참조를 전부 제거한다. Task 2에서 판정이 이미 차 개별 값으로 옮겨졌으므로 남은 참조가 없어야 한다. **남아 있으면 그 지점을 보고하라.**

- [ ] **Step 4: `SimEngine`이 창을 제공**

```csharp
        // 목적지 타일 → 그 회사 유형의 출퇴근 창. 카탈로그·유형이 없으면 SimConfig 폴백.
        internal CommuteWindow CommuteWindowAt(Vector2Int sink)
        {
            if (_companyTypes != null
                && _demand.TryGetCompanyTypeId(sink, out string id)
                && _companyTypes.TryGet(id, out CompanyTypeSO def))
            {
                return new CommuteWindow(
                    id,
                    def.workStartHour, def.workStartWindow,
                    def.workEndHour,   def.workEndWindow);
            }

            // 폴백 — School 등 유형이 없는 목적지와 카탈로그 미주입 상황
            return new CommuteWindow(
                string.Empty,
                _config.MorningStartHour, _config.MorningEndHour - _config.MorningStartHour,
                _config.EveningStartHour, _config.EveningEndHour - _config.EveningStartHour);
        }
```

- [ ] **Step 5: `CarSim` 호출부 갱신**

`CarSim.cs:358`의 `_scheduler.Rebuild(...)`에서 시각 인자 4개를 빼고 콜백을 넘긴다. `CarSim`이 `SimEngine`을 참조하지 않으므로 **`CarSim.Rebuild`에 콜백을 인자로 받아 전달**한다. `SimEngine`이 `_carSim.Rebuild(..., CommuteWindowAt, ...)` 형태로 넘기는 배선이 필요하다. 실제 시그니처는 `CarSim.Rebuild`를 읽고 맞춘다.

- [ ] **Step 6: 통과 확인**

`refresh_unity` → `read_console` → `run_tests`(EditMode, `CityFlow.Sim.Tests`)
Expected: **420/420 PASS** (419 + 신규 1)

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

### Task 6 (조건부): 공사 중 유형 보존 — **PR #171 머지 후에만**

`ConstructionSite`는 PR #171(건물 건설시간)이 도입한 타입이다. **#171이 develop에 머지되기 전에는 이 태스크를 시작하지 마라.** `git log origin/develop --oneline | grep -i "건설시간"`로 확인한다.

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
            var catalog = ScriptableObject.CreateInstance<CompanyTypeCatalogSO>();
            catalog.SetTypesForTest(new[] { NewType("office", 6f, 15f), NewType("factory", 20f, 5f) });
            engine.SetCompanyTypeCatalog(catalog);

            Assert.IsTrue(engine.Place(V(4, 0), TileType.Office, PlacementDirection.North, "factory"));
            Assert.AreEqual(TileType.UnderConstruction, engine.GetTileType(V(4, 0)));

            // 공사 중 저장 → 로드
            var snap = engine.CreateSnapshot();
            var restored = new SimEngine(cfg, new SimEventHub());
            restored.SetCompanyTypeCatalog(catalog);
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

- [ ] **Step 5: 통과 확인** — `run_tests` **421/421 PASS**

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

**Files:**
- Create: `Assets/05_ScriptableObjects/Companies/CompanyType_Office.asset`
- Create: `Assets/05_ScriptableObjects/Companies/CompanyType_Factory.asset`
- Create: `Assets/05_ScriptableObjects/Companies/CompanyType_Warehouse.asset`
- Create: `Assets/05_ScriptableObjects/Companies/CompanyTypeCatalog.asset`

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

`SimEngine.SetCompanyTypeCatalog`를 누가 부르는지 확인하고, 카탈로그를 주입하는 경로가 없으면 `CityBootstrap`에 `[SerializeField] private CompanyTypeCatalogSO companyTypes;` 를 더해 엔진 생성 직후 주입한다.

> ⚠️ 이건 **통합 씬 직렬화**와 얽힌다. `CityBootstrap`에 `[SerializeField]`를 더하면 씬에서 값을 물려야 하고, 씬 커밋 금지 규칙에 걸린다. **씬을 건드리지 않으려면** `Resources.Load` 또는 카탈로그를 `SimConfigAsset` 옆에 두고 코드에서 경로로 읽는 방식을 쓴다. 어느 쪽이든 **씬 저장이 필요 없어야 한다.**

- [ ] **Step 5: 검증 — 에셋만 바뀌므로 테스트는 무변경**

`refresh_unity` → `read_console` → `run_tests`(EditMode, `CityFlow.Sim.Tests`)
Expected: **421/421 PASS** (테스트는 카탈로그를 코드로 주입하므로 에셋 추가에 영향받지 않는다)

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

- EditMode `CityFlow.Sim.Tests` **421/421 green** (기준선 410 + 신규 11). Task 6(조건부)을 못 하면 420.
- 컴파일 `error CS` 0
- 통합 씬 파일이 커밋에 **없음**
- 신규 `.cs`의 `.cs.meta` 전부 커밋됨
- **PR 1** = Task 1~2 (동작 무변경) · **PR 2** = Task 3~5 + Task 7 · **PR 3(조건부)** = Task 6
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
