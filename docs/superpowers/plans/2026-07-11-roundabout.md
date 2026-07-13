# 회전교차로 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 교차로에 구매·배치하는 회전교차로 — 낮은 간섭(λ=0.25)+용량 페널티(×0.7) 수식, 신호와 배타 배치, 세이브, 뷰(원형 마커+차가 도는 연출).

**Architecture:** 신호 배치 3종(`TryPlaceSignal` 등)과 동형의 배치 리스트를 SimEngine에 병렬 추가. FlowSolver 무신호 루프에서 로터리 타일만 다른 계수 적용(엔진 소유 HashSet을 Resolve에 직접 전달 — Rebuild 불필요). 뷰는 폴링 마커 + 차량 위치의 뷰 전용 원호 보정.

**Tech Stack:** Unity(EditMode NUnit, Unity MCP로 컴파일 확인·테스트 실행), C#. 스펙: `docs/superpowers/specs/2026-07-11-roundabout-design.md`.

## Global Constraints

- 브랜치 `feat-roundabout-hwan` (스택: feat-signal-placement-hwan 위). 커밋 접두 `[Feat]`/`docs:`/`chore:` (팀 규약).
- 결정론: 같은 입력 = 같은 delivered. 순회는 flat(y,x) 오름차순, 배치 리스트는 flat 정렬 유지.
- 계수: `RoundaboutInterference = 0.25f`, `RoundaboutCapacityFactor = 0.7f` (SimConfig, 스펙 §1 그대로).
- 신호 주기 `CycleSlots = 16`(Signal 기본값) — 테스트의 greenSlots 수치가 이 값 전제.
- Sim 내부 클래스는 `internal`(테스트는 InternalsVisibleTo로 접근). 엔진→뷰 노출은 SimEngine public/계약.
- 스크립트 수정 후 반드시 Unity MCP `read_console`로 컴파일 에러 0 확인(도메인 리로드 대기). 비포커스 에디터는 임포트를 안 하므로 필요 시 `refresh_unity`(ForceUpdate) 먼저.
- 테스트 실행: Unity MCP `run_tests`(EditMode). 이 브랜치 기존 스위트 = 131개 전부 그린 유지.

---

### Task 1: 배치 API + 계약 (SimEngine + ISignalControl)

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimEngine.cs` (필드 ~22행 옆, API ~216행 뒤, RebuildSignals ~98행)
- Modify: `Assets/01_Scripts/CityFlow/Contracts/ISignalControl.cs` (인터페이스 끝)
- Test: `Assets/Tests/EditMode/RoundaboutTests.cs` (신규)

**Interfaces:**
- Consumes: `_placedSignals`/`_placedSet`/`RebuildSignals()`(기존 신호 배치 인프라), `_grid.IsIntersection`, `_config.AutoDetectSignals`.
- Produces: `IReadOnlyList<Vector2Int> RoundaboutTiles`, `bool CanPlaceRoundabout(Vector2Int)`, `bool TryPlaceRoundabout(Vector2Int)`, `bool TryRemoveRoundabout(Vector2Int)`, 내부 `HashSet<Vector2Int> _roundaboutSet`(Task 2가 Resolve에 전달). Task 3·4가 `_placedRoundabouts`/`RoundaboutTiles` 사용.

- [ ] **Step 1: 실패하는 테스트 작성** — `Assets/Tests/EditMode/RoundaboutTests.cs` 신규. 기하는 SignalPlacementTests.Build와 동일(검증된 좌표 — 임의 변경 금지):

```csharp
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    // 회전교차로(스펙 2026-07-11): 신호·무신호에 이은 셋째 형제. 배치 모드 전용, 신호와 배타.
    public class RoundaboutTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        // SignalPlacementTests와 동일 기하: 직선 도로 + 곁가지 2개 → 교차로 (3,0)·(6,0).
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
        public void Place_OnIntersection_Works_AndListsFlatSorted()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsTrue(e.CanPlaceRoundabout(V(6, 0)));
            Assert.IsTrue(e.TryPlaceRoundabout(V(6, 0)));     // 뒤 타일 먼저
            Assert.IsTrue(e.TryPlaceRoundabout(V(3, 0)));
            Assert.AreEqual(2, e.RoundaboutTiles.Count);
            Assert.AreEqual(V(3, 0), e.RoundaboutTiles[0]);   // flat 정렬(결정론)
            Assert.AreEqual(V(6, 0), e.RoundaboutTiles[1]);
        }

        [Test]
        public void Place_RejectsNonIntersection_Duplicate_AndAutoMode()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsFalse(e.TryPlaceRoundabout(V(1, 0)));    // 직선 도로 — 교차로 아님
            Assert.IsFalse(e.TryPlaceRoundabout(V(1, 1)));    // 도로 아님
            Assert.IsTrue(e.TryPlaceRoundabout(V(3, 0)));
            Assert.IsFalse(e.TryPlaceRoundabout(V(3, 0)));    // 중복
            Assert.IsFalse(e.CanPlaceRoundabout(V(3, 0)));

            var auto = Build(autoDetect: true, out _);
            Assert.IsFalse(auto.CanPlaceRoundabout(V(3, 0))); // 자동 모드 — 배치 개념 없음
            Assert.IsFalse(auto.TryPlaceRoundabout(V(3, 0)));
        }

        [Test]
        public void SignalAndRoundabout_AreMutuallyExclusive()
        {
            var e = Build(autoDetect: false, out _);
            Assert.IsTrue(e.TryPlaceSignal(V(3, 0), 8));
            Assert.IsFalse(e.CanPlaceRoundabout(V(3, 0)));    // 신호 위에 로터리 금지
            Assert.IsFalse(e.TryPlaceRoundabout(V(3, 0)));
            Assert.IsTrue(e.TryPlaceRoundabout(V(6, 0)));
            Assert.IsFalse(e.CanPlaceSignal(V(6, 0)));        // 로터리 위에 신호 금지
            Assert.IsFalse(e.TryPlaceSignal(V(6, 0), 8));
            // 철거 후엔 교체 가능(한 타일 한 장치 — 교체는 "철거 후 배치")
            Assert.IsTrue(e.TryRemoveRoundabout(V(6, 0)));
            Assert.IsTrue(e.TryPlaceSignal(V(6, 0), 8));
        }

        [Test]
        public void Remove_Works_AndRejectsAbsent()
        {
            var e = Build(autoDetect: false, out _);
            e.TryPlaceRoundabout(V(3, 0));
            Assert.IsTrue(e.TryRemoveRoundabout(V(3, 0)));
            Assert.AreEqual(0, e.RoundaboutTiles.Count);
            Assert.IsFalse(e.TryRemoveRoundabout(V(3, 0)));   // 이미 없음
        }

        [Test]
        public void RoadRemoval_KillsRoundaboutNextRebuild()
        {
            var e = Build(autoDetect: false, out _);
            e.TryPlaceRoundabout(V(3, 0));
            e.Remove(V(3, 1));                                // 곁가지 철거 → (3,0) 교차로 해제
            e.Tick(0.25f);                                    // 재구축 소비
            Assert.AreEqual(0, e.RoundaboutTiles.Count);      // 자동 소멸(신호와 동일 규약)
            Assert.IsFalse(e.TryRemoveRoundabout(V(3, 0)));
        }
    }
}
```

- [ ] **Step 2: 컴파일 실패 확인** — Unity MCP `read_console`(도메인 리로드 후): `TryPlaceRoundabout` 미정의 CS 에러가 떠야 정상(테스트가 먼저).

- [ ] **Step 3: 최소 구현** — 3파일 수정.

`ISignalControl.cs` — 인터페이스 끝(TryRemoveSignal 아래)에 추가:

```csharp
        // 회전교차로 배치(스펙 2026-07-11): 신호와 배타(한 타일 한 장치). 배치 모드 전용.
        // 조율값 없음 — "조율 안 해도 흐르는 것"이 정체성. 수식(λ 0.25·용량 ×0.7)은 엔진 소관.
        // 제안: 상점 UI 창구(신호 3종의 자매). 최종 확정은 김건 합의.
        IReadOnlyList<Vector2Int> RoundaboutTiles { get; }
        bool CanPlaceRoundabout(Vector2Int tile);
        bool TryPlaceRoundabout(Vector2Int tile);
        bool TryRemoveRoundabout(Vector2Int tile);
```

`SimEngine.cs` — 필드는 `_placedSet` 선언(22행) 바로 아래:

```csharp
        // 회전교차로(스펙 2026-07-11): 신호와 배타 배치. SignalMap 무관 — FlowSolver가 셋을 직접 봄.
        readonly List<Vector2Int> _placedRoundabouts = new();
        readonly HashSet<Vector2Int> _roundaboutSet = new();
```

API는 `TryRemoveSignal` 메서드 아래:

```csharp
        // ── 회전교차로 배치(스펙 2026-07-11): 신호 3종의 자매. Rebuild 불필요(SignalMap 무관) ──
        public IReadOnlyList<Vector2Int> RoundaboutTiles => _placedRoundabouts;

        public bool CanPlaceRoundabout(Vector2Int tile) =>
            !_config.AutoDetectSignals && _grid.IsIntersection(tile)
            && !_roundaboutSet.Contains(tile) && !_placedSet.Contains(tile);   // 신호와 배타

        public bool TryPlaceRoundabout(Vector2Int tile)
        {
            if (!CanPlaceRoundabout(tile)) return false;
            int flat = tile.y * _config.GridWidth + tile.x;
            int idx = _placedRoundabouts.FindIndex(t => t.y * _config.GridWidth + t.x > flat);
            if (idx < 0) _placedRoundabouts.Add(tile); else _placedRoundabouts.Insert(idx, tile);
            _roundaboutSet.Add(tile);
            return true;
        }

        public bool TryRemoveRoundabout(Vector2Int tile)
        {
            if (_config.AutoDetectSignals || !_roundaboutSet.Remove(tile)) return false;
            _placedRoundabouts.Remove(tile);
            return true;
        }
```

`CanPlaceSignal`(195행) — 로터리 배타 추가:

```csharp
        public bool CanPlaceSignal(Vector2Int tile) =>
            !_config.AutoDetectSignals && _grid.IsIntersection(tile)
            && !_placedSet.Contains(tile) && !_roundaboutSet.Contains(tile);   // 로터리와 배타
```

`RebuildSignals()`(98행) — 배치 모드 분기에 로터리 소멸 추가(`_placedSignals.RemoveAll` 바로 뒤):

```csharp
            _placedRoundabouts.RemoveAll(t =>
            {
                if (_grid.IsIntersection(t)) return false;
                _roundaboutSet.Remove(t);      // 교차로 해제 → 로터리도 소멸(신호와 동일 규약)
                return true;
            });
```

- [ ] **Step 4: 컴파일 확인 + 테스트 그린** — `read_console` 에러 0 → `run_tests`(EditMode, filter `RoundaboutTests`): 5/5 PASS. 이어서 filter `SignalPlacementTests`: 10/10 PASS(배타 추가 회귀 없음).

- [ ] **Step 5: 커밋**

```bash
cd ~/Gamemaker/GreenLight
git add Assets/01_Scripts/CityFlow/Sim/SimEngine.cs Assets/01_Scripts/CityFlow/Contracts/ISignalControl.cs Assets/Tests/EditMode/RoundaboutTests.cs Assets/Tests/EditMode/RoundaboutTests.cs.meta
git commit -m "[Feat] 회전교차로 배치 API — 신호와 배타, ISignalControl 제안 4종"
```

---

### Task 2: 수식 — SimConfig 계수 + FlowSolver 분기 + 배선

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimConfig.cs` (필드 ~58행 옆 + Default ~108행)
- Modify: `Assets/01_Scripts/CityFlow/Sim/FlowSolver.cs` (Resolve 오버로드 ~103행, 무신호 루프 ~134-149행)
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimEngine.cs` (Step 82행 + SettleOffline 132행의 Resolve 호출)
- Test: `Assets/Tests/EditMode/RoundaboutTests.cs` (수식 테스트 추가)

**Interfaces:**
- Consumes: Task 1의 `_roundaboutSet`, `TryPlaceRoundabout`.
- Produces: `SimConfig.RoundaboutInterference`/`.RoundaboutCapacityFactor`, `FlowSolver.Resolve(in SimConfig, SignalMap, CityGrid, HashSet<Vector2Int> roundabouts, double simTime = 0)` (기존 3-인자 Resolve는 null 위임 — 기존 테스트 무수정 생존).

- [ ] **Step 1: 실패하는 테스트 작성** — RoundaboutTests에 추가. 기하는 SignalPlacementTests.PlacedMode_UnsignaledInterferenceIsLive의 십자 도시 재사용(dev-log-12 기술노트 3: 이 좌표는 코너컷·배치 충돌 검증 완료 — 임의 변경 금지).

**비교의 근거(2026-07-11 실측 보정):** DemandMap은 다목적지(집마다 회사·학교 수요 각 1건)라 손계산 fH=fV는 성립하지 않는다 — hx=vy=6, d=1.5 실측: fH(6,6)=9, fV(6,6)=7.5, 동쪽 간선 ratio 1.5, 세로 간선 (6,4) ratio 1.25. 대신 **라우팅은 신호 무관이라 시나리오(무신호/신호/로터리) 간 흐름이 완전히 동일** — 교차로 계수만 다르므로 비교가 성립한다. greenSlots=9(g=0.5625)로 신호 학교축 병목이 1.4286(교차로)이 되고, 로터리는 1.1607로 내려가 병목이 간선 1.25로 이동 → 학교행 경로가 엄격히 이긴다(회사행은 양쪽 다 동쪽 간선 1.5에 막혀 동률 — 총합은 로터리 승).

```csharp
        // 십자 도시(검증된 좌표): 가로 간선(y=6)·세로 간선(x=6), H집 hx개(행7)·V집 vy개(x=5열).
        // 주의: DemandMap은 다목적지(집마다 회사·학교 수요) — 손계산 fH/fV는 안 맞는다.
        // 비교의 근거는 "시나리오 간 흐름 동일"(라우팅 신호 무관): 교차로 계수 차이만 남는다.
        static SimEngine BuildCross(int hHouses, int vHouses, float demandPerHouse,
                                    out SimEventHub hub)
        {
            var c = SimConfig.Default();
            c.TickInterval = 0.25f;
            c.GridWidth = 13; c.GridHeight = 13;
            c.DemandPerHouse = demandPerHouse;
            c.RoadCapacity = 12f;
            c.DemandChoicePool = 1;
            c.SchoolCapacity = vHouses;     // V집이 학교를 정확히 채움 → H집은 회사로
            c.OfficeCapacity = 20;
            c.RushAmplitude = 0f;
            c.AutoDetectSignals = false;
            hub = new SimEventHub();
            var e = new SimEngine(c, hub);
            for (int x = 0; x <= 12; x++) e.Place(V(x, 6), TileType.Road);
            for (int y = 0; y <= 12; y++) if (y != 6) e.Place(V(6, y), TileType.Road);
            for (int i = 0; i < hHouses; i++) e.Place(V(i, 7), TileType.House);
            for (int i = 0; i < vHouses; i++) e.Place(V(5, i), TileType.House);
            e.Place(V(12, 7), TileType.Office);
            e.Place(V(5, 12), TileType.School);
            e.Tick(0.25f);
            return e;
        }

        enum Node { None, Signal, Roundabout }

        static float Run(int hHouses, int vHouses, float demand, Node node, int greenSlots = 8)
        {
            var e = BuildCross(hHouses, vHouses, demand, out _);
            if (node == Node.Signal) Assert.IsTrue(e.TryPlaceSignal(V(6, 6), greenSlots));
            if (node == Node.Roundabout) Assert.IsTrue(e.TryPlaceRoundabout(V(6, 6)));
            e.Tick(0.25f);
            return e.DeliveredTotal;
        }

        [Test]
        public void BalancedCross_RoundaboutBeatsSignal_BeatsNothing()
        {
            // 실측 fH=9, fV=7.5. 교차로 ratio: 무신호 H(9+11.25)/12=1.6875·V 1.75 /
            // 신호 g=9/16: H 1.333·V 1.4286 / 로터리 H 1.2946·V 1.1607.
            // 학교행 병목: 무신호 1.75 → 신호 1.4286 → 로터리 1.25(간선으로 이동) — 사슬 전체 엄격.
            float none = Run(6, 6, 1.5f, Node.None);
            float signal = Run(6, 6, 1.5f, Node.Signal, greenSlots: 9);
            float ra = Run(6, 6, 1.5f, Node.Roundabout);
            Assert.Less(none, signal);
            Assert.Less(signal, ra);     // 균형 교차로 = 로터리가 최적(스펙 §1 3분할)
        }

        [Test]
        public void AsymmetricCross_SignalBeatsRoundabout()
        {
            // 편중 십자(5집 vs 2집): 큰축 몰빵 신호(g=11/16)가 로터리를 이긴다.
            // (실측 흐름은 다목적지 수요로 손계산과 다르지만 편중 영역 유지 — 부등식 실측 검증)
            float signal = Run(5, 2, 2f, Node.Signal, greenSlots: 11);   // 큰축에 초록 몰빵
            float ra = Run(5, 2, 2f, Node.Roundabout);
            Assert.Less(ra, signal);     // 편중 교차로 = 신호가 최적
        }

        [Test]
        public void NoCrossTraffic_RoundaboutIsWaste()
        {
            // 극단 편중의 끝점(s=0): 교차 교통이 없으면 로터리는 주축 감속(용량 ×0.7)만 남는다.
            // 집1·회사1 단일 직선 경로 + 더미 지선(교차로 성립용) — 다목적지 수요 오염이 구조적으로 불가능.
            // ratio(H축): 무신호 14/12=1.167(교차 0이라 λ 무관) / 로터리 14/8.4=1.667.
            var c = SimConfig.Default();
            c.TickInterval = 0.25f;
            c.GridWidth = 13; c.GridHeight = 13;
            c.DemandPerHouse = 14f;
            c.RoadCapacity = 12f;
            c.RushAmplitude = 0f;
            c.AutoDetectSignals = false;
            System.Func<bool, float> run = placeRoundabout =>
            {
                var e = new SimEngine(c, new SimEventHub());
                for (int x = 0; x <= 12; x++) e.Place(V(x, 6), TileType.Road);
                e.Place(V(6, 5), TileType.Road);              // 더미 지선 → (6,6) 교차로 성립
                e.Place(V(6, 7), TileType.Road);
                e.Place(V(0, 7), TileType.House);
                e.Place(V(12, 7), TileType.Office);
                e.Tick(0.25f);
                if (placeRoundabout) Assert.IsTrue(e.TryPlaceRoundabout(V(6, 6)));
                e.Tick(0.25f);
                return e.DeliveredTotal;
            };
            Assert.Less(run(true), run(false));   // 극단 편중 = 돈 쓰면 손해(전략 3분할의 셋째 날)
        }

        [Test]
        public void Roundabout_IsDeterministic()
        {
            float a = Run(6, 6, 1.5f, Node.Roundabout);
            float b = Run(6, 6, 1.5f, Node.Roundabout);
            Assert.AreEqual(a, b);       // 같은 입력 = 같은 delivered(기존 관례)
        }
```

- [ ] **Step 2: 테스트 실패 확인** — `read_console` 컴파일 OK 후 `run_tests`(filter `RoundaboutTests`): 신규 4개 중 **Balanced·NoCross 2개 FAIL**(로터리가 수식에 없어 무신호와 동일 → Less 불성립). Asymmetric·Determinism은 수식 전에도 성립하는 약한 핀이라 PASS — 정상. 기존 5개 PASS.

- [ ] **Step 3: 구현**

`SimConfig.cs` — `UnsignaledInterference` 필드(58행) 아래:

```csharp
        // ── 회전교차로(스펙 2026-07-11): 낮은 양보 간섭 + 전원 감속(용량 페널티) ──
        // 균형 교차로(s>2/3)=로터리, 편중(0.375~2/3)=신호, 극단(<0.375)=무신호가 최적 — 3분할 전략.
        // 상수 λ만 쓰면 최적 신호를 항상 이겨 전략이 죽는다(스펙 §1) — cf<1이 균형추 🔓
        public float RoundaboutInterference;    // λr: 교차 교통의 방해 계수
        public float RoundaboutCapacityFactor;  // cf: 로터리 타일 유효 용량 배율

        // ── 유기적 라우팅(혼잡 회피 강도) ──
```

`Default()`의 `UnsignaledInterference = 1.5f,` 아래:

```csharp
            RoundaboutInterference = 0.25f,
            RoundaboutCapacityFactor = 0.7f,
```

`FlowSolver.cs` — 기존 캐노니컬(103행)을 위임으로 바꾸고 5-인자 신설:

```csharp
        // 로터리 없는 호출(기존 테스트 호환).
        public void Resolve(in SimConfig cfg, SignalMap signals, CityGrid grid, double simTime = 0)
            => Resolve(cfg, signals, grid, null, simTime);

        // 캐노니컬: delivered = 수요 × E(축별 병목) × SignalFactor(그린웨이브).
        // roundabouts = 엔진 소유 배치 셋(조회만 — 소유·갱신은 SimEngine, 스펙 §2).
        public void Resolve(in SimConfig cfg, SignalMap signals, CityGrid grid,
                            HashSet<Vector2Int> roundabouts, double simTime = 0)
```

무신호 루프(①'') 본문 — 신호 스킵 뒤에 로터리 분기(기존 무신호 두 줄은 else로):

```csharp
                        if (signals != null && signals.TryGet(t, out _)) continue;   // 신호가 처리함
                        int i = Index(t);
                        if (roundabouts != null && roundabouts.Contains(t))
                        {
                            // 로터리: 양보 간섭 급감(λr) 대신 전원 감속(용량 ×cf) — 스펙 §1 수식.
                            float cap = cfg.RoadCapacity * cfg.RoundaboutCapacityFactor;
                            _ratioH[i] = (_flowH[i] + cfg.RoundaboutInterference * _flowV[i]) / cap;
                            _ratioV[i] = (_flowV[i] + cfg.RoundaboutInterference * _flowH[i]) / cap;
                        }
                        else
                        {
                            _ratioH[i] = (_flowH[i] + cfg.UnsignaledInterference * _flowV[i]) / cfg.RoadCapacity;
                            _ratioV[i] = (_flowV[i] + cfg.UnsignaledInterference * _flowH[i]) / cfg.RoadCapacity;
                        }
                        _level[i] = Classify(Mathf.Max(_ratioH[i], _ratioV[i]), cfg);
```

주석도 갱신: 루프 머리의 `// ①'' 무신호 교차로:` 주석에 `로터리는 λr·cf(스펙 2026-07-11)` 한 줄 추가.

`SimEngine.cs` — Resolve 호출 2곳(Step 82행, SettleOffline 132행)에 셋 전달:

```csharp
            _solver.Resolve(_config, _signals, _grid, _roundaboutSet, _simTime);
```

- [ ] **Step 4: 테스트 그린** — `read_console` 에러 0 → `run_tests`(filter `RoundaboutTests`): 9/9 PASS.

- [ ] **Step 5: 전체 회귀** — `run_tests`(EditMode 전체): 기존 131 + 신규 9 = 140 PASS.

- [ ] **Step 6: 커밋**

```bash
cd ~/Gamemaker/GreenLight
git add Assets/01_Scripts/CityFlow/Sim/SimConfig.cs Assets/01_Scripts/CityFlow/Sim/FlowSolver.cs Assets/01_Scripts/CityFlow/Sim/SimEngine.cs Assets/Tests/EditMode/RoundaboutTests.cs
git commit -m "[Feat] 회전교차로 수식 — λ 0.25 + 용량 ×0.7, 3분할 전략 테스트로 고정"
```

---

### Task 3: 세이브 — Roundabouts 필드 + 복원

**Files:**
- Create: `Assets/01_Scripts/CityFlow/Contracts/Save/RoundaboutSaveData.cs`
- Modify: `Assets/01_Scripts/CityFlow/Contracts/Save/SimSaveData.cs`
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimEngine.cs` (CreateSnapshot ~311행, RestoreSnapshot ~339행)
- Test: `Assets/Tests/EditMode/RoundaboutTests.cs`

**Interfaces:**
- Consumes: Task 1의 `_placedRoundabouts`/`_roundaboutSet`, 기존 스냅샷 흐름.
- Produces: `SimSaveData.Roundabouts` (`RoundaboutSaveData[]`, 좌표만). 구세이브 = null → 로터리 0개.

- [ ] **Step 1: 실패하는 테스트 작성** — RoundaboutTests에 추가(직선 기하 Build 재사용):

```csharp
        [Test]
        public void SaveRoundtrip_RestoresRoundabouts()
        {
            var e = Build(autoDetect: false, out _);
            e.TryPlaceRoundabout(V(6, 0));
            e.TryPlaceRoundabout(V(3, 0));
            var snap = e.CreateSnapshot();

            var fresh = Build(autoDetect: false, out _);
            fresh.RestoreSnapshot(snap);
            fresh.Tick(0.25f);
            Assert.AreEqual(2, fresh.RoundaboutTiles.Count);
            Assert.AreEqual(V(3, 0), fresh.RoundaboutTiles[0]);       // flat 정렬 복구
            Assert.IsTrue(fresh.TryRemoveRoundabout(V(3, 0)));        // 소유까지 복원
            Assert.IsFalse(fresh.CanPlaceSignal(V(6, 0)));            // 배타도 복원됨
        }

        [Test]
        public void LegacySave_WithoutRoundabouts_RestoresClean()
        {
            var e = Build(autoDetect: false, out _);
            var snap = e.CreateSnapshot();
            snap.Roundabouts = null;                                  // 구세이브 = 필드 없음
            var fresh = Build(autoDetect: false, out _);
            fresh.TryPlaceRoundabout(V(3, 0));                        // 이전 세션 잔존 상태
            fresh.RestoreSnapshot(snap);
            fresh.Tick(0.25f);
            Assert.AreEqual(0, fresh.RoundaboutTiles.Count);          // 복원 = 전체 교체
        }
```

- [ ] **Step 2: 테스트 실패 확인** — `snap.Roundabouts` 미정의 CS 에러(`read_console`) — 테스트가 먼저.

- [ ] **Step 3: 구현**

`RoundaboutSaveData.cs` 신규:

```csharp
using System;

namespace CityFlow.Contracts.Save
{
    // 회전교차로는 좌표만 — 조율값 없음(스펙 2026-07-11). SignalSaveData의 자매.
    [Serializable]
    public sealed class RoundaboutSaveData
    {
        public int X;
        public int Y;
    }
}
```

`SimSaveData.cs`:

```csharp
        public TileSaveData[] PlacedTiles;
        public SignalSaveData[] SignalOffsets;
        public RoundaboutSaveData[] Roundabouts;   // 구세이브 = null(로터리 0개) — 마이그레이션 공짜
```

`SimEngine.CreateSnapshot` — signals 리스트 만든 뒤:

```csharp
            var roundabouts = new RoundaboutSaveData[_placedRoundabouts.Count];
            for (int i = 0; i < _placedRoundabouts.Count; i++)
                roundabouts[i] = new RoundaboutSaveData { X = _placedRoundabouts[i].x, Y = _placedRoundabouts[i].y };

            return new SimSaveData { PlacedTiles = tiles.ToArray(), SignalOffsets = signals.ToArray(), Roundabouts = roundabouts };
```

`SimEngine.RestoreSnapshot` — 배치 모드 블록(`if (!_config.AutoDetectSignals)`) 안, `_placedSignals.Sort` 뒤에:

```csharp
                _placedRoundabouts.Clear();
                _roundaboutSet.Clear();
                if (snapshot.Roundabouts != null)
                    foreach (var r in snapshot.Roundabouts)
                    {
                        var tile = new Vector2Int(r.X, r.Y);
                        if (_roundaboutSet.Add(tile)) _placedRoundabouts.Add(tile);
                    }
                _placedRoundabouts.Sort((a, b) =>
                    (a.y * _config.GridWidth + a.x).CompareTo(b.y * _config.GridWidth + b.x));
                // 비교차로 잔재는 직후 RebuildSignals()의 소멸 프루닝이 청소(신호와 동일 경로).
```

- [ ] **Step 4: 테스트 그린** — `run_tests`(filter `RoundaboutTests`): 11/11 PASS. 이어서 `SignalPlacementTests` 10/10(스냅샷 회귀 없음).

- [ ] **Step 5: 커밋**

```bash
cd ~/Gamemaker/GreenLight
git add Assets/01_Scripts/CityFlow/Contracts/Save/RoundaboutSaveData.cs Assets/01_Scripts/CityFlow/Contracts/Save/RoundaboutSaveData.cs.meta Assets/01_Scripts/CityFlow/Contracts/Save/SimSaveData.cs Assets/01_Scripts/CityFlow/Sim/SimEngine.cs Assets/Tests/EditMode/RoundaboutTests.cs
git commit -m "[Feat] 회전교차로 세이브 — 좌표 배열 optional 필드, 구세이브 무마이그레이션"
```

---

### Task 4: 뷰 — 원형 마커 + 차가 도는 연출

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/View/MainCityView.cs` (필드 ~53행, Initialize/Update ~140-167행, RefreshSignals 뒤 ~365행, MoveVehicle ~545행, IsSignalTile 뒤 ~664행)

**Interfaces:**
- Consumes: Task 1의 `simEngine.RoundaboutTiles`(폴링), 기존 `GridToLocal`/`ApplyRendererColor`/`PrepareRenderer`/`ContainsSignal`/laneOffset 인프라.
- Produces: 뷰 전용 — 후속 소비자 없음.

- [ ] **Step 1: 마커 구현** — 필드(53행 `signalVisuals` 옆):

```csharp
        private readonly Dictionary<Vector2Int, GameObject> roundaboutVisuals = new();
```

세팅 필드(36행 laneOffset 옆):

```csharp
        [SerializeField] private float roundaboutOrbitRadius = 0.3f;   // 로터리 궤도 반경(타일 비율)
```

색상(49행 selectedSignalColor 옆):

```csharp
        [SerializeField] private Color roundaboutColor = new Color(0.35f, 0.78f, 0.45f);
```

`RefreshSignals()` 아래 메서드 2개:

```csharp
        // 로터리 마커: RoundaboutTiles 폴링으로 생성/제거 — RefreshSignals와 동일 수명 규약.
        private void RefreshRoundabouts()
        {
            if (simEngine == null)
            {
                return;
            }

            IReadOnlyList<Vector2Int> tiles = simEngine.RoundaboutTiles;

            for (int i = 0; i < tiles.Count; i++)
            {
                if (!roundaboutVisuals.ContainsKey(tiles[i]))
                {
                    roundaboutVisuals.Add(tiles[i], CreateRoundaboutVisual(tiles[i]));
                }
            }

            foreach (Vector2Int tile in new List<Vector2Int>(roundaboutVisuals.Keys))
            {
                if (ContainsSignal(tiles, tile))
                {
                    continue;
                }

                Destroy(roundaboutVisuals[tile]);
                roundaboutVisuals.Remove(tile);
            }
        }

        private GameObject CreateRoundaboutVisual(Vector2Int tile)
        {
            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = $"Roundabout_{tile.x}_{tile.y}";
            ring.transform.SetParent(signalRoot, false);
            ring.transform.localPosition = GridToLocal(tile, signalZ);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // 원반을 보드(XY)와 평행하게
            ring.transform.localScale = new Vector3(tileSize * 0.6f, 0.02f, tileSize * 0.6f);
            ApplyRendererColor(PrepareRenderer(ring.GetComponent<Renderer>()), roundaboutColor);
            return ring;
        }
```

`Initialize`의 `RefreshSignals();` 뒤와 `Update`의 `RefreshSignals();` 뒤에 각각 `RefreshRoundabouts();` 호출 추가.

- [ ] **Step 2: 차가 도는 연출** — `MoveVehicle`에서 위치 대입부(547행 `vehicle.Object.transform.localPosition = Vector3.Lerp(a, b, t) + lane;`)를 다음으로 교체:

```csharp
            Vector3 pos = Vector3.Lerp(a, b, t) + lane;

            // 로터리 연출(뷰 전용 — 엔진 무관): 타일 안에선 진행 방향 오른쪽으로 부풀어
            // 중앙 섬을 반시계로 돌아가는 궤적. 경계에서 0(직선과 연속), 중심에서 최대.
            Vector2Int insideTile = t < 0.5f ? route[index] : route[index + 1];
            if (IsRoundaboutTile(insideTile))
            {
                Vector3 center = GridToLocal(insideTile, vehicleZ);
                float along = Vector3.Dot(pos - center, travelDir);   // lane은 수직이라 영향 없음
                float bulge = Mathf.Cos(Mathf.PI * Mathf.Clamp(along / tileSize, -0.5f, 0.5f));
                float extra = Mathf.Max(0f, tileSize * (roundaboutOrbitRadius - laneOffset)) * bulge;
                pos += new Vector3(travelDir.y, -travelDir.x, 0f) * extra;
            }

            vehicle.Object.transform.localPosition = pos;
```

`IsSignalTile` 아래에:

```csharp
        private bool IsRoundaboutTile(Vector2Int tile)
        {
            if (simEngine == null)
            {
                return false;
            }

            return ContainsSignal(simEngine.RoundaboutTiles, tile);   // 선형 목록 검색 헬퍼 공용
        }
```

- [ ] **Step 3: 컴파일 확인** — `refresh_unity`(ForceUpdate) → `read_console` 에러 0.

- [ ] **Step 4: Play 프로그래매틱 검증** — 비포커스 에디터 규약(기술노트 2): `EditorApplication.isPaused=true` + `Step()` 펌핑, `Thread.Sleep` 금지. `execute_code`로:
  1. `AssetDatabase.FindAssets("t:SimConfigAsset")`로 씬이 쓰는 config 에셋을 찾아 `AutoDetectSignals` 현재값 기록 후 `false`로 변경(+`AssetDatabase.SaveAssets`).
  2. Play 진입 → 펌핑으로 부트스트랩 틱 소비 → 씬의 `MainCityView`에서 `simEngine` 리플렉션 취득(또는 Bootstrap 서비스 로케이터) → 도로 교차로 하나에 `TryPlaceRoundabout` 성공 확인.
  3. 펌핑 수십 프레임 후 검증: (a) `GameObject.Find("Roundabout_x_y")` 존재, (b) 로터리 타일을 지나는 활성 차량들의 위치를 샘플링 — 타일 중심 근접 시 진행축 수직 거리 > `tileSize*laneOffset*1.5` 인 샘플 존재(부풀음 증거), 로터리 밖 직선 구간은 기존 차선 오프셋 그대로.
  4. Play 종료 → config 에셋 `AutoDetectSignals`를 기록해둔 원래 값으로 복원 + SaveAssets. **복원 누락 금지**(라이브는 자동 모드).

- [ ] **Step 5: 전체 회귀** — `run_tests`(EditMode 전체): 142 PASS(뷰 변경은 테스트 무관이지만 컴파일 회귀 게이트).

- [ ] **Step 6: 커밋**

```bash
cd ~/Gamemaker/GreenLight
git add Assets/01_Scripts/CityFlow/View/MainCityView.cs
git commit -m "[Feat] 회전교차로 뷰 — 원형 마커 + 차량 반시계 궤도 연출"
```

---

## 완료 기준

- EditMode 142/142 (기존 131 + Roundabout 11).
- Play 검증: 마커 표시 + 차량 부풀음 샘플 확인, config 에셋 원복 확인.
- `ISignalControl` 로터리 4종이 "김건 합의 대상" 누적 목록에 추가된 상태(주석 명기) — 합의는 PR에서.
