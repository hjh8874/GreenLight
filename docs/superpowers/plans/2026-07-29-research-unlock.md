# 연구 해금 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 특수건물 8종과 교통 도구 8종을 처음부터 다 주지 않고, 플레이 성과로 목록에 나타나게 한 뒤 코인으로 사게 한다.

**Architecture:** 해금 서비스·세이브·게이트는 **이미 있다**(`IResearchUnlockService`, `ResearchSaveData`, `SpecialBuildingService.IsDefinitionUnlocked`). 비어 있는 것은 (1) 성과 조건을 판정하는 로직 (2) 항목을 정의하는 카탈로그 (3) 목록 UI 세 가지뿐이다. 조건은 **집계 수치를 새로 만들지 않고** 이미 게임이 세는 값(도착 수·차량 수·시설 개수)을 직접 읽는다.

**Tech Stack:** Unity 6000.5.2f1 · C# · NUnit EditMode (`CityFlow.Sim.Tests`)

**설계 문서:** `docs/superpowers/specs/2026-07-29-research-unlock-design.md`

## Global Constraints

- 기준 브랜치: **`develop`에서 직접 분기.** 스택 금지 — Squash 머지라 브랜치를 쌓으면 diff가 중복된다.
- **PR #164 머지 이후에 착수한다.** #164가 `BuildPanelController`·`SimEngine`·`CityQuestSystem`을 대량 수정/삭제하며, 본 계획이 `BuildPanelController`를 건드리므로 충돌이 확정적이다. 착수 전 `gh pr view 164 --json state`로 확인한다.
- 회귀 기준선: **EditMode `CityFlow.Sim.Tests` 410/410 green** (`develop 43c0d5f` 실측, 2026-07-29). #164 머지 후 숫자가 달라지므로 **착수 시점에 다시 재고 그 값을 기준선으로 삼는다.** **부분 실패 허용 없음.**
- 검증 순서 (매 태스크 끝): `refresh_unity`(compile=request, mode=force) → `read_console`(types=["error"]) → `run_tests`(EditMode, `CityFlow.Sim.Tests`).
- **무시해도 되는 콘솔 에러 2건** — 환경/도구 로그이지 코드 문제가 아니다:
  1. `Required external font asset is missing: 'Assets/99_Download/Fonts/NanumGothic SDF.asset'`
  2. `MCP-FOR-UNITY: Connection verification failed: Bridge not running`
  **판단 기준: 메시지에 `error CS`가 들어 있으면 진짜 컴파일 에러다.**
- **테스트가 돌았다는 것이 컴파일 성공을 뜻하지 않는다.** Unity는 컴파일 실패 시 직전 성공 DLL로 테스트를 돌린다. 반드시 `read_console`을 먼저 본다.
- **작업은 본 체크아웃 `/Users/hwan/Gamemaker/GreenLight`에서만.** `git worktree`·격리 사본 금지.
- **통합 씬을 커밋하지 않는다.**
- 신규 `.cs`는 **`.cs.meta`를 함께 커밋한다.**
- 커밋 메시지 접두 `[Feat]`/`[Fix]`. 커밋 전 `git status`로 코드 파일만 스테이징됐는지 확인한다.
- **한 번 열린 것은 다시 잠기지 않는다.** 조건은 **역대 최고 기록** 기준이다. 플레이어가 건물을 부숴 지표가 떨어져도 이미 열린 항목이 닫히면 안 된다 — 방치형에서 진행이 되돌아가는 것은 가장 나쁜 배신이다.

## 이미 있는 것 (새로 만들지 마라)

| 자산 | 위치 | 상태 |
|---|---|---|
| 해금 서비스 계약 | `Contracts/IResearchUnlockService.cs` | `IsUnlocked` / `TryUnlock` / `UnlockedCount` / 이벤트 2종 |
| 구현체 | `Gameplay/Research/ResearchUnlockService.cs` | **판정이 비어 있다** — `TryUnlock`이 `HashSet.Add` 후 true(`:66-80`) |
| 세이브 | `Contracts/Save/ResearchSaveData.cs` | `UnlockedResearchIds[]` · `PurchasedUpgradeIds[]` |
| 특수건물 게이트 | `Buildings/SpecialBuildingService.cs:436` `IsDefinitionUnlocked` | **이미 `research?.IsUnlocked(...)`를 호출한다.** 배선 완료 |
| 잠김 UI | `UI/Controllers/BuildSlotController.cs:141,148,153,241` | `IsUnlocked`로 버튼·문구를 이미 가른다 |
| 인구(차량 수) | `Contracts/IReadOnlyCityStats.ActiveVehicleCount` | 노출돼 있다 |
| 교통도구 배치 깔때기 | `UI/Controllers/InfrastructurePlacementCoordinator.cs:518` `CheckCanPlace(coord, data)` | `InfrastructureKind` 스위치 — **8종이 여기 한 곳을 지난다** |

**만들 것은 3개뿐이다**: 조건 판정 · 카탈로그 SO · 목록 UI.

## 설계 대비 단순화 1건

설계 §0.2는 시설 조건을 "학교·병원 **커버**"로 적었으나, 커버리지 계산은 UI 층
(`FacilityInfluenceSelectionController`)에 있고 Sim으로 옮기면 작업이 배로 는다.

**대신 "학교/병원을 N개 보유했는가"로 한다.** 플레이어에게 요구하는 것("도시를 갖춰라")은
동일하고, 타일만 세면 되므로 새 계산이 없다. 커버리지 조건이 필요해지면 나중에
조건 종류를 하나 더 추가하면 된다 — 카탈로그가 종류를 데이터로 들고 있으므로 확장이 열려 있다.

## File Structure

| 파일 | 책임 | 변경 |
|---|---|---|
| `Assets/01_Scripts/CityFlow/Contracts/ResearchCondition.cs` | 조건 종류·임계값 + 충족 판정 순수 함수 | **신규** |
| `Assets/01_Scripts/CityFlow/Contracts/IReadOnlyCityStats.cs` | 도시 통계 계약 | 수정 — 조건 소스 3종 노출 |
| `Assets/01_Scripts/CityFlow/Sim/SimEngine.cs` | 파사드 | 수정 — 조건 소스 구현 |
| `Assets/01_Scripts/CityFlow/Configs/Research/ResearchEntrySO.cs` | 연구 항목 1건 정의 | **신규** |
| `Assets/01_Scripts/CityFlow/Configs/Research/ResearchCatalogSO.cs` | 항목 카탈로그(id 조회) | **신규** |
| `Assets/01_Scripts/CityFlow/Gameplay/Research/ResearchUnlockService.cs` | 해금 상태·판정 | 수정 — 2단계 판정·최고기록 |
| `Assets/01_Scripts/CityFlow/Contracts/IResearchUnlockService.cs` | 해금 계약 | 수정 — `IsAvailable` 추가 |
| `Assets/01_Scripts/CityFlow/UI/Controllers/Placement/InfrastructurePlacementCoordinator.cs` | 교통도구 배치 | 수정 — 게이트 1곳 |
| `Assets/01_Scripts/CityFlow/UI/Panels/ResearchListPanelController.cs` | 연구 목록 UI | **신규** |
| `Assets/Tests/EditMode/ResearchConditionTests.cs` | 조건 판정 테스트 | **신규** |
| `Assets/Tests/EditMode/ResearchUnlockTests.cs` | 2단계 해금·최고기록·세이브 | **신규** |

## 단계와 PR 분할

```
PR 1   Task 1~3   조건 판정 + 카탈로그 + 2단계 해금 로직   (UI 없음, Sim/Contracts만)
PR 2   Task 4~5   교통도구 게이트 + 연구 목록 UI          ← 타 도메인, 승인 경로
PR 3   Task 6     에셋 16종 + 카탈로그
```

PR 1은 **기능이 꺼진 채** 들어간다 — 카탈로그가 비어 있으면 판정할 항목이 없어 아무것도 잠기지 않는다. 건설시간 PR과 같은 전략이다.

---

### Task 1: 조건 판정 + 조건 소스 노출

**Files:**
- Create: `Assets/01_Scripts/CityFlow/Contracts/ResearchCondition.cs`
- Modify: `Assets/01_Scripts/CityFlow/Contracts/IReadOnlyCityStats.cs`
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimEngine.cs`
- Modify: `Assets/01_Scripts/CityFlow/Fakes/FakeFlowReader.cs` (계약 구현체)
- Test: `Assets/Tests/EditMode/ResearchConditionTests.cs` (신규)

**Interfaces:**
- Produces:
  - `public enum ResearchConditionKind { Arrivals, Population, Schools, Hospitals }`
  - `public readonly struct ResearchCondition { ResearchConditionKind Kind; int Threshold; }`
  - `public static bool ResearchCondition.IsMet(in ResearchCondition c, in ResearchProgress p)`
  - `public readonly struct ResearchProgress { int BestDayArrivals; int BestPopulation; int SchoolCount; int HospitalCount; }`
  - `IReadOnlyCityStats.PeakDayArrivalCount` / `SchoolTileCount` / `HospitalTileCount`
  - Task 3이 쓴다.

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/EditMode/ResearchConditionTests.cs`:

```csharp
using CityFlow.Contracts;
using NUnit.Framework;

namespace CityFlow.Sim.Tests
{
    public class ResearchConditionTests
    {
        static ResearchProgress P(int arrivals, int pop, int schools, int hospitals) =>
            new ResearchProgress(arrivals, pop, schools, hospitals);

        [Test]
        public void IsMet_ComparesTheRightMetric()
        {
            var p = P(arrivals: 150, pop: 40, schools: 2, hospitals: 0);

            Assert.IsTrue(ResearchCondition.IsMet(
                new ResearchCondition(ResearchConditionKind.Arrivals, 100), p));
            Assert.IsFalse(ResearchCondition.IsMet(
                new ResearchCondition(ResearchConditionKind.Arrivals, 200), p));

            Assert.IsTrue(ResearchCondition.IsMet(
                new ResearchCondition(ResearchConditionKind.Population, 40), p), "임계값과 같으면 충족");
            Assert.IsFalse(ResearchCondition.IsMet(
                new ResearchCondition(ResearchConditionKind.Population, 41), p));

            Assert.IsTrue(ResearchCondition.IsMet(
                new ResearchCondition(ResearchConditionKind.Schools, 2), p));
            Assert.IsFalse(ResearchCondition.IsMet(
                new ResearchCondition(ResearchConditionKind.Hospitals, 1), p), "병원 0개");
        }

        [Test]
        public void IsMet_ZeroThreshold_IsAlwaysTrue()
        {
            var empty = P(0, 0, 0, 0);
            Assert.IsTrue(ResearchCondition.IsMet(
                new ResearchCondition(ResearchConditionKind.Arrivals, 0), empty),
                "임계값 0 = 조건 없음 = 처음부터 열림");
        }
    }
}
```

- [ ] **Step 2: 실패 확인**

`run_tests`(EditMode, `CityFlow.Sim.Tests`, test_names=`CityFlow.Sim.Tests.ResearchConditionTests`)
Expected: 컴파일 에러 — `ResearchCondition` 미정의

- [ ] **Step 3: 조건 타입 구현**

`Assets/01_Scripts/CityFlow/Contracts/ResearchCondition.cs`:

```csharp
namespace CityFlow.Contracts
{
    // 연구가 목록에 나타나기 위한 성과 조건의 종류.
    // 새 집계 수치를 만들지 않고 게임이 이미 세는 값만 쓴다.
    public enum ResearchConditionKind
    {
        Arrivals,    // 하루 도착 수 역대 최고
        Population,  // 차량 수 역대 최고 (= 인구)
        Schools,     // 보유 학교 수
        Hospitals,   // 보유 병원 수
    }

    public readonly struct ResearchCondition
    {
        public readonly ResearchConditionKind Kind;
        public readonly int Threshold;

        public ResearchCondition(ResearchConditionKind kind, int threshold)
        {
            Kind = kind;
            Threshold = threshold;
        }

        // 임계값 0 이하 = 조건 없음(처음부터 열림).
        public static bool IsMet(in ResearchCondition c, in ResearchProgress p) =>
            c.Threshold <= 0 || Value(c.Kind, p) >= c.Threshold;

        static int Value(ResearchConditionKind kind, in ResearchProgress p) =>
            kind switch
            {
                ResearchConditionKind.Arrivals   => p.BestDayArrivals,
                ResearchConditionKind.Population => p.BestPopulation,
                ResearchConditionKind.Schools    => p.SchoolCount,
                ResearchConditionKind.Hospitals  => p.HospitalCount,
                _ => 0,
            };
    }

    // 조건 판정에 필요한 현재 진행도 묶음. 순수 함수 판정을 위해 값으로 넘긴다.
    public readonly struct ResearchProgress
    {
        public readonly int BestDayArrivals;
        public readonly int BestPopulation;
        public readonly int SchoolCount;
        public readonly int HospitalCount;

        public ResearchProgress(
            int bestDayArrivals, int bestPopulation,
            int schoolCount, int hospitalCount)
        {
            BestDayArrivals = bestDayArrivals;
            BestPopulation = bestPopulation;
            SchoolCount = schoolCount;
            HospitalCount = hospitalCount;
        }
    }
}
```

- [ ] **Step 4: 조건 소스를 계약에 노출**

`IReadOnlyCityStats.cs`에 3개를 더한다. `ActiveVehicleCount`는 이미 있으므로 그대로 쓴다.

```csharp
        int RoadTileCount { get; }

        // 연구 조건 소스. 새 집계를 만들지 않고 이미 세는 값을 노출한다.
        int PeakDayArrivalCount { get; }   // 하루 도착 수 역대 최고 — 절대 내려가지 않는다
        int SchoolTileCount { get; }
        int HospitalTileCount { get; }
```

`SimEngine`에 구현한다. `SimStats.DayArrivalCount`는 현재 `internal`이고 **당일 값**이므로, 최고기록을 따로 들어야 한다.

```csharp
        private int _peakDayArrivals;
        public int PeakDayArrivalCount => _peakDayArrivals;

        // Step() 안, _stats.UpdateCarSim(...) 뒤에서 갱신한다.
        // 최고기록은 절대 내려가지 않는다 — 건물을 부숴도 이미 열린 연구가 닫히면 안 된다.
        if (_stats.DayArrivalCount > _peakDayArrivals)
            _peakDayArrivals = _stats.DayArrivalCount;
```

학교·병원 개수는 `CityGrid`를 세어 돌려준다. `RoadTileCount`가 이미 있는 방식을 따르되, 매 프레임 전수 순회를 만들지 마라 — `PlacedEvent`/`TopologyVersion`으로 캐시하거나 배치·철거 시 증감시킨다. **`CityGrid.RoadTileCount`의 구현을 먼저 읽고 같은 방식을 쓴다.**

`FakeFlowReader`에도 3개를 구현한다(전부 0 반환).

- [ ] **Step 5: 통과 확인**

`refresh_unity` → `read_console` → `run_tests`(EditMode, `CityFlow.Sim.Tests`)
Expected: 기준선 + 신규 2 PASS

- [ ] **Step 6: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Contracts/ResearchCondition.cs \
        Assets/01_Scripts/CityFlow/Contracts/ResearchCondition.cs.meta \
        Assets/01_Scripts/CityFlow/Contracts/IReadOnlyCityStats.cs \
        Assets/01_Scripts/CityFlow/Sim/SimEngine.cs \
        Assets/01_Scripts/CityFlow/Fakes/FakeFlowReader.cs \
        Assets/Tests/EditMode/ResearchConditionTests.cs \
        Assets/Tests/EditMode/ResearchConditionTests.cs.meta
git commit -m "[Feat] 연구 조건 판정 + 조건 소스 노출

집계 수치를 새로 만들지 않고 도착 수·차량 수·시설 개수를 직접 읽는다.
도착 수는 역대 최고 기록이라 절대 내려가지 않는다."
```

---

### Task 2: 연구 카탈로그 SO

**Files:**
- Create: `Assets/01_Scripts/CityFlow/Configs/Research/ResearchEntrySO.cs`
- Create: `Assets/01_Scripts/CityFlow/Configs/Research/ResearchCatalogSO.cs`
- Test: `Assets/Tests/EditMode/ResearchUnlockTests.cs` (신규)

**Interfaces:**
- Consumes: `ResearchCondition` (Task 1)
- Produces: `ResearchCatalogSO.TryGet(string, out ResearchEntrySO)` / `IReadOnlyList<ResearchEntrySO> Entries` / `int Count`

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/EditMode/ResearchUnlockTests.cs`:

```csharp
using CityFlow.Contracts;
using CityFlow.Content;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Sim.Tests
{
    public class ResearchUnlockTests
    {
        internal static ResearchEntrySO NewEntry(
            string id, int cost, ResearchConditionKind kind, int threshold)
        {
            var so = ScriptableObject.CreateInstance<ResearchEntrySO>();
            so.researchId = id;
            so.displayName = id;
            so.cost = cost;
            so.conditionKind = kind;
            so.conditionThreshold = threshold;
            return so;
        }

        [Test]
        public void Catalog_LooksUpById_AndRejectsUnknown()
        {
            var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
            catalog.SetEntriesForTest(new[] {
                NewEntry("research_building_mall", 800, ResearchConditionKind.Arrivals, 100),
                NewEntry("research_tool_signal",   200, ResearchConditionKind.Arrivals, 0),
            });

            Assert.AreEqual(2, catalog.Count);
            Assert.IsTrue(catalog.TryGet("research_building_mall", out ResearchEntrySO mall));
            Assert.AreEqual(800, mall.cost);
            Assert.AreEqual(100, mall.conditionThreshold);

            Assert.IsFalse(catalog.TryGet("nope", out _));
            Assert.IsFalse(catalog.TryGet(null, out _));
            Assert.IsFalse(catalog.TryGet("", out _));
        }
    }
}
```

- [ ] **Step 2: 실패 확인** — `ResearchEntrySO` 미정의

- [ ] **Step 3: SO 구현**

`ResearchEntrySO.cs`:

```csharp
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Content
{
    // 연구 항목 1건. 항목 추가가 에셋 편집만으로 끝나야 한다(코드 변경 0).
    [CreateAssetMenu(
        fileName = "ResearchEntry",
        menuName = "CityFlow/Content/Research Entry")]
    public sealed class ResearchEntrySO : ScriptableObject
    {
        public string researchId;      // 특수건물은 BuildingDefinitionSO.requiredResearchId 와 같은 값
        public string displayName;
        public int    cost;            // 코인

        public ResearchConditionKind conditionKind;
        public int conditionThreshold; // 0 이하 = 조건 없음(처음부터 목록에 등장)
    }
}
```

`ResearchCatalogSO.cs` — **`BuildingCatalogSO.cs`를 먼저 읽고 그 형태를 따른다**(id 인덱스, 중복·누락 경고, `OnValidate` 재색인). `SetEntriesForTest`는 테스트 주입용이다.

> ⚠️ `Configs/Research/`가 어느 어셈블리인지 먼저 확인하라. `Assembly-CSharp`이면 테스트에서
> `internal` 접근이 안 될 수 있다. 그 경우 `SetEntriesForTest`를 `public`으로 두고 주석으로
> 테스트 전용임을 밝힌다.

- [ ] **Step 4: 통과 확인** — 기준선 + 신규 3 PASS

- [ ] **Step 5: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Configs/Research/ \
        Assets/Tests/EditMode/ResearchUnlockTests.cs \
        Assets/Tests/EditMode/ResearchUnlockTests.cs.meta
git commit -m "[Feat] 연구 카탈로그 SO — 항목·비용·조건을 데이터로

항목 추가가 에셋 편집만으로 끝나게 한다."
```

---

### Task 3: 2단계 해금 판정 (성과 → 구매)

`TryUnlock`이 지금은 조건도 비용도 안 보고 무조건 성공한다(`ResearchUnlockService.cs:66-80`). 이를 **성과로 등장 → 코인으로 구매** 2단계로 만든다.

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Contracts/IResearchUnlockService.cs`
- Modify: `Assets/01_Scripts/CityFlow/Gameplay/Research/ResearchUnlockService.cs`
- Test: `Assets/Tests/EditMode/ResearchUnlockTests.cs` (추가)

**Interfaces:**
- Consumes: `ResearchCondition.IsMet` (Task 1), `ResearchCatalogSO` (Task 2)
- Produces:
  - `IResearchUnlockService.IsAvailable(string researchId)` — 성과 조건 충족(구매 전)
  - `IsUnlocked(string)` 의미는 **그대로** — 구매 완료. 기존 게이트(`SpecialBuildingService`·`BuildSlotController`)가 이 의미로 쓰고 있으므로 바꾸지 않는다.
  - `TryUnlock(string)` — 조건 미충족이거나 코인 부족이면 `false`

- [ ] **Step 1: 실패하는 테스트 작성**

먼저 `ResearchUnlockTests.cs`에 서비스 생성 헬퍼를 추가한다. `ResearchUnlockService`는
MonoBehaviour이므로 `new`가 아니라 `AddComponent`로 만들고, 카탈로그와 테스트 seam을 주입한다.

```csharp
        static ResearchUnlockService NewService(ResearchCatalogSO catalog)
        {
            var go = new GameObject("ResearchUnlockServiceForTest");
            var svc = go.AddComponent<ResearchUnlockService>();
            svc.SetCatalogForTest(catalog);
            svc.InitializeForTest();          // 기존 Registered 경로와 같은 상태로 만든다
            return svc;
        }
```

> `SetCatalogForTest`·`InitializeForTest`는 Step 3에서 만든다. `ResearchUnlockService`의 기존
> 초기화(`initialized` 플래그를 세우는 `Register` 계열)를 먼저 읽고, **그 경로를 재사용**하라 —
> 초기화 로직을 테스트용으로 복제하지 마라.

```csharp
        [Test]
        public void TryUnlock_RequiresConditionAndCoins()
        {
            var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
            catalog.SetEntriesForTest(new[] {
                NewEntry("research_building_mall", 800, ResearchConditionKind.Arrivals, 100),
            });
            var svc = NewService(catalog);          // 아래 Step 3에서 만드는 테스트 헬퍼

            // 조건 미충족 — 목록에 나타나지도 않는다
            svc.SetProgressForTest(new ResearchProgress(50, 0, 0, 0));
            svc.SetCoinsForTest(10000);
            Assert.IsFalse(svc.IsAvailable("research_building_mall"), "도착 50 < 100");
            Assert.IsFalse(svc.TryUnlock("research_building_mall"), "조건 미충족이면 구매 불가");
            Assert.IsFalse(svc.IsUnlocked("research_building_mall"));

            // 조건 충족, 코인 부족
            svc.SetProgressForTest(new ResearchProgress(120, 0, 0, 0));
            svc.SetCoinsForTest(100);
            Assert.IsTrue(svc.IsAvailable("research_building_mall"), "조건은 충족 — 목록에 나타난다");
            Assert.IsFalse(svc.TryUnlock("research_building_mall"), "코인 800 < 100 이라 구매 실패");
            Assert.IsFalse(svc.IsUnlocked("research_building_mall"));

            // 조건 충족 + 코인 충분
            svc.SetCoinsForTest(1000);
            Assert.IsTrue(svc.TryUnlock("research_building_mall"));
            Assert.IsTrue(svc.IsUnlocked("research_building_mall"));
            Assert.IsFalse(svc.TryUnlock("research_building_mall"), "중복 구매 불가");
        }

        [Test]
        public void Availability_NeverRegresses_WhenProgressDrops()
        {
            var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
            catalog.SetEntriesForTest(new[] {
                NewEntry("research_tool_roundabout", 300, ResearchConditionKind.Population, 40),
            });
            var svc = NewService(catalog);

            svc.SetProgressForTest(new ResearchProgress(0, 40, 0, 0));
            Assert.IsTrue(svc.IsAvailable("research_tool_roundabout"));

            // 진행도가 떨어져도 이미 열린 것은 닫히지 않는다.
            // (조건 소스가 '역대 최고'라 실제로는 떨어지지 않지만 계약으로 못박는다)
            svc.SetProgressForTest(new ResearchProgress(0, 10, 0, 0));
            Assert.IsTrue(svc.IsAvailable("research_tool_roundabout"),
                "한 번 열린 것은 다시 잠기지 않는다");
        }

        [Test]
        public void UnknownId_IsNeverAvailableOrUnlockable()
        {
            var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
            catalog.SetEntriesForTest(new ResearchEntrySO[0]);
            var svc = NewService(catalog);
            svc.SetProgressForTest(new ResearchProgress(9999, 9999, 9, 9));
            svc.SetCoinsForTest(99999);

            Assert.IsFalse(svc.IsAvailable("nope"));
            Assert.IsFalse(svc.TryUnlock("nope"));
        }
```

- [ ] **Step 2: 실패 확인** — `IsAvailable` / `SetProgressForTest` 미정의

- [ ] **Step 3: 계약과 구현 확장**

`IResearchUnlockService.cs`:

```csharp
        int UnlockedCount { get; }

        // 성과 조건을 충족해 목록에 나타났는가(구매 전). IsUnlocked 는 구매 완료를 뜻한다.
        bool IsAvailable(string researchId);

        event Action<string> ResearchUnlocked;
```

`ResearchUnlockService.cs` — `TryUnlock`(`:66`)을 바꾼다.

```csharp
        private readonly HashSet<string> availableResearchIds = new(StringComparer.Ordinal);

        public bool IsAvailable(string researchId)
        {
            string id = NormalizeId(researchId);
            if (id.Length == 0 || catalog == null) return false;
            if (availableResearchIds.Contains(id)) return true;      // 한 번 열리면 유지
            if (!catalog.TryGet(id, out ResearchEntrySO entry)) return false;

            var condition = new ResearchCondition(entry.conditionKind, entry.conditionThreshold);
            if (!ResearchCondition.IsMet(condition, CurrentProgress())) return false;

            availableResearchIds.Add(id);   // 래칭 — 진행도가 떨어져도 닫히지 않는다
            return true;
        }

        public bool TryUnlock(string researchId)
        {
            string id = NormalizeId(researchId);
            if (!initialized || id.Length == 0) return false;
            if (unlockedResearchIds.Contains(id)) return false;      // 중복 구매 불가
            if (!IsAvailable(id)) return false;                      // 조건 미충족
            if (!catalog.TryGet(id, out ResearchEntrySO entry)) return false;
            if (!TrySpendCoins(entry.cost)) return false;            // 코인 부족

            unlockedResearchIds.Add(id);
            Debug.Log($"[ResearchUnlockService] Unlocked {id}.", this);
            ResearchUnlocked?.Invoke(id);
            return true;
        }
```

`CurrentProgress()`는 `CityFlowServices.Stats`(= `IReadOnlyCityStats`)에서 4개 값을 읽어 `ResearchProgress`를 만든다. `TrySpendCoins`는 `Services.Economy`를 쓴다. **`EconomyService`의 실제 차감 API를 먼저 읽고 맞춘다** — `AddCoins(-cost)` 식으로 음수를 넣지 말고 지불 전용 메서드가 있으면 그것을 쓴다.

테스트 주입용 seam을 추가한다(기존 `*ForTest` 패턴과 같은 성격):

```csharp
        internal void SetProgressForTest(ResearchProgress p) { _testProgress = p; _useTestProgress = true; }
        internal void SetCoinsForTest(int coins) { _testCoins = coins; _useTestCoins = true; }
```

- [ ] **Step 4: 세이브에 availability 저장**

`ResearchSaveData`는 `UnlockedResearchIds[]`만 갖는다. **래칭된 availability도 저장해야** 로드 후 "열렸던 것이 다시 잠기는" 일이 없다.

```csharp
        public string[] UnlockedResearchIds;
        public string[] AvailableResearchIds;   // 신규 — 래칭된 등장 목록. 구세이브 = null
        public string[] PurchasedUpgradeIds;
```

`CreateSnapshot`/`RestoreSnapshot`에 배선한다. **구세이브(null)는 빈 집합으로 우아 복원**하고, 그 경우 조건 재평가로 자연 복구된다(조건 소스가 최고기록이라 세이브에 있으므로).

- [ ] **Step 5: 통과 확인** — 기준선 + 신규 6 PASS

- [ ] **Step 6: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Contracts/IResearchUnlockService.cs \
        Assets/01_Scripts/CityFlow/Contracts/Save/ResearchSaveData.cs \
        Assets/01_Scripts/CityFlow/Gameplay/Research/ResearchUnlockService.cs \
        Assets/Tests/EditMode/ResearchUnlockTests.cs
git commit -m "[Feat] 2단계 해금 판정 — 성과로 등장, 코인으로 구매

IsAvailable(성과 충족) 과 IsUnlocked(구매 완료) 를 분리한다.
등장은 래칭되어 진행도가 떨어져도 닫히지 않는다."
```

> 여기까지가 **PR 1**이다. 카탈로그가 비어 있으면 아무것도 잠기지 않으므로 단독 머지가 안전하다.

---

### Task 4: 교통 도구 게이트

교통 도구 8종은 지금 전부 열려 있다. `InfrastructurePlacementCoordinator.CheckCanPlace`(`:518`) **한 곳**이 8종의 공통 깔때기이므로 여기에만 검사를 넣는다.

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/UI/Controllers/Placement/InfrastructurePlacementCoordinator.cs`
- Modify: `InfrastructureDataSO`(경로는 파일 검색으로 확인) — `requiredResearchId` 필드 추가
- Test: `Assets/Tests/EditMode/ResearchUnlockTests.cs` (추가)

**Interfaces:**
- Consumes: `IResearchUnlockService.IsUnlocked` (Task 3)

- [ ] **Step 1: 실패하는 테스트 작성**

> ⚠️ `InfrastructurePlacementCoordinator`는 MonoBehaviour이고 UI 의존이 많다.
> **EditMode로 전체를 세우려 하지 마라.** 게이트 판정만 순수 함수로 뽑아 테스트한다:
> ```csharp
> internal static bool IsInfrastructureUnlocked(
>     string requiredResearchId, IResearchUnlockService research) =>
>     string.IsNullOrEmpty(requiredResearchId) || (research?.IsUnlocked(requiredResearchId) ?? false);
> ```
> 이 순수 함수를 테스트하고, `CheckCanPlace`는 그것을 부르기만 한다.
> 순수 함수로 뽑을 수 없으면 **억지 하니스를 만들지 말고** 리포트에 근거를 적어라.

```csharp
        [Test]
        public void InfrastructureGate_AllowsWhenNoResearchRequired_OrWhenUnlocked()
        {
            var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
            catalog.SetEntriesForTest(new[] {
                NewEntry("research_tool_roundabout", 0, ResearchConditionKind.Arrivals, 0),
            });
            var svc = NewService(catalog);
            svc.SetProgressForTest(new ResearchProgress(999, 0, 0, 0));
            svc.SetCoinsForTest(999);

            // 연구 id 가 비면 항상 허용 (기존 도구·하위호환)
            Assert.IsTrue(InfrastructurePlacementCoordinator
                .IsInfrastructureUnlocked(null, svc));
            Assert.IsTrue(InfrastructurePlacementCoordinator
                .IsInfrastructureUnlocked("", svc));

            // 연구가 필요한데 미구매 → 차단
            Assert.IsFalse(InfrastructurePlacementCoordinator
                .IsInfrastructureUnlocked("research_tool_roundabout", svc));

            // 구매 후 → 허용
            Assert.IsTrue(svc.TryUnlock("research_tool_roundabout"));
            Assert.IsTrue(InfrastructurePlacementCoordinator
                .IsInfrastructureUnlocked("research_tool_roundabout", svc));
        }
```

- [ ] **Step 2: 실패 확인** — `IsInfrastructureUnlocked` 미정의

- [ ] **Step 3: `InfrastructureDataSO`에 필드 추가**

```csharp
        [Tooltip("비우면 처음부터 사용 가능. 값이 있으면 그 연구를 구매해야 배치할 수 있다.")]
        public string requiredResearchId;
```

- [ ] **Step 4: 게이트 함수 + `CheckCanPlace` 배선**

위 순수 함수를 `InfrastructurePlacementCoordinator`에 `internal static`으로 추가하고, `CheckCanPlace`(`:518`) 맨 앞에서 부른다.

```csharp
        private bool CheckCanPlace(Vector2Int coord, InfrastructureDataSO data)
        {
            // 연구 게이트 — 8종이 이 한 곳을 지난다.
            if (!IsInfrastructureUnlocked(data?.requiredResearchId, _research)) return false;
            // ... 기존 스위치 ...
```

`_research`는 `CityFlowServices`에서 주입받는다. **`_facilityService` 등 기존 서비스가 주입되는 방식을 그대로 따른다.**

- [ ] **Step 5: 통과 확인** — 기준선 + 신규 1 PASS

- [ ] **Step 6: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/UI/Controllers/Placement/InfrastructurePlacementCoordinator.cs \
        # InfrastructureDataSO 경로 \
        Assets/Tests/EditMode/ResearchUnlockTests.cs
git commit -m "[Feat] 교통 도구 연구 게이트 — 단일 깔때기 1곳

CheckCanPlace 한 곳에서 8종을 커버한다. requiredResearchId 가 비면 기존대로 허용."
```

> ⚠️ `InfrastructurePlacementCoordinator`는 **타 도메인**이다. PR 본문에 변경 이유와 재현 절차를
> 적고 원작성자 승인을 받는다.

---

### Task 5: 연구 목록 UI

**Files:**
- Create: `Assets/01_Scripts/CityFlow/UI/Panels/ResearchListPanelController.cs`
- Create: `Assets/02_Prefabs/UI/ResearchListPanel.prefab`

> 기존 `UI/Panels/ResearchPanelController.cs`(49줄)는 **스탯 업그레이드 버튼 2개**용이고
> 본 계획의 범위(Q3에서 스탯은 제외)와 무관하다. **그 파일을 고치지 말고 새로 만든다.**

- [ ] **Step 1: 컨트롤러 구현**

`CompanyHiringGaugeOverlay.cs`(163줄)와 **같은 패턴**으로 만든다: `ICityFlowServiceConsumer` 구현, `Initialize(CityFlowServices)` 주입, 주기 갱신(0.5초). 그 파일을 먼저 읽고 구조를 그대로 따른다.

항목 한 줄이 보여줄 것:

```
[이름]            [조건 진행도]        [상태]
Mall              도착 87 / 100        잠김 — 조건 미달
Cinema            도착 120 / 100       구매 가능 (800 코인)
회전교차로         —                    보유
```

- **잠긴 것도 이름과 필요 수치를 보여준다.** 숨기면 목표가 사라진다. "저기까지 가면 저게 열린다"가 보여야 길을 더 손볼 이유가 생긴다.
- 구매 버튼은 `IsAvailable && !IsUnlocked && 코인 충분`일 때만 활성.

- [ ] **Step 2: 프리팹 생성**

`Assets/02_Prefabs/UI/ResearchListPanel.prefab`
**경로를 반드시 `02_Prefabs`로 한다.** `03_Prefabs`는 이 레포에 없다(`03_`은 `03_Art`).

통합 담당자가 **프리팹 하나를 씬에 끌어다 놓는 것만으로 동작**해야 한다(팀 기능 완성도 기준).

- [ ] **Step 3: 라이브 확인**

`Assets/00_Scenes/Debug/CityFlowIntegrated_hwan.unity`에서:
- 조건 미달 항목이 잠김으로 보이고 필요 수치가 표시되는가
- 조건 충족 시 구매 가능으로 바뀌는가
- 구매 후 건설 패널에서 실제로 놓을 수 있는가
- **프리팹을 빼도 게임이 정상 동작하는가**(표시만 없음)

> 폰트가 프로젝트에 없으면 라벨이 안 보인다. `Assets/99_Download/Fonts`에 4개 파일을 배치해야
> 한다(README 469~491줄). 없으면 라이브 확인을 못 하므로 먼저 복구한다.

- [ ] **Step 4: 커밋 — 씬 제외**

```bash
git status   # .unity 가 보이면 절대 add 하지 마라
git add Assets/01_Scripts/CityFlow/UI/Panels/ResearchListPanelController.cs \
        Assets/01_Scripts/CityFlow/UI/Panels/ResearchListPanelController.cs.meta \
        Assets/02_Prefabs/UI/ResearchListPanel.prefab \
        Assets/02_Prefabs/UI/ResearchListPanel.prefab.meta
git commit -m "[Feat] 연구 목록 패널 — 조건 진행도와 구매

잠긴 항목도 이름과 필요 수치를 보여준다. 프리팹 드롭인으로 동작."
```

---

### Task 6: 에셋 16종 + 카탈로그

**Files:**
- Create: `Assets/05_ScriptableObjects/Research/ResearchEntry_*.asset` × 16
- Create: `Assets/05_ScriptableObjects/Research/ResearchCatalog.asset`
- Modify: `Assets/05_ScriptableObjects/Buildings/Building_*.asset` (확인만 — 이미 `requiredResearchId` 기입됨)
- Modify: 교통 도구 `InfrastructureDataSO` 에셋 8개 — `requiredResearchId` 기입

- [ ] **Step 1: 특수건물 8종 — id를 그대로 쓴다**

**새로 짓지 마라.** 에셋에 이미 기입된 값이다(실측).

| 건물 에셋 | `requiredResearchId` |
|---|---|
| `Building_Mall.asset` | `research_building_mall` |
| `Building_Cinema.asset` | `research_building_cinema` |
| `Building_AutoRepair.asset` | `research_building_auto_repair` |
| `Building_CoffeeShop.asset` | `research_building_coffee_shop` |
| `Building_PoliceStation.asset` | `research_building_police_station` |
| `Building_StoreCorner_Video.asset` | `research_building_video_store` |
| `Building_PetrolStation.asset` | `research_building_petrol_station` |
| `Building_StoreCorner_Drug.asset` | `research_building_pharmacy` |

- [ ] **Step 2: 교통 도구 8종 id 부여**

`research_tool_signal` · `research_tool_roundabout` · `research_tool_overpass` · `research_tool_oneway` · `research_tool_turn_restrict` · `research_tool_priority_road` · `research_tool_highway` · `research_tool_bus_stop`

각 `InfrastructureDataSO` 에셋의 `requiredResearchId`에 기입한다.

> ⚠️ **신호등은 초반에 열어야 한다.** 길이 막히기 시작할 때 신호등이 없으면 플레이가 그대로
> 멈춘다. `research_tool_signal`의 `conditionThreshold`를 **0**(처음부터 등장)으로 두고
> 가격만 낮게 잡는다. 회전교차로가 두 번째 칸이다.

- [ ] **Step 3: `ResearchEntry` 16개 생성 + 값 기입**

조건 종류를 섞는다 — 한 지표만 쓰면 "집만 더 지으면 다 열린다"가 된다.

| 계열 | 조건 종류 | 의도 |
|---|---|---|
| 교통 도구 초반 | `Arrivals` 낮음 / 0 | 길을 뚫으면 열린다 |
| 교통 도구 후반 | `Arrivals` 높음 | 실력 게이트 |
| 상점(특수건물) 일부 | `Population` | 도시를 키워라 |
| 병원·경찰 계열 | `Hospitals` / `Schools` | 도시를 갖춰라 |

시작값을 아래로 잡는다. 라이브 튜닝 전제이고, 순서를 만드는 것이 목적이다.

| 순서 | `researchId` | 조건 | 임계값 | 비용 |
|---|---|---|---|---|
| 1 | `research_tool_signal` | Arrivals | **0** | 0 |
| 2 | `research_tool_roundabout` | Arrivals | 30 | 200 |
| 3 | `research_building_coffee_shop` | Population | 20 | 300 |
| 4 | `research_tool_oneway` | Arrivals | 60 | 300 |
| 5 | `research_building_pharmacy` | Hospitals | 1 | 400 |
| 6 | `research_tool_turn_restrict` | Arrivals | 90 | 400 |
| 7 | `research_building_video_store` | Population | 40 | 500 |
| 8 | `research_tool_priority_road` | Arrivals | 120 | 500 |
| 9 | `research_building_auto_repair` | Arrivals | 140 | 600 |
| 10 | `research_building_petrol_station` | Population | 60 | 600 |
| 11 | `research_tool_bus_stop` | Population | 70 | 700 |
| 12 | `research_building_police_station` | Schools | 2 | 700 |
| 13 | `research_tool_overpass` | Arrivals | 180 | 800 |
| 14 | `research_building_cinema` | Arrivals | 200 | 900 |
| 15 | `research_building_mall` | Population | 90 | 1200 |
| 16 | `research_tool_highway` | Arrivals | 250 | 1500 |

조건 종류가 섞여 있는 것이 핵심이다. `Arrivals`만 쓰면 "집만 더 지으면 다 열린다"가 되고,
`Population`만 쓰면 길을 고칠 이유가 없어진다. `Schools`/`Hospitals`는 도시 구성을 요구한다.

⚠️ **`conditionThreshold`는 절대값이다.** 차량 상한(`MaxSimCars`, 현재 96)이 바뀌면
`Population` 조건과 후반 `Arrivals` 조건이 도달 불가가 되거나 너무 쉬워진다.
**상한 변경 시 재검토**라는 사실을 카탈로그 에셋 옆 README나 문서에 남긴다.

- [ ] **Step 4: 카탈로그에 16개 등록**

`ResearchCatalog.asset`의 `entries`에 전부 넣고, 인스펙터에서 중복 id·빈 id 경고가 없는지 확인한다.

- [ ] **Step 5: 카탈로그 주입 배선**

`ResearchUnlockService`가 카탈로그를 어떻게 받는지 확인하고 배선한다.

> ⚠️ `[SerializeField]`로 받으면 **씬 저장이 필요**해져 씬 커밋 금지 규칙에 걸린다.
> `ResearchUnlockService`는 `SpecialBuildingSystem.prefab`에 이미 배선돼 있으므로
> **프리팹에 카탈로그를 물리면 씬을 안 건드린다.** 그 경로를 우선 검토한다.

- [ ] **Step 6: 검증**

`refresh_unity` → `read_console` → `run_tests`(EditMode, `CityFlow.Sim.Tests`)
Expected: 기준선 유지 (에셋 추가는 테스트에 영향 없음 — 테스트는 카탈로그를 코드로 주입한다)

라이브: 새 게임 시작 → 신호등만 열려 있고 나머지는 잠김 → 길을 뚫어 도착 수를 올리면 순서대로 등장

- [ ] **Step 7: 커밋**

```bash
git add Assets/05_ScriptableObjects/Research/ \
        Assets/05_ScriptableObjects/Buildings/ \
        # InfrastructureDataSO 에셋 경로
git commit -m "[Feat] 연구 항목 16종 에셋 + 카탈로그

특수건물 8종은 기존 requiredResearchId 를 그대로 쓴다.
신호등은 조건 0 — 초반에 막히면 플레이가 멈추기 때문."
```

---

## 완료 기준

- EditMode `CityFlow.Sim.Tests` **기준선 + 신규 12 green** (착수 시점 기준선을 다시 잰다)
- 컴파일 `error CS` 0
- 통합 씬 파일이 커밋에 **없음**
- 신규 `.cs`의 `.cs.meta` 전부 커밋됨
- **PR 1** = Task 1~3 (UI 없음, 카탈로그 비면 무동작) · **PR 2** = Task 4~5 (타 도메인, 승인 필요) · **PR 3** = Task 6
- PR은 **15:00~16:00에만** 제출 (팀 규칙)

## 범위 밖 (하지 않을 것)

| 항목 | 사유 |
|---|---|
| 월드그리드 확장 10단계 | 설계 Q2 — 별도 기능. #169 성능 검증과 청크 라우팅 라이브 확인이 선행 |
| 스탯 업그레이드(주행속도·쿨타임) | 설계 Q3 — 런타임 `SimConfig` 변경 + Sim 계층 진입이라 난이도가 다르다. 세이브의 `PurchasedUpgradeIds` 자리가 비어 있어 나중에 붙일 수 있다 |
| 선행조건 사슬·트리 | 설계 Q4 — 평면. 조건 종류가 이미 4가지라 그 자체가 순서를 만든다 |
| 특수건물 8종 임시 해제 | 설계 Q5 — 잠긴 채로 둔다 |
| 커버리지 기반 시설 조건 | 개수로 대체(위 「설계 대비 단순화」). 필요해지면 조건 종류를 하나 더 추가 |
| 건설 패널 카테고리 재편 | 별도 담당자 |
| 행복도 집계 수치 | 설계 §0.2 — 폐기된 안정도의 재현이라 만들지 않는다 |

## 리스크

1. **#164 충돌.** `BuildPanelController`·`SimEngine`·`CityQuestSystem`이 겹친다. #164 머지 전 착수 금지.
2. **`InfrastructurePlacementCoordinator`가 타 도메인.** Task 4는 PR + 원작성자 승인 경로를 탄다. 게이트를 **한 곳(`CheckCanPlace`)에만** 넣어 변경면을 최소화한 이유다.
3. **`conditionThreshold`가 절대값이다.** 차량 상한이 바뀌면 후반 목표가 도달 불가가 되거나 너무 쉬워진다. 상한 변경 시 재검토 항목으로 남긴다.
4. **교통 도구를 새로 잠그는 것은 기존 플레이어 경험을 바꾼다.** 지금 전부 열려 있으므로, 진행 중인 세이브에서 갑자기 도구가 잠기면 혼란스럽다. **구세이브 복원 시 이미 쓰던 도구를 어떻게 할지** 결정이 필요하다 — 기본은 "구세이브는 전부 해금됨으로 복원"이 안전하다. 이 판단을 Task 3 세이브 작업에서 명시적으로 처리한다.
5. **폰트 미복구 시 Task 5 라이브 확인 불가.** `Assets/99_Download/Fonts` 4개 파일이 선행 조건이다.
