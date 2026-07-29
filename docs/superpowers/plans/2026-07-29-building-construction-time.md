# 건물 건설시간 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 건물을 배치하면 즉시 완성되지 않고 종류별 공사시간이 지나야 기능하게 만든다.

**Architecture:** 공사 중엔 `CityGrid`에 실제 타입 대신 `TileType.UnderConstruction`을 넣고, 완성 시 `CityGrid.Promote()`로 실제 타입으로 갈아끼운 뒤 현재 `SimEngine.Place`의 후처리를 그대로 실행한다. 기능 정지가 코드가 아니라 데이터로 강제되므로 건물 타입을 보는 소비자 15개 파일 100여 곳을 건드리지 않는다. 진행은 `SimEngine.Step()`이 `_simTime` 기준으로 돌므로 오프라인 정지가 자동 성립한다.

**Tech Stack:** Unity 6000.5.2f1 · C# · NUnit EditMode (`CityFlow.Sim.Tests`)

**설계 문서:** `docs/superpowers/specs/2026-07-29-building-construction-time-design.md`

## Global Constraints

- 브랜치: `feat-building-construction-time-hwan` (develop `b4690be` 직분기). **스택 금지** — 팀 규칙상 Squash 머지라 브랜치를 쌓으면 diff가 중복된다.
- 회귀 기준선: **EditMode `CityFlow.Sim.Tests` 393/393 green** (2026-07-29 develop `b4690be` 실측). CLAUDE.md의 340은 낡은 값이다. **부분 실패 허용 없음.**
- 검증 순서 (매 태스크 끝): `mcp__unityMCP__refresh_unity`(compile=request) → `mcp__unityMCP__read_console`(types=error, 0건) → `mcp__unityMCP__run_tests`(EditMode, `CityFlow.Sim.Tests`).
- **작업은 본 체크아웃 `/Users/hwan/Gamemaker/GreenLight`에서만.** `git worktree`·격리 사본 금지 — `Library/`가 없어 전체 재임포트(10~30분)가 필요하고 Unity 에디터·unityMCP가 본 체크아웃에만 붙어 있어 컴파일 검증이 불가능하다.
- **통합 씬을 커밋하지 않는다.** `MainCityView`가 붙은 씬 7개. 라이브 확인은 `Assets/00_Scenes/Debug/CityFlowIntegrated_hwan.unity`에서만 하고 씬 diff는 커밋에서 제외한다.
- `SimConfig`에 필드를 추가하면 **`.asset` 3개를 반드시 함께 채운다** (2026-07-22 팀 규칙). 순서가 아니라 **누락**이 위험 — 누락 시 조용히 0이 들어간다.
  ```
  Assets/05_ScriptableObjects/SimConfig.asset
  Assets/05_ScriptableObjects/SimConfig_Integrated.asset
  Assets/05_ScriptableObjects/SimConfig_Sandbox.asset
  ```
- 테스트 환경 상수: `CarSimEngineTests.Cfg()`는 `DayLengthSeconds = 24f`, `TickInterval = 0.25f`를 쓴다(`CarSimEngineTests.cs:18,32`). 따라서 **테스트 안에서는 1 게임시간 = 1 시뮬초 = 4틱**이다. 게임 실행 시(`DayLengthSeconds = 120f`)와 다르므로 혼동하지 말 것.
- `SimConfig.Default()`의 공사시간은 **전부 0f**(즉시 완성)로 둔다. 0이 아니면 기존 EditMode 테스트가 대량으로 깨진다.
- 커밋 메시지 접두: `[Feat]` / `[Fix]`. 커밋 전 `git status`로 **코드 파일만** 스테이징됐는지 확인한다.

## File Structure

| 파일 | 책임 | 변경 |
|---|---|---|
| `Assets/01_Scripts/CityFlow/Contracts/CityFlowTypes.cs` | `TileType` enum | 수정 — `UnderConstruction` 추가 |
| `Assets/01_Scripts/CityFlow/Contracts/IReadOnlyTileData.cs` | 뷰용 읽기 계약 | 수정 — 진행도 조회 추가 |
| `Assets/01_Scripts/CityFlow/Contracts/Save/SimSaveData.cs` | 세이브 계약 | 수정 — `Constructions` 배열 추가 |
| `Assets/01_Scripts/CityFlow/Contracts/Save/ConstructionSaveData.cs` | 공사 사이트 DTO | **신규** |
| `Assets/01_Scripts/CityFlow/Sim/SimConfig.cs` | 튜닝값 | 수정 — 종류별 공사시간 5개 |
| `Assets/01_Scripts/CityFlow/Sim/CityGrid.cs` | 타일 격자 | 수정 — `Promote()` 추가 |
| `Assets/01_Scripts/CityFlow/Sim/ConstructionSites.cs` | 공사 사이트 보관·진행·완성 판정 | **신규** |
| `Assets/01_Scripts/CityFlow/Sim/SimEngine.cs` | 파사드·파이프라인 | 수정 — Place 분기 / Step 구동 / Remove 정리 / 세이브 |
| `Assets/Tests/EditMode/BuildingConstructionTests.cs` | 본 기능 테스트 | **신규** |

`ConstructionSites`를 `DemandMap`에 넣지 않는다 — `DemandMap`의 책임은 수요 배정이고 집·학교·병원은 그 관심사가 아니다. 채용 램프와 나란히 `Step()`에서 구동되지만 소유는 분리한다.

---

### Task 1: `TileType.UnderConstruction` 추가

새 enum 값을 넣고 기존 393 테스트가 그대로 green인지 확인한다. 이 태스크는 동작을 바꾸지 않는다 — 값만 존재하게 만들고 `switch` 소비자들이 깨지지 않는지 확인하는 것이 전부다.

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Contracts/CityFlowTypes.cs`
- Review (수정 없을 수 있음): `Assets/01_Scripts/CityFlow/Contents/BusStopRegistry.cs`, `Assets/01_Scripts/CityFlow/Debug/SimDebugOverlay.cs`, `Assets/01_Scripts/CityFlow/Debug/SimTileRenderer.cs`, `Assets/01_Scripts/CityFlow/Gameplay/Quests/CityQuestSystem.cs`, `Assets/01_Scripts/CityFlow/UI/Panels/AnalysisCardController.cs`

**Interfaces:**
- Produces: `TileType.UnderConstruction` — 이후 모든 태스크가 사용한다.

- [ ] **Step 1: enum 값 추가 (맨 뒤)**

`CityFlowTypes.cs`의 `TileType`을 아래로 바꾼다. **맨 뒤에 추가해야** 기존 값의 정수 번호가 안 변하고 기존 세이브가 그대로 읽힌다.

```csharp
    public enum TileType
    {
        Empty,
        Road,
        House,
        Office,
        School,
        Hospital,
        SpecialBuilding,
        // 공사 중. 완성 시 CityGrid.Promote()가 실제 타입으로 교체한다.
        // IsBuilding()이 true라 2x2 풋프린트 예약이 그대로 유지된다(겹침 방지 공짜).
        UnderConstruction
    }
```

- [ ] **Step 2: `switch` 5개 파일 전수 검토**

아래를 실행해 각 `switch`/`case`가 `default` 또는 그에 준하는 폴백을 갖는지 확인한다.

```bash
grep -n -A3 "switch.*TileType\|case TileType\." \
  Assets/01_Scripts/CityFlow/Contents/BusStopRegistry.cs \
  Assets/01_Scripts/CityFlow/Debug/SimDebugOverlay.cs \
  Assets/01_Scripts/CityFlow/Debug/SimTileRenderer.cs \
  Assets/01_Scripts/CityFlow/Gameplay/Quests/CityQuestSystem.cs \
  Assets/01_Scripts/CityFlow/UI/Panels/AnalysisCardController.cs
```

폴백이 없어 컴파일 경고(CS8509 등)나 누락이 생기는 곳만 `default:` 분기를 추가한다. **동작을 바꾸지 말 것** — `UnderConstruction`은 "아직 아무것도 아님"으로 취급하면 된다(기존 `Empty`와 같은 처리).

- [ ] **Step 3: 컴파일 + 회귀 확인**

`mcp__unityMCP__refresh_unity`(compile=request) → `mcp__unityMCP__read_console`(types=error)
Expected: 에러 0건

`mcp__unityMCP__run_tests`(EditMode, `CityFlow.Sim.Tests`)
Expected: **393/393 PASS** — 이 태스크는 동작 무변경이므로 숫자가 정확히 같아야 한다.

- [ ] **Step 4: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Contracts/CityFlowTypes.cs
# switch 파일을 고쳤다면 함께 add
git commit -m "[Feat] TileType.UnderConstruction 추가 — 동작 무변경

맨 뒤에 추가해 기존 값 번호를 보존(세이브 하위호환).
switch 소비자 5개 파일 전수 검토 완료. EditMode 393/393 유지."
```

---

### Task 2: `SimConfig` 공사시간 필드 + `.asset` 3개

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimConfig.cs`
- Modify: `Assets/05_ScriptableObjects/SimConfig.asset`
- Modify: `Assets/05_ScriptableObjects/SimConfig_Integrated.asset`
- Modify: `Assets/05_ScriptableObjects/SimConfig_Sandbox.asset`

**Interfaces:**
- Produces: `SimConfig.ConstructionHoursHouse` / `...Office` / `...School` / `...Hospital` / `...Special` (전부 `float`) — Task 4가 소비한다.

- [ ] **Step 1: 필드 선언 추가**

`SimConfig.cs`의 `CompanyHiringSlotsPerGameHour` 선언 근처(같은 "회사/건물" 구역)에 추가한다.

```csharp
        // ── 건물 건설시간 (게임시간) ─────────────────────
        // 배치 후 이 시간이 지나야 실제 건물이 된다. 0 이하 = 즉시 완성.
        // Default()는 전부 0 — 기존 EditMode 테스트가 "놓으면 바로 돈다"를 전제하기 때문.
        // 실제 게임 값은 SimConfig*.asset 3개에만 기입한다.
        public float ConstructionHoursHouse;      // 🔓
        public float ConstructionHoursOffice;     // 🔓
        public float ConstructionHoursSchool;     // 🔓
        public float ConstructionHoursHospital;   // 🔓
        public float ConstructionHoursSpecial;    // 🔓
```

- [ ] **Step 2: `Default()`에 0 기입**

`Default()`의 `CompanyHiringSlotsPerGameHour = 2f,` 근처에 추가한다.

```csharp
            ConstructionHoursHouse    = 0f,
            ConstructionHoursOffice   = 0f,
            ConstructionHoursSchool   = 0f,
            ConstructionHoursHospital = 0f,
            ConstructionHoursSpecial  = 0f,
```

- [ ] **Step 3: `.asset` 3개에 실제 값 기입 — 누락 금지**

각 파일에서 `CompanyHiringSlotsPerGameHour: 2` 줄을 찾아 그 아래에 아래 5줄을 추가한다. **YAML 들여쓰기(공백 2칸)를 기존 줄과 정확히 맞춘다.**

```yaml
  ConstructionHoursHouse: 2
  ConstructionHoursOffice: 4
  ConstructionHoursSchool: 6
  ConstructionHoursHospital: 8
  ConstructionHoursSpecial: 6
```

기입 후 3개 전부 들어갔는지 확인한다:

```bash
for f in Assets/05_ScriptableObjects/SimConfig.asset \
         Assets/05_ScriptableObjects/SimConfig_Integrated.asset \
         Assets/05_ScriptableObjects/SimConfig_Sandbox.asset; do
  echo -n "$(basename $f): "; grep -c "ConstructionHours" "$f"
done
```
Expected: 세 줄 모두 `5`

- [ ] **Step 4: 컴파일 + 회귀 확인**

`refresh_unity` → `read_console`(error 0) → `run_tests`
Expected: **393/393 PASS** (Default가 0이라 동작 무변경)

- [ ] **Step 5: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Sim/SimConfig.cs Assets/05_ScriptableObjects/SimConfig*.asset
git commit -m "[Feat] SimConfig 건물 건설시간 필드 5종 + .asset 3개 기입

Default()는 0(즉시 완성) — 기존 테스트 전제 보존.
실제 값(집2/회사4/학교6/병원8/특수6 게임시간)은 .asset 3개에만."
```

---

### Task 3: `CityGrid.Promote()`

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Sim/CityGrid.cs`
- Test: `Assets/Tests/EditMode/BuildingConstructionTests.cs` (신규)

**Interfaces:**
- Consumes: `TileType.UnderConstruction` (Task 1)
- Produces: `internal bool CityGrid.Promote(Vector2Int anchor, TileType targetType)` — Task 4가 호출한다. anchor가 풋프린트 앵커가 아니거나 격자 밖이면 `false`.

- [ ] **Step 1: 실패하는 테스트 작성**

새 파일 `Assets/Tests/EditMode/BuildingConstructionTests.cs`:

```csharp
using CityFlow.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Sim.Tests
{
    public class BuildingConstructionTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        // CarSimEngineTests.Cfg()와 같은 형태. DayLengthSeconds=24 → 1 게임시간 = 1 시뮬초.
        static SimConfig Cfg()
        {
            SimConfig cfg = SimConfig.Default();
            cfg.GridWidth = 8;
            cfg.GridHeight = 4;
            cfg.TickInterval = 0.25f;
            cfg.MaxStepsPerFrame = 20;
            cfg.DayLengthSeconds = 24f;
            cfg.CompanyHiringSlotsPerGameHour = 100f;
            return cfg;
        }

        [Test]
        public void Promote_ReplacesFootprintTypeAndKeepsAnchorAndDirection()
        {
            var grid = new CityGrid(8, 4);
            Assert.IsTrue(grid.Place(V(0, 0), TileType.UnderConstruction, PlacementDirection.East));

            Assert.IsTrue(grid.Promote(V(0, 0), TileType.House));

            // 2x2 풋프린트 전체가 교체된다
            Assert.AreEqual(TileType.House, grid.GetTile(V(0, 0)));
            Assert.AreEqual(TileType.House, grid.GetTile(V(1, 0)));
            Assert.AreEqual(TileType.House, grid.GetTile(V(0, 1)));
            Assert.AreEqual(TileType.House, grid.GetTile(V(1, 1)));
            // 방향과 앵커는 보존된다
            Assert.AreEqual(PlacementDirection.East, grid.GetDirection(V(1, 1)));
            Assert.IsTrue(grid.TryGetFootprintAnchor(V(1, 1), out Vector2Int anchor));
            Assert.AreEqual(V(0, 0), anchor);
        }

        [Test]
        public void Promote_ReturnsFalseForNonAnchorOrEmptyTile()
        {
            var grid = new CityGrid(8, 4);
            Assert.IsTrue(grid.Place(V(0, 0), TileType.UnderConstruction));

            Assert.IsFalse(grid.Promote(V(1, 1), TileType.House), "앵커가 아닌 타일은 거부");
            Assert.IsFalse(grid.Promote(V(5, 3), TileType.House), "빈 타일은 거부");
            Assert.IsFalse(grid.Promote(V(-1, 0), TileType.House), "격자 밖은 거부");
        }
    }
}
```

- [ ] **Step 2: 실패 확인**

`mcp__unityMCP__run_tests`(EditMode, `CityFlow.Sim.Tests`, test_names=`CityFlow.Sim.Tests.BuildingConstructionTests`)
Expected: 컴파일 에러 `'CityGrid' does not contain a definition for 'Promote'`

- [ ] **Step 3: `Promote` 구현**

`CityGrid.cs`의 `Place()` 아래에 추가한다. `Place()`는 `CanPlace()`로 점유 타일을 거부하므로 승격 전용 경로가 필요하다.

```csharp
        // 공사 완성 승격. Place()와 달리 CanPlace() 검사를 하지 않는다 —
        // 이미 UnderConstruction이 점유한 풋프린트의 타입만 제자리에서 교체하기 때문이다.
        // anchor/direction은 보존한다(재배치가 아니라 타입 변경).
        internal bool Promote(Vector2Int anchor, TileType targetType)
        {
            if (!InBounds(anchor)) return false;
            int anchorIndex = Index(anchor);
            if (_tiles[anchorIndex] == TileType.Empty) return false;
            if (_footprintAnchors[anchorIndex] != anchor) return false;   // 앵커에서만 승격

            PlacementDirection direction = _directions[anchorIndex];
            Vector2Int size = TileFootprint.GetRotatedSize(targetType, direction);
            for (int y = 0; y < size.y; y++)
            {
                for (int x = 0; x < size.x; x++)
                {
                    Vector2Int occupied = anchor + new Vector2Int(x, y);
                    if (!InBounds(occupied)) return false;
                    _tiles[Index(occupied)] = targetType;
                }
            }

            MarkDirty();
            return true;
        }
```

- [ ] **Step 4: 통과 확인**

`run_tests`(EditMode, test_names=`CityFlow.Sim.Tests.BuildingConstructionTests`)
Expected: 2/2 PASS

그 다음 전체: `run_tests`(EditMode, `CityFlow.Sim.Tests`)
Expected: **395/395 PASS** (393 + 신규 2)

- [ ] **Step 5: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Sim/CityGrid.cs Assets/Tests/EditMode/BuildingConstructionTests.cs
git commit -m "[Feat] CityGrid.Promote — 풋프린트 타입 제자리 교체

Place()는 CanPlace로 점유 타일을 거부하므로 승격 전용 경로가 필요하다.
앵커·방향은 보존하고 타입만 바꾼다."
```

---

### Task 4: 공사 상태 + Place 분기 + Step 진행/완성 (핵심)

**Files:**
- Create: `Assets/01_Scripts/CityFlow/Sim/ConstructionSites.cs`
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimEngine.cs` (`Place` L503-520, `Step` L196 부근)
- Test: `Assets/Tests/EditMode/BuildingConstructionTests.cs`

**Interfaces:**
- Consumes: `CityGrid.Promote` (Task 3), `SimConfig.ConstructionHours*` (Task 2)
- Produces:
  - `internal sealed class ConstructionSites` — `Register(Vector2Int anchor, TileType targetType, PlacementDirection direction, double completeAtSimSeconds)` / `bool Cancel(Vector2Int anchor)` / `int Count` / `IReadOnlyList<ConstructionSite> Sites` / `void Clear()`
  - `internal readonly struct ConstructionSite { Vector2Int Anchor; TileType TargetType; PlacementDirection Direction; double CompleteAtSimSeconds; }`
  - Task 5(철거)·Task 6(세이브)·Task 7(진행도)이 전부 이 타입을 쓴다.

- [ ] **Step 1: 실패하는 테스트 작성**

`BuildingConstructionTests.cs`에 추가한다.

```csharp
        [Test]
        public void Building_StaysUnderConstruction_UntilDurationElapses()
        {
            SimConfig cfg = Cfg();
            cfg.ConstructionHoursHouse = 2f;   // 1게임시간=1시뮬초 → 2초 = 8틱
            var engine = new SimEngine(cfg, new SimEventHub());

            Assert.IsTrue(engine.Place(V(0, 0), TileType.House));
            Assert.AreEqual(TileType.UnderConstruction, engine.GetTileType(V(0, 0)),
                "배치 직후는 공사 중");

            for (int i = 0; i < 7; i++) engine.Tick(0.25f);
            Assert.AreEqual(TileType.UnderConstruction, engine.GetTileType(V(0, 0)),
                "7틱(1.75초)까지는 미완");

            engine.Tick(0.25f);
            Assert.AreEqual(TileType.House, engine.GetTileType(V(0, 0)),
                "8틱(2.0초)에 완성");
        }

        [Test]
        public void ZeroConstructionHours_CompletesImmediately()
        {
            SimConfig cfg = Cfg();
            cfg.ConstructionHoursHouse = 0f;
            var engine = new SimEngine(cfg, new SimEventHub());

            Assert.IsTrue(engine.Place(V(0, 0), TileType.House));
            Assert.AreEqual(TileType.House, engine.GetTileType(V(0, 0)),
                "0 이하 = 즉시 완성 (구 config·미기입 자산 방어)");
        }

        [Test]
        public void UnderConstructionBuilding_ProducesNoCommute()
        {
            SimConfig cfg = Cfg();
            cfg.ConstructionHoursHouse = 100f;
            cfg.ConstructionHoursOffice = 100f;
            var engine = new SimEngine(cfg, new SimEventHub());
            for (int x = 0; x <= 7; x++) Assert.IsTrue(engine.Place(V(x, 2), TileType.Road));
            Assert.IsTrue(engine.Place(V(0, 0), TileType.House));
            Assert.IsTrue(engine.Place(V(4, 0), TileType.Office));
            engine.SetGameHour(7f);

            for (int i = 0; i < 12; i++) engine.Tick(0.25f);

            Assert.AreEqual(0, engine.ActiveVehicleCount,
                "공사 중 건물은 통근을 만들지 않는다 — 소비자 수정 없이 데이터로 강제됨");
        }

        [Test]
        public void UnderConstructionTile_RejectsOverlappingPlacement()
        {
            SimConfig cfg = Cfg();
            cfg.ConstructionHoursHouse = 100f;
            var engine = new SimEngine(cfg, new SimEventHub());
            Assert.IsTrue(engine.Place(V(0, 0), TileType.House));

            Assert.IsFalse(engine.Place(V(0, 0), TileType.Office), "같은 앵커 중복 배치 불가");
            Assert.IsFalse(engine.Place(V(1, 1), TileType.Office), "2x2 풋프린트 겹침도 불가");
        }

        [Test]
        public void OfficeHiringRamp_StartsAtCompletion_NotPlacement()
        {
            SimConfig cfg = Cfg();
            cfg.ConstructionHoursOffice = 2f;   // 8틱
            var engine = new SimEngine(cfg, new SimEventHub());

            Assert.IsTrue(engine.Place(V(4, 0), TileType.Office));
            Assert.IsFalse(engine.TryGetCompanyStaffing(V(4, 0), out _),
                "공사 중엔 회사로 등록되지 않는다");

            for (int i = 0; i < 8; i++) engine.Tick(0.25f);

            Assert.IsTrue(engine.TryGetCompanyStaffing(V(4, 0), out CompanyStaffing staffing),
                "완성 시각부터 회사로 등록 — 공사와 채용 램프가 직렬로 이어진다");
            Assert.AreEqual(cfg.OfficeCapacity, staffing.Capacity);
        }

        [Test]
        public void Construction_DoesNotAdvanceWithoutTicks()
        {
            SimConfig cfg = Cfg();
            cfg.ConstructionHoursHouse = 2f;   // 8틱
            var engine = new SimEngine(cfg, new SimEventHub());
            Assert.IsTrue(engine.Place(V(0, 0), TileType.House));

            for (int i = 0; i < 4; i++) engine.Tick(0.25f);   // 절반만 진행

            // Tick을 부르지 않는 동안(= 게임이 꺼진 동안) 아무리 시간이 흘러도 진행이 없다.
            // _simTime은 Step()에서만 증가하므로 오프라인 정지가 자동 성립한다.
            engine.SetGameHour(23f);   // 게임 시각만 크게 움직여도
            Assert.AreEqual(TileType.UnderConstruction, engine.GetTileType(V(0, 0)),
                "Tick 없이는 공사가 진행되지 않는다 — 오프라인 정지");

            for (int i = 0; i < 4; i++) engine.Tick(0.25f);
            Assert.AreEqual(TileType.House, engine.GetTileType(V(0, 0)),
                "틱이 재개되면 남은 만큼만 진행해 완성");
        }
```

- [ ] **Step 2: 실패 확인**

`run_tests`(EditMode, test_names=`CityFlow.Sim.Tests.BuildingConstructionTests`)
Expected: `Building_StaysUnderConstruction_UntilDurationElapses` 등이 FAIL — 배치 직후 타입이 `House`(공사 개념 없음)

- [ ] **Step 3: `ConstructionSites` 신규 파일**

```csharp
using System.Collections.Generic;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Sim
{
    internal readonly struct ConstructionSite
    {
        public readonly Vector2Int Anchor;
        public readonly TileType TargetType;
        public readonly PlacementDirection Direction;
        // StartedAt은 Task 7의 진행도 계산(경과/전체)에 필요하다. 처음부터 넣어 재작업을 피한다.
        public readonly double StartedAtSimSeconds;
        public readonly double CompleteAtSimSeconds;

        public ConstructionSite(
            Vector2Int anchor,
            TileType targetType,
            PlacementDirection direction,
            double startedAtSimSeconds,
            double completeAtSimSeconds)
        {
            Anchor = anchor;
            TargetType = targetType;
            Direction = direction;
            StartedAtSimSeconds = startedAtSimSeconds;
            CompleteAtSimSeconds = completeAtSimSeconds;
        }
    }

    // 공사 중인 건물 사이트 보관소. 진행은 시각 비교뿐이라 상태가 없다(결정론 안전).
    // DemandMap에 넣지 않는 이유: DemandMap의 책임은 수요 배정이고 집·학교·병원은 그 관심사가 아니다.
    internal sealed class ConstructionSites
    {
        private readonly List<ConstructionSite> _sites = new(16);

        public int Count => _sites.Count;
        public IReadOnlyList<ConstructionSite> Sites => _sites;

        public void Register(
            Vector2Int anchor,
            TileType targetType,
            PlacementDirection direction,
            double startedAtSimSeconds,
            double completeAtSimSeconds)
        {
            Cancel(anchor);   // 같은 앵커 중복 방지
            _sites.Add(new ConstructionSite(
                anchor, targetType, direction, startedAtSimSeconds, completeAtSimSeconds));
        }

        public bool Cancel(Vector2Int anchor)
        {
            for (int i = 0; i < _sites.Count; i++)
            {
                if (_sites[i].Anchor != anchor) continue;
                _sites.RemoveAt(i);
                return true;
            }
            return false;
        }

        public bool TryGet(Vector2Int anchor, out ConstructionSite site)
        {
            for (int i = 0; i < _sites.Count; i++)
            {
                if (_sites[i].Anchor != anchor) continue;
                site = _sites[i];
                return true;
            }
            site = default;
            return false;
        }

        // 완성된 사이트를 목록에서 빼서 반환한다. 호출자가 승격 후처리를 실행한다.
        // 역순 순회 — 제거하면서 순회하기 위함.
        public void DrainCompleted(double simSeconds, List<ConstructionSite> completed)
        {
            completed.Clear();
            for (int i = _sites.Count - 1; i >= 0; i--)
            {
                if (_sites[i].CompleteAtSimSeconds > simSeconds) continue;
                completed.Add(_sites[i]);
                _sites.RemoveAt(i);
            }
        }

        public void Clear() => _sites.Clear();
    }
}
```

- [ ] **Step 4: `SimEngine`에 필드와 헬퍼 추가**

`SimEngine`의 필드 선언부(`_demand` 근처)에 추가한다.

```csharp
        private readonly ConstructionSites _construction = new();
        private readonly List<ConstructionSite> _completedBuffer = new(16);
```

같은 클래스 안에 헬퍼를 추가한다.

```csharp
        // 게임시간 → 시뮬초. 채용 램프(CompanyCapacityCalculator)의 환산식 역산이다.
        private double ConstructionSeconds(TileType type)
        {
            float hours = type switch
            {
                TileType.House           => _config.ConstructionHoursHouse,
                TileType.Office          => _config.ConstructionHoursOffice,
                TileType.School          => _config.ConstructionHoursSchool,
                TileType.Hospital        => _config.ConstructionHoursHospital,
                TileType.SpecialBuilding => _config.ConstructionHoursSpecial,
                _ => 0f
            };
            if (hours <= 0f || _config.DayLengthSeconds <= 0f) return 0d;
            return hours * _config.DayLengthSeconds / 24d;
        }
```

- [ ] **Step 5: `Place`에 공사 분기 추가**

`SimEngine.Place`(현재 L503-520)의 `if (!_grid.Place(tile, type, direction)) return false;` **바로 다음**에 아래 블록을 넣고, 기존 후처리는 그대로 둔다.

```csharp
            if (!_grid.Place(tile, type, direction)) return false;

            // 건물은 공사부터 시작한다. 공사시간 0이면 아래 분기를 타지 않고 현행대로 즉시 완성.
            double constructionSeconds = TileFootprint.IsBuilding(type)
                ? ConstructionSeconds(type)
                : 0d;
            if (constructionSeconds > 0d)
            {
                _grid.Promote(tile, TileType.UnderConstruction);
                _construction.Register(
                    tile, type, direction, _simTime, _simTime + constructionSeconds);
                _events.QueuePlaced(
                    new PlacedEvent(tile, TileType.UnderConstruction, isRemove: false, direction));
                return true;
            }

            if (type == TileType.Office)
                _demand.RegisterCompany(tile, type, _simTime);
            // ... 기존 후처리 그대로 ...
```

> `_grid.Place(tile, type, ...)`로 먼저 놓고 곧바로 `Promote(tile, UnderConstruction)`로 바꾸는 이유: `CanPlace`가 **목표 타입 기준**으로 겹침·풋프린트를 검사해야 하기 때문이다. `UnderConstruction`으로 바로 놓으면 크기가 같아 지금은 문제가 없지만, 타입별 풋프린트가 갈라지는 순간 조용히 어긋난다.

- [ ] **Step 6: 완성 처리 메서드 추가**

`SimEngine`에 추가한다. 후처리는 `Place`의 것과 **동일해야** 한다.

```csharp
        // 완성 = 현재 Place 후처리의 발화 시점을 뒤로 민 것. 새 인과관계를 만들지 않는다.
        private void AdvanceConstruction()
        {
            if (_construction.Count == 0) return;
            _construction.DrainCompleted(_simTime, _completedBuffer);
            for (int i = 0; i < _completedBuffer.Count; i++)
            {
                ConstructionSite site = _completedBuffer[i];
                if (!_grid.Promote(site.Anchor, site.TargetType)) continue;   // 철거된 사이트 방어

                if (site.TargetType == TileType.Office)
                    _demand.RegisterCompany(site.Anchor, site.TargetType, _simTime);
                if (site.TargetType == TileType.Office || site.TargetType == TileType.School)
                    _demandRebalancePending = true;
                _buildingAssignmentChangePending = true;
                _events.QueuePlaced(
                    new PlacedEvent(site.Anchor, site.TargetType, isRemove: false, site.Direction));
            }
        }
```

- [ ] **Step 7: `Step()`에서 구동**

`SimEngine.Step()`의 `_simTime += _config.TickInterval;` **바로 다음**, `_demand.AdvanceCompanyCapacities` **앞**에 한 줄을 넣는다. 공사가 먼저 완성돼야 같은 틱에 채용 램프가 그 회사를 볼 수 있다.

```csharp
            _simTime += _config.TickInterval;

            AdvanceConstruction();   // 공사 완성 → 승격. 채용 램프보다 먼저.

            if (_demand.AdvanceCompanyCapacities(
```

- [ ] **Step 8: 통과 확인**

`refresh_unity` → `read_console`(error 0) → `run_tests`(EditMode, `CityFlow.Sim.Tests`)
Expected: **401/401 PASS** (393 + Task 3의 2 + 본 태스크 6)

- [ ] **Step 9: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Sim/ConstructionSites.cs \
        Assets/01_Scripts/CityFlow/Sim/SimEngine.cs \
        Assets/Tests/EditMode/BuildingConstructionTests.cs
git commit -m "[Feat] 건물 건설시간 — 공사 상태와 완성 승격

배치 시 UnderConstruction으로 두고 Step()에서 _simTime 기준으로 완성 판정.
완성 후처리는 Place의 것과 동일 — 새 인과관계 없이 발화 시점만 뒤로 민다.
_simTime은 Step에서만 증가하므로 오프라인 정지가 자동 성립."
```

---

### Task 5: 철거 시 사이트 정리

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimEngine.cs` (`Remove` L540 부근)
- Test: `Assets/Tests/EditMode/BuildingConstructionTests.cs`

**Interfaces:**
- Consumes: `ConstructionSites.Cancel(Vector2Int)` (Task 4)

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
        [Test]
        public void RemovingUnderConstruction_ClearsSiteAndDoesNotResurrect()
        {
            SimConfig cfg = Cfg();
            cfg.ConstructionHoursHouse = 2f;   // 8틱
            var engine = new SimEngine(cfg, new SimEventHub());
            Assert.IsTrue(engine.Place(V(0, 0), TileType.House));

            Assert.IsTrue(engine.Remove(V(0, 0)));
            Assert.AreEqual(TileType.Empty, engine.GetTileType(V(0, 0)));

            for (int i = 0; i < 20; i++) engine.Tick(0.25f);

            Assert.AreEqual(TileType.Empty, engine.GetTileType(V(0, 0)),
                "철거된 사이트는 완성 시각이 지나도 되살아나지 않는다");
        }
```

- [ ] **Step 2: 실패 확인**

`run_tests`(test_names=`CityFlow.Sim.Tests.BuildingConstructionTests.RemovingUnderConstruction_ClearsSiteAndDoesNotResurrect`)
Expected: FAIL — 20틱 뒤 타일이 `House`로 되살아남

- [ ] **Step 3: `Remove`에 정리 추가**

`SimEngine.Remove`의 `if (!_grid.TryRemove(tile, out var removed, out Vector2Int anchor)) return false;` **바로 다음**에 한 줄을 넣는다.

```csharp
            if (!_grid.TryRemove(tile, out var removed, out Vector2Int anchor)) return false;
            _construction.Cancel(anchor);   // 공사 중 철거 — 사이트 제거(환불은 UI 층 기존 경로)
            if (removed == TileType.Office)
```

> 환불은 Sim이 관여하지 않는다. 환불은 UI 층(`InfrastructurePlacementCoordinator.cs:396,442,502`)이 처리하며 Sim은 경제를 모른다. Sim이 경제에 개입하면 계층 경계가 깨진다.

- [ ] **Step 4: 통과 확인**

`run_tests`(EditMode, `CityFlow.Sim.Tests`)
Expected: **402/402 PASS**

- [ ] **Step 5: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Sim/SimEngine.cs Assets/Tests/EditMode/BuildingConstructionTests.cs
git commit -m "[Fix] 공사 중 철거 시 사이트 정리 — 유령 완성 방지

환불은 Sim이 관여하지 않는다(UI 층 기존 경로)."
```

---

### Task 6: 세이브 왕복

**Files:**
- Create: `Assets/01_Scripts/CityFlow/Contracts/Save/ConstructionSaveData.cs`
- Modify: `Assets/01_Scripts/CityFlow/Contracts/Save/SimSaveData.cs`
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimEngine.cs` (`CreateSnapshot` L1078·L1148, `RestoreSnapshot` L1171)
- Test: `Assets/Tests/EditMode/BuildingConstructionTests.cs`

**Interfaces:**
- Consumes: `ConstructionSites.Sites` / `Register` / `Clear` (Task 4)
- Produces: `SimSaveData.Constructions` (`ConstructionSaveData[]`)

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
        [Test]
        public void Construction_SurvivesSaveRoundTrip_WithRemainingTime()
        {
            SimConfig cfg = Cfg();
            cfg.ConstructionHoursHouse = 4f;   // 4초 = 16틱
            var engine = new SimEngine(cfg, new SimEventHub());
            Assert.IsTrue(engine.Place(V(0, 0), TileType.House));
            for (int i = 0; i < 8; i++) engine.Tick(0.25f);   // 절반(2초) 진행

            CityFlow.Contracts.Save.SimSaveData snap = engine.CreateSnapshot();
            Assert.AreEqual(1, snap.Constructions.Length);
            Assert.AreEqual(2f, snap.Constructions[0].RemainingSimSeconds, 0.01f,
                "절대 완료시각이 아니라 잔여시간으로 저장한다");

            var restored = new SimEngine(cfg, new SimEventHub());
            restored.RestoreSnapshot(snap);
            Assert.AreEqual(TileType.UnderConstruction, restored.GetTileType(V(0, 0)));

            for (int i = 0; i < 7; i++) restored.Tick(0.25f);
            Assert.AreEqual(TileType.UnderConstruction, restored.GetTileType(V(0, 0)),
                "잔여 2초 중 1.75초 경과 — 아직 미완");

            restored.Tick(0.25f);
            Assert.AreEqual(TileType.House, restored.GetTileType(V(0, 0)),
                "잔여시간을 이어받아 완성");
        }

        [Test]
        public void LegacySave_WithoutConstructions_RestoresWithoutError()
        {
            SimConfig cfg = Cfg();
            var engine = new SimEngine(cfg, new SimEventHub());
            Assert.IsTrue(engine.Place(V(0, 0), TileType.House));   // 공사시간 0 = 즉시 완성

            CityFlow.Contracts.Save.SimSaveData snap = engine.CreateSnapshot();
            snap.Constructions = null;   // 구세이브 모사

            var restored = new SimEngine(cfg, new SimEventHub());
            Assert.DoesNotThrow(() => restored.RestoreSnapshot(snap));
            Assert.AreEqual(TileType.House, restored.GetTileType(V(0, 0)));
        }
```

- [ ] **Step 2: 실패 확인**

`run_tests`(test_names=`CityFlow.Sim.Tests.BuildingConstructionTests`)
Expected: 컴파일 에러 `'SimSaveData' does not contain a definition for 'Constructions'`

- [ ] **Step 3: DTO 신규 파일**

`Assets/01_Scripts/CityFlow/Contracts/Save/ConstructionSaveData.cs`:

```csharp
using System;

namespace CityFlow.Contracts.Save
{
    // 공사 중인 건물 1건. 절대 완료시각이 아니라 잔여시간으로 저장한다 —
    // _simTime은 로드 시 리셋되지 않으므로 절대시각으로 저장하면 즉시 완성되거나 영원히 안 끝난다.
    [Serializable]
    public sealed class ConstructionSaveData
    {
        public int X;
        public int Y;
        public TileType TargetType;
        public PlacementDirection Direction;
        public float RemainingSimSeconds;
    }
}
```

- [ ] **Step 4: `SimSaveData`에 배열 추가**

`SimSaveData.cs`의 `BusStops` 아래에 추가한다.

```csharp
        public BusStopSaveData[] BusStops;
        public ConstructionSaveData[] Constructions;   // 구세이브 = null(공사 0건) — 마이그레이션 공짜
```

- [ ] **Step 5: `CreateSnapshot`에 기록**

`CreateSnapshot`의 `return new SimSaveData` 바로 앞에 배열을 만든다.

```csharp
            var constructions = new ConstructionSaveData[_construction.Count];
            for (int i = 0; i < _construction.Sites.Count; i++)
            {
                ConstructionSite site = _construction.Sites[i];
                constructions[i] = new ConstructionSaveData
                {
                    X = site.Anchor.x,
                    Y = site.Anchor.y,
                    TargetType = site.TargetType,
                    Direction = site.Direction,
                    RemainingSimSeconds =
                        (float)System.Math.Max(0d, site.CompleteAtSimSeconds - _simTime),
                };
            }
```

`return new SimSaveData { ... }` 안의 `BusStops = busStops,` 아래에 추가한다.

```csharp
                BusStops = busStops,
                Constructions = constructions,
```

- [ ] **Step 6: `RestoreSnapshot`에 복원**

`RestoreSnapshot`의 초기화 블록에서 `_carSim.ClearPopulation();` 아래에 추가한다.

```csharp
            _carSim.ClearPopulation();
            _construction.Clear();
```

그리고 `PlacedTiles` 복원 루프 **바로 다음**에 사이트를 재등록한다. 타일은 `UnderConstruction` 타입으로 이미 복원돼 있다.

```csharp
            // 참고: PlacedEvent는 안 쏨 — 복원은 '건설'이 아니고, 뷰는 폴링이라 다음 프레임 자동 갱신.

            // 공사 사이트 복원. 구세이브(null)는 공사 0건으로 우아 복원.
            if (snapshot.Constructions != null)
                foreach (var c in snapshot.Constructions)
                {
                    var anchor = RestoreTile(c.X, c.Y, restoreOffset);
                    if (_grid.GetTile(anchor) != TileType.UnderConstruction) continue;   // 불일치 방어
                    double remaining = System.Math.Max(0f, c.RemainingSimSeconds);
                    double total = ConstructionSeconds(c.TargetType);
                    // 이미 지난 만큼(total - remaining)을 뒤로 물려 진행도(Task 7)가 이어지게 한다.
                    double started = _simTime - System.Math.Max(0d, total - remaining);
                    _construction.Register(
                        anchor, c.TargetType, c.Direction, started, _simTime + remaining);
                }
```

- [ ] **Step 7: 통과 확인**

`refresh_unity` → `read_console`(error 0) → `run_tests`(EditMode, `CityFlow.Sim.Tests`)
Expected: **404/404 PASS**

- [ ] **Step 8: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Contracts/Save/ConstructionSaveData.cs \
        Assets/01_Scripts/CityFlow/Contracts/Save/ConstructionSaveData.cs.meta \
        Assets/01_Scripts/CityFlow/Contracts/Save/SimSaveData.cs \
        Assets/01_Scripts/CityFlow/Sim/SimEngine.cs \
        Assets/Tests/EditMode/BuildingConstructionTests.cs
git commit -m "[Feat] 공사 진행도 세이브 왕복

잔여시간(상대값)으로 저장 — 절대 완료시각은 로드 후 _simTime 기준이 달라져 깨진다.
구세이브 = null → 공사 0건. 기존 Roundabouts/Oneways와 같은 마이그레이션 공짜 패턴."
```

> `.cs.meta`를 반드시 함께 커밋한다. 누락하면 각 머신이 다른 GUID를 만들어 다른 사람 환경에서 참조가 깨진다(PR #165가 이 실수로 블로커를 받았다).

---

### Task 7: 뷰용 진행도 계약

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Contracts/IReadOnlyTileData.cs`
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimEngine.cs`
- Modify: `Assets/01_Scripts/CityFlow/Fakes/FakeFlowReader.cs` (인터페이스 구현체)
- Test: `Assets/Tests/EditMode/BuildingConstructionTests.cs`

**Interfaces:**
- Produces: `bool IReadOnlyTileData.TryGetConstructionProgress01(Vector2Int tile, out float progress01)` — Task 8(View)이 소비한다. 풋프린트 어느 타일로 물어도 앵커로 환산해 답한다. 공사 중이 아니면 `false`.

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
        [Test]
        public void ConstructionProgress_ReportsFraction_AndFalseWhenNotUnderConstruction()
        {
            SimConfig cfg = Cfg();
            cfg.ConstructionHoursHouse = 4f;   // 4초 = 16틱
            var engine = new SimEngine(cfg, new SimEventHub());
            Assert.IsTrue(engine.Place(V(0, 0), TileType.House));

            Assert.IsTrue(engine.TryGetConstructionProgress01(V(0, 0), out float start));
            Assert.AreEqual(0f, start, 0.01f);

            for (int i = 0; i < 8; i++) engine.Tick(0.25f);
            Assert.IsTrue(engine.TryGetConstructionProgress01(V(0, 0), out float half));
            Assert.AreEqual(0.5f, half, 0.01f);

            // 풋프린트 비앵커 타일로 물어도 같은 값
            Assert.IsTrue(engine.TryGetConstructionProgress01(V(1, 1), out float halfAtNonAnchor));
            Assert.AreEqual(0.5f, halfAtNonAnchor, 0.01f);

            for (int i = 0; i < 8; i++) engine.Tick(0.25f);
            Assert.IsFalse(engine.TryGetConstructionProgress01(V(0, 0), out _),
                "완성 후엔 false");
            Assert.IsFalse(engine.TryGetConstructionProgress01(V(6, 3), out _),
                "빈 타일도 false");
        }
```

- [ ] **Step 2: 실패 확인**

Expected: 컴파일 에러 `'SimEngine' does not contain a definition for 'TryGetConstructionProgress01'`

- [ ] **Step 3: 계약에 추가**

`IReadOnlyTileData.cs`의 `IsFootprintAnchor` 아래에 추가한다.

```csharp
        bool IsFootprintAnchor(Vector2Int tile);

        // 공사 진행도 0..1. 공사 중이 아니면 false.
        // 풋프린트 어느 타일로 물어도 앵커로 환산해 답한다(뷰가 앵커를 몰라도 되게).
        bool TryGetConstructionProgress01(Vector2Int tile, out float progress01);
```

- [ ] **Step 4: `SimEngine`에 구현**

```csharp
        public bool TryGetConstructionProgress01(Vector2Int tile, out float progress01)
        {
            progress01 = 0f;
            if (!_grid.TryGetFootprintAnchor(tile, out Vector2Int anchor)) return false;
            if (!_construction.TryGet(anchor, out ConstructionSite site)) return false;

            double total = site.CompleteAtSimSeconds - site.StartedAtSimSeconds;
            if (total <= 0d) { progress01 = 1f; return true; }

            double elapsed = _simTime - site.StartedAtSimSeconds;
            progress01 = Mathf.Clamp01((float)(elapsed / total));
            return true;
        }
```

`ConstructionSite.StartedAtSimSeconds`는 Task 4에서 이미 정의했으므로 struct 변경은 없다.

다만 **Task 6의 `RestoreSnapshot` 재등록은 시작 시각을 역산하도록 고쳐야 한다.** 세이브에는
잔여시간만 있기 때문이다. Task 6에서 쓴 `Register` 호출을 아래로 바꾼다.

```csharp
                    double remaining = System.Math.Max(0f, c.RemainingSimSeconds);
                    double total = ConstructionSeconds(c.TargetType);
                    // 이미 지난 만큼(total - remaining)을 뒤로 물려 진행도가 이어지게 한다.
                    double started = _simTime - System.Math.Max(0d, total - remaining);
                    _construction.Register(
                        anchor, c.TargetType, c.Direction, started, _simTime + remaining);
```

- [ ] **Step 5: `FakeFlowReader`에 구현 추가**

`IReadOnlyTileData`를 구현하는 다른 클래스가 있으면 전부 고쳐야 컴파일된다. 아래로 확인한다.

```bash
grep -rln "IReadOnlyTileData" Assets/01_Scripts --include='*.cs'
```

각 구현체에 아래를 추가한다(Fake는 공사 개념이 없다).

```csharp
        public bool TryGetConstructionProgress01(Vector2Int tile, out float progress01)
        {
            progress01 = 0f;
            return false;
        }
```

- [ ] **Step 6: 통과 확인**

`refresh_unity` → `read_console`(error 0) → `run_tests`(EditMode, `CityFlow.Sim.Tests`)
Expected: **405/405 PASS**

- [ ] **Step 7: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Contracts/IReadOnlyTileData.cs \
        Assets/01_Scripts/CityFlow/Sim/ConstructionSites.cs \
        Assets/01_Scripts/CityFlow/Sim/SimEngine.cs \
        Assets/01_Scripts/CityFlow/Fakes/FakeFlowReader.cs \
        Assets/Tests/EditMode/BuildingConstructionTests.cs
git commit -m "[Feat] 공사 진행도 조회 계약 — 뷰용 읽기 전용

풋프린트 어느 타일로 물어도 앵커로 환산해 답한다."
```

---

### Task 8: 공사 비주얼 + 프리팹 (별도 PR)

> **이 태스크는 `MainCityView`의 타일 비주얼을 건드리므로 이진우 소유 구역이다**
> (`docs/2026-07-21-parallel-work-ownership.md`). Task 1~7과 **별도 PR**로 올리고
> 원작성자 승인을 받는다. Task 1~7만으로도 기능은 완결이며(공사 중엔 건물이 안 보임),
> 본 태스크는 연출을 더한다.

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/View/MainCityView.cs` (타일 비주얼 생성부)
- Create: `Assets/02_Prefabs/UI/BuildingConstructionSystem.prefab`
- Create: `Assets/01_Scripts/CityFlow/UI/Controllers/BuildingConstructionOverlay.cs`

**Interfaces:**
- Consumes: `IReadOnlyTileData.TryGetConstructionProgress01` (Task 7), `TileType.UnderConstruction` (Task 1)

- [ ] **Step 1: 타일 비주얼에 `UnderConstruction` 분기 추가**

`MainCityView`가 타일 타입별 비주얼을 만드는 지점을 찾는다.

```bash
grep -n "CreateTileVisual\|case TileType.House" Assets/01_Scripts/CityFlow/View/MainCityView.cs | head
```

`UnderConstruction`에 공사 비주얼(골조/펜스 프리팹 또는 임시로 회색 박스)을 배정한다. **에셋이 없으면 임시 프리미티브로 시작하고 에셋 교체는 후속으로 남긴다** — 이 태스크의 목적은 "공사 중임이 화면에 보인다"까지다.

- [ ] **Step 2: 진행도 오버레이 스크립트**

`BuildingConstructionOverlay.cs` — `CompanyHiringGaugeOverlay.cs`(163줄)와 **같은 패턴**으로 만든다: `ICityFlowServiceConsumer` 구현, `Initialize(CityFlowServices)` 주입, 주기 전수 스캔(0.5초), 월드 라벨. 그 파일을 먼저 읽고 구조를 그대로 따를 것.

핵심 루프:

```csharp
if (!_services.TileData.TryGetConstructionProgress01(tile, out float progress)) continue;
label.text = $"{Mathf.RoundToInt(progress * 100f)}%";
```

- [ ] **Step 3: 프리팹 생성**

`Assets/02_Prefabs/UI/BuildingConstructionSystem.prefab` — `BuildingConstructionOverlay`가 붙은 루트 + 라벨 템플릿.

**경로를 반드시 `02_Prefabs`로 할 것.** `03_Prefabs`는 이 레포에 없다(`03_`은 `03_Art`). PR #165가 이 실수로 블로커를 받았다.

- [ ] **Step 4: 라이브 확인**

`Assets/00_Scenes/Debug/CityFlowIntegrated_hwan.unity`를 열고 프리팹을 드래그해 넣는다.
확인 항목:
- 건물을 놓으면 공사 비주얼이 뜨고 진행도 %가 올라간다
- 완성되면 실제 건물로 바뀌고 오버레이가 사라진다
- 공사 중 철거하면 즉시 사라진다
- **프리팹을 빼도 게임은 정상 동작한다**(표시만 없음) — 배선 실수에 안전한지 확인

- [ ] **Step 5: 커밋 — 씬 제외**

```bash
git status   # 씬 파일이 목록에 있으면 절대 add 하지 말 것
git add Assets/01_Scripts/CityFlow/View/MainCityView.cs \
        Assets/01_Scripts/CityFlow/UI/Controllers/BuildingConstructionOverlay.cs \
        Assets/01_Scripts/CityFlow/UI/Controllers/BuildingConstructionOverlay.cs.meta \
        Assets/02_Prefabs/UI/BuildingConstructionSystem.prefab \
        Assets/02_Prefabs/UI/BuildingConstructionSystem.prefab.meta
git commit -m "[Feat] 공사 비주얼 + 진행도 오버레이 프리팹

MainCityView 타일 비주얼 수정 포함 — 이진우 소유 구역이라 원작성자 승인 필요.
프리팹은 표시 전담이라 씬에 없어도 시뮬은 정상 동작한다."
```

---

## 완료 기준

- EditMode `CityFlow.Sim.Tests` **405/405 green** (393 기준선 + 신규 12)
- 컴파일 에러 0
- 통합 씬 파일이 커밋에 **없음**
- `.asset` 3개에 `ConstructionHours*` 5개씩 전부 기입됨
- 신규 `.cs` 파일의 `.cs.meta`가 전부 커밋됨
- Task 1~7 = PR 1개 (Sim/Contracts), Task 8 = PR 1개 (View, 이진우 승인)
- PR은 **15:00~16:00에만** 제출 (팀 규칙)
