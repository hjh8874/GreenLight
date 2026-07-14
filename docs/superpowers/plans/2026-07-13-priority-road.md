# 우선도로(도로 우선권) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 무신호 교차로의 대칭 간섭 λ를 비대칭으로 확장한 6번째 배치물 "우선도로"를 추가한다 — 메인축은 무정차(λ≈0), 곁길은 양보(λ↑).

**Architecture:** 우선도로 = 로터리(교차로 전용·좌표 HashSet·TopologyDirty 없음·세이브 골격) + 일방통행(방향값을 Dictionary로 저장) 조합. 엔진 효과는 FlowSolver의 무신호 간섭 분기에 `else if` 하나 추가. 축 값은 신규 `Axis` enum.

**Tech Stack:** Unity C#, EditMode 테스트(NUnit), rate 기반 SimEngine.

## Global Constraints

- **결정론 유지**: 같은 입력 → 같은 해시. 순회·계수 결정론적. (스펙 §3)
- **무료 철거**: 우선도로 철거는 코인 소모 없음. (프로젝트 기조)
- **AutoDetectSignals 게이트**: 모든 Can/TryPlace/TryRemove는 `!_config.AutoDetectSignals`일 때만 동작 (현행 라이브 미노출, 신호 구매 피벗 전환일에 활성). (스펙 §9)
- **4자 배타**: 우선도로는 신호·로터리·입체와 한 교차로에 공존 불가. (스펙 §4)
- **TopologyDirty 안 함**: 우선도로는 경로가 아니라 간섭 계수만 바꿈 → 로터리처럼 `MarkTopologyDirty()` 호출 안 함(재계획 불요). (스펙 §4)
- **λ 값 잠정**: `PriorityMainInterference=0.1f`, `PriorityYieldInterference=2.5f` — 진우 밸런스 튜닝 대상. (스펙 §3)
- **파일 규약**: `SimConfig`는 `struct` + `[System.Serializable]`, const 금지.

---

### Task 1: Axis enum + 엔진 배치 API + 계약

**Files:**
- Create: `Assets/01_Scripts/CityFlow/Contracts/Axis.cs`
- Modify: `Assets/01_Scripts/CityFlow/Contracts/IIntersectionFacilityService.cs` (로터리 블록 뒤에 추가)
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimEngine.cs` (필드 L24-25 근처, 로터리 CanPlace L304, 입체 CanPlace, 메서드 L302-325 뒤)
- Test: `Assets/Tests/EditMode/PriorityRoadTests.cs`

**Interfaces:**
- Produces: `enum Axis { Horizontal, Vertical }`; `SimEngine.PriorityRoadTiles : IReadOnlyList<Vector2Int>`; `SimEngine.GetPriorityAxis(Vector2Int) : Axis`; `CanPlacePriorityRoad(Vector2Int) : bool`; `TryPlacePriorityRoad(Vector2Int, Axis) : bool`; `TryRemovePriorityRoad(Vector2Int) : bool`.

- [ ] **Step 1: Axis enum 생성**

Create `Assets/01_Scripts/CityFlow/Contracts/Axis.cs`:
```csharp
namespace CityFlow.Contracts
{
    // 우선도로의 우선축(스펙 2026-07-13). 양방향 통행, 축만 우선(일방통행의 방향과 달리 부호 없음).
    public enum Axis
    {
        Horizontal,
        Vertical
    }
}
```

- [ ] **Step 2: 계약에 우선도로 메서드 추가**

In `IIntersectionFacilityService.cs`, 입체교차 블록(L30-33) 뒤에 추가:
```csharp
        // 우선도로 배치(스펙 2026-07-13): 무신호 교차로에 우선축 지정 — 메인축 무정차·곁길 양보.
        // 신호·로터리·입체와 4자 배타(한 교차로 한 장치). 축 값(H/V) 보유 — 로터리와 달리 방향성 있음.
        IReadOnlyList<Vector2Int> PriorityRoadTiles { get; }
        Axis GetPriorityAxis(Vector2Int tile);
        bool CanPlacePriorityRoad(Vector2Int tile);
        bool TryPlacePriorityRoad(Vector2Int tile, Axis mainAxis);
        bool TryRemovePriorityRoad(Vector2Int tile);
```
파일 상단 `using`에 `CityFlow.Contracts`가 자기 네임스페이스라 `Axis`는 그대로 참조 가능(같은 네임스페이스).

- [ ] **Step 3: SimEngine 필드 추가**

In `SimEngine.cs`, 로터리 필드(L24-25) 뒤에 추가:
```csharp
        // 우선도로(스펙 2026-07-13): 교차로 전용(로터리처럼) + 축 우선순위(Dictionary).
        // 간섭 계수만 바꿈 — 라우팅 무관이라 MarkTopologyDirty 안 함(로터리 규약).
        readonly List<Vector2Int> _placedPriorityRoads = new();
        readonly Dictionary<Vector2Int, Axis> _priorityDirs = new();
```

- [ ] **Step 4: 실패 테스트 작성**

Create `Assets/Tests/EditMode/PriorityRoadTests.cs` (RoundaboutTests의 Build 헬퍼 패턴 복제):
```csharp
using NUnit.Framework;
using UnityEngine;
using CityFlow.Sim;
using CityFlow.Contracts;

public class PriorityRoadTests
{
    // 직선 도로(y=0) + 곁가지로 (3,0)·(6,0)을 교차로로 만든 도시. autoDetect=false로 배치 모드.
    static SimEngine Build()
    {
        var cfg = SimConfig.Default();
        cfg.GridWidth = 10; cfg.GridHeight = 10; cfg.AutoDetectSignals = false;
        var e = new SimEngine(cfg);
        for (int x = 0; x < 10; x++) e.Place(new Vector2Int(x, 0), TileType.Road);
        e.Place(new Vector2Int(3, 1), TileType.Road);   // (3,0) 교차로화
        e.Place(new Vector2Int(6, 1), TileType.Road);   // (6,0) 교차로화
        e.Tick(0.25f);   // topology 재구축 소비
        return e;
    }

    [Test]
    public void Place_OnIntersection_Works_AndStoresAxis()
    {
        var e = Build();
        Assert.IsTrue(e.TryPlacePriorityRoad(new Vector2Int(3, 0), Axis.Horizontal));
        Assert.AreEqual(1, e.PriorityRoadTiles.Count);
        Assert.AreEqual(Axis.Horizontal, e.GetPriorityAxis(new Vector2Int(3, 0)));
    }

    [Test]
    public void Place_RejectsNonIntersection_Duplicate_AndAutoMode()
    {
        var e = Build();
        Assert.IsFalse(e.TryPlacePriorityRoad(new Vector2Int(1, 0), Axis.Horizontal)); // 비교차로
        e.TryPlacePriorityRoad(new Vector2Int(3, 0), Axis.Horizontal);
        Assert.IsFalse(e.TryPlacePriorityRoad(new Vector2Int(3, 0), Axis.Vertical));   // 중복
    }

    [Test]
    public void PriorityRoad_And_Roundabout_AreMutuallyExclusive()
    {
        var e = Build();
        Assert.IsTrue(e.TryPlaceRoundabout(new Vector2Int(3, 0)));
        Assert.IsFalse(e.TryPlacePriorityRoad(new Vector2Int(3, 0), Axis.Horizontal)); // 로터리 있음
        Assert.IsFalse(e.CanPlaceRoundabout(new Vector2Int(6, 0)) &&
                       !e.TryPlacePriorityRoad(new Vector2Int(6, 0), Axis.Horizontal)); // 역방향
        e.TryPlacePriorityRoad(new Vector2Int(6, 0), Axis.Horizontal);
        Assert.IsFalse(e.TryPlaceRoundabout(new Vector2Int(6, 0)));                     // 우선도로 있음
    }

    [Test]
    public void Remove_Works_AndRejectsAbsent()
    {
        var e = Build();
        e.TryPlacePriorityRoad(new Vector2Int(3, 0), Axis.Horizontal);
        Assert.IsTrue(e.TryRemovePriorityRoad(new Vector2Int(3, 0)));
        Assert.AreEqual(0, e.PriorityRoadTiles.Count);
        Assert.IsFalse(e.TryRemovePriorityRoad(new Vector2Int(3, 0)));
    }
}
```

- [ ] **Step 5: 테스트 실패 확인**

Run: Unity Test Runner(EditMode) → `PriorityRoadTests`
Expected: FAIL (컴파일 에러 "TryPlacePriorityRoad not defined")

- [ ] **Step 6: SimEngine 메서드 구현**

In `SimEngine.cs`, 로터리 메서드(L325) 뒤에 추가:
```csharp
        public IReadOnlyList<Vector2Int> PriorityRoadTiles => _placedPriorityRoads;

        public Axis GetPriorityAxis(Vector2Int tile) =>
            _priorityDirs.TryGetValue(tile, out var a) ? a : Axis.Horizontal;

        public bool CanPlacePriorityRoad(Vector2Int tile) =>
            !_config.AutoDetectSignals && _grid.IsIntersection(tile)
            && !_priorityDirs.ContainsKey(tile) && !_placedSet.Contains(tile)
            && !_roundaboutSet.Contains(tile) && !_overpassSet.Contains(tile);   // 4자 배타

        public bool TryPlacePriorityRoad(Vector2Int tile, Axis mainAxis)
        {
            if (!CanPlacePriorityRoad(tile)) return false;
            int flat = tile.y * _config.GridWidth + tile.x;
            int idx = _placedPriorityRoads.FindIndex(t => t.y * _config.GridWidth + t.x > flat);
            if (idx < 0) _placedPriorityRoads.Add(tile); else _placedPriorityRoads.Insert(idx, tile);
            _priorityDirs[tile] = mainAxis;
            return true;                          // MarkTopologyDirty 안 함(로터리 규약 — 라우팅 무관)
        }

        public bool TryRemovePriorityRoad(Vector2Int tile)
        {
            if (_config.AutoDetectSignals || !_priorityDirs.Remove(tile)) return false;
            _placedPriorityRoads.Remove(tile);
            return true;
        }
```

- [ ] **Step 7: 기존 3자 배타에 우선도로 추가(역방향 배타)**

In `SimEngine.cs`, `CanPlaceRoundabout`(L304-308)에 조건 추가:
```csharp
        public bool CanPlaceRoundabout(Vector2Int tile) =>
            !_config.AutoDetectSignals && _grid.IsIntersection(tile)
            && !_roundaboutSet.Contains(tile) && !_placedSet.Contains(tile)
            && !_overpassSet.Contains(tile)
            && !_turnSigns.ContainsKey(tile)
            && !_priorityDirs.ContainsKey(tile);   // 우선도로와 배타
```
`CanPlaceOverpass`(입체)와 신호 배치(`CanPlaceSignal` 또는 해당 검사)에도 동일하게 `&& !_priorityDirs.ContainsKey(tile)` 추가. (신호는 배치 검사 위치를 grep `CanPlaceSignal`로 확인 후 동일 패턴.)

- [ ] **Step 8: 테스트 통과 확인**

Run: Unity Test Runner(EditMode) → `PriorityRoadTests`
Expected: PASS (4개 테스트)

- [ ] **Step 9: Commit**

```bash
git add Assets/01_Scripts/CityFlow/Contracts/Axis.cs \
        Assets/01_Scripts/CityFlow/Contracts/IIntersectionFacilityService.cs \
        Assets/01_Scripts/CityFlow/Sim/SimEngine.cs \
        Assets/Tests/EditMode/PriorityRoadTests.cs
git commit -m "[Feat] 우선도로 배치 API + 4자 배타 (Axis enum, 로터리 골격)"
```

---

### Task 2: 엔진 효과 — 비대칭 λ

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimConfig.cs` (필드 L59 근처, Default L116 근처)
- Modify: `Assets/01_Scripts/CityFlow/Sim/FlowSolver.cs` (Resolve 오버로드 L107-115, 무신호 분기 L165-176)
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimEngine.cs` (Resolve 호출부 L131, L211)
- Test: `Assets/Tests/EditMode/PriorityRoadTests.cs` (추가)

**Interfaces:**
- Consumes: Task 1의 `_priorityDirs`, `Axis`.
- Produces: `SimConfig.PriorityMainInterference : float`, `SimConfig.PriorityYieldInterference : float`; `FlowSolver.Resolve(..., IReadOnlyDictionary<Vector2Int, Axis> priorityRoads, double simTime)`.

- [ ] **Step 1: SimConfig 계수 필드 추가**

In `SimConfig.cs`, 로터리 필드(L65) 뒤에 추가:
```csharp
        // ── 우선도로(스펙 2026-07-13): 무신호 간섭의 비대칭 확장 ──
        // 메인축은 곁길로부터 거의 방해 안 받고(λ_main≈0), 곁길은 메인축에 크게 양보(λ_yield↑).
        public float PriorityMainInterference;    // λ_main
        public float PriorityYieldInterference;   // λ_yield
```

- [ ] **Step 2: Default()에 초기값**

In `SimConfig.cs`, `Default()`의 로터리 값(L118) 뒤에 추가:
```csharp
            PriorityMainInterference = 0.1f,
            PriorityYieldInterference = 2.5f,
```

- [ ] **Step 3: 실패 테스트 작성**

In `PriorityRoadTests.cs`, 편중 교차 도시 헬퍼 + 이득 테스트 추가:
```csharp
    // 십자 교차로 (c,c) 하나. 가로 수요 hi, 세로 수요 lo. 우선도로(H) vs 무신호 delivered 비교.
    static float RunCross(int c, float hi, float lo, bool priorityH)
    {
        var cfg = SimConfig.Default();
        cfg.GridWidth = 2 * c + 1; cfg.GridHeight = 2 * c + 1; cfg.AutoDetectSignals = false;
        var e = new SimEngine(cfg);
        for (int x = 0; x < cfg.GridWidth; x++) e.Place(new Vector2Int(x, c), TileType.Road);
        for (int y = 0; y < cfg.GridHeight; y++) e.Place(new Vector2Int(c, y), TileType.Road);
        e.Place(new Vector2Int(0, c), TileType.House);  e.Place(new Vector2Int(cfg.GridWidth - 1, c), TileType.Office);
        e.Place(new Vector2Int(c, 0), TileType.House);  e.Place(new Vector2Int(c, cfg.GridHeight - 1), TileType.Office);
        e.Tick(0.25f);
        if (priorityH) e.TryPlacePriorityRoad(new Vector2Int(c, c), Axis.Horizontal);
        for (int k = 0; k < 8; k++) e.Tick(0.25f);
        return e.DeliveredTotal;
    }

    [Test]
    public void SkewedCross_PriorityRoad_BeatsUnsignaled()
    {
        // 가로가 압도적으로 많은 편중 교차 — 가로 우선도로가 무신호(대칭 λ)보다 총 처리량↑
        float nothing = RunCross(4, 3.0f, 0.5f, priorityH: false);
        float priority = RunCross(4, 3.0f, 0.5f, priorityH: true);
        Assert.Greater(priority, nothing);
    }

    [Test]
    public void PriorityRoad_IsDeterministic()
    {
        // 같은 도시·같은 배치를 두 번 → delivered 동일(결정론, 스펙 §8).
        float a = RunCross(4, 3.0f, 0.5f, priorityH: true);
        float b = RunCross(4, 3.0f, 0.5f, priorityH: true);
        Assert.AreEqual(a, b, 1e-6f);
    }
```
(수요 주입 API가 `Place(House/Office)`가 아니라 별도라면 RoundaboutTests의 `BalancedCross`/`Run` 헬퍼의 실제 수요 세팅 방식을 그대로 복제할 것 — `DeliveredTotal` 비교 패턴은 동일.)

- [ ] **Step 4: 테스트 실패 확인**

Run: EditMode → `SkewedCross_PriorityRoad_BeatsUnsignaled`
Expected: FAIL (우선도로가 아직 delivered에 영향 없음 → priority == nothing)

- [ ] **Step 5: FlowSolver Resolve 오버로드 확장**

In `FlowSolver.cs`, 캐노니컬 시그니처(L113-115)를 확장하고 기존 시그니처를 오버로드로:
```csharp
        // 기존 5-인자 → 새 캐노니컬로 위임(priorityRoads=null)
        public void Resolve(in SimConfig cfg, SignalMap signals, CityGrid grid,
                            HashSet<Vector2Int> roundabouts, HashSet<Vector2Int> overpasses,
                            double simTime = 0)
            => Resolve(cfg, signals, grid, roundabouts, overpasses, null, simTime);

        public void Resolve(in SimConfig cfg, SignalMap signals, CityGrid grid,
                            HashSet<Vector2Int> roundabouts, HashSet<Vector2Int> overpasses,
                            IReadOnlyDictionary<Vector2Int, Axis> priorityRoads,
                            double simTime = 0)
```
(파일 상단 `using CityFlow.Contracts;` 확인 — `Axis` 참조용. 없으면 추가.)

- [ ] **Step 6: 무신호 분기에 우선도로 else if 추가**

In `FlowSolver.cs`, 로터리 분기(L165-171)와 순수 무신호 else(L172-176) 사이에 삽입:
```csharp
                        else if (priorityRoads != null && priorityRoads.TryGetValue(t, out var mainAxis))
                        {
                            // 우선도로: 비대칭 λ — 메인축 무정차, 곁길 양보(스펙 2026-07-13).
                            bool hMain = mainAxis == Axis.Horizontal;
                            float lamH = hMain ? cfg.PriorityMainInterference : cfg.PriorityYieldInterference;
                            float lamV = hMain ? cfg.PriorityYieldInterference : cfg.PriorityMainInterference;
                            _ratioH[i] = AxisRatio(_flowH[i] + lamH * _flowV[i], cfg.RoadCapacity, cfg);
                            _ratioV[i] = AxisRatio(_flowV[i] + lamV * _flowH[i], cfg.RoadCapacity, cfg);
                        }
```

- [ ] **Step 7: SimEngine 호출부에 _priorityDirs 전달**

In `SimEngine.cs`, Resolve 호출 2곳(L131 Step, L211 SettleOffline):
```csharp
            _solver.Resolve(_config, _signals, _grid, _roundaboutSet, _overpassSet, _priorityDirs, _simTime);
```
(`_priorityDirs`는 `Dictionary<Vector2Int, Axis>` → `IReadOnlyDictionary`로 암묵 변환됨.)

- [ ] **Step 8: 테스트 통과 확인**

Run: EditMode → `SkewedCross_PriorityRoad_BeatsUnsignaled` + 기존 Task1 테스트
Expected: PASS (전부). λ 값으로 이득이 안 나오면 `PriorityYieldInterference`를 올리거나 편중도(hi/lo)를 키워 검증(값은 잠정).

- [ ] **Step 9: Commit**

```bash
git add Assets/01_Scripts/CityFlow/Sim/SimConfig.cs \
        Assets/01_Scripts/CityFlow/Sim/FlowSolver.cs \
        Assets/01_Scripts/CityFlow/Sim/SimEngine.cs \
        Assets/Tests/EditMode/PriorityRoadTests.cs
git commit -m "[Feat] 우선도로 엔진 효과 — 비대칭 λ (편중 교차 메인축 무정차)"
```

---

### Task 3: 교차로 해제 시 소멸 프루닝

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimEngine.cs` (RebuildSignals 내, 로터리 프루닝 L160-165 뒤)
- Test: `Assets/Tests/EditMode/PriorityRoadTests.cs` (추가)

**Interfaces:**
- Consumes: Task 1의 `_placedPriorityRoads`, `_priorityDirs`.

- [ ] **Step 1: 실패 테스트 작성**

In `PriorityRoadTests.cs`:
```csharp
    [Test]
    public void RoadRemoval_KillsPriorityRoadNextRebuild()
    {
        var e = Build();
        e.TryPlacePriorityRoad(new Vector2Int(3, 0), Axis.Horizontal);
        e.Place(new Vector2Int(3, 1), TileType.Empty);   // 곁가지 철거 → (3,0)이 교차로 아님
        e.Tick(0.25f);                                    // RebuildSignals가 프루닝
        Assert.AreEqual(0, e.PriorityRoadTiles.Count);
    }
```

- [ ] **Step 2: 테스트 실패 확인**

Run: EditMode → `RoadRemoval_KillsPriorityRoadNextRebuild`
Expected: FAIL (프루닝 없어 우선도로 잔존)

- [ ] **Step 3: 프루닝 구현**

In `SimEngine.cs`, `RebuildSignals` 내 로터리 프루닝(L160-165) 뒤에 추가:
```csharp
            _placedPriorityRoads.RemoveAll(t =>
            {
                if (_grid.IsIntersection(t)) return false;
                _priorityDirs.Remove(t);      // 교차로 해제 → 우선도로 소멸(로터리 규약)
                return true;
            });
```

- [ ] **Step 4: 테스트 통과 확인**

Run: EditMode → `RoadRemoval_KillsPriorityRoadNextRebuild`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Assets/01_Scripts/CityFlow/Sim/SimEngine.cs Assets/Tests/EditMode/PriorityRoadTests.cs
git commit -m "[Feat] 우선도로 교차로 해제 시 자동 소멸 (로터리 프루닝 규약)"
```

---

### Task 4: 세이브 왕복

**Files:**
- Create: `Assets/01_Scripts/CityFlow/Contracts/Save/PriorityRoadSaveData.cs`
- Modify: `Assets/01_Scripts/CityFlow/Contracts/Save/SimSaveData.cs` (필드 1줄)
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimEngine.cs` (CreateSnapshot L537-560, RestoreSnapshot L595-658)
- Test: `Assets/Tests/EditMode/PriorityRoadTests.cs` (추가)

**Interfaces:**
- Consumes: Task 1의 `_placedPriorityRoads`, `_priorityDirs`; `Axis`.
- Produces: `PriorityRoadSaveData { int X, Y, Axis }`; `SimSaveData.PriorityRoads : PriorityRoadSaveData[]`.

- [ ] **Step 1: PriorityRoadSaveData 생성**

Create `Assets/01_Scripts/CityFlow/Contracts/Save/PriorityRoadSaveData.cs`:
```csharp
using System;

namespace CityFlow.Contracts.Save
{
    [Serializable]
    public sealed class PriorityRoadSaveData
    {
        public int X;
        public int Y;
        public int Axis;   // (int)CityFlow.Contracts.Axis — 0=Horizontal, 1=Vertical
    }
}
```

- [ ] **Step 2: SimSaveData에 필드 추가**

In `SimSaveData.cs`, TurnSigns 필드(L13) 뒤에 추가:
```csharp
        public PriorityRoadSaveData[] PriorityRoads;   // 구세이브 = null — 마이그레이션 공짜
```

- [ ] **Step 3: 실패 테스트 작성**

In `PriorityRoadTests.cs`:
```csharp
    [Test]
    public void SaveRoundtrip_RestoresPriorityRoadsWithAxis()
    {
        var e = Build();
        e.TryPlacePriorityRoad(new Vector2Int(3, 0), Axis.Horizontal);
        e.TryPlacePriorityRoad(new Vector2Int(6, 0), Axis.Vertical);
        var snap = e.CreateSnapshot();

        var e2 = Build();
        e2.RestoreSnapshot(snap);
        Assert.AreEqual(2, e2.PriorityRoadTiles.Count);
        Assert.AreEqual(Axis.Horizontal, e2.GetPriorityAxis(new Vector2Int(3, 0)));
        Assert.AreEqual(Axis.Vertical, e2.GetPriorityAxis(new Vector2Int(6, 0)));
    }

    [Test]
    public void LegacySave_WithoutPriorityRoads_RestoresClean()
    {
        var e = Build();
        var snap = e.CreateSnapshot();
        snap.PriorityRoads = null;   // 구세이브 시뮬
        var e2 = Build();
        Assert.DoesNotThrow(() => e2.RestoreSnapshot(snap));
        Assert.AreEqual(0, e2.PriorityRoadTiles.Count);
    }
```

- [ ] **Step 4: 테스트 실패 확인**

Run: EditMode → `SaveRoundtrip_RestoresPriorityRoadsWithAxis`
Expected: FAIL (스냅샷에 우선도로 미포함)

- [ ] **Step 5: CreateSnapshot 구현**

In `SimEngine.cs`, `CreateSnapshot`의 turnSigns 배열 생성(L557 근처) 뒤에 추가:
```csharp
            var priorityRoads = new PriorityRoadSaveData[_placedPriorityRoads.Count];
            for (int i = 0; i < _placedPriorityRoads.Count; i++)
            {
                var t = _placedPriorityRoads[i];
                priorityRoads[i] = new PriorityRoadSaveData { X = t.x, Y = t.y, Axis = (int)_priorityDirs[t] };
            }
```
반환 객체(L560)에 필드 추가: `..., TurnSigns = turnSigns, PriorityRoads = priorityRoads };`

- [ ] **Step 6: RestoreSnapshot 구현**

In `SimEngine.cs`, `RestoreSnapshot`의 `!_config.AutoDetectSignals` 블록 안, 일방/턴 복원 뒤에 추가:
```csharp
            _placedPriorityRoads.Clear();
            _priorityDirs.Clear();
            if (snapshot.PriorityRoads != null)
                foreach (var p in snapshot.PriorityRoads)
                {
                    var tile = new Vector2Int(p.X, p.Y);
                    // 손상 세이브 방어: 배치 조건 재검증(4자 배타·교차로) + Axis 값 범위 검증.
                    if (CanPlacePriorityRoad(tile) && (p.Axis == 0 || p.Axis == 1))
                    {
                        _priorityDirs[tile] = (Axis)p.Axis;
                        _placedPriorityRoads.Add(tile);
                    }
                }
            _placedPriorityRoads.Sort((a, b) =>
                (a.y * _config.GridWidth + a.x).CompareTo(b.y * _config.GridWidth + b.x));
```

- [ ] **Step 7: 테스트 통과 확인**

Run: EditMode → `SaveRoundtrip_RestoresPriorityRoadsWithAxis`, `LegacySave_WithoutPriorityRoads_RestoresClean` + 전체
Expected: PASS

- [ ] **Step 8: Commit**

```bash
git add Assets/01_Scripts/CityFlow/Contracts/Save/PriorityRoadSaveData.cs \
        Assets/01_Scripts/CityFlow/Contracts/Save/SimSaveData.cs \
        Assets/01_Scripts/CityFlow/Sim/SimEngine.cs \
        Assets/Tests/EditMode/PriorityRoadTests.cs
git commit -m "[Feat] 우선도로 세이브 왕복 — 축 보존 + 레거시/손상 방어"
```

---

### Task 5: 뷰 마커 (양보 표지)

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/View/MainCityView.cs` (딕셔너리 L63 뒤, Refresh 호출 L192/L223, Refresh/Create 메서드 로터리 뒤)

**Interfaces:**
- Consumes: `intersectionFacility.PriorityRoadTiles`, `intersectionFacility.GetPriorityAxis(tile)`.

**Note:** 뷰는 EditMode 테스트 대상이 아님 — 배치 후 Play 모드에서 육안 검증(양보 삼각형이 곁길 쪽에 표시, 축에 따라 회전). 커밋 전 Play 1회.

- [ ] **Step 1: 딕셔너리 필드 추가**

In `MainCityView.cs`, turnSignVisuals(L63) 뒤:
```csharp
        private readonly Dictionary<Vector2Int, GameObject> priorityRoadVisuals = new();
```

- [ ] **Step 2: Refresh 호출 추가**

In `MainCityView.cs`, 두 진입점(L192 Initialize, L223 Update)의 `RefreshTurnSigns();` 뒤:
```csharp
            RefreshPriorityRoads();
```

- [ ] **Step 3: RefreshPriorityRoads + CreatePriorityRoadVisual 구현**

In `MainCityView.cs`, `RefreshRoundabouts`(L428) 패턴 복제 + 축 회전:
```csharp
        private void RefreshPriorityRoads()
        {
            if (intersectionFacility == null) return;
            IReadOnlyList<Vector2Int> tiles = intersectionFacility.PriorityRoadTiles;
            for (int i = 0; i < tiles.Count; i++)
            {
                if (!priorityRoadVisuals.TryGetValue(tiles[i], out GameObject v))
                {
                    v = CreatePriorityRoadVisual(tiles[i]);
                    priorityRoadVisuals.Add(tiles[i], v);
                }
                // 메인축에 맞춰 표지 회전: 세로 우선이면 90°.
                float z = intersectionFacility.GetPriorityAxis(tiles[i]) == Axis.Vertical ? 90f : 0f;
                v.transform.localRotation = Quaternion.Euler(0f, 0f, z);
            }
            foreach (Vector2Int tile in new List<Vector2Int>(priorityRoadVisuals.Keys))
            {
                if (ContainsSignal(tiles, tile)) continue;
                Destroy(priorityRoadVisuals[tile]);
                priorityRoadVisuals.Remove(tile);
            }
        }

        private GameObject CreatePriorityRoadVisual(Vector2Int tile)
        {
            // 임시 프리미티브 양보 표지(▽): 얇은 큐브 막대를 곁길 방향으로.
            // ponytail: 표지판 3D 에셋은 아트 단계
            GameObject root = new GameObject($"PriorityRoad_{tile.x}_{tile.y}");
            root.transform.SetParent(signalRoot, false);
            root.transform.localPosition = GridToLocal(tile, signalZ);
            Renderer bar = CreateSignalBar(root.transform, "Bar",
                new Vector3(tileSize * 0.5f, tileSize * 0.08f, 0.02f), Vector3.zero);
            ApplyRendererColor(PrepareRenderer(bar), onewayColor);   // 기존 색 재사용(에셋 전)
            return root;
        }
```
(`CreateSignalBar` 시그니처는 일방통행 `CreateOnewayVisual`(L556)에서 쓰는 것과 동일 — 실제 인자 순서는 그 메서드를 참조해 맞출 것.)

- [ ] **Step 4: 컴파일 확인 + Play 육안 검증**

Unity에서 컴파일 에러 0 확인 → Play → 샌드박스에서 우선도로 배치 → 표지가 교차로에 뜨고 축에 따라 회전하는지 육안 확인.

- [ ] **Step 5: Commit**

```bash
git add Assets/01_Scripts/CityFlow/View/MainCityView.cs
git commit -m "[Feat] 우선도로 뷰 마커 — 양보 표지(임시 프리미티브, 축 회전)"
```

---

## 착수 후

- 전체 EditMode 그린 확인 후 push + PR to develop (`base=develop`, [[team-git-workflow]]).
- 상점 UI(김건)·가격(진우)은 별도 — 계약(`IIntersectionFacilityService.TryPlacePriorityRoad`)에 붙임.
- 공통화 리팩터(6종 좌표-전용 배치물)는 별도 티켓 — 이번 스코프 밖.
