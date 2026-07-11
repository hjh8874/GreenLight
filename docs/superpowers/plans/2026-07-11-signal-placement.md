# 신호 배치형 전환 (구매 피벗 2단계 엔진 기반) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 엔진이 "배치된 곳에만 신호 존재" 모드를 지원한다 — `AutoDetectSignals=true`(기본, 현행 무변경) staging, `TryPlaceSignal(tile, greenSlots)` 배치 API, 세이브 포맷 무변경 복원.

**Architecture:** SignalMap에 placed 오버로드(`Rebuild(grid, placed)`), SimEngine이 배치 목록 소유(flat 정렬 = 결정론) + `RebuildSignals()` 공통 헬퍼로 3개 재구축 지점 통일. RestoreSnapshot은 배치 모드에서 저장된 신호를 배치 목록으로 재구성(구세이브 = 전 교차로 신호 → 자동 호환). ISignalControl에 3메서드 제안(E-1 관례).

**Tech Stack:** Unity 6000.5 C#, EditMode NUnit, Unity MCP.

## Global Constraints

- 스펙: `docs/superpowers/specs/2026-07-11-signal-placement-design.md`.
- 브랜치: `feat-signal-placement-hwan` (이미 체크아웃, 스택 최상단). 브랜치 전환 금지.
- **기본값 `AutoDetectSignals = true` = 현행 동작 바이트 동일** — 기존 테스트 120 무수정 생존이 필수 게이트.
- 결정론: `_placedSignals`는 flat(y*W+x) 오름차순 유지 — SignalMap 순회 순서의 단일 출처. Dictionary 순회 금지(SignalMap.Rebuild의 기존 dead-키 수집 패턴은 제거 대상 아님 — placed 경로도 같은 패턴 재사용).
- 신호 배치/소멸은 TopologyDirty 무관(현행과 동일 — 경로·수요에 영향 없음).
- 커밋 `[Feat]` + `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- Unity MCP 검증 절차: ①`execute_code` `UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceUpdate); return "ok";`(타임아웃=도메인 리로드 정상, 브릿지 끊기면 set_active_instance) ②`mcpforunity://editor/state` idle ③`read_console` Error 0(stale→clear) ④`run_tests` EditMode 전체.
- baseline **120**. Task 1 후 **127**(+7), Task 2 후 **131**(+4).

---

### Task 1: 엔진 코어 — 배치 모드 + 배치 API + Config

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimConfig.cs` (필드+Default)
- Modify: `Assets/05_ScriptableObjects/SimConfig.asset` (execute_code)
- Modify: `Assets/01_Scripts/CityFlow/Sim/SignalMap.cs` (placed 오버로드)
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimEngine.cs` (배치 목록·API·RebuildSignals 헬퍼·재구축 2곳)
- Test: `Assets/Tests/EditMode/SignalPlacementTests.cs` (신규)

**Interfaces:**
- Consumes: `CityGrid.IsIntersection(Vector2Int)`, 기존 `TrySetSignalGreenSlots`.
- Produces (Task 2·팀 UI가 사용): `bool SimEngine.CanPlaceSignal(Vector2Int)` / `bool TryPlaceSignal(Vector2Int, int greenSlots)` / `bool TryRemoveSignal(Vector2Int)`; `SignalMap.Rebuild(CityGrid, IReadOnlyList<Vector2Int> placed)`; `SimEngine.RebuildSignals()`(private 헬퍼); `SimConfig.AutoDetectSignals`(bool).

- [ ] **Step 1: SimConfig + asset**

`SimConfig.cs`의 `RoutingCongestionWeight` 필드 아래:

```csharp
        // ── 신호 배치 모드(구매 피벗 2단계) ──
        // true = 현행 자동 감지(모든 교차로에 신호). false = 배치된 곳에만 존재(TryPlaceSignal).
        // 상점 UI(김건) 도입 시 asset에서 false 전환 — 그날 무신호 간섭 λ가 라이브 활성화 🔓
        public bool AutoDetectSignals;
```
`Default()`의 `RoutingCongestionWeight = 2f,` 아래: `AutoDetectSignals = true,`

refresh 후 execute_code:
```csharp
var so = UnityEditor.AssetDatabase.LoadAssetAtPath<CityFlow.Configs.SimConfigAsset>("Assets/05_ScriptableObjects/SimConfig.asset");
so.Value.AutoDetectSignals = true;
UnityEditor.EditorUtility.SetDirty(so);
UnityEditor.AssetDatabase.SaveAssets();
return "ok";
```

- [ ] **Step 2: 실패 테스트 작성** — `Assets/Tests/EditMode/SignalPlacementTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    // 신호 배치형 전환(스펙 2026-07-11): AutoDetectSignals=false면 배치된 곳에만 신호가 존재.
    // 기본 true = 현행 자동 감지 — 기존 스위트 전체가 그 회귀 게이트.
    public class SignalPlacementTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        // 직선 도로 + 곁가지 2개 → 교차로 (3,0)·(6,0). 집/회사로 흐름도 형성 가능.
        static SimEngine Build(bool autoDetect, out SimEventHub hub)
        {
            var c = SimConfig.Default();
            c.TickInterval = 0.25f;
            c.GridWidth = 10; c.GridHeight = 2;
            c.AutoDetectSignals = autoDetect;
            hub = new SimEventHub();
            var e = new SimEngine(c, hub);
            for (int x = 0; x <= 9; x++) e.Place(V(x, 0), TileType.Road);
            e.Place(V(3, 1), TileType.Road);
            e.Place(V(6, 1), TileType.Road);
            e.Place(V(0, 1), TileType.House);
            e.Place(V(9, 1), TileType.Office);
            e.Tick(0.25f);                        // 재구축 소비
            return e;
        }

        [Test]
        public void PlacedMode_StartsWithNoSignals_EvenOnIntersections()
        {
            var e = Build(autoDetect: false, out _);
            Assert.AreEqual(0, e.SignalTiles.Count);   // 교차로 2개 있어도 신호 0 — 자동 생성 죽음
        }

        [Test]
        public void PlacedMode_PlaceOnIntersection_AppliesGreenSlots()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsTrue(e.CanPlaceSignal(V(3, 0)));
            Assert.IsTrue(e.TryPlaceSignal(V(3, 0), greenSlots: 12));   // 가로 우선 듀티로 구매
            Assert.AreEqual(1, e.SignalTiles.Count);
            Assert.AreEqual(V(3, 0), e.SignalTiles[0]);
            Assert.AreEqual(12, e.GetSignalGreenSlots(V(3, 0)));        // 방향+초가 즉시 반영
        }

        [Test]
        public void PlacedMode_RejectsNonIntersection_Duplicate_AndAutoMode()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsFalse(e.TryPlaceSignal(V(1, 0), 8));    // 직선 도로 — 교차로 아님
            Assert.IsFalse(e.TryPlaceSignal(V(1, 1), 8));    // 도로 아님
            Assert.IsTrue(e.TryPlaceSignal(V(3, 0), 8));
            Assert.IsFalse(e.TryPlaceSignal(V(3, 0), 8));    // 중복
            Assert.IsFalse(e.CanPlaceSignal(V(3, 0)));

            var auto = Build(autoDetect: true, out _);
            Assert.IsFalse(auto.CanPlaceSignal(V(3, 0)));    // 자동 모드 — 배치 개념 없음
            Assert.IsFalse(auto.TryPlaceSignal(V(3, 0), 8));
            Assert.AreEqual(2, auto.SignalTiles.Count);       // 자동은 현행대로 전 교차로
        }

        [Test]
        public void PlacedMode_RemoveSignal_Works()
        {
            var e = Build(autoDetect: false, out _);
            e.TryPlaceSignal(V(3, 0), 8);
            e.TryPlaceSignal(V(6, 0), 8);
            Assert.IsTrue(e.TryRemoveSignal(V(3, 0)));
            Assert.AreEqual(1, e.SignalTiles.Count);
            Assert.AreEqual(V(6, 0), e.SignalTiles[0]);
            Assert.IsFalse(e.TryRemoveSignal(V(3, 0)));      // 이미 없음
        }

        [Test]
        public void PlacedMode_PlacedListStaysFlatSorted()
        {
            var e = Build(autoDetect: false, out _);
            e.TryPlaceSignal(V(6, 0), 8);                     // 뒤 타일 먼저 배치
            e.TryPlaceSignal(V(3, 0), 8);
            Assert.AreEqual(V(3, 0), e.SignalTiles[0]);       // 순회 순서는 flat 정렬(결정론)
            Assert.AreEqual(V(6, 0), e.SignalTiles[1]);
        }

        [Test]
        public void PlacedMode_RoadRemoval_KillsSignalNextRebuild()
        {
            var e = Build(autoDetect: false, out _);
            e.TryPlaceSignal(V(3, 0), 8);
            e.Remove(V(3, 1));                                // 곁가지 철거 → (3,0) 교차로 해제
            e.Tick(0.25f);                                    // 재구축 소비
            Assert.AreEqual(0, e.SignalTiles.Count);          // 신호 자동 소멸
            Assert.IsFalse(e.TryRemoveSignal(V(3, 0)));       // 배치 목록에서도 사라짐
        }

        [Test]
        public void PlacedMode_GreenSlotsClampStillApplies()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsTrue(e.TryPlaceSignal(V(3, 0), 999));    // 과대값
            Assert.AreEqual(15, e.GetSignalGreenSlots(V(3, 0)));   // [1, 주기-1] 클램프(기존 규약)
        }
    }
}
```

- [ ] **Step 3: 실패 확인** — refresh → `run_tests` EditMode. Expected: 컴파일 에러(`CanPlaceSignal`/`TryPlaceSignal`/`TryRemoveSignal`/`AutoDetectSignals` 미정의).

- [ ] **Step 4: SignalMap 오버로드** — `SignalMap.cs`의 `Rebuild(CityGrid grid)`를 다음으로 교체:

```csharp
        // 자동 감지 모드(현행): 모든 교차로에 신호.
        public void Rebuild(CityGrid grid) => Rebuild(grid, null);

        // placed != null = 배치 모드(구매 피벗 2단계): 배치 목록에 있고 아직 교차로인 타일만.
        // placed 순서가 순회 순서의 단일 출처(엔진이 flat 정렬 유지 — 결정론).
        public void Rebuild(CityGrid grid, IReadOnlyList<Vector2Int> placed)
        {
            _tiles.Clear();
            _alive.Clear();

            if (placed == null)
            {
                for (int y = 0; y < grid.Height; y++)                  // flat(y,x) 순서 고정
                    for (int x = 0; x < grid.Width; x++)
                        Consider(grid, new Vector2Int(x, y));
            }
            else
            {
                for (int i = 0; i < placed.Count; i++)
                    Consider(grid, placed[i]);
            }

            // 더는 교차로가 아닌(또는 배치 해제된) 신호 제거 — 유저 조율도 함께 소멸.
            var dead = new List<Vector2Int>();                          // ponytail: Rebuild는 드묾, 지역 할당 OK
            foreach (var key in _signals.Keys)
                if (!_alive.Contains(key)) dead.Add(key);
            foreach (var key in dead) _signals.Remove(key);
        }

        void Consider(CityGrid grid, Vector2Int t)
        {
            if (grid.GetTile(t) != TileType.Road) return;
            if (!grid.IsIntersection(t)) return;                        // 교차로 규칙은 CityGrid가 오너
            _tiles.Add(t);
            _alive.Add(t);
            if (!_signals.ContainsKey(t)) _signals[t] = new Signal();   // 기존이면 오프셋 보존
        }
```
(클래스 주석의 "유저는 신호를 '짓지' 않고" 문구를 "자동 모드에선 짓지 않고 조율만, 배치 모드(2단계)에선 산 곳에만 존재"로 갱신.)

- [ ] **Step 5: SimEngine 배치 소유 + API + 재구축 통일**

필드(`_signals` 아래):
```csharp
        // 배치 모드(AutoDetectSignals=false) 소유 상태: flat 정렬 유지 = SignalMap 순회 순서(결정론).
        readonly List<Vector2Int> _placedSignals = new();
        readonly HashSet<Vector2Int> _placedSet = new();
```

재구축 헬퍼(기존 `_signals.Rebuild(_grid)` 2곳 — Step·SettleOffline — 을 이걸로 교체):
```csharp
        // 신호 재구축 단일 창구: 자동 = 전 교차로 스캔 / 배치 = 배치 목록(비교차로는 먼저 소멸).
        void RebuildSignals()
        {
            if (_config.AutoDetectSignals)
            {
                _signals.Rebuild(_grid);
                return;
            }
            _placedSignals.RemoveAll(t =>
            {
                if (_grid.IsIntersection(t)) return false;
                _placedSet.Remove(t);          // 도로 철거로 교차로 해제 → 배치도 소멸(환불은 경제 영역)
                return true;
            });
            _signals.Rebuild(_grid, _placedSignals);
        }
```

배치 API(`TrySetSignalGreenSlots` 아래):
```csharp
        // ── 신호 배치(구매 피벗 2단계, 스펙 2026-07-11): 배치 모드에서만. 가격·UI는 팀(김건·진우) ──
        public bool CanPlaceSignal(Vector2Int tile) =>
            !_config.AutoDetectSignals && _grid.IsIntersection(tile) && !_placedSet.Contains(tile);

        public bool TryPlaceSignal(Vector2Int tile, int greenSlots)
        {
            if (!CanPlaceSignal(tile)) return false;
            int flat = tile.y * _config.GridWidth + tile.x;
            int idx = _placedSignals.FindIndex(t => t.y * _config.GridWidth + t.x > flat);
            if (idx < 0) _placedSignals.Add(tile); else _placedSignals.Insert(idx, tile);
            _placedSet.Add(tile);
            RebuildSignals();
            TrySetSignalGreenSlots(tile, greenSlots);   // 구매 파라미터(방향+초) — 기존 클램프 재사용
            return true;
        }

        public bool TryRemoveSignal(Vector2Int tile)
        {
            if (_config.AutoDetectSignals || !_placedSet.Remove(tile)) return false;
            _placedSignals.Remove(tile);
            RebuildSignals();
            return true;
        }
```

- [ ] **Step 6: GREEN + 전체 회귀** — refresh → `run_tests` EditMode 전체. Expected: 120 + 7 = **127/127** (기본 true라 기존 무영향 — 하나라도 깨지면 STOP·보고).

- [ ] **Step 7: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Sim/SimConfig.cs Assets/05_ScriptableObjects/SimConfig.asset Assets/01_Scripts/CityFlow/Sim/SignalMap.cs Assets/01_Scripts/CityFlow/Sim/SimEngine.cs Assets/Tests/EditMode/SignalPlacementTests.cs Assets/Tests/EditMode/SignalPlacementTests.cs.meta
git commit -m "[Feat] 신호 배치 모드 — AutoDetectSignals staging + TryPlaceSignal API

배치 모드(false)면 산 곳에만 신호 존재: 교차로에만 배치, greenSlots(방향+초)
즉시 적용, 도로 철거로 교차로 해제 시 자동 소멸. 기본 true = 현행 바이트 동일
(기존 120 무수정 생존). placed 목록 flat 정렬 = 결정론. 테스트 7종.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: 복원 경로 + 계약 제안 + 통합 테스트

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimEngine.cs` (RestoreSnapshot 배치 분기)
- Modify: `Assets/01_Scripts/CityFlow/Contracts/ISignalControl.cs` (제안 3종 + 주석)
- Test: `Assets/Tests/EditMode/SignalPlacementTests.cs` (추가 4종)

**Interfaces:**
- Consumes: Task 1의 `_placedSignals`/`_placedSet`/`RebuildSignals()`/배치 API.
- Produces: `ISignalControl.CanPlaceSignal/TryPlaceSignal/TryRemoveSignal`(제안 — SimEngine이 이미 구현이라 선언만).

- [ ] **Step 1: 실패 테스트 추가** — `SignalPlacementTests.cs`에:

```csharp
        [Test]
        public void PlacedMode_SaveRoundtrip_RestoresPlacementAndLevers()
        {
            var e = Build(autoDetect: false, out _);
            e.TryPlaceSignal(V(3, 0), 12);
            e.TryPlaceSignal(V(6, 0), 4);
            e.TrySetSignalOffsetSlots(V(6, 0), 5);
            var snap = e.CreateSnapshot();

            var fresh = Build(autoDetect: false, out _);
            Assert.AreEqual(0, fresh.SignalTiles.Count);
            fresh.RestoreSnapshot(snap);
            fresh.Tick(0.25f);
            Assert.AreEqual(2, fresh.SignalTiles.Count);              // 배치가 세이브에서 복원됨
            Assert.AreEqual(12, fresh.GetSignalGreenSlots(V(3, 0)));
            Assert.AreEqual(4, fresh.GetSignalGreenSlots(V(6, 0)));
            Assert.AreEqual(5, fresh.GetSignalOffsetSlots(V(6, 0)));
            Assert.IsTrue(fresh.TryRemoveSignal(V(3, 0)));            // 복원된 것도 배치 소유로 관리됨
        }

        [Test]
        public void LegacyAutoSave_RestoredInPlacedMode_PlacesAllSavedSignals()
        {
            // 자동 시절 세이브(전 교차로 신호) → 배치 모드로 열면 그 신호들이 전부 배치된 걸로.
            var auto = Build(autoDetect: true, out _);
            var snap = auto.CreateSnapshot();

            var placed = Build(autoDetect: false, out _);
            placed.RestoreSnapshot(snap);
            placed.Tick(0.25f);
            Assert.AreEqual(2, placed.SignalTiles.Count);             // 마이그레이션 공짜
        }

        [Test]
        public void PlacedMode_CorridorOverride_CollectsPlacedLine()
        {
            // 배치된 신호 3개 라인에서 코리도어가 그대로 작동(SignalMap 경유라 자동 정합).
            var c = SimConfig.Default();
            c.TickInterval = 0.25f;
            c.GridWidth = 12; c.GridHeight = 5;
            c.AutoDetectSignals = false;
            c.OverrideDurationSeconds = 0.5f;
            c.OverrideCooldownSeconds = 1f;
            var e = new SimEngine(c, new SimEventHub());
            for (int x = 0; x <= 10; x++) e.Place(V(x, 2), TileType.Road);
            e.Place(V(2, 3), TileType.Road);
            e.Place(V(5, 3), TileType.Road);
            e.Place(V(8, 3), TileType.Road);
            e.Tick(0.25f);
            e.TryPlaceSignal(V(2, 2), 8);
            e.TryPlaceSignal(V(5, 2), 8);
            e.TryPlaceSignal(V(8, 2), 8);

            Assert.IsTrue(e.TryOverrideSignal(V(5, 2), horizontal: true));
            Assert.Greater(e.GetOverrideSecondsLeft(V(2, 2)), 0f);
            Assert.Greater(e.GetOverrideSecondsLeft(V(8, 2)), 0f);
        }

        [Test]
        public void PlacedMode_UnsignaledInterferenceIsLive_SignalBeatsIt()
        {
            // 배치 모드에서 무신호 간섭(1단계 잠복 수학)이 라이브: 붐비는 십자에 신호를 사면 이긴다.
            // 십자 기하는 AxisFlowTests.CrossCity와 동일 원리(직진 관통·코너컷 검증 좌표).
            var c = SimConfig.Default();
            c.TickInterval = 0.25f;
            c.GridWidth = 13; c.GridHeight = 13;
            c.DemandPerHouse = 1f;
            c.RoadCapacity = 12f;
            c.DemandChoicePool = 1;
            c.SchoolCapacity = 6;
            c.OfficeCapacity = 20;
            c.RushAmplitude = 0f;
            c.AutoDetectSignals = false;

            System.Func<bool, float> run = placeSignal =>
            {
                var e = new SimEngine(c, new SimEventHub());
                for (int x = 0; x <= 12; x++) e.Place(V(x, 6), TileType.Road);
                for (int y = 0; y <= 12; y++) if (y != 6) e.Place(V(6, y), TileType.Road);
                for (int i = 0; i < 6; i++) e.Place(V(i, 7), TileType.House);
                for (int i = 0; i < 6; i++) e.Place(V(5, i), TileType.House);
                e.Place(V(12, 7), TileType.Office);
                e.Place(V(5, 12), TileType.School);
                e.Tick(0.25f);
                if (placeSignal) e.TryPlaceSignal(V(6, 6), 8);
                e.Tick(0.25f);
                return e.DeliveredTotal;
            };

            Assert.Less(run(false), run(true));   // 무신호 간섭 손실 > 신호 듀티 손실 = 사는 이유
        }
```

- [ ] **Step 2: 실패 확인** — refresh → `run_tests` `CityFlow.Sim.Tests.SignalPlacementTests`. Expected: 왕복/레거시 테스트 FAIL(복원이 자동 감지 경로라 배치 목록 미구성 — placed 모드에서 SignalTiles 0), 코리도어/간섭은 통과 가능(이미 Task 1 배치가 SignalMap 경유).

- [ ] **Step 3: RestoreSnapshot 배치 분기** — `SimEngine.cs`의 RestoreSnapshot에서 `_signals.Rebuild(_grid);` 한 줄을 다음으로 교체:

```csharp
            // 배치 모드: 저장된 신호 목록 = 배치 기록(스펙 §3). 구세이브(자동 시절 = 전 교차로 신호)도
            // 같은 경로로 전부 배치 복원 — 포맷·마이그레이션 공짜. 자동 모드는 현행 스캔.
            if (!_config.AutoDetectSignals)
            {
                _placedSignals.Clear();
                _placedSet.Clear();
                if (snapshot.SignalOffsets != null)
                    foreach (var s in snapshot.SignalOffsets)
                    {
                        var tile = new Vector2Int(s.X, s.Y);
                        if (_placedSet.Add(tile)) _placedSignals.Add(tile);
                    }
                _placedSignals.Sort((a, b) =>
                    (a.y * _config.GridWidth + a.x).CompareTo(b.y * _config.GridWidth + b.x));   // flat 정렬 복구
            }
            RebuildSignals();
```
(비교차로 배치 항목은 `RebuildSignals`의 prune이 제거 — 별도 필터 불요.)

- [ ] **Step 4: ISignalControl 제안** — 오버라이드 3종 아래에 추가:

```csharp
        // 신호 배치(구매 피벗 2단계, 스펙 2026-07-11): AutoDetectSignals=false 모드에서만 유효.
        // greenSlots = 구매 시 정하는 "방향+초"(가로 초록 슬롯 — 주기 절반 초과 = 가로 우선).
        // 가격 검증은 상점(UI+경제)이 호출 전에 — 엔진은 배치 규칙(교차로·중복)만 지킨다.
        // 제안: 상점 UI가 붙을 창구. 최종 확정은 김건 합의.
        bool CanPlaceSignal(Vector2Int tile);
        bool TryPlaceSignal(Vector2Int tile, int greenSlots);
        bool TryRemoveSignal(Vector2Int tile);
```
(상단 `SignalTiles` 주석 "자동 감지된 교차로(신호) 타일들"을 "존재하는 신호 타일들(자동 감지 또는 배치)"로 갱신. SimEngine은 이미 public 구현이라 선언 추가만으로 충족.)

- [ ] **Step 5: GREEN + 전체 회귀** — refresh → `run_tests` EditMode 전체. Expected: 127 + 4 = **131/131**.

- [ ] **Step 6: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Sim/SimEngine.cs Assets/01_Scripts/CityFlow/Contracts/ISignalControl.cs Assets/Tests/EditMode/SignalPlacementTests.cs
git commit -m "[Feat] 배치 신호 세이브 복원 + ISignalControl 배치 3종 제안

배치 모드 복원 = 저장된 신호 목록을 배치 기록으로 재구성(flat 정렬) —
구세이브(전 교차로 신호)는 전부 배치로 열림(마이그레이션 공짜).
계약 제안 3종(상점 UI 창구, 김건 합의 대상). 코리도어·무신호 간섭
라이브 통합 테스트 포함.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## 완료 후

- 플레이 검증(배치 모드 asset 전환은 아직 금지 — 상점 UI 없음): 테스트로 충분, 라이브 전환은 팀 결정.
- PR 본문 명기: 기본 true = 무변화 staging, 계약 제안 3종(김건), 전환 시 무신호 간섭 활성(밸런스 체감 변화 — 진우).
- 후속(팀): 상점 UI(김건)·가격(진우)·라이브 전환 시점 회의.

## Self-Review 결과

- **스펙 커버리지**: §1(T1 Step4) §2(T1 Step5) §3(T2 Step3) §4(T2 Step4) §5(T1 Step1) 검증 계획 11종(T1 7+T2 4) — 전부 매핑.
- **플레이스홀더**: 없음.
- **타입 일관성**: `TryPlaceSignal(Vector2Int, int)`·`CanPlaceSignal`·`TryRemoveSignal`·`RebuildSignals()`·`Rebuild(CityGrid, IReadOnlyList<Vector2Int>)`·`AutoDetectSignals` — T1 정의와 T2/테스트 사용 일치. 간섭 테스트 기하 = 검증된 CrossCity 좌표(가로 집 y=7 x=0..5 서쪽만 — 충돌 함정 회피 반영).
