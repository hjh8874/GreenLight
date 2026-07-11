# 교차로 축분리 + 무신호 간섭 + 오버라이드 양축 초록 — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 교차로 타일의 가로/세로 흐름을 분리해 신호 듀티·무신호 간섭·오버라이드(양축 초록)가 축별로 정직하게 작동하게 한다 — 신호 구매 피벗의 엔진 기초.

**Architecture:** ①`CityGrid.IsIntersection`(lazy, TopologyVersion 키) seam — SignalMap과 FlowSolver가 같은 교차로 규칙 공유. ②`FlowSolver`가 `_flowH/_flowV`(SoA)로 축별 적립·축별 ratio 4분기(일반/신호/오버라이드/무신호간섭)·경로 병목을 건너는 축으로 판정. ③오버라이드 = 양축 초록(정령 마법): `GetSignalPhase` 양축 Green, `Signal.OverrideHorizontal` 은퇴.

**Tech Stack:** Unity 6000.5 C#, EditMode NUnit(InternalsVisibleTo로 Sim internals 접근), Unity MCP(컴파일·테스트).

## Global Constraints

- 스펙: `docs/superpowers/specs/2026-07-11-intersection-axis-split-design.md` — 값 원본.
- 브랜치: `feat-intersection-axis-hwan` (PR#38 스택, 이미 체크아웃). 브랜치 전환 금지.
- 결정론 불변: 고정 순회 순서·순수 함수·Dictionary 순회 금지. 세이브 포맷 무변경. `ISignalControl` 시그니처 무변경(주석만 갱신).
- 확정값: `UnsignaledInterference = 1.5f`(신설), 대각 스텝 = 양축 0.5/0.5(진입 축 기준, 첫 타일은 출발 스텝, 단일 타일 경로 0.5/0.5), 대각 병목 = max(양축).
- 일반 도로(비교차로) ratio = `(flowH+flowV)/C` — 현행과 수치 동일해야 함.
- 커밋 `[Feat]`/`[Test]` 접두, 끝에 `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- Unity MCP 검증 절차(외부 편집은 강제 임포트 필수): ①`execute_code`로 `UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceUpdate); return "ok";`(타임아웃 = 도메인 리로드, 정상) ②리소스 `mcpforunity://editor/state`에서 `is_compiling:false`·`phase:"idle"` 확인 ③`read_console` types=["Error"] 0건(stale 에러면 clear 후 재리프레시) ④`run_tests` EditMode 전체.
- baseline EditMode = **106**. 기존 테스트 중 갱신 허용 = `OverrideSignal_ForcesAxisGreen_ThenCooldownAndExpiry` **하나뿐**(의미 변경 — 스펙 §3). 나머지 회귀 0. (분석 근거: SignalFlowTests 기하는 전부 직선 가로 흐름이라 flowV=0 → 축별 듀티로도 수치 동일.)
- ⚠️ 엔진 사실: BFS 8방향 코너컷 때문에 **꺾는 경로는 교차로 타일을 대각으로 스킵**함. 교차 흐름 테스트 기하는 반드시 **직진 관통**(가로 일직선·세로 일직선)만 사용 — 이 계획의 테스트 기하는 그걸 전제로 이미 검증된 좌표임. 임의 변경 금지.

---

### Task 1: CityGrid.IsIntersection seam (+ SignalMap DRY)

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Sim/CityGrid.cs`
- Modify: `Assets/01_Scripts/CityFlow/Sim/SignalMap.cs` (RoadNeighbors/Dirs 삭제 → grid.IsIntersection 사용)
- Test: `Assets/Tests/EditMode/CityGridIntersectionTests.cs` (신규)

**Interfaces:**
- Consumes: 기존 `CityGrid.TopologyVersion`, `GetTile`, `InBounds`, `Index`.
- Produces: `public bool CityGrid.IsIntersection(Vector2Int t)` — 도로이면서 직각 도로 이웃 ≥3. TopologyVersion 키 lazy 캐시(RoadNetwork.EnsureRegions 패턴). Task 2의 FlowSolver가 사용.

- [ ] **Step 1: 실패 테스트 작성** — `Assets/Tests/EditMode/CityGridIntersectionTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    // CityGrid.IsIntersection: 교차로 규칙(직각 도로 이웃 ≥3)의 단일 출처.
    public class CityGridIntersectionTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        [Test]
        public void IsIntersection_CrossAndTee_DetectedArmsAndStraightNot()
        {
            var g = new CityGrid(5, 5);
            for (int x = 0; x <= 4; x++) g.Place(V(x, 2), TileType.Road);   // 가로줄
            g.Place(V(2, 3), TileType.Road);                                 // T자 가지

            Assert.IsTrue(g.IsIntersection(V(2, 2)));    // T자(이웃 3)
            Assert.IsFalse(g.IsIntersection(V(1, 2)));   // 직선(이웃 2)
            Assert.IsFalse(g.IsIntersection(V(2, 3)));   // 가지 끝(이웃 1)
            Assert.IsFalse(g.IsIntersection(V(0, 0)));   // 도로 아님
            Assert.IsFalse(g.IsIntersection(V(-1, 2)));  // OOB 무사고

            g.Place(V(2, 1), TileType.Road);             // 십자로 승격
            Assert.IsTrue(g.IsIntersection(V(2, 2)));    // 이웃 4
        }

        [Test]
        public void IsIntersection_RecomputesAfterRemove()
        {
            var g = new CityGrid(5, 5);
            for (int x = 0; x <= 4; x++) g.Place(V(x, 2), TileType.Road);
            g.Place(V(2, 3), TileType.Road);
            Assert.IsTrue(g.IsIntersection(V(2, 2)));

            g.Remove(V(2, 3));                            // 가지 철거 → TopologyVersion++
            Assert.IsFalse(g.IsIntersection(V(2, 2)));    // lazy 캐시가 버전 키로 재계산
        }
    }
}
```

- [ ] **Step 2: 실패 확인** — Unity MCP refresh 절차 → `run_tests` EditMode. Expected: 컴파일 에러(`IsIntersection` 미정의).

- [ ] **Step 3: CityGrid 구현** — `CityGrid.cs`의 `ClearTopologyDirty()` 위에 삽입:

```csharp
        // ── 교차로 판정(직각 도로 이웃 ≥3)의 단일 출처 — SignalMap·FlowSolver가 공유.
        // TopologyVersion 키 lazy 캐시(RoadNetwork.EnsureRegions와 같은 패턴). 구매 피벗 2단계에서
        // "교차로 ≠ 신호"가 되므로 신호와 무관한 여기(grid)가 오너.
        static readonly Vector2Int[] OrthoDirs =
            { new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0) };

        bool[] _intersection;
        int _intersectionVersion = -1;

        public bool IsIntersection(Vector2Int t)
        {
            if (!InBounds(t) || GetTile(t) != TileType.Road) return false;
            EnsureIntersections();
            return _intersection[Index(t)];
        }

        void EnsureIntersections()
        {
            if (_intersectionVersion == TopologyVersion) return;
            _intersectionVersion = TopologyVersion;
            _intersection ??= new bool[_tiles.Length];

            for (int y = 0; y < _height; y++)
                for (int x = 0; x < _width; x++)
                {
                    var t = new Vector2Int(x, y);
                    int i = Index(t);
                    if (GetTile(t) != TileType.Road) { _intersection[i] = false; continue; }
                    int n = 0;
                    foreach (var d in OrthoDirs)
                    {
                        var v = t + d;
                        if (InBounds(v) && GetTile(v) == TileType.Road) n++;
                    }
                    _intersection[i] = n >= 3;
                }
        }
```

- [ ] **Step 4: SignalMap DRY** — `SignalMap.cs`에서 `static readonly Vector2Int[] Dirs` 필드와 `static int RoadNeighbors(...)` 메서드를 삭제하고, `Rebuild` 내부의

```csharp
                    if (RoadNeighbors(grid, t) < 3) continue;          // 직선(2)·끝(1)은 신호 없음
```
을
```csharp
                    if (!grid.IsIntersection(t)) continue;             // 교차로 규칙은 CityGrid가 오너
```
로 교체.

- [ ] **Step 5: 통과 + 전체 회귀** — refresh 절차 → `run_tests` EditMode 전체. Expected: 106 + 2 = **108/108** (SignalMap 동작 동일 — 같은 규칙의 위치만 이동).

- [ ] **Step 6: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Sim/CityGrid.cs Assets/01_Scripts/CityFlow/Sim/SignalMap.cs Assets/Tests/EditMode/CityGridIntersectionTests.cs
git commit -m "[Feat] CityGrid.IsIntersection seam — 교차로 규칙 단일 출처화 (SignalMap DRY)

직각 도로 이웃 ≥3 판정을 CityGrid lazy 캐시(TopologyVersion 키)로.
SignalMap이 이를 사용 — 구매 피벗 2단계(교차로≠신호) 대비 seam. 테스트 2종.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: FlowSolver 축분리 + 무신호 간섭 + 오버라이드 양축 용량

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimConfig.cs` (λ 필드)
- Modify: `Assets/05_ScriptableObjects/SimConfig.asset` (execute_code)
- Modify: `Assets/01_Scripts/CityFlow/Sim/FlowSolver.cs` (핵심 — 전면 수정)
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimEngine.cs:77,105` (Resolve에 `_grid` 전달 2곳)
- Test: `Assets/Tests/EditMode/AxisFlowTests.cs` (신규)

**Interfaces:**
- Consumes: Task 1의 `CityGrid.IsIntersection(Vector2Int)`; 기존 `SignalMath.GreenRatio(Signal)`, `Signal.OverrideUntil`.
- Produces: `FlowSolver.Resolve(in SimConfig, SignalMap, CityGrid, double simTime = 0)` 캐노니컬(기존 2개 오버로드는 grid:null 위임 유지 — 테스트 호환). `GetRatio`(타일/flat) = max(축). 테스트 seam `internal float GetFlowHForTest(int flat)` / `GetFlowVForTest(int flat)`. `SimConfig.UnsignaledInterference`(float).

- [ ] **Step 1: SimConfig 필드 + Default**

`SimConfig.cs` 오버라이드 섹션 아래(`OverrideCorridorSignals` 다음)에:

```csharp
        // ── 무신호 교차로 간섭(신호 구매 피벗 1단계) ──
        // 교차 교통 1이 내 축을 λ만큼 방해(양보 협상 오버헤드) — MM식 자연 양보의 rate 근사.
        // λ=1이면 기존 합산과 동일(연속성). 자동생성 유지 중엔 라이브 미노출(모든 교차로에 신호) 🔓
        public float UnsignaledInterference;
```
`Default()`의 `OverrideCorridorSignals = 3,` 아래에:
```csharp
            UnsignaledInterference = 1.5f,
```

- [ ] **Step 2: asset 갱신** — refresh 절차로 Step 1 컴파일 후, `execute_code`:

```csharp
var so = UnityEditor.AssetDatabase.LoadAssetAtPath<CityFlow.Configs.SimConfigAsset>("Assets/05_ScriptableObjects/SimConfig.asset");
so.Value.UnsignaledInterference = 1.5f;
UnityEditor.EditorUtility.SetDirty(so);
UnityEditor.AssetDatabase.SaveAssets();
return "ok";
```
(다른 필드 — RushAmplitude 0.6 등 asset 고유 튜닝 — 건드리지 않음.)

- [ ] **Step 3: 실패 테스트 작성** — `Assets/Tests/EditMode/AxisFlowTests.cs`. 기하는 코너컷 제약(Global Constraints)에 맞게 설계·검증된 좌표 — 그대로 사용:

```csharp
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    // 축분리 + 무신호 간섭(스펙 2026-07-11): 교차로에서 가로/세로 흐름이 분리되어
    // 신호 듀티·간섭·오버라이드가 축별로 정직하게 작동한다.
    public class AxisFlowTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        // 십자 도시: 가로 간선 y=6(x=0..12) + 세로 간선 x=6(y=0..12), 교차로 (6,6) 하나.
        // 가로 흐름 = 서쪽 집들(y=7, 접점 하(x,6)) → 동단 Office(12,7). 직진 관통.
        // 세로 흐름 = 북쪽 집들(x=5, 접점 우(6,y)) → 남단 School(5,12). 직진 관통.
        // ⚠️ 세로 집들의 Office 수요는 꺾는 경로 = 대각 코너컷으로 (6,6)을 스킵(엔진 성질) →
        //    교차로 축 흐름을 오염시키지 않음. 단 간선에 흐름은 추가되므로 RoadCapacity로 여유 확보.
        static CityGrid CrossCity(int hHouses, int vHouses)
        {
            var g = new CityGrid(13, 13);
            for (int x = 0; x <= 12; x++) g.Place(V(x, 6), TileType.Road);
            for (int y = 0; y <= 12; y++) if (y != 6) g.Place(V(6, y), TileType.Road);

            // 가로 집은 전부 서쪽(x<6) — 세로 간선(x=6)과 충돌 금지 + 전원 (6,6) 직진 관통.
            // y=7 행 6채, 넘치면 y=5 행(최대 12채). ((6,7)에 놓으면 Place가 도로라 거부되고
            // 옆집이 세로 도로로 코너컷해 교차로를 스킵 — 기하 검증에서 잡은 함정.)
            for (int i = 0; i < hHouses; i++)
                g.Place(i < 6 ? V(i, 7) : V(i - 6, 5), TileType.House);              // 접점 (x,6)
            for (int i = 0; i < vHouses; i++) g.Place(V(5, i), TileType.House);     // 접점 (6,i)
            g.Place(V(12, 7), TileType.Office);                                      // 접점 (12,6)
            g.Place(V(5, 12), TileType.School);                                      // 접점 (6,12)
            return g;
        }

        // SchoolCapacity = vHouses: 세로 집들(flat 순서상 y가 작아 먼저)이 School 슬롯을 전부 차지
        // → 가로 집들은 School 수요 없음(만석 스킵). Office는 넉넉히 → 전원 배정.
        static SimConfig CrossCfg(int vHouses, float capacity)
        {
            var c = SimConfig.Default();
            c.GridWidth = 13; c.GridHeight = 13;
            c.DemandPerHouse = 1f;
            c.RoadCapacity = capacity;
            c.DemandChoicePool = 1;          // 항상 최근접(결정론 단순화)
            c.SchoolCapacity = vHouses;
            c.OfficeCapacity = 20;
            return c;
        }

        static (FlowSolver solver, SignalMap signals, CityGrid grid) Solve(
            CityGrid g, in SimConfig cfg, bool withSignals, System.Action<SignalMap> tune = null)
        {
            var dm = new DemandMap(cfg); dm.Reassign(g, new RoadNetwork(g));
            var net = new RoadNetwork(g);
            SignalMap signals = null;
            if (withSignals) { signals = new SignalMap(); signals.Rebuild(g); tune?.Invoke(signals); }
            var solver = new FlowSolver(g.Width, g.Height);
            solver.Assign(dm, net, cfg);
            solver.Resolve(cfg, signals, g);
            return (solver, signals, g);
        }

        [Test]
        public void S1_QuietUnsignaledIntersection_NoLoss()
        {
            // 한산(가로3/세로1, C=10): 간섭 있어도 ratio<0.7 → 전 수요 통과. 무신호 = 무해.
            var cfg = CrossCfg(vHouses: 1, capacity: 10f);
            var (solver, _, _) = Solve(CrossCity(3, 1), cfg, withSignals: false);
            // 수요 = 가로 Office 3 + 세로 School 1 + 세로집 Office 1(코너컷 경로) = 5
            Assert.AreEqual(5f, solver.DeliveredTotal, 1e-3f);
        }

        [Test]
        public void S2_BusyUnsignaledIntersection_SignalBeatsIt()
        {
            // 붐빔(6/6, C=12): 무신호 교차로 ratioH=(6+1.5×6)/12=1.25 Jam → 신호(듀티0.5: 6/6=1.0)가 이김.
            var cfg = CrossCfg(vHouses: 6, capacity: 12f);
            var (unsig, _, _) = Solve(CrossCity(6, 6), cfg, withSignals: false);
            var (sig, _, _) = Solve(CrossCity(6, 6), cfg, withSignals: true);
            Assert.Less(unsig.DeliveredTotal, sig.DeliveredTotal);   // 신호를 살 이유
        }

        [Test]
        public void S3_AsymmetricFlows_DutyTuningIsARealDecision()
        {
            // 비대칭(가로9/세로1, C=10): d=0.9(바쁜 축에 몰기) > 기본 d=0.5 — "방향+초"가 진짜 결정.
            var cfg = CrossCfg(vHouses: 1, capacity: 10f);
            var (half, _, _) = Solve(CrossCity(9, 1), cfg, withSignals: true);   // 기본 듀티 8/16=0.5
            var (tuned, _, _) = Solve(CrossCity(9, 1), cfg, withSignals: true, tune: sm =>
            {
                sm.TryGet(V(6, 6), out var s);
                s.GreenSlots = 14;                       // d=14/16=0.875 — 가로에 몰기
            });
            // d=0.5: ratioH=9/5=1.8 → E=0.36 vs d=0.875: 9/8.75≈1.03 → E≈0.98
            Assert.Less(half.DeliveredTotal, tuned.DeliveredTotal);
            // 잘못 산 신호(세로에 몰기)는 더 나쁨 — 오설정 리스크가 실재
            var (wrong, _, _) = Solve(CrossCity(9, 1), cfg, withSignals: true, tune: sm =>
            {
                sm.TryGet(V(6, 6), out var s);
                s.GreenSlots = 2;                        // d=0.125 — 바쁜 가로가 질식
            });
            Assert.Less(wrong.DeliveredTotal, half.DeliveredTotal);
        }

        [Test]
        public void DiagonalStep_SplitsHalfHalf()
        {
            // 대각으로만 이어진 두 도로: 경로의 모든 스텝이 대각 → 양축 0.5/0.5 적립.
            var g = new CityGrid(4, 4);
            g.Place(V(1, 1), TileType.Road);
            g.Place(V(2, 2), TileType.Road);
            g.Place(V(0, 1), TileType.House);    // 접점 우(1,1)
            g.Place(V(3, 2), TileType.Office);   // 접점 좌(2,2)
            var cfg = SimConfig.Default();
            cfg.GridWidth = 4; cfg.GridHeight = 4; cfg.DemandPerHouse = 1f;

            var (solver, _, _) = Solve(g, cfg, withSignals: false);
            int i11 = 1 * 4 + 1, i22 = 2 * 4 + 2;
            Assert.AreEqual(0.5f, solver.GetFlowHForTest(i11), 1e-4f);
            Assert.AreEqual(0.5f, solver.GetFlowVForTest(i11), 1e-4f);
            Assert.AreEqual(0.5f, solver.GetFlowHForTest(i22), 1e-4f);
            Assert.AreEqual(0.5f, solver.GetFlowVForTest(i22), 1e-4f);
        }

        [Test]
        public void Determinism_SameCitySameResolve_SameDelivered()
        {
            var cfg = CrossCfg(vHouses: 6, capacity: 12f);
            var (a, _, _) = Solve(CrossCity(6, 6), cfg, withSignals: false);
            var (b, _, _) = Solve(CrossCity(6, 6), cfg, withSignals: false);
            Assert.AreEqual(a.DeliveredTotal, b.DeliveredTotal);
        }
    }
}
```

- [ ] **Step 4: 실패 확인** — refresh → `run_tests` EditMode. Expected: 컴파일 에러(`Resolve(cfg, signals, g)` 오버로드·`GetFlowHForTest`·`UnsignaledInterference` 미정의 → Step 1 후엔 λ만 통과).

- [ ] **Step 5: FlowSolver 구현** — `FlowSolver.cs` 수정. 필드 교체:

```csharp
        readonly float[] _flowH;   // 타일별 가로축 흐름(대/초). 대각 스텝은 양축 0.5씩(근사)
        readonly float[] _flowV;
        readonly float[] _ratioH;  // 축별 ratio. 교차로가 아니면 양축 동일(합산/C = 기존 규약)
        readonly float[] _ratioV;
```
(기존 `_flow`/`_ratio` 삭제. 생성자에서 4개 배열 할당으로 교체.)

`Assign`의 flow 적립 루프 교체:

```csharp
                for (int p = 0; p < path.Count; p++)
                {
                    var (wH, wV) = AxisWeights(path, p);
                    int i = Index(path[p]);
                    _flowH[i] += DemandRate * wH;
                    _flowV[i] += DemandRate * wV;
                }
```
(맨 위 `Array.Clear(_flow, ...)`도 `_flowH`/`_flowV` 두 줄로.)

헬퍼 추가:

```csharp
        // 타일 p의 축 가중치 — 진입 스텝 기준(첫 타일은 출발 스텝). 대각 = 양축 절반.
        // 단일 타일 경로(출발=도착)는 축 모호 → 0.5/0.5 (결정론적 근사).
        static (float wH, float wV) AxisWeights(List<Vector2Int> path, int p)
        {
            if (path.Count < 2) return (0.5f, 0.5f);
            var step = p > 0 ? path[p] - path[p - 1] : path[1] - path[0];
            if (step.x != 0 && step.y != 0) return (0.5f, 0.5f);
            return step.y == 0 ? (1f, 0f) : (0f, 1f);
        }

        // 축별 ratio — 용량 0 규약은 기존과 동일(흐르면 최악 병목).
        static float AxisRatio(float flow, float cap, in SimConfig cfg) =>
            cap > 0f ? flow / cap : flow > 0f ? cfg.EfficiencyMinRatio : 0f;
```

`Resolve` 오버로드 3단(기존 2개는 위임 유지):

```csharp
        // 신호·grid 없는 호출(기존 테스트 호환) — 전 타일 일반 도로 규약.
        public void Resolve(in SimConfig cfg) => Resolve(cfg, null, null);

        // grid 없는 호출(기존 테스트 호환) — 무신호 간섭 없음(신호 타일만 축별 듀티).
        public void Resolve(in SimConfig cfg, SignalMap signals, double simTime = 0)
            => Resolve(cfg, signals, null, simTime);

        // 캐노니컬: delivered = 수요 × E(축별 병목) × SignalFactor(그린웨이브).
        public void Resolve(in SimConfig cfg, SignalMap signals, CityGrid grid, double simTime = 0)
```

캐노니컬 본문의 ① 단계(기존 ratio 루프 교체):

```csharp
            // ① 기본: 전 타일 합산 ratio(일반 도로 — 직선엔 교차 충돌 없음). 교차로만 아래서 덮어씀.
            for (int i = 0; i < _flowH.Length; i++)
            {
                float r = (_flowH[i] + _flowV[i]) / cfg.RoadCapacity;
                _ratioH[i] = r;
                _ratioV[i] = r;
                _level[i] = Classify(r, cfg);
            }

            // ①' 신호 교차로: 축별 듀티 용량(가로 d·세로 1−d) — "보는 것 = 버는 것".
            // 오버라이드(정령 마법)는 양축 풀 용량 = 3초간 충돌 소멸(스펙 §3).
            if (signals != null)
            {
                var tiles = signals.Tiles;
                for (int k = 0; k < tiles.Count; k++)
                {
                    if (!signals.TryGet(tiles[k], out var s)) continue;
                    if (s.CycleSlots <= 0) continue;   // 주기 0 = 항상 초록(IsGreen과 같은 규약)
                    bool ovr = s.OverrideUntil > simTime;
                    float g = SignalMath.GreenRatio(s);
                    int i = Index(tiles[k]);
                    _ratioH[i] = AxisRatio(_flowH[i], cfg.RoadCapacity * (ovr ? 1f : g), cfg);
                    _ratioV[i] = AxisRatio(_flowV[i], cfg.RoadCapacity * (ovr ? 1f : 1f - g), cfg);
                    _level[i] = Classify(Mathf.Max(_ratioH[i], _ratioV[i]), cfg);
                }
            }

            // ①'' 무신호 교차로: 간섭 모델 — 교차 교통이 양보 협상만큼(λ) 내 축을 방해(스펙 §2).
            // 자동생성 유지 중엔 라이브 미노출(모든 교차로에 신호) — 구매 피벗 2단계 대비.
            if (grid != null)
            {
                for (int y = 0; y < grid.Height; y++)
                    for (int x = 0; x < grid.Width; x++)
                    {
                        var t = new Vector2Int(x, y);
                        if (!grid.IsIntersection(t)) continue;
                        if (signals != null && signals.TryGet(t, out _)) continue;   // 신호가 처리함
                        int i = Index(t);
                        _ratioH[i] = (_flowH[i] + cfg.UnsignaledInterference * _flowV[i]) / cfg.RoadCapacity;
                        _ratioV[i] = (_flowV[i] + cfg.UnsignaledInterference * _flowH[i]) / cfg.RoadCapacity;
                        _level[i] = Classify(Mathf.Max(_ratioH[i], _ratioV[i]), cfg);
                    }
            }
```

② 병목 루프의 ratio 읽기 교체(경로가 건너는 축 기준, 대각 = max):

```csharp
                for (int p = 0; p < path.Count; p++)
                {
                    var (wH, wV) = AxisWeights(path, p);
                    int idx = Index(path[p]);
                    float rt = wH > 0f && wV > 0f ? Mathf.Max(_ratioH[idx], _ratioV[idx])
                             : wH > 0f ? _ratioH[idx] : _ratioV[idx];
                    if (rt > bottleneck) { bottleneck = rt; bottleneckIdx = idx; } // strict > → 결정론
                }
```

getter 교체 + 테스트 seam:

```csharp
        public CongestionLevel GetCongestion(Vector2Int t) => _level[Index(t)];

        // 단일값 소비자(BurstDetector·CongestionNotifier·차량 감속·안정도)용 — 최악 축.
        // 배관이지 화면 페인트 아님: 혼잡 표현은 차 중심 원칙(스펙 §2, 환 2026-07-11).
        public float GetRatio(Vector2Int t) => GetRatio(Index(t));
        public float GetRatio(int flatIndex) => Mathf.Max(_ratioH[flatIndex], _ratioV[flatIndex]);

        // 테스트 관찰용 seam(InternalsVisibleTo) — 축 적립 검증.
        internal float GetFlowHForTest(int flatIndex) => _flowH[flatIndex];
        internal float GetFlowVForTest(int flatIndex) => _flowV[flatIndex];
```

- [ ] **Step 6: SimEngine 배선** — `SimEngine.cs` 2곳: `Step()`의 `_solver.Resolve(_config, _signals, _simTime);` → `_solver.Resolve(_config, _signals, _grid, _simTime);` / `SettleOffline`의 동일 호출도 같은 형태로.

- [ ] **Step 7: 통과 + 전체 회귀** — refresh → `run_tests` EditMode 전체. Expected: 108 + 5 = **113/113**. (SignalFlowTests는 직선 가로 기하 = flowV 0이라 수치 불변 — 하나라도 깨지면 STOP, 원인 분석 후 보고. 임의 수정 금지.)

- [ ] **Step 8: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Sim/SimConfig.cs Assets/05_ScriptableObjects/SimConfig.asset Assets/01_Scripts/CityFlow/Sim/FlowSolver.cs Assets/01_Scripts/CityFlow/Sim/SimEngine.cs Assets/Tests/EditMode/AxisFlowTests.cs
git commit -m "[Feat] 교차로 축분리 + 무신호 간섭 λ — 신호 듀티가 축별로 정직해짐

FlowSolver가 가로/세로 흐름을 분리 적립(대각 0.5/0.5), ratio 4분기
(일반/신호 듀티/오버라이드 양축 풀/무신호 간섭 λ=1.5). 경로 병목은
건너는 축 기준. 초록 몰기가 교차 축을 실제로 막는 진짜 트레이드오프.
S1~S3·대각·결정론 테스트 5종.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: 오버라이드 양쪽 초록 — 페이즈·필드 은퇴·계약 주석

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Sim/SignalMath.cs` (Signal.OverrideHorizontal 삭제)
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimEngine.cs` (GetSignalPhase 오버라이드 분기 → 양축 Green, OverrideHorizontal 쓰기 삭제)
- Modify: `Assets/01_Scripts/CityFlow/Contracts/ISignalControl.cs` (주석만)
- Test: `Assets/Tests/EditMode/SimEngineTests.cs` (기존 1건 갱신 + 신규 1건)

**Interfaces:**
- Consumes: Task 2의 FlowSolver 오버라이드 분기(이미 양축 풀 — 여긴 페이즈/필드 정리).
- Produces: `GetSignalPhase(tile, horizontal)` — 오버라이드 중 양축 `SignalPhase.Green`. `Signal`에서 `OverrideHorizontal` 제거(계약·세이브 무변경 — 이 필드는 저장 안 됐음).

- [ ] **Step 1: 참조 전수 확인** — `grep -rn "OverrideHorizontal" Assets --include="*.cs" | grep -v Library`. Expected: `SignalMath.cs`(선언), `SimEngine.cs`(쓰기 1·읽기 1)만. 그 외(뷰 등)가 나오면 STOP 후 보고.

- [ ] **Step 2: 기존 테스트 갱신(RED 정의)** — `SimEngineTests.cs`의 `OverrideSignal_ForcesAxisGreen_ThenCooldownAndExpiry`: 이름을 `OverrideSignal_ForcesBothAxesGreen_ThenCooldownAndExpiry`로, 주석과 교차 축 단언을 양축 초록으로:

```csharp
            // 오버라이드 스킬(정령 마법): 양축 강제 초록(충돌 소멸), 쿨다운 중 재사용 거절, 만료 후 복귀.
```
```csharp
            Assert.IsTrue(e.TryOverrideSignal(V(4, 0), horizontal: false));
            Assert.AreEqual(SignalPhase.Green, e.GetSignalPhase(V(4, 0), false));  // 지정 축 초록
            Assert.AreEqual(SignalPhase.Green, e.GetSignalPhase(V(4, 0), true));   // 교차 축도 초록(양축)
```
(그 아래 쿨다운·만료 단언은 그대로.)

- [ ] **Step 3: 실패 확인** — refresh → `run_tests` EditMode `CityFlow.Sim.Tests.SimEngineTests`. Expected: 갱신된 테스트 1건 FAIL(교차 축이 아직 Red).

- [ ] **Step 4: 구현** — 세 곳:

`SignalMath.cs`의 `Signal` 클래스에서 삭제:
```csharp
        public bool OverrideHorizontal = true;
```
(위 `OverrideUntil` 주석의 "한 방향" 문구를 "양축"으로 갱신: `// 오버라이드 스킬(기획 §2-D): 이 시각(simTime)까지 양축 강제 초록 — 정령 마법, 충돌 소멸.`)

`SimEngine.cs` `TryOverrideSignal`의 코리도어 적용 루프에서 `s.OverrideHorizontal = horizontal;` 줄 삭제.

`SimEngine.cs` `GetSignalPhase` 오버라이드 분기 교체:
```csharp
            if (s.OverrideUntil > _simTime)
                return SignalPhase.Green;   // 정령 마법: 양축 초록(충돌 소멸) — 스펙 2026-07-11 §3
```

`ISignalControl.cs` 오버라이드 주석 블록(E-1 3메서드 위) 교체:
```csharp
        // 오버라이드 스킬(기획 §2-D): duration초 양축 강제 초록(정령 마법 — 충돌 소멸) + 엔진 쿨다운.
        // horizontal은 초록 축이 아니라 **코리도어 걷기 방향**(그 라인의 신호들을 함께 발동).
        // 쿨다운을 엔진이 들고 있어 UI(트러스트 경계 밖)가 못 우회 → 조회만 계약으로 노출.
```

- [ ] **Step 5: 신규 테스트 — 오버라이드 양축 용량(솔버 수치)** — `AxisFlowTests.cs`에 추가:

```csharp
        [Test]
        public void Override_BothAxesFullCapacity_ClearsTheJam()
        {
            // S3 도시(9/1, C=10) 재사용: 듀티 0.5면 교차로 ratioH=9/5=1.8(Jam, E=0.36).
            // 오버라이드 = 양축 풀 용량 → ratioH=9/10=0.9 — 3초간 충돌 소멸, 어떤 듀티보다 좋음.
            // (8/8 같은 대칭 기하는 측면 간선 정체가 병목을 지배해 오버라이드 효과가 안 보임 — 금지.)
            var cfg = CrossCfg(vHouses: 1, capacity: 10f);
            var (normal, _, _) = Solve(CrossCity(9, 1), cfg, withSignals: true);
            var (burst, _, _) = Solve(CrossCity(9, 1), cfg, withSignals: true, tune: sm =>
            {
                sm.TryGet(V(6, 6), out var s);
                s.OverrideUntil = 999.0;   // Resolve(simTime 기본 0) 동안 활성
            });
            Assert.AreEqual(CongestionLevel.Jam, normal.GetCongestion(V(6, 6)));      // 평상시 병목
            Assert.AreNotEqual(CongestionLevel.Jam, burst.GetCongestion(V(6, 6)));    // 마법으로 해소
            Assert.Less(normal.DeliveredTotal, burst.DeliveredTotal);
        }
```

- [ ] **Step 6: 통과 + 전체 회귀** — refresh → `run_tests` EditMode 전체. Expected: 113 + 1 = **114/114** (갱신 1건 포함 전부 GREEN).

- [ ] **Step 7: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Sim/SignalMath.cs Assets/01_Scripts/CityFlow/Sim/SimEngine.cs Assets/01_Scripts/CityFlow/Contracts/ISignalControl.cs Assets/Tests/EditMode/SimEngineTests.cs Assets/Tests/EditMode/AxisFlowTests.cs
git commit -m "[Feat] 오버라이드 = 양쪽 초록(정령 마법) — OverrideHorizontal 은퇴

GetSignalPhase 오버라이드 분기가 양축 Green(충돌 소멸). horizontal 파라미터는
코리도어 걷기 방향 전용으로 의미 축소(계약 주석 갱신, 시그니처 불변).
기존 페이즈 테스트 갱신 + 양축 용량 테스트 1종.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## 완료 후

- 플레이 검증(SimDebug/통합 씬): ①초록 몰기 → 교차 축 차들이 실제로 밀림 ②오버라이드 → 코리도어 신호 양축 초록 + 아무도 안 멈춤 ③HUD 처리량이 듀티 조작에 반응.
- PR#38 머지 후 develop 위로 리베이스 → PR to develop. PR 본문에 명기: 초록 레버가 트레이드오프化(밸런스 감각 변화), 오버라이드 양축 초록(정령 마법), 무신호 간섭은 2단계(구매) 대비 잠복.
- 후속 스펙: 혼잡 인지 라우팅(증분 배정) → 2단계(구매/배치).

## Self-Review 결과

- **스펙 커버리지**: §1(Task2 Step5 Assign/AxisWeights) §2 표 4분기(Task2 Step5 ①·①'·①'') §2 병목 축(Task2 ② 루프) §2 max 배관(GetRatio) §3 전체(Task3 + Task2 오버라이드 분기) §4 λ(Task2 Step1-2) §5-2 기존 테스트(Task3 Step2 갱신 1건, SignalFlowTests 불변 분석 명기) 검증계획 6종(S1·S2·S3·양축·대각·결정론) — 전부 매핑.
- **플레이스홀더**: 없음(모든 코드 실체, 테스트 기하 좌표까지 확정).
- **타입 일관성**: `IsIntersection(Vector2Int)`(T1→T2), `Resolve(in SimConfig, SignalMap, CityGrid, double)`(T2 정의·SimEngine 사용), `GetFlowHForTest/GetFlowVForTest(int)`(T2 정의·테스트 사용), `UnsignaledInterference`(T2), `OverrideUntil`(기존) — 일치. S3 듀티 계산: GreenSlots 14/16=0.875(클램프 [1,15] 내 ✓), 2/16=0.125 ✓.
