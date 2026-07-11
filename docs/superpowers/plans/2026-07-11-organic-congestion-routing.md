# 유기적 혼잡 라우팅 (증분 배정 + 물리 거리) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 재건축 시 수요를 혼잡 인지 증분 배정(Dijkstra, 물리 거리 √2)으로 계획해 평행 도로 분산·우회로 흡수가 창발하게 한다 — BFS 최단 단일 경로 은퇴.

**Architecture:** 신규 `RoutePlanner`(Sim 내부)가 TopologyDirty 소비 시점에 수요별 경로 테이블을 계산(앞 수요의 부하를 뒤 수요가 회피). `FlowSolver.Assign`은 매 틱 그 테이블을 읽음. `RoadNetwork`는 FindPath/BFS/캐시를 은퇴하고 접점·도달성(Region)만 남김.

**Tech Stack:** Unity 6000.5 C#, EditMode NUnit(InternalsVisibleTo), Unity MCP(컴파일·테스트).

## Global Constraints

- 스펙: `docs/superpowers/specs/2026-07-11-organic-congestion-routing-design.md` — 값 원본.
- 브랜치: `feat-organic-routing-hwan` (이미 체크아웃, 스택: PR#38←축분리←파밍가드). 브랜치 전환 금지.
- 확정값: 스텝 비용 = **물리거리(직각 1, 대각 √2) × (1 + w × load/RoadCapacity)**, w = `RoutingCongestionWeight = 2f`(신설 🔓, asset+Default). 부하 적립 = `DemandPerHouse`(맥동 무반영 — 평균 기준).
- 결정론: 수요 순서 = DemandMap 순서, Dijkstra 타이브레이크 = (비용 strict <, flat index 오름차순 스캔), Dictionary 순회 금지. 재계획은 topology 변경 시에만(신호·오버라이드·맥동은 트리거 아님).
- 이웃/연결 규칙 = 기존 8방·코너컷 그대로(RoadNetwork.DX/DY와 같은 순서: 직각 4 → 대각 4).
- `Plan`은 틱 파이프라인 밖(재건축 시)이라 힙 할당 허용(경로 List 등). 단 버퍼(비용·방문·cameFrom·load)는 생성자 선할당 재사용.
- 커밋 `[Feat]` 접두 + `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- Unity MCP 검증 절차: ①`execute_code`로 `UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceUpdate); return "ok";`(타임아웃=도메인 리로드, 정상) ②리소스 `mcpforunity://editor/state`에서 idle 확인 ③`read_console` Error 0건(stale이면 clear 후 재시도) ④`run_tests` EditMode.
- baseline EditMode = **117**. Task 1 후 127(+10), Task 2 후 **120**(RoadNetworkTests 경로 7건 이관 삭제). 그 외 기존 테스트 회귀 0 — 깨지면 STOP, 원인 보고(임의 수정 금지).

---

### Task 1: RoutePlanner + SimConfig w (독립 신규 유닛)

**Files:**
- Create: `Assets/01_Scripts/CityFlow/Sim/RoutePlanner.cs`
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimConfig.cs` (필드+Default)
- Modify: `Assets/05_ScriptableObjects/SimConfig.asset` (execute_code)
- Test: `Assets/Tests/EditMode/RoutePlannerTests.cs` (신규)

**Interfaces:**
- Consumes: `RoadNetwork.TryGetAccessRoad(Vector2Int, out Vector2Int)`(유지되는 접점 조회), `DemandMap.Demands`(`IReadOnlyList<Demand>`, `Demand{Source,Sink}`), `CityGrid.GetTile/Width/Height`, `SimConfig.RoutingCongestionWeight/RoadCapacity/DemandPerHouse`.
- Produces (Task 2가 사용): `RoutePlanner(int width, int height)` 생성자; `void Plan(DemandMap demand, RoadNetwork net, CityGrid grid, in SimConfig cfg)`; `IReadOnlyList<List<Vector2Int>> Routes`(수요 인덱스 정렬, 미연결/접점없음 = null); 테스트 seam `internal List<Vector2Int> Search(CityGrid grid, Vector2Int from, Vector2Int to, in SimConfig cfg)`(현재 load 상태 기준 — 신규 planner면 load 0).

- [ ] **Step 1: SimConfig 필드 + Default + asset**

`SimConfig.cs`의 `UnsignaledInterference` 필드 아래에:

```csharp
        // ── 유기적 라우팅(혼잡 회피 강도) ──
        // 증분 배정의 스텝 비용 = 물리거리 × (1 + w × 부하/용량). 0 = 순수 물리 최단.
        // 2면 부하율 1.5 타일이 4배 비쌈 → 몇 칸 우회가 이득 🔓
        public float RoutingCongestionWeight;
```
`Default()`의 `UnsignaledInterference = 1.5f,` 아래에:
```csharp
            RoutingCongestionWeight = 2f,
```
refresh 후 `execute_code`:
```csharp
var so = UnityEditor.AssetDatabase.LoadAssetAtPath<CityFlow.Configs.SimConfigAsset>("Assets/05_ScriptableObjects/SimConfig.asset");
so.Value.RoutingCongestionWeight = 2f;
UnityEditor.EditorUtility.SetDirty(so);
UnityEditor.AssetDatabase.SaveAssets();
return "ok";
```

- [ ] **Step 2: 실패 테스트 작성** — `Assets/Tests/EditMode/RoutePlannerTests.cs` (기하는 코너컷·접점 스캔 규칙 대조로 사전 검증됨 — 좌표 임의 변경 금지):

```csharp
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    // 유기적 라우팅(스펙 2026-07-11): 증분 배정 + 물리 거리(√2).
    // 기하 테스트(구 RoadNetworkTests에서 이관)는 Search seam으로, 분산·흡수는 Plan으로 핀.
    public class RoutePlannerTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static CityGrid GridWithRoads(int w, int h, params Vector2Int[] roads)
        {
            var g = new CityGrid(w, h);
            foreach (var r in roads) g.Place(r, TileType.Road);
            return g;
        }

        static RoutePlanner Fresh(CityGrid g) => new RoutePlanner(g.Width, g.Height);

        static SimConfig Cfg()
        {
            var c = SimConfig.Default();
            c.DemandPerHouse = 1f;
            c.RoadCapacity = 10f;
            c.DemandChoicePool = 1;
            return c;
        }

        // ── 기하(이관): 빈 부하 Search = 물리 최단 ──

        [Test]
        public void Search_StraightLine_InOrder()
        {
            var g = GridWithRoads(5, 5, V(0, 0), V(1, 0), V(2, 0));
            Assert.AreEqual(new[] { V(0, 0), V(1, 0), V(2, 0) },
                Fresh(g).Search(g, V(0, 0), V(2, 0), Cfg()));
        }

        [Test]
        public void Search_LShaped_TakesDiagonalShortcut()
        {
            // 안쪽 코너 대각(√2≈1.41)이 직각 2보다 물리적으로도 짧음 → 지름길 유지.
            var g = GridWithRoads(5, 5, V(0, 0), V(0, 1), V(0, 2), V(1, 2), V(2, 2));
            Assert.AreEqual(new[] { V(0, 0), V(0, 1), V(1, 2), V(2, 2) },
                Fresh(g).Search(g, V(0, 0), V(2, 2), Cfg()));
        }

        [Test]
        public void Search_DiagonalStaircase_ConnectsViaCornerCut()
        {
            var g = GridWithRoads(5, 5, V(0, 0), V(1, 1), V(2, 2));
            Assert.AreEqual(new[] { V(0, 0), V(1, 1), V(2, 2) },
                Fresh(g).Search(g, V(0, 0), V(2, 2), Cfg()));
        }

        [Test]
        public void Search_PrefersStraightOverZigzag_PhysicalDistance()
        {
            // (0,0)→(4,0): 직선 4.0 vs 지그재그(대각 4개) 5.66 — √2 반영으로 직선이 strict 승리.
            var g = GridWithRoads(5, 3,
                V(0, 0), V(1, 0), V(2, 0), V(3, 0), V(4, 0),   // 직선
                V(1, 1), V(3, 1));                               // 지그재그 유혹
            var path = Fresh(g).Search(g, V(0, 0), V(4, 0), Cfg());
            Assert.AreEqual(new[] { V(0, 0), V(1, 0), V(2, 0), V(3, 0), V(4, 0) }, path);
        }

        [Test]
        public void Search_SameTile_ReturnsSingle()
        {
            var g = GridWithRoads(5, 5, V(2, 2));
            Assert.AreEqual(new[] { V(2, 2) }, Fresh(g).Search(g, V(2, 2), V(2, 2), Cfg()));
        }

        [Test]
        public void Search_DisconnectedOrNonRoad_ReturnsNull()
        {
            var g = GridWithRoads(6, 5, V(0, 0), V(1, 0), V(4, 0), V(5, 0));
            Assert.IsNull(Fresh(g).Search(g, V(0, 0), V(5, 0), Cfg()));   // 미연결
            Assert.IsNull(Fresh(g).Search(g, V(0, 0), V(2, 0), Cfg()));   // 끝점이 도로 아님
        }

        // ── 창발(신규): 분산·흡수·결정론 ──

        // 평행 도시: 서 접점 (1,1) ─ 북로 y=0 / 남로 y=2 (같은 물리 길이) ─ 동 접점 (6,1) ─ Office(7,1).
        // 집 2채가 같은 접점(1,1)에서 출발 → 첫 수요가 한 줄을 채우면 둘째는 반대 줄.
        static CityGrid ParallelCity()
        {
            var g = new CityGrid(8, 3);
            g.Place(V(1, 1), TileType.Road);
            g.Place(V(6, 1), TileType.Road);
            for (int x = 2; x <= 5; x++) { g.Place(V(x, 0), TileType.Road); g.Place(V(x, 2), TileType.Road); }
            g.Place(V(1, 0), TileType.House);    // 접점: 하(1,1)? 스캔 상(1,1)아님… 하(1,-1)OOB → 검증: 상(1,1) road ✓
            g.Place(V(0, 1), TileType.House);    // 우(1,1) road ✓
            g.Place(V(7, 1), TileType.Office);   // 좌(6,1) road ✓
            return g;
        }

        [Test]
        public void Plan_ParallelRoads_SplitAcrossBoth()
        {
            var g = ParallelCity();
            var cfg = Cfg();
            var net = new RoadNetwork(g);
            var dm = new DemandMap(cfg); dm.Reassign(g, net);
            Assert.AreEqual(2, dm.Demands.Count);

            var planner = Fresh(g);
            planner.Plan(dm, net, g, cfg);

            bool north0 = planner.Routes[0].Contains(V(3, 0));
            bool north1 = planner.Routes[1].Contains(V(3, 0));
            Assert.AreNotEqual(north0, north1);   // 한 수요는 북로, 다른 수요는 남로 — 분산 창발
        }

        // 우회 도시: 간선 y=1 (1..7) — (1,1) 출발 기준 직선 6.0 / 우회 y=2 (대각 진입) 6.83.
        // w=2: 수요1이 간선을 채우면(×1.2 → 7.0) 수요2부터 우회(6.11)가 이김 — 검증된 수치.
        static CityGrid BypassCity()
        {
            var g = new CityGrid(9, 4);
            for (int x = 1; x <= 7; x++) g.Place(V(x, 1), TileType.Road);          // 간선
            for (int x = 2; x <= 6; x++) g.Place(V(x, 2), TileType.Road);          // 우회(대각 진입/진출)
            g.Place(V(0, 0), TileType.House); g.Place(V(1, 0), TileType.House);    // 접점 둘 다 (1,1)
            g.Place(V(2, 0), TileType.House); g.Place(V(3, 0), TileType.House);
            g.Place(V(4, 0), TileType.House); g.Place(V(5, 0), TileType.House);    // 접점 상(x,1)
            g.Place(V(8, 1), TileType.Office);                                      // 접점 좌(7,1)
            return g;
        }

        [Test]
        public void Plan_CongestedTrunk_BypassAbsorbsOverflow()
        {
            var cfg = Cfg();
            var g = BypassCity();
            var net = new RoadNetwork(g);
            var dm = new DemandMap(cfg); dm.Reassign(g, net);
            Assert.AreEqual(6, dm.Demands.Count);

            var planner = Fresh(g);
            planner.Plan(dm, net, g, cfg);

            bool anyTrunk = false, anyBypass = false;
            foreach (var r in planner.Routes)
            {
                if (r == null) continue;
                if (r.Contains(V(4, 1))) anyTrunk = true;
                if (r.Contains(V(4, 2))) anyBypass = true;
            }
            Assert.IsTrue(anyTrunk, "간선은 여전히 주력");
            Assert.IsTrue(anyBypass, "포화 후 우회로가 흡수");   // 현행 BFS에선 절대 불가능한 동작
        }

        [Test]
        public void Plan_Deterministic_SameCitySamePlan()
        {
            var cfg = Cfg();
            var g = BypassCity();
            var net = new RoadNetwork(g);
            var dm = new DemandMap(cfg); dm.Reassign(g, net);

            var a = Fresh(g); a.Plan(dm, net, g, cfg);
            var b = Fresh(g); b.Plan(dm, net, g, cfg);

            Assert.AreEqual(a.Routes.Count, b.Routes.Count);
            for (int i = 0; i < a.Routes.Count; i++)
                CollectionAssert.AreEqual(a.Routes[i], b.Routes[i]);
        }

        [Test]
        public void Plan_UnreachableDemand_NullRoute()
        {
            // 섬 분리: 집 섬과 회사 섬이 안 이어짐 → 해당 수요 경로 null(무사고).
            var g = new CityGrid(7, 3);
            g.Place(V(0, 1), TileType.Road); g.Place(V(1, 1), TileType.Road);
            g.Place(V(5, 1), TileType.Road); g.Place(V(6, 1), TileType.Road);
            g.Place(V(0, 0), TileType.House);   // 접점 하(0,1)? 스캔 상(0,1)! → 상(0,1) road ✓
            g.Place(V(6, 0), TileType.Office);  // 상? (6,1) road ✓
            var cfg = Cfg();
            var net = new RoadNetwork(g);
            var dm = new DemandMap(cfg); dm.Reassign(g, net);
            Assert.AreEqual(1, dm.Demands.Count);   // 도달성 폴백(최근접 배정, 흐름 0 예정)

            var planner = Fresh(g);
            planner.Plan(dm, net, g, cfg);
            Assert.IsNull(planner.Routes[0]);
        }
    }
}
```

- [ ] **Step 3: 실패 확인** — refresh → `run_tests` EditMode. Expected: 컴파일 에러(`RoutePlanner` 미정의).

- [ ] **Step 4: RoutePlanner 구현** — `Assets/01_Scripts/CityFlow/Sim/RoutePlanner.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Sim
{
    // 유기적 혼잡 라우팅(스펙 2026-07-11): 재건축 시 수요를 고정 순서로 하나씩 배정하고,
    // 앞 수요가 채운 부하를 뒤 수요가 비용으로 회피 → 평행 분산·우회 흡수가 창발.
    // 스텝 비용 = 물리거리(직각 1, 대각 √2) × (1 + w × load/용량). 재계획은 topology 변경 시에만 —
    // 신호 레버·맥동은 트리거 아님("차들은 습관대로, 건설이 습관을 바꾼다").
    // ponytail: 20×20이라 배열 스캔 Dijkstra(O(n²))로 충분 — 힙 불요. 틱 밖이라 경로 List 할당 허용.
    internal sealed class RoutePlanner
    {
        // 이웃 순서는 RoadNetwork(접점·Region)와 동일: 직각 4 → 대각 4 (결정론 공유 규약).
        static readonly int[] DX = { 0, 1, 0, -1, 1, 1, -1, -1 };
        static readonly int[] DY = { 1, 0, -1, 0, 1, -1, -1, 1 };
        const float Sqrt2 = 1.4142135f;

        readonly int _w, _h;
        readonly float[] _cost;      // Dijkstra 누적 비용
        readonly bool[] _done;
        readonly int[] _cameFrom;
        readonly float[] _load;      // 이번 계획에서 이미 배정된 흐름(대/초)

        readonly List<List<Vector2Int>> _routes = new(128);   // 수요 인덱스 정렬, 미연결 = null

        public IReadOnlyList<List<Vector2Int>> Routes => _routes;

        public RoutePlanner(int width, int height)
        {
            _w = width; _h = height;
            int n = width * height;
            _cost = new float[n];
            _done = new bool[n];
            _cameFrom = new int[n];
            _load = new float[n];
        }

        // 수요별 경로 테이블 계산. 부하 적립은 DemandPerHouse(평균 — 맥동 무반영, 정산 철학과 동일).
        public void Plan(DemandMap demand, RoadNetwork net, CityGrid grid, in SimConfig cfg)
        {
            _routes.Clear();
            Array.Clear(_load, 0, _load.Length);

            var demands = demand.Demands;
            for (int i = 0; i < demands.Count; i++)
            {
                List<Vector2Int> path = null;
                if (net.TryGetAccessRoad(demands[i].Source, out var from) &&
                    net.TryGetAccessRoad(demands[i].Sink, out var to))
                    path = Search(grid, from, to, cfg);

                _routes.Add(path);                            // null = 이 수요는 흐르지 않음(무사고)
                if (path == null) continue;
                for (int p = 0; p < path.Count; p++)
                    _load[path[p].y * _w + path[p].x] += cfg.DemandPerHouse;
            }
        }

        // 현재 _load 기준 최소 비용 경로(내부 + 테스트 seam). 미연결/비도로 끝점 = null.
        internal List<Vector2Int> Search(CityGrid grid, Vector2Int from, Vector2Int to, in SimConfig cfg)
        {
            if (!IsRoad(grid, from.x, from.y) || !IsRoad(grid, to.x, to.y)) return null;

            int n = _cost.Length;
            for (int i = 0; i < n; i++) { _cost[i] = float.MaxValue; _done[i] = false; }
            int start = from.y * _w + from.x;
            int goal = to.y * _w + to.x;
            _cost[start] = 0f;
            _cameFrom[start] = -1;

            // 용량 0 방어: 부하항 무시(순수 물리 최단으로 퇴화).
            float capInv = cfg.RoadCapacity > 0f ? 1f / cfg.RoadCapacity : 0f;
            float w = cfg.RoutingCongestionWeight;

            while (true)
            {
                // 미확정 최소 비용 노드 — flat 오름차순 스캔 + strict < = 동률 시 낮은 인덱스(결정론).
                int cur = -1;
                float best = float.MaxValue;
                for (int i = 0; i < n; i++)
                    if (!_done[i] && _cost[i] < best) { best = _cost[i]; cur = i; }
                if (cur == -1) return null;                   // 프런티어 고갈 = 미연결
                if (cur == goal) break;
                _done[cur] = true;

                int cx = cur % _w, cy = cur / _w;
                for (int d = 0; d < DX.Length; d++)
                {
                    int nx = cx + DX[d], ny = cy + DY[d];
                    if (!IsRoad(grid, nx, ny)) continue;
                    int ni = ny * _w + nx;
                    if (_done[ni]) continue;
                    float phys = d < 4 ? 1f : Sqrt2;          // 물리 거리 — 선택과 그린웨이브 타이밍 일치
                    float step = phys * (1f + w * _load[ni] * capInv);
                    float cand = _cost[cur] + step;
                    if (cand < _cost[ni]) { _cost[ni] = cand; _cameFrom[ni] = cur; }
                }
            }

            var path = new List<Vector2Int>();
            for (int node = goal; node != -1; node = _cameFrom[node])
                path.Add(new Vector2Int(node % _w, node / _w));
            path.Reverse();
            return path;
        }

        bool IsRoad(CityGrid grid, int x, int y) =>
            x >= 0 && x < _w && y >= 0 && y < _h &&
            grid.GetTile(new Vector2Int(x, y)) == CityFlow.Contracts.TileType.Road;
    }
}
```

- [ ] **Step 5: 통과 + 전체 회귀** — refresh → `run_tests` EditMode 전체. Expected: 117 + 10 = **127/127** (아직 아무도 planner를 안 쓰므로 기존 무영향).

- [ ] **Step 6: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Sim/RoutePlanner.cs Assets/01_Scripts/CityFlow/Sim/SimConfig.cs Assets/05_ScriptableObjects/SimConfig.asset Assets/Tests/EditMode/RoutePlannerTests.cs
git commit -m "[Feat] RoutePlanner — 혼잡 인지 증분 배정 + 물리 거리 √2

재건축 시 수요를 고정 순서로 배정, 앞 수요의 부하를 뒤 수요가 회피
(비용 = 물리거리 × (1 + w×부하/용량), w=RoutingCongestionWeight=2 신설).
평행 분산·우회 흡수 창발을 테스트로 핀. 아직 미배선(Task 2에서 전환).

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: 배선 전환 + BFS 은퇴

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Sim/FlowSolver.cs:50-78` (Assign 시그니처·본문)
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimEngine.cs` (필드·재구축 블록 2곳·Assign 호출 2곳)
- Modify: `Assets/01_Scripts/CityFlow/Sim/RoadNetwork.cs` (FindPath/Bfs/캐시/Rebuild 삭제)
- Modify: `Assets/Tests/EditMode/RoadNetworkTests.cs` (경로 테스트 7건 삭제 — Task 1에서 이관 완료)
- Modify: 테스트 헬퍼 콜사이트(`SignalFlowTests.cs`·`AxisFlowTests.cs` 외 — Step 3에서 grep으로 전수)

**Interfaces:**
- Consumes: Task 1의 `RoutePlanner.Plan(DemandMap, RoadNetwork, CityGrid, in SimConfig)` / `Routes`.
- Produces: `FlowSolver.Assign(DemandMap demand, RoutePlanner planner, in SimConfig cfg, float demandScale = 1f)` — net 대신 planner. `RoadNetwork`에서 `FindPath`/`Rebuild` 소멸(외부 계약 아님 — internal).

- [ ] **Step 1: FlowSolver.Assign 전환**

`FlowSolver.cs`의 Assign을 다음으로 교체(주변 필드·AxisWeights는 불변):

```csharp
        // demandScale: 이번 틱 수요 맥동 배율(SimConfig.DemandPulse). 1 = 균일(기존 동작).
        // 경로는 RoutePlanner가 재건축 시 계획한 테이블(수요 인덱스 정렬)을 읽음 — 매 틱 탐색 없음.
        public void Assign(DemandMap demand, RoutePlanner planner, in SimConfig cfg, float demandScale = 1f)
        {
            Array.Clear(_flowH, 0, _flowH.Length);
            Array.Clear(_flowV, 0, _flowV.Length);
            _routes.Clear();
            _routeSinks.Clear();
            DemandRate = cfg.DemandPerHouse * demandScale;

            var demands = demand.Demands;
            var planned = planner.Routes;
            for (int i = 0; i < demands.Count; i++)
            {
                var path = planned[i];
                if (path == null) continue;                   // 접점 없음/미연결 = 흐르지 않음(무사고)

                for (int p = 0; p < path.Count; p++)
                {
                    var (wH, wV) = AxisWeights(path, p);
                    int i2 = Index(path[p]);
                    _flowH[i2] += DemandRate * wH;
                    _flowV[i2] += DemandRate * wV;
                }
                _routes.Add(path);
                _routeSinks.Add(demands[i].Sink);
            }
        }
```

- [ ] **Step 2: SimEngine 배선**

필드(생성자 초기화 포함 — `_solver` 옆):
```csharp
        readonly RoutePlanner _planner;
```
생성자(`_solver = new FlowSolver(...)` 아래):
```csharp
            _planner = new RoutePlanner(config.GridWidth, config.GridHeight);
```
`Step()`의 재구축 블록 교체(`_network.Rebuild()` 삭제 — Region은 lazy 자가 무효화):
```csharp
            if (_grid.TopologyDirty)
            {
                _demand.Reassign(_grid, _network);            // 도달성(같은 섬) 우선 배정
                _signals.Rebuild(_grid);                      // 교차로 재감지(살아남은 신호 오프셋 보존)
                _planner.Plan(_demand, _network, _grid, _config);   // 혼잡 인지 증분 배정(경로 테이블)
                _grid.ClearTopologyDirty();
            }
```
Assign 호출 2곳: `_solver.Assign(_demand, _planner, _config, SimConfig.DemandPulse(_simTime, _config));` / `SettleOffline`의 재구축 블록도 동일 형태로 교체 + `_solver.Assign(_demand, _planner, _config);`

- [ ] **Step 3: 테스트 콜사이트 전수 갱신**

`grep -rn "\.Assign(" Assets/Tests --include="*.cs"` 로 전수 확인 후, 각 지점에서 기계적 치환:

```csharp
// 이전
var net = new RoadNetwork(g);
solver.Assign(dm, net, cfg);
// 이후
var net = new RoadNetwork(g);
var planner = new RoutePlanner(g.Width, g.Height);
planner.Plan(dm, net, g, cfg);
solver.Assign(dm, planner, cfg);
```
(알려진 파일: `SignalFlowTests.cs`의 `Solve`/`SolveCity`/`SingleSignal_NoEffect` 인라인, `AxisFlowTests.cs`의 `Solve` 헬퍼. grep에 더 나오면 같은 패턴 — `FlowSolverTests`·`SimStatsTests` 가능성. demandScale 인자를 쓰던 곳은 그대로 4번째 인자 유지.)

- [ ] **Step 4: RoadNetwork 은퇴**

`RoadNetwork.cs`에서 삭제: `_visited`·`_cameFrom` 필드(생성자 할당 포함 — `_queue`는 Region용이라 **유지**), `_cache`·`_cachedVersion`, `Rebuild()`, `FindPath()`, `Bfs()`, `InBounds`/`IsRoad`가 다른 데서 안 쓰이면 함께(TryGetAccessRoad가 `IsRoad` 사용 — **유지**). 클래스 주석을 "접점(TryGetAccessRoad)·도달성(RegionOf)만 담당 — 경로는 RoutePlanner"로 갱신.

`RoadNetworkTests.cs`에서 경로 테스트 7건 삭제(Straight/LShaped/Staircase/Disconnected/NonRoad/SameTile/TopologyChange — Task 1의 Search 테스트로 이관 완료, 캐시 테스트는 캐시 소멸로 대상 없음). 접점 3건(AccessRoad_*)은 유지.

- [ ] **Step 5: GREEN + 전체 회귀** — refresh → `read_console` 0 에러 → `run_tests` EditMode 전체. Expected: **120/120** (127 − 7). 기존 테스트 중 경로 변화로 깨지는 게 있으면 STOP — 원인(대각 √2·부하 회피 중 무엇인지) 분석해 보고. (분석 예상: 기존 스위트의 도시들은 대안 경로가 없는 외길 기하라 경로 불변 — SignalFlowTests·AxisFlowTests·BurstGuardTests 전부 생존 예상.)

- [ ] **Step 6: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Sim/FlowSolver.cs Assets/01_Scripts/CityFlow/Sim/SimEngine.cs Assets/01_Scripts/CityFlow/Sim/RoadNetwork.cs Assets/Tests/EditMode/RoadNetworkTests.cs Assets/Tests/EditMode/SignalFlowTests.cs Assets/Tests/EditMode/AxisFlowTests.cs
git commit -m "[Feat] 라우팅 배선 전환 — Assign이 경로 테이블 소비, BFS/캐시 은퇴

SimEngine 재건축 블록에 RoutePlanner.Plan 추가(network.Rebuild 소멸 —
Region은 lazy 자가 무효화). RoadNetwork는 접점·도달성만 담당.
RoadNetworkTests 경로 7건은 RoutePlannerTests로 이관 완료라 삭제.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```
(grep에서 추가 파일이 나왔으면 add에 포함.)

---

## 완료 후

- 플레이 검증(SimDebug/통합 씬): ①평행 도로를 놓으면 차들이 나눠 탐 ②정체난 간선 옆 우회로에 일부가 돌아감 ③신호 오프셋 조작 중 경로가 안 출렁임.
- PR은 스택 순차(#38→축분리→가드→라우팅). PR 본문 명기: 대각 √2로 일부 경로 변화(물리 정직화), w=2 노브(진우), 신호는 재배정 트리거 아님(설계).
- 후속: 라우팅 돋보기 UI(경로 테이블이 기반), 일방통행 툴(장기).

## Self-Review 결과

- **스펙 커버리지**: §1 RoutePlanner(T1 Step4 — 증분·비용식·결정론 타이브레이크·버퍼 선할당), §2 배선(T2 Step1-2)·FindPath 은퇴(T2 Step4)·RegionOf/접점 유지(T2 Step4 명시)·RoadNetworkTests 이관(T1 Step2 + T2 Step4), §3 w=2(T1 Step1), §5 파급(T2 Step5 STOP 규약), 검증 계획 6종(직선/L대각/계단/지그재그√2/분산/흡수/결정론/미연결 — T1 Step2) — 전부 매핑.
- **플레이스홀더**: BypassCity의 빈 for 줄 제거 필요 → 수정함(아래). 그 외 없음.
- **타입 일관성**: `Plan(DemandMap, RoadNetwork, CityGrid, in SimConfig)`·`Routes`·`Search(CityGrid, V2I, V2I, in SimConfig)`·`Assign(DemandMap, RoutePlanner, in SimConfig, float)` — T1 정의와 T2/테스트 사용 일치. `RoutingCongestionWeight` T1 정의·Search 사용 일치.
