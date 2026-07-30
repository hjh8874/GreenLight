# 연구 해금 1차 — 특수건물 8종 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 도시 성장(통행량·인구·시설)에 따라 특수건물 8종이 자동으로 해금되고, 코인으로 지어 방문 수익을 내게 한다.

**Architecture:** 조건 판정은 순수 정적 함수(`ResearchConditionEvaluator`), 사다리는 SO 카탈로그(에셋 편집만으로 변경), 평가는 기존 `ResearchUnlockService`에 트리거 4개를 얹는다. 방문 코인은 트립 요청에 실어 보내 도착 시 기존 `ArrivalEvent` 경로로 지급한다 — 주간 적립·HUD·피드·퀘스트가 공짜로 따라온다. 산출물은 씬에 끌어다 놓으면 배선이 끝나는 프리팹 하나다.

**Tech Stack:** Unity 6000.5.2f1 · C# · NUnit EditMode (`CityFlow.Sim.Tests` + 기본 에디터 어셈블리 이름 필터)

**설계 문서:** `docs/superpowers/specs/2026-07-30-research-unlock-buildings-design.md`

## Global Constraints

- 기준 브랜치: **`feat-research-unlock-hwan`** (develop `d50078e` 직분기 리베이스 완료). 스택 금지.
- 회귀 기준선: EditMode `CityFlow.Sim.Tests` — **Task 0에서 실측해 기록**한다(예상 423/423, develop `d50078e`). **부분 실패 허용 없음.**
- 검증 순서 (매 태스크 끝, 순서 고정):
  1. `mcp__unityMCP__refresh_unity`(compile="request", mode="force")
  2. `mcp__unityMCP__read_console`(types=["error"]) → **메시지에 `error CS`가 들어 있으면 진짜 컴파일 에러.** 무시해도 되는 것: `MCP-FOR-UNITY: Bridge not running` · `NanumGothic SDF` 폰트 누락. 그 외 에러는 멈추고 보고
  3. `mcp__unityMCP__run_tests`(EditMode, assembly_names=["CityFlow.Sim.Tests"]) — 기본 에디터 어셈블리 테스트는 `group_names=[".*클래스명.*"]` **이름 필터로만** 돌 수 있다(전체 EditMode는 자동 완주 불가)
- **테스트가 돌았다는 것 ≠ 컴파일 성공.** Unity는 컴파일 실패 시 직전 DLL로 테스트를 돌린다. `read_console`을 먼저 본다.
- **새 테스트는 RED를 먼저 증명한다.** 컴파일 에러(CS0246 등)도 RED로 인정. 픽스 없이 통과하는 단정 금지.
- 작업은 본 체크아웃 `/Users/hwan/Gamemaker/GreenLight`에서만. worktree·격리 사본 금지(Library 재임포트 10~30분 + unityMCP가 본 체크아웃에만 붙음).
- **씬 파일(.unity) 커밋 금지.** 라이브 확인은 `CityFlowIntegrated_hwan`에서만.
- 신규 `.cs`는 `.cs.meta` 함께 커밋. 프리팹·에셋도 `.meta` 함께.
- 커밋 전 `git status`로 스테이징 목록 확인 — `git add`는 항상 명시 목록으로.
- **소유권**: `Contracts/*.cs` = 공유 계약(수정 시 PR 본문 명시) · `05_ScriptableObjects/Buildings/*.asset`·`BuildingDefinitionSO` 계열 = **진우 소유**(수정은 하되 PR 리뷰어로 원작성자 지정) · `MainCityView`는 **건드리지 않는다**.
- 어셈블리 지도: `Sim/`=`CityFlow.Sim`(Contracts만 참조, **Assembly-CSharp 참조 불가**) · `Gameplay/`·`Configs/`·`Buildings/`·`UI/`=Assembly-CSharp · `Assets/Tests/EditMode`=`CityFlow.Sim.Tests` · `Assets/Tests/ViewEditMode/Editor`=기본 에디터 어셈블리(Assembly-CSharp 참조 가능).

## File Structure

| 파일 | 책임 | 변경 |
|---|---|---|
| `Assets/01_Scripts/CityFlow/Contracts/IReadOnlyCityStats.cs` | 어제 도착 수 노출 | 수정 (Task 1, 공유 계약) |
| `Assets/01_Scripts/CityFlow/Sim/SimStats.cs` | 하루 경계에서 확정치 캡처 | 수정 (Task 1) |
| `Assets/01_Scripts/CityFlow/Sim/SimEngine.cs` | 계약 구현 | 수정 (Task 1) |
| `Assets/01_Scripts/CityFlow/Gameplay/Research/ResearchConditionEvaluator.cs` | 순수 조건 판정 | **신규** (Task 2) |
| `Assets/01_Scripts/CityFlow/Configs/Research/ResearchCatalogSO.cs` | 사다리 카탈로그 | **신규** (Task 2) |
| `Assets/01_Scripts/CityFlow/Gameplay/Research/ResearchUnlockService.cs` | 평가 루프(트리거 4개) | 수정 (Task 3) |
| `Assets/01_Scripts/CityFlow/Contracts/IVehicleTripService.cs` | 방문 요청에 보상 코인 | 수정 (Task 4, 공유 계약) |
| `Assets/01_Scripts/CityFlow/Sim/TripScheduler.cs` | 방문 트립에 코인 적재 | 수정 (Task 4) |
| `Assets/01_Scripts/CityFlow/Sim/CarSim.cs` | 방문 도착 → ArrivalEvent | 수정 (Task 4) |
| `Assets/01_Scripts/CityFlow/Buildings/SpecialBuildingVisitTripSource.cs` | 요청에 CoinPerVisit 채움 | 수정 (Task 4) |
| `Assets/05_ScriptableObjects/Resources/CityFlow/ResearchCatalog.asset` | 사다리 8칸 | **신규** (Task 5) |
| `Assets/05_ScriptableObjects/Buildings/Building_*.asset` ×8 | 값 채우기 | 수정 (Task 5, **진우 소유**) |
| `Assets/01_Scripts/CityFlow/UI/Panels/ResearchPanelController.cs` | 패널 표시 | **신규** (Task 6) |
| `Assets/02_Prefabs/UI/ResearchPanel.prefab` | 끌어다 놓으면 끝 | **신규** (Task 6) |
| `Assets/Tests/EditMode/SpecialVisitRewardTests.cs` | 방문 코인 지급 | **신규** (Task 4) |
| `Assets/Tests/ViewEditMode/Editor/ResearchConditionTests.cs` | 판정·카탈로그·평가 루프 | **신규** (Task 2~3) |
| `Assets/Tests/ViewEditMode/Editor/ResearchPanelTests.cs` | 패널 | **신규** (Task 6) |

## 실측된 접점 (2026-07-30, develop `d50078e` — 워커는 재확인만, 재조사 불필요)

- `SimStats.DayArrivalCount`(`SimStats.cs:16`)는 **오늘 누적치**이고 하루 경계(`wrapped`)에서 0으로 리셋된다(`:43`). "어제 도착"은 리셋 직전 값을 캡처해야 한다.
- 방문 트립 생성: `SpecialBuildingVisitTripSource.ScheduleStatistics` → `IVehicleTripService.TryScheduleSpecialBuildingVisit(request)` → `TripScheduler.TryEnqueue` → `SpecialTripJourney.CreateTrip`(`TripScheduler.cs:320-335`)이 **`rewardCoins: 0` 하드코딩** — 여기가 구멍.
- 방문 도착: `CarSim.HandleSpecialTripArrival`(`CarSim.cs:1498`)이 `journey.CompleteCurrentLeg()` → `VehicleTripSnapshot`(RewardCoins 필드 **이미 있음**) → `QueueTripArrival`. **코인은 안 준다.** 통근 코인은 `CarSim.cs:744` `QueueArrival(new ArrivalEvent(car.Work, _cfg.CoinPerTrip))`.
- `ArrivalEvent`를 쏘면 `WeeklyEconomyLoopService`(주간 적립)·`DistanceRewardService`(거리 보너스)·HUD·피드·퀘스트가 이미 구독 중 — **지급 이후 흐름은 전부 공짜.**
- `StepResult.Arrivals`(`RoadQueueNetwork.cs:1156,1262`)는 네트워크 계층 카운트라 **방문 차량 도착도 `DayArrivalCount`에 포함**된다(기존 동작, 변경하지 않는다 — 통행량 조건은 총 처리량을 잰다).
- 해금 → 건설 반응은 **이미 배선됨**: `SpecialBuildingService:444-446`이 `IsUnlocked(RequiredResearchId)` 검사. 8종 에셋의 `requiredResearchId`도 기입 완료.
- 공사 완성 시 `PlacedEvent`(실타입)가 발화된다(`SimEngine.AdvanceConstruction` `:587-588`) — 건설시간이 켜져도 `Placed` 트리거(시설 조건)가 성립한다.
- `ResearchSaveData.UnlockedResearchIds` 세이브 왕복 **이미 구현** — 추가 작업 0.
- 서비스 조회: `Services.Stats`(`IReadOnlyCityStats`) · `Services.Population`(`IReadOnlyPopulationData.CurrentPopulation`) · `Services.GameCalendar.DayChanged` · `Services.Events.Placed` · `Services.Save.RestoreCompleted` · `Services.WorldGrid`(크기) · `Services.TileData`(타일 조회).

## 단계와 실행 방식

Unity 본 체크아웃이 하나뿐이라 **구현은 직렬**이다. 태스크 순서 = 의존 순서.

```
Task 0  기준선 실측
Task 1  어제 도착 수 계약 노출          (Sim + Contracts)
Task 2  조건 판정 + 카탈로그 SO         (Assembly-CSharp, 순수)
Task 3  평가 루프                      (ResearchUnlockService)
Task 4  방문 코인 지급 배관             (Sim + Contracts + TripSource)
Task 5  카탈로그 에셋 + 8종 값 채우기    (에셋만)
Task 6  연구 패널 프리팹                (UI)
Task 7  최종 게이트
```

---

### Task 0: 기준선 실측

- [ ] **Step 1:** `git branch --show-current` → `feat-research-unlock-hwan` 확인. 아니면 멈추고 보고.
- [ ] **Step 2:** `refresh_unity`(compile="request", mode="force") → `read_console`(types=["error"]) → `error CS` 0건 확인.
- [ ] **Step 3:** `run_tests`(EditMode, assembly_names=["CityFlow.Sim.Tests"]) → **전부 통과 확인, 총 개수를 기록**(이후 태스크의 기준선). 실패가 있으면 멈추고 보고.

---

### Task 1: 어제 도착 수 계약 노출

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Contracts/IReadOnlyCityStats.cs` (공유 계약 — PR 명시)
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimStats.cs`
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimEngine.cs`
- Test: `Assets/Tests/EditMode/SimStatsTests.cs` (있으면 추가, 없으면 신규)

**Interfaces:**
- Produces: `IReadOnlyCityStats.LastDayArrivalCount { get; }` — **어제(마지막으로 완주한 하루)의 최종 도착 수.** 오늘 누적치가 아니다. Task 3(조건 입력)·Task 6(계기판 "어제 도착")이 쓴다.

- [ ] **Step 1: 실패하는 테스트 작성**

`SimStats`는 internal이라 `CityFlow.Sim.Tests`에서 직접 보인다(`InternalsVisibleTo`). 신규 파일이면 네임스페이스는 `CityFlow.Sim.Tests`.

```csharp
        [Test]
        public void LastDayArrivalCount_CapturesFinalValueAtDayWrap()
        {
            var stats = new SimStats();
            SimConfig cfg = SimConfig.Default();

            stats.UpdateCarSim(gameHour: 8f, arrivals: 3, carCount: 4, jumped: false, jamRatio: 0f, in cfg);
            stats.UpdateCarSim(gameHour: 20f, arrivals: 2, carCount: 4, jumped: false, jamRatio: 0f, in cfg);
            Assert.AreEqual(0, stats.LastDayArrivalCount, "하루가 끝나기 전엔 어제 값이 없다");

            stats.UpdateCarSim(gameHour: 1f, arrivals: 0, carCount: 4, jumped: false, jamRatio: 0f, in cfg);   // wrap
            Assert.AreEqual(5, stats.LastDayArrivalCount, "경계에서 어제의 최종치(3+2)를 캡처한다");

            stats.UpdateCarSim(gameHour: 9f, arrivals: 7, carCount: 4, jumped: false, jamRatio: 0f, in cfg);
            Assert.AreEqual(5, stats.LastDayArrivalCount, "오늘 누적은 어제 값을 건드리지 않는다");
        }
```

> `UpdateCarSim`의 실제 시그니처(인자 순서·`in` 여부)는 `SimStats.cs`를 열어 맞춘다. 시그니처가 다르면 테스트를 코드에 맞추고, 이 계획과 다른 점을 보고에 기록한다.

- [ ] **Step 2: RED 확인** — `run_tests`(assembly_names=["CityFlow.Sim.Tests"]) 전에 `refresh_unity`+`read_console`. Expected: `error CS1061`(`LastDayArrivalCount` 미정의).
- [ ] **Step 3: 구현**

`SimStats.cs` — wrap 분기에서 리셋 **직전에** 캡처:

```csharp
        private int _lastDayArrivals;
        internal int LastDayArrivalCount => _lastDayArrivals;
```

wrap 처리부(`else if (wrapped)` 블록)의 `_dayArrivals = 0;` **앞에**:

```csharp
                if (!_skipCurrentDay)
                {
                    _lastDayArrivals = _dayArrivals;   // 시각 점프로 끊긴 날은 캡처하지 않는다
                }
```

`IReadOnlyCityStats.cs`:

```csharp
        // 어제(마지막으로 완주한 하루)의 최종 도착 수. 오늘 누적치가 아니다 —
        // 하루 경계에서 확정되며, 시각 점프로 끊긴 날은 갱신하지 않는다.
        // 연구 해금의 통행량 조건과 연구 패널 계기판이 읽는다.
        int LastDayArrivalCount { get; }
```

`SimEngine.cs` — 기존 `IReadOnlyCityStats` 구현부(`ActiveVehicleCount` 근처)에:

```csharp
        public int LastDayArrivalCount => _stats.LastDayArrivalCount;
```

- [ ] **Step 4: GREEN 확인** — 게이트 3단 전부. 기준선 + 1 통과.
- [ ] **Step 5: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Contracts/IReadOnlyCityStats.cs \
        Assets/01_Scripts/CityFlow/Sim/SimStats.cs \
        Assets/01_Scripts/CityFlow/Sim/SimEngine.cs \
        Assets/Tests/EditMode/SimStatsTests.cs Assets/Tests/EditMode/SimStatsTests.cs.meta
git commit -m "[Feat] 어제 도착 수를 계약으로 노출 — LastDayArrivalCount

DayArrivalCount 는 오늘 누적치라 하루 경계에서 리셋된다. 연구 해금의 통행량
조건은 완주한 하루의 확정치가 필요해 wrap 직전 값을 캡처해 노출한다.
IReadOnlyCityStats 는 공유 계약 — 구현체는 SimEngine 하나뿐.
세이브에는 싣지 않는다(로드 직후엔 첫 하루 완주까지 0 — 해금 상태 자체는 저장되므로 무해)."
```

---

### Task 2: 조건 판정 + 카탈로그 SO

**Files:**
- Create: `Assets/01_Scripts/CityFlow/Gameplay/Research/ResearchConditionEvaluator.cs`
- Create: `Assets/01_Scripts/CityFlow/Configs/Research/ResearchCatalogSO.cs`
- Test: `Assets/Tests/ViewEditMode/Editor/ResearchConditionTests.cs` (신규 — 기본 에디터 어셈블리, **이름 필터로 실행**)

**Interfaces:**
- Produces (Task 3·5·6이 쓴다):

```csharp
public enum ResearchConditionKind { DailyArrivals, Population, BuildingCount }

public readonly struct ResearchConditionInputs
{
    public readonly int LastDayArrivals;
    public readonly int Population;
    public readonly System.Func<CityFlow.Contracts.TileType, int> CountBuildings;   // null 허용(0 취급)
    public ResearchConditionInputs(int lastDayArrivals, int population,
        System.Func<CityFlow.Contracts.TileType, int> countBuildings) { ... }
}

public static class ResearchConditionEvaluator
{
    public static bool IsSatisfied(ResearchEntry entry, in ResearchConditionInputs inputs);
    public static int CurrentValue(ResearchEntry entry, in ResearchConditionInputs inputs);  // 패널 진행 표시용
}

[System.Serializable] public sealed class ResearchEntry
{
    public string researchId; public string displayName;
    public ResearchConditionKind conditionKind;
    public int threshold;
    public CityFlow.Contracts.TileType targetTileType;   // BuildingCount 에서만 사용
}

public sealed class ResearchCatalogSO : ScriptableObject
{
    public const string DefaultResourcePath = "CityFlow/ResearchCatalog";
    public static ResearchCatalogSO LoadDefault();      // Resources.Load
    public System.Collections.Generic.List<ResearchEntry> ValidEntries();  // 빈 id·중복 id 경고 후 스킵
}
```

- [ ] **Step 1: 실패하는 테스트 작성** — `ResearchConditionTests.cs`

```csharp
using System.Collections.Generic;
using CityFlow.Contracts;
using CityFlow.Gameplay.Research;
using NUnit.Framework;
using UnityEngine;

// 기본 에디터 어셈블리. 실행: run_tests(group_names=[".*ResearchConditionTests.*"])
public class ResearchConditionTests
{
    static ResearchEntry Entry(string id, ResearchConditionKind kind, int threshold,
        TileType target = TileType.Empty) =>
        new ResearchEntry { researchId = id, displayName = id,
            conditionKind = kind, threshold = threshold, targetTileType = target };

    static ResearchConditionInputs Inputs(int arrivals = 0, int population = 0,
        int schools = 0, int hospitals = 0) =>
        new ResearchConditionInputs(arrivals, population,
            t => t == TileType.School ? schools : t == TileType.Hospital ? hospitals : 0);

    [Test]
    public void DailyArrivals_IsSatisfiedAtThreshold_HalfOpenBelow()
    {
        var e = Entry("a", ResearchConditionKind.DailyArrivals, 60);
        Assert.IsFalse(ResearchConditionEvaluator.IsSatisfied(e, Inputs(arrivals: 59)));
        Assert.IsTrue(ResearchConditionEvaluator.IsSatisfied(e, Inputs(arrivals: 60)), "경계 = 충족");
        Assert.IsTrue(ResearchConditionEvaluator.IsSatisfied(e, Inputs(arrivals: 200)));
    }

    [Test]
    public void Population_And_BuildingCount_ReadTheirOwnInputs()
    {
        var pop = Entry("p", ResearchConditionKind.Population, 20);
        Assert.IsFalse(ResearchConditionEvaluator.IsSatisfied(pop, Inputs(population: 19)));
        Assert.IsTrue(ResearchConditionEvaluator.IsSatisfied(pop, Inputs(population: 20)));

        var school = Entry("s", ResearchConditionKind.BuildingCount, 1, TileType.School);
        Assert.IsFalse(ResearchConditionEvaluator.IsSatisfied(school, Inputs(schools: 0)));
        Assert.IsTrue(ResearchConditionEvaluator.IsSatisfied(school, Inputs(schools: 1)));
        Assert.IsFalse(ResearchConditionEvaluator.IsSatisfied(school, Inputs(hospitals: 3)),
            "다른 타일 개수는 안 센다");
    }

    [Test]
    public void CurrentValue_ReturnsTheConditionSourceValue()
    {
        Assert.AreEqual(131, ResearchConditionEvaluator.CurrentValue(
            Entry("a", ResearchConditionKind.DailyArrivals, 150), Inputs(arrivals: 131)));
        Assert.AreEqual(84, ResearchConditionEvaluator.CurrentValue(
            Entry("p", ResearchConditionKind.Population, 80), Inputs(population: 84)));
    }

    [Test]
    public void Catalog_ValidEntries_WarnsAndSkips_EmptyAndDuplicateIds()
    {
        var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
        var so = new UnityEditor.SerializedObject(catalog);
        var list = so.FindProperty("entries");
        list.arraySize = 3;
        // [0] 정상, [1] 빈 id, [2] 중복 id — managed reference 가 아니라 plain serializable 이므로
        // 자식 프로퍼티를 직접 채운다
        SetEntry(list.GetArrayElementAtIndex(0), "research_a");
        SetEntry(list.GetArrayElementAtIndex(1), "  ");
        SetEntry(list.GetArrayElementAtIndex(2), "research_a");
        so.ApplyModifiedPropertiesWithoutUndo();

        UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
            new System.Text.RegularExpressions.Regex("id"));
        UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
            new System.Text.RegularExpressions.Regex("중복"));
        List<ResearchEntry> valid = catalog.ValidEntries();
        Assert.AreEqual(1, valid.Count);
        Assert.AreEqual("research_a", valid[0].researchId);
    }

    static void SetEntry(UnityEditor.SerializedProperty p, string id)
    {
        p.FindPropertyRelative("researchId").stringValue = id;
        p.FindPropertyRelative("displayName").stringValue = id;
        p.FindPropertyRelative("threshold").intValue = 1;
    }
}
```

- [ ] **Step 2: RED 확인** — `refresh_unity` → `read_console`. Expected: `error CS0246`(`ResearchConditionEvaluator` 등 미정의).
- [ ] **Step 3: 구현**

`ResearchConditionEvaluator.cs` (네임스페이스 `CityFlow.Gameplay.Research`):

```csharp
using System;
using System.Collections.Generic;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Gameplay.Research
{
    public enum ResearchConditionKind { DailyArrivals, Population, BuildingCount }

    // 사다리 한 칸. SO 리스트 직렬화용 plain class — 에셋 편집만으로 사다리를 바꾼다.
    [Serializable]
    public sealed class ResearchEntry
    {
        public string researchId;
        public string displayName;
        public ResearchConditionKind conditionKind;
        public int threshold;
        public TileType targetTileType;   // BuildingCount 에서만 사용
    }

    // 평가에 필요한 현재값 묶음. 서비스가 채워 넘긴다 — 판정은 이 값만 본다(순수).
    public readonly struct ResearchConditionInputs
    {
        public readonly int LastDayArrivals;
        public readonly int Population;
        public readonly Func<TileType, int> CountBuildings;

        public ResearchConditionInputs(
            int lastDayArrivals, int population, Func<TileType, int> countBuildings)
        {
            LastDayArrivals = lastDayArrivals;
            Population = population;
            CountBuildings = countBuildings;
        }
    }

    // 순수 판정 — MonoBehaviour 없음, 결정론, EditMode 가 직접 때린다.
    public static class ResearchConditionEvaluator
    {
        public static bool IsSatisfied(ResearchEntry entry, in ResearchConditionInputs inputs) =>
            CurrentValue(entry, inputs) >= Mathf.Max(0, entry.threshold);

        public static int CurrentValue(ResearchEntry entry, in ResearchConditionInputs inputs) =>
            entry.conditionKind switch
            {
                ResearchConditionKind.DailyArrivals => inputs.LastDayArrivals,
                ResearchConditionKind.Population => inputs.Population,
                ResearchConditionKind.BuildingCount =>
                    inputs.CountBuildings?.Invoke(entry.targetTileType) ?? 0,
                _ => 0,
            };
    }
}
```

`ResearchCatalogSO.cs` (네임스페이스 `CityFlow.Content` — `CompanyTypeCatalogSO` 관례):

```csharp
using System.Collections.Generic;
using CityFlow.Gameplay.Research;
using UnityEngine;

namespace CityFlow.Content
{
    // 연구 사다리 카탈로그. 항목 추가 = 에셋 한 줄(코드 0). Resources 경로로 읽어
    // 씬을 건드리지 않는다 — CompanyTypeCatalogSO 와 같은 방식.
    [CreateAssetMenu(fileName = "ResearchCatalog", menuName = "CityFlow/Research/Catalog")]
    public sealed class ResearchCatalogSO : ScriptableObject
    {
        public const string DefaultResourcePath = "CityFlow/ResearchCatalog";

        [SerializeField] private List<ResearchEntry> entries = new();

        public static ResearchCatalogSO LoadDefault() =>
            Resources.Load<ResearchCatalogSO>(DefaultResourcePath);

        // 빈 id·중복 id 는 경고하고 건너뛴다 — 에셋 실수가 조용히 묻히지 않게.
        public List<ResearchEntry> ValidEntries()
        {
            var result = new List<ResearchEntry>(entries?.Count ?? 0);
            if (entries == null) return result;

            var seen = new HashSet<string>();
            for (int i = 0; i < entries.Count; i++)
            {
                string id = entries[i]?.researchId?.Trim();
                if (entries[i] == null || string.IsNullOrEmpty(id))
                {
                    Debug.LogWarning($"[ResearchCatalogSO] {i}번 항목에 연구 id 가 없다.", this);
                    continue;
                }
                if (!seen.Add(id))
                {
                    Debug.LogWarning($"[ResearchCatalogSO] 중복된 연구 id: {id}", this);
                    continue;
                }
                result.Add(entries[i]);
            }
            return result;
        }
    }
}
```

- [ ] **Step 4: GREEN 확인** — `refresh_unity` → `read_console` → `run_tests`(group_names=[".*ResearchConditionTests.*"]) 4/4 → `run_tests`(assembly_names=["CityFlow.Sim.Tests"]) 회귀 0.
- [ ] **Step 5: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Gameplay/Research/ResearchConditionEvaluator.cs \
        Assets/01_Scripts/CityFlow/Gameplay/Research/ResearchConditionEvaluator.cs.meta \
        Assets/01_Scripts/CityFlow/Configs/Research/ResearchCatalogSO.cs \
        Assets/01_Scripts/CityFlow/Configs/Research/ResearchCatalogSO.cs.meta \
        Assets/01_Scripts/CityFlow/Configs/Research.meta \
        Assets/Tests/ViewEditMode/Editor/ResearchConditionTests.cs \
        Assets/Tests/ViewEditMode/Editor/ResearchConditionTests.cs.meta
git commit -m "[Feat] 연구 조건 판정 + 사다리 카탈로그 SO

판정은 순수 정적 함수(MonoBehaviour 없음) — 조건 3종(통행량·인구·시설 개수).
카탈로그는 Resources 경로(씬 무접촉), 빈 id·중복 id 는 경고 후 스킵."
```

---

### Task 3: 평가 루프 — ResearchUnlockService

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Gameplay/Research/ResearchUnlockService.cs`
- Test: `Assets/Tests/ViewEditMode/Editor/ResearchConditionTests.cs` (추가)

**Interfaces:**
- Consumes: Task 1 `LastDayArrivalCount` · Task 2 전부
- Produces: `internal void EvaluatePendingResearch()` — 트리거 4개가 공유하는 단일 평가 진입점. `internal ResearchConditionInputs BuildInputsForTest()` 관찰 seam. Task 6 패널이 `ResearchUnlocked`·`ResearchStateRestored` 이벤트(기존)와 카탈로그를 쓴다.

- [ ] **Step 1: 실패하는 테스트 작성** — `ResearchConditionTests`에 추가

```csharp
    [Test]
    public void EvaluatePendingResearch_UnlocksSatisfied_SkipsLocked_NeverRelocks()
    {
        var owner = new GameObject("research");
        try
        {
            var service = owner.AddComponent<ResearchUnlockService>();
            var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
            // SerializedObject 로 entries 2개 구성: pop20(인구 20)·arr60(통행량 60)
            //   — Step 1 의 Catalog 테스트와 같은 SetEntry 헬퍼 확장 사용
            ConfigureCatalog(catalog,
                ("research_pop20", ResearchConditionKind.Population, 20),
                ("research_arr60", ResearchConditionKind.DailyArrivals, 60));
            SetPrivateField(service, "catalog", catalog);

            var services = new CityFlowServices(new SimEventHub(), null, null);
            service.Initialize(services);

            var unlocked = new List<string>();
            service.ResearchUnlocked += id => unlocked.Add(id);

            // 인구만 충족하는 입력을 주입해 평가
            SetTestInputs(service, population: 25, arrivals: 10);
            service.EvaluatePendingResearch();
            CollectionAssert.AreEquivalent(new[] { "research_pop20" }, unlocked);
            Assert.IsTrue(service.IsUnlocked("research_pop20"));
            Assert.IsFalse(service.IsUnlocked("research_arr60"), "미달 조건은 잠긴 채");

            // 재평가 — 이미 열린 것은 다시 이벤트가 나가지 않는다
            service.EvaluatePendingResearch();
            Assert.AreEqual(1, unlocked.Count, "이중 발화 금지");

            // 통행량 충족 → 남은 것 해금
            SetTestInputs(service, population: 25, arrivals: 60);
            service.EvaluatePendingResearch();
            CollectionAssert.AreEquivalent(
                new[] { "research_pop20", "research_arr60" }, unlocked);
        }
        finally { Object.DestroyImmediate(owner); }
    }
```

> `SetTestInputs`: 서비스에 `internal Func<ResearchConditionInputs> inputsOverrideForTest` 필드를 두고 테스트가 주입한다(실전에서는 null → 실제 서비스에서 수집). `SetPrivateField`·`ConfigureCatalog`는 리플렉션 헬퍼 — `BuildingConstructionOverlayTests`의 `SetPrivate` 패턴을 그대로 쓴다.

- [ ] **Step 2: RED 확인** — Expected: `EvaluatePendingResearch` 미정의(CS1061).
- [ ] **Step 3: 구현** — `ResearchUnlockService`에 추가(기존 코드는 유지, `playModeTestResearchId` ContextMenu 훅도 유지):

```csharp
        [SerializeField] private ResearchCatalogSO catalog;   // 프리팹이 직렬화. 비면 Resources 폴백

        private CityFlowServices cityServices;
        internal Func<ResearchConditionInputs> inputsOverrideForTest;

        // Initialize(services) 끝부분에 추가:
        //   cityServices = services;
        //   if (catalog == null) catalog = ResearchCatalogSO.LoadDefault();
        //   services.Events.Placed += OnPlacedForResearch;
        //   if (services.Save != null) services.Save.RestoreCompleted += OnRestoreForResearch;
        //   BindCalendar(services.GameCalendar);
        //   services.GameCalendarRegistered += BindCalendar;      // 등록 지연 대비
        //   BindPopulation(services.Population);
        //   services.PopulationRegistered += BindPopulation;      // 이벤트 명칭은 CityFlowServices 를 열어 실측 — 없으면 Population 은 평가 시점 직접 조회로 대체하고 보고
        //   EvaluatePendingResearch();                            // 초기 1회
        // OnDestroy 에서 전부 해제.

        private void OnPlacedForResearch(PlacedEvent e)
        {
            if (e.IsRemove) return;
            EvaluatePendingResearch();     // 학교·병원 배치 즉시 시설 조건 반영
        }
        private void OnRestoreForResearch(RestoreCompletedEvent _) => EvaluatePendingResearch();
        private void OnDayChangedForResearch(int _) => EvaluatePendingResearch();
        private void OnPopulationChangedForResearch(int _) => EvaluatePendingResearch();

        internal void EvaluatePendingResearch()
        {
            if (!initialized || catalog == null) return;
            ResearchConditionInputs inputs = inputsOverrideForTest?.Invoke() ?? BuildInputs();
            List<ResearchEntry> entries = catalog.ValidEntries();
            for (int i = 0; i < entries.Count; i++)
            {
                ResearchEntry entry = entries[i];
                if (IsUnlocked(entry.researchId)) continue;               // §9: 다시 잠기지 않는다
                if (!ResearchConditionEvaluator.IsSatisfied(entry, inputs)) continue;
                TryUnlock(entry.researchId);                              // 기존 경로 → 이벤트·세이브 공짜
            }
        }

        private ResearchConditionInputs BuildInputs()
        {
            int arrivals = cityServices?.Stats?.LastDayArrivalCount ?? 0;
            int population = cityServices?.Population?.CurrentPopulation ?? 0;
            return new ResearchConditionInputs(arrivals, population, CountBuildings);
        }

        // 시설 개수: 평가 시점에만 그리드 전수(20×20=400칸, 앵커만 센다).
        // ponytail: 캐시 없음 — 배치·하루 경계에만 도는 스캔이라 프레임 비용이 아니다.
        private int CountBuildings(TileType type)
        {
            IReadOnlyTileData tiles = cityServices?.TileData;
            IWorldGridAccess grid = cityServices?.WorldGrid;
            if (tiles == null || grid == null) return 0;
            int count = 0;
            for (int y = 0; y < grid.WorldHeight; y++)
                for (int x = 0; x < grid.WorldWidth; x++)
                {
                    var tile = new Vector2Int(x, y);
                    if (tiles.GetTileType(tile) == type && tiles.IsFootprintAnchor(tile)) count++;
                }
            return count;
        }
```

> ⚠️ 구독 대상의 정확한 멤버명(`PopulationRegistered` 존재 여부, `GameCalendarRegistered` 시그니처)은 `CityFlowServices.cs`를 열어 실측하고, 없으면 해당 트리거를 "평가 시점 직접 조회"로 대체한 뒤 **계획과 다른 점을 보고에 기록**한다.

- [ ] **Step 4: GREEN 확인** — 이름 필터 + Sim.Tests 회귀 0.
- [ ] **Step 5: 커밋** — `[Feat] 연구 자동 해금 평가 루프 — 트리거 4개(하루·인구·배치·로드), 열린 것은 재평가 스킵`

---

### Task 4: 방문 코인 지급 배관

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Contracts/IVehicleTripService.cs` (공유 계약 — `SpecialBuildingVisitTripRequest`)
- Modify: `Assets/01_Scripts/CityFlow/Sim/TripScheduler.cs` (`SpecialTripJourney.CreateTrip`)
- Modify: `Assets/01_Scripts/CityFlow/Sim/CarSim.cs` (`HandleSpecialTripArrival`)
- Modify: `Assets/01_Scripts/CityFlow/Buildings/SpecialBuildingVisitTripSource.cs` (`ScheduleStatistics`)
- Test: `Assets/Tests/EditMode/SpecialVisitRewardTests.cs` (신규, `CityFlow.Sim.Tests`)

**Interfaces:**
- Produces: `SpecialBuildingVisitTripRequest.RewardCoins { get; }` (생성자 마지막 인자, 기본값 없음 — 호출자 전원 갱신). 방문 도착 시 `ArrivalEvent(destination, rewardCoins)` 발행.

- [ ] **Step 1: 실패하는 테스트 작성**

기존 특수건물 방문 테스트(`SpecialBuildingTests` 계열)를 먼저 읽고 **그 파일의 엔진 구성·방문 스케줄 패턴을 재사용**한다. 핵심 단정:

```csharp
        [Test]
        public void SpecialVisitArrival_PaysRewardCoins_CommuteStaysCoinPerTrip()
        {
            // 기존 방문 테스트의 도시 구성 헬퍼를 그대로 사용해 방문 트립을 1건 스케줄:
            //   RewardCoins = 7 로 요청 (통근 CoinPerTrip 10 과 다른 값 — 섞이면 즉시 드러난다)
            // 도착까지 Tick 진행 후:
            //   - 방문 도착 ArrivalEvent.Coins == 7  (정확히 1건)
            //   - 통근 도착 ArrivalEvent.Coins == 10 (기존 값 유지)
            //   - 방문 귀가(건물→집) leg 에서는 코인 이벤트가 없다
        }
```

> 위는 뼈대다 — 실제 코드는 기존 방문 테스트의 하니스를 읽고 맞춘 뒤, 단정 3개(방문=RewardCoins·통근=CoinPerTrip·귀가 leg 0코인)를 반드시 포함한다. 하니스 재사용이 불가능하면 멈추고 보고.

- [ ] **Step 2: RED 확인** — Expected: `SpecialBuildingVisitTripRequest` 생성자 인자 불일치(CS1729/CS7036) 또는 단정 실패(코인 0).
- [ ] **Step 3: 구현**

`IVehicleTripService.cs` — `SpecialBuildingVisitTripRequest`에 추가:

```csharp
        public int RewardCoins { get; }    // 최종 목적지(특수건물) 도착 시 지급. 귀가 leg 는 0
```

생성자 마지막에 `int rewardCoins` 추가, `RewardCoins = Mathf.Max(0, rewardCoins);`.

`SpecialBuildingVisitTripSource.ScheduleStatistics` — 요청 생성부에서:

```csharp
            int rewardCoins = 0;
            if (buildings != null &&
                buildings.TryGetBuildOption(statistics.BuildingId,
                    out SpecialBuildingBuildOption option))
            {
                rewardCoins = option.CoinPerVisit;
            }
            // ... new SpecialBuildingVisitTripRequest(..., scheduledHour, rewardCoins)
```

`TripScheduler.cs` `SpecialTripJourney.CreateTrip` — 하드코딩 `0` 교체:

```csharp
                // 보상은 방문 leg(0: 집→건물)에만 싣는다. 귀가는 0 — 통근 outbound 규칙과 동일.
                legIndex == 0 ? Request.RewardCoins : 0);
```

`CarSim.cs` `HandleSpecialTripArrival` — `QueueTripArrival` 직후:

```csharp
            // 방문 도착 보상. ArrivalEvent 를 타면 주간 적립·HUD·피드·퀘스트가 기존 구독으로 따라온다.
            if (completed.RewardCoins > 0)
                events.QueueArrival(new ArrivalEvent(completed.Destination, completed.RewardCoins));
```

> `TryScheduleSpecialBuildingVisit`의 다른 호출자(에디터 베이커 등)가 있으면 컴파일 에러로 드러난다 — `rewardCoins: 0`으로 갱신한다.

- [ ] **Step 4: GREEN 확인** — 게이트 3단 + Sim.Tests 회귀 0.
- [ ] **Step 5: 커밋** — `[Feat] 방문 도착 코인 지급 — 요청에 실어 도착 시 ArrivalEvent 로` (본문에: `CreateTrip rewardCoins:0` 하드코딩이 구멍이었다 · 귀가 leg 0 · 계약 파일 수정 명시)

---

### Task 5: 카탈로그 에셋 + 8종 값 채우기

**Files:**
- Create: `Assets/05_ScriptableObjects/Resources/CityFlow/ResearchCatalog.asset`
- Modify: `Assets/05_ScriptableObjects/Buildings/Building_*.asset` ×8 (**진우 소유 — PR 리뷰어 지정**)

- [ ] **Step 1: 카탈로그 에셋 생성** — `manage_scriptable_object`(create, type_name="CityFlow.Content.ResearchCatalogSO", folder_path="Assets/05_ScriptableObjects/Resources/CityFlow", asset_name="ResearchCatalog") 후 entries 8개를 patches 로 기입 (설계 §3 사다리):

| idx | researchId | displayName | conditionKind | threshold | targetTileType |
|---|---|---|---|---|---|
| 0 | research_building_coffee_shop | 커피숍 | Population | 20 | — |
| 1 | research_building_video_store | 비디오 대여점 | Population | 40 | — |
| 2 | research_building_pharmacy | 약국 | BuildingCount | 1 | School |
| 3 | research_building_petrol_station | 주유소 | DailyArrivals | 60 | — |
| 4 | research_building_auto_repair | 정비소 | DailyArrivals | 100 | — |
| 5 | research_building_cinema | 영화관 | Population | 80 | — |
| 6 | research_building_police_station | 경찰서 | BuildingCount | 1 | Hospital |
| 7 | research_building_mall | 큰 상점 | DailyArrivals | 150 | — |

> enum 직렬화 값은 생성 후 `.asset`을 열어 확인한다(`conditionKind` 인덱스·`targetTileType` 정수). 배열 patch 가 안 되면 `.asset` YAML 직접 편집도 허용(카탈로그는 우리 소유 신규 에셋).

- [ ] **Step 2: 8종 값 기입** — `manage_scriptable_object`(modify) × 8, 각각 `buildCost`·`coinPerVisit`·`visitCadence.visitsPerPeriod=1`·`visitCadence.periodDays=7`:

| 에셋 | buildCost | coinPerVisit |
|---|---|---|
| Building_CoffeeShop | 200 | 10 |
| Building_StoreCorner_Video | 250 | 10 |
| Building_StoreCorner_Drug | 300 | 10 |
| Building_PetrolStation | 500 | 10 |
| Building_AutoRepair | 600 | 10 |
| Building_Cinema | 800 | 10 |
| Building_PoliceStation | 900 | 10 |
| Building_Mall | 1200 | 10 |

- [ ] **Step 3: 검증** — `refresh_unity` → `read_console` 0건 → `run_tests`(assembly_names=["CityFlow.Sim.Tests"]) 회귀 0 → `run_tests`(group_names=[".*ResearchConditionTests.*"]) 유지. `git diff`로 8개 에셋에 **의도한 3필드만** 바뀌었는지 확인(Unity 재직렬화 잡음 커밋 금지).
- [ ] **Step 4: 커밋** — `[Feat] 연구 사다리 에셋 + 특수건물 8종 값 기입 (진우 님 소유 에셋 수정 — 리뷰 요청)` (본문에 회수 검산: 인구 40 커피숍 하루 ~5.7방문 × 10 = 57코인 → 200 회수 3.5일)

---

### Task 6: 연구 패널 프리팹

**Files:**
- Create: `Assets/01_Scripts/CityFlow/UI/Panels/ResearchPanelController.cs`
- Create: `Assets/02_Prefabs/UI/ResearchPanel.prefab`
- Test: `Assets/Tests/ViewEditMode/Editor/ResearchPanelTests.cs` (신규)

**Interfaces:**
- Consumes: `ResearchCatalogSO.ValidEntries()` · `ResearchConditionEvaluator.CurrentValue` · `IResearchUnlockService.IsUnlocked`/`ResearchUnlocked`/`ResearchStateRestored` · `LastDayArrivalCount`/`CurrentPopulation`

- [ ] **Step 1: 실패하는 테스트 작성** — `ResearchPanelTests.cs`. `BuildingConstructionOverlayTests` 패턴(리플렉션으로 private 주입, `Object.DestroyImmediate` 정리):

```csharp
    [Test]
    public void Initialize_BuildsOneRowPerEntry_WithLockState()
    {
        // 카탈로그 2칸(하나는 열림, 하나는 잠김) 구성 → controller.Initialize(services)
        // 단정: 행 2개 생성 · 열린 행은 "열림" 상태 · 잠긴 행은 이름+필요 수치 노출(숨김 금지)
    }
    [Test]
    public void ResearchUnlockedEvent_RefreshesRowState() { /* 이벤트 발화 → 행 상태 갱신 */ }
```

> 뼈대다 — 행 판별은 컨트롤러의 `internal IReadOnlyList<...> RowsForTest` seam 을 만들어 단정한다. TMP 텍스트 내용까지 단정하지 말 것(카피 변경에 취약) — 상태 enum·개수·잠금 여부만.

- [ ] **Step 2: RED 확인** — CS0246.
- [ ] **Step 3: 컨트롤러 구현** — `ICityFlowServiceConsumer`. `Initialize`에서 카탈로그 로드(직렬화 필드 → `LoadDefault` 폴백), 행 템플릿 복제로 8행 생성, `ResearchUnlocked`·`ResearchStateRestored` 구독, 계기판 3줄(`어제 도착 n` — **"어제" 라벨 필수**(§8) · `인구 n` · 목표 대비 진행). 갱신은 이벤트 + `OnEnable`. `OnDestroy` 해제. `Camera`·`Update` 의존 없음(EditMode 검증 가능).
- [ ] **Step 4: 프리팹 조립** — `manage_prefabs`/`manage_gameobject`로: 루트 `ResearchPanel`(자체 `Canvas`+`CanvasScaler`, sortingOrder 낮게) ─ `Panel` ─ `Dashboard`(TMP 3줄) ─ `RowTemplate`(비활성, 이름·진행·상태 TMP). 루트에 `ResearchPanelController` + **`ResearchUnlockService`**(씬에 없어도 프리팹 하나로 완결 — `RegisterResearch`가 중복 등록을 거부하므로 씬에 이미 있어도 안전). 직렬화 필드에 카탈로그·행 템플릿 연결. **씬에 인스턴스를 남기지 않는다** — 만들었다면 프리팹 저장 후 삭제.
- [ ] **Step 5: GREEN 확인** — 이름 필터 + Sim.Tests 회귀 0. `git status`에 `.unity` 없음 확인.
- [ ] **Step 6: 커밋** — `[Feat] 연구 패널 프리팹 — 끌어다 놓으면 배선 끝 (서비스 동봉)`

---

### Task 7: 최종 게이트

- [ ] **Step 1:** `refresh_unity`(force) → `read_console` `error CS` 0건.
- [ ] **Step 2:** `run_tests`(assembly_names=["CityFlow.Sim.Tests"]) — 기준선 + Task 1·4 신규 전부 그린.
- [ ] **Step 3:** `run_tests`(group_names=[".*ResearchConditionTests.*|.*ResearchPanelTests.*"]) — 전부 그린.
- [ ] **Step 4:** `git log --oneline develop..HEAD`로 커밋 구성 확인(태스크당 1커밋 + 문서). `git diff develop --stat`에 씬 파일 0건.
- [ ] **Step 5:** 보고서 작성 — 태스크별 결과·계획과 달랐던 점·미검증 항목(라이브 확인은 감독이 별도 수행).

## 완료 기준

- `CityFlow.Sim.Tests` 기준선 + 신규 전부 그린 · `error CS` 0 · 씬 커밋 0
- 이름 필터 스위트(`ResearchConditionTests`·`ResearchPanelTests`) 전부 그린
- 프리팹 하나(`ResearchPanel.prefab`)로 해금+패널이 완결 — 씬 배선 필요 0
- **PR 본문 필수 기재**: 공유 계약 2건 수정(`IReadOnlyCityStats`·`IVehicleTripService`) · 진우 소유 에셋 8건 수정 · 미검증 항목(라이브 육안, 방문 코인 실플레이 체감)

## 범위 밖 (하지 않을 것)

| 항목 | 사유 |
|---|---|
| 교통 도구 8종 잠금 | 2차 (설계 §7) |
| 연구비 | Q1 갱신 — 코인 관문은 buildCost 하나 |
| 스탯 업그레이드·월드그리드 확장 | Q3·Q2 유지 |
| 메인 씬 배치 | 주석 님 담당 — 프리팹까지가 우리 몫 |
| `DayArrivalCount`에서 방문 도착 제외 | 기존 동작 유지 — 통행량 조건은 총 처리량을 잰다 |
