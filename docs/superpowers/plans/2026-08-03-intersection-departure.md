# 교차로 출발 허용 — 구현 계획

> **For agentic workers:** 이 계획은 태스크 단위로 실행한다. 각 태스크는 RED → 구현 → GREEN → 커밋으로 끝난다.
> **Unity 실행은 절대 하지 말 것.** 본 체크아웃이 하나뿐이라 감독이 게이트를 돌린다.
> 설계 정본: `docs/superpowers/specs/2026-08-03-intersection-departure-design.md`

**Goal:** 진입로가 전부 교차로인 건물의 통근차가 교차로에 정식 진입해 출발하도록 한다. 영구 오프네트워크 스톨을 없앤다.

**Architecture:** 교차로는 2x2 사분면 마이크로그리드로 관리되고, 차는 Entry→Conflict→Exit 스테이지로 셀을 예약한다. 지금은 스테이지 없는 차를 앉힐 수 없어 스폰이 거부된다. 스테이지와 exit 방향을 명시해 넣는 전용 진입점을 만들고, **신규 출발 경로에서만** 쓴다. 재개(resume) 경로와 `IsSafeResumeTile`은 건드리지 않는다.

**Tech Stack:** Unity 6000.5.2f1 · C# · NUnit EditMode (`CityFlow.Sim.Tests`)

## Global Constraints

- `SimConfig.Default()`(`Sim/SimConfig.cs` L123-173) 편집·필드 추가 **금지**. 임계값은 `private const`.
- `IsSafeResumeTile`(`RoadQueueNetwork.cs:416`) **수정 금지**. 재개 규칙이다.
- 로터리·로터리 팔은 **이번 범위 밖**. `_roundabouts` / `IsRoundaboutArm` 경로를 건드리지 말 것.
- `.unity` 씬·`ProjectSettings/` **커밋 금지**. 작업 트리에 이미 수정된 것이 있으니 `git add`는 명시 목록으로만.
- 새 `.cs`는 `.cs.meta` 동반 커밋. push·PR 금지(감독이 한다).
- 계획서의 파일:라인이나 판단이 틀렸다고 보이면 **고치지 말고 보고**하라. 2026-08-03 세션에서 워커가 감독 계획서 결함 2건을 잡았고 둘 다 워커가 옳았다.

## 배경 사실 (실코드 확인 완료)

- 교차로 노드의 **진입 방향은 큐 인덱스로 암묵 표현**된다. `_intersectionOccupancy` 누적 루프(`RoadQueueNetwork.cs:1075-1098`)가 `(Dir)direction`을 큐 인덱스에서 읽는다.
- 기존 진입 시 스테이지 결정(`RoadQueueNetwork.cs:1449-1453`): **직진이면 곧바로 `Exit`**, 회전이면 `Entry`. `_intersectionMovementExits[node] = exit`도 함께 채운다.
- `UsesSharedBudget(tile)`(`L1642-1643`) = `_intersections[tile] && !_overpasses[tile] && !_roundabouts[tile]` — 이번 대상은 정확히 이 조건이다.
- `IntersectionMicroGrid.Dir`는 **진행 방향**이다(`EntryCell(N)=SouthEast` = 북진 차량이 우측통행으로 동쪽 절반 남단 점유).

---

### Task 1: 건물 → 진입로 직교 방향 조회

설계 D2-1. 차고 진출 방향을 실제 배치에서 읽는다.

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Sim/RoadNetwork.cs`
- Test: `Assets/Tests/EditMode/AccessRoadDepartureDirectionTests.cs` (신규, `.cs.meta` 동반)

**Interfaces:**
- Produces: `internal bool TryGetDepartureEntryDir(Vector2Int building, Vector2Int road, out Dir entry)`
  — 건물 풋프린트 셀 중 `road`와 **직교 인접**한 셀이 있으면 `entry` = 건물→도로 진행 방향으로 채우고 `true`.
  대각으로만 닿았거나 인접이 없으면 `false`(호출자가 exit 폴백).

**설계 근거(구현자가 고민하지 말 것):** 직교 인접 셀은 **최대 1개**다. 풋프린트가 직사각형(`TileFootprint.GetRotatedSize`)이고 `road`는 풋프린트 밖이므로, `road`의 4방 이웃 중 풋프린트 셀이 둘이면 그 사이 칸까지 풋프린트가 덮어야 해 모순이다. 다중 후보 처리 로직 불필요.

- [x] **Step 1: 실패 테스트 작성**

```csharp
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public class AccessRoadDepartureDirectionTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        [Test]
        public void DepartureEntryDir_OrthogonalFrontage_PointsFromBuildingToRoad()
        {
            var grid = new CityGrid(8, 8);
            // 집 (2,2) 2x2 → (2,2)(3,2)(2,3)(3,3) 점유. 그 위 y=4 가 도로.
            Assert.IsTrue(grid.Place(V(2, 4), TileType.Road));
            Assert.IsTrue(grid.Place(V(3, 4), TileType.Road));
            Assert.IsTrue(grid.Place(V(2, 2), TileType.House));
            var net = new RoadNetwork(grid);

            Assert.IsTrue(net.TryGetDepartureEntryDir(V(2, 2), V(2, 4), out Dir entry),
                "집 셀 (2,3) 이 도로 (2,4) 와 직교 인접하므로 방향이 나와야 한다");
            Assert.AreEqual(Dir.N, entry, "건물에서 도로로 향하는 진행 방향은 북쪽이다");
        }

        [Test]
        public void DepartureEntryDir_DiagonalOnlyFrontage_ReturnsFalse()
        {
            var grid = new CityGrid(8, 8);
            // 집 (2,2) 2x2 점유는 x=2..3, y=2..3. (4,4) 는 (3,3) 과 대각으로만 닿는다.
            Assert.IsTrue(grid.Place(V(4, 4), TileType.Road));
            Assert.IsTrue(grid.Place(V(2, 2), TileType.House));
            var net = new RoadNetwork(grid);

            Assert.IsFalse(net.TryGetDepartureEntryDir(V(2, 2), V(4, 4), out _),
                "대각으로만 닿은 진입로는 진출 방향을 정의할 수 없다 — 호출자가 exit 폴백을 쓴다");
        }
    }
}
```

- [x] **Step 2: RED 확인 요청**

감독에게 보고한다. 기대: `error CS1061 TryGetDepartureEntryDir` (메서드 없음).
**직접 Unity를 실행하지 말 것.**

- [x] **Step 3: 최소 구현**

`RoadNetwork.cs`의 `CollectAccessRoads` 아래에 추가한다. 기존 `DX`/`DY`의 **앞 4개만** 직교다(`RoadNetwork.cs:13-14`).

```csharp
        // 차고 진출 방향: 건물 풋프린트 셀 중 진입로와 직교 인접한 셀에서 도로로 향하는 진행 방향.
        // 직사각형 풋프린트라 후보는 최대 1개다(둘이면 사이 칸까지 덮어야 해 모순).
        // 대각으로만 닿으면 false — 호출자가 exit 방향 폴백을 쓴다(설계 D2-2).
        internal bool TryGetDepartureEntryDir(
            Vector2Int building,
            Vector2Int road,
            out Dir entry)
        {
            Vector2Int size = GetBuildingFootprintSize(building);
            for (int y = 0; y < size.y; y++)
            {
                for (int x = 0; x < size.x; x++)
                {
                    Vector2Int cell = building + new Vector2Int(x, y);
                    Vector2Int delta = road - cell;
                    if (delta == new Vector2Int(0, 1)) { entry = Dir.N; return true; }
                    if (delta == new Vector2Int(1, 0)) { entry = Dir.E; return true; }
                    if (delta == new Vector2Int(0, -1)) { entry = Dir.S; return true; }
                    if (delta == new Vector2Int(-1, 0)) { entry = Dir.W; return true; }
                }
            }
            entry = default;
            return false;
        }
```

- [ ] **Step 4: GREEN 확인 요청** — 감독이 게이트를 돌린다.

- [x] **Step 5: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Sim/RoadNetwork.cs \
        Assets/Tests/EditMode/AccessRoadDepartureDirectionTests.cs \
        Assets/Tests/EditMode/AccessRoadDepartureDirectionTests.cs.meta
git commit -m "[Feat] 건물→진입로 직교 진출 방향 조회 추가"
```

---

### Task 2: 교차로 스폰 진입점

설계 D1·D3. 스테이지와 exit를 명시해 교차로 큐에 노드를 넣는다.

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Sim/RoadQueueNetwork.cs`
- Test: `Assets/Tests/EditMode/IntersectionDepartureTests.cs` (신규, `.cs.meta` 동반)

**Interfaces:**
- Consumes: 없음
- Produces: `internal bool TryEnqueueAtIntersection(Vector2Int tile, Dir entryDir, Dir exitDir, int carId, int occupancyUnits)`
  — 교차로 타일에만 성공. 셀 충돌이면 `false`(호출자가 다음 틱 재시도).

**주의 — 기존 private `TryEnqueue`에 `out int node`를 추가해야 한다.** 노드 번호를 알아야 스테이지를 채운다. 기존 public 오버로드 시그니처는 **바꾸지 말 것**(호출자 다수).

- [x] **Step 1: 실패 테스트 작성**

```csharp
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public class IntersectionDepartureTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        // CarSimTests 의 ResumeRouteProvider 는 private 중첩 클래스라 여기서 못 쓴다
        // (2차 리뷰 P0). 이 파일 전용으로 최소 구현을 둔다.
        private sealed class RouteProvider : ICarRouteProvider
        {
            private readonly System.Collections.Generic.Dictionary<int, Vector2Int[]> _routes = new();

            public void Add(int carId, params Vector2Int[] route) => _routes[carId] = route;

            public bool IsDestination(int carId, Vector2Int tile) =>
                _routes.TryGetValue(carId, out var r) && r.Length > 0 && r[r.Length - 1] == tile;

            public bool TryGetNextTile(int carId, Vector2Int from, out Vector2Int next, out Dir exit)
            {
                next = default;
                exit = Dir.N;
                if (!_routes.TryGetValue(carId, out var r)) return false;
                for (int i = 0; i < r.Length - 1; i++)
                {
                    if (r[i] != from) continue;
                    next = r[i + 1];
                    Vector2Int d = next - from;
                    if (d == new Vector2Int(0, 1)) exit = Dir.N;
                    else if (d == new Vector2Int(1, 0)) exit = Dir.E;
                    else if (d == new Vector2Int(0, -1)) exit = Dir.S;
                    else if (d == new Vector2Int(-1, 0)) exit = Dir.W;
                    else return false;
                    return true;
                }
                return false;
            }
        }

        static SimConfig Cfg()
        {
            SimConfig cfg = SimConfig.Default();
            cfg.GridWidth = 8;
            cfg.GridHeight = 8;
            cfg.TickInterval = 0.25f;
            return cfg;
        }

        // 십자 교차로 (4,4). 팔 4개.
        static CityGrid CrossGrid()
        {
            var grid = new CityGrid(8, 8);
            Assert.IsTrue(grid.Place(V(4, 4), TileType.Road));
            Assert.IsTrue(grid.Place(V(4, 5), TileType.Road));
            Assert.IsTrue(grid.Place(V(4, 3), TileType.Road));
            Assert.IsTrue(grid.Place(V(3, 4), TileType.Road));
            Assert.IsTrue(grid.Place(V(5, 4), TileType.Road));
            Assert.IsTrue(grid.IsIntersection(V(4, 4)));
            return grid;
        }

        [Test]
        public void SpawnAtIntersection_EmptyCells_Succeeds()
        {
            SimConfig cfg = Cfg();
            var grid = CrossGrid();
            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);

            Assert.IsTrue(
                net.TryEnqueueAtIntersection(V(4, 4), Dir.N, Dir.N, carId: 7, occupancyUnits: 1),
                "빈 교차로에는 스테이지를 부여해 스폰할 수 있어야 한다");
            Assert.IsTrue(net.TryLocateCar(7, out Vector2Int tile, out _, out int slot));
            Assert.AreEqual(V(4, 4), tile);
            Assert.GreaterOrEqual(slot, 0, "스폰한 차는 네트워크에 실제로 올라와 있어야 한다");
        }

        [Test]
        public void SpawnAtIntersection_ConflictingCells_FailsThenSucceedsWhenCleared()
        {
            SimConfig cfg = Cfg();
            var grid = CrossGrid();
            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);

            // 북진 직진이 동쪽 절반(SouthEast|NorthEast)을 점유한다.
            Assert.IsTrue(net.TryEnqueueAtIntersection(V(4, 4), Dir.N, Dir.N, 1, 1));
            // 같은 동쪽 절반을 요구하는 두 번째 북진은 들어갈 수 없다.
            Assert.IsFalse(
                net.TryEnqueueAtIntersection(V(4, 4), Dir.N, Dir.N, 2, 1),
                "이미 예약된 셀과 겹치면 스폰은 실패하고 다음 틱에 재시도한다");

            Assert.IsTrue(net.TryRemoveCarForRescue(1));
            Assert.IsTrue(
                net.TryEnqueueAtIntersection(V(4, 4), Dir.N, Dir.N, 2, 1),
                "셀이 비면 반드시 진입한다 — 이것이 수렴 보장이다");
        }

        // 설계 §6 T2. 스테이지별 점유 범위가 실제 계약대로인지 고정한다.
        //   회전 스폰 = Entry  → OccupancyMask = StageMask = EntryCell(entry) 한 칸
        //   직진 스폰 = Exit   → OccupancyMask = MovementMask(entry, exit) 두 칸
        // (IntersectionMicroGrid.cs:41-47,107-125)
        // 최초 계획서는 회전 스폰이 MovementMask 두 칸을 잡는다고 잘못 적었다. 리뷰가 잡았다.
        [Test]
        public void SpawnAtIntersection_TurnHoldsEntryCellOnly()
        {
            SimConfig cfg = Cfg();
            var grid = CrossGrid();
            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);

            // 북진 좌회전 = Entry 스테이지 → EntryCell(N) = SouthEast 한 칸만 예약
            Assert.IsTrue(
                net.TryEnqueueAtIntersection(V(4, 4), Dir.N, Dir.W, 1, 1),
                "빈 교차로에서 좌회전 스폰은 성공한다");

            // 동진 직진 = Exit 스테이지 → MovementMask(E,E) = SouthWest|SouthEast.
            // SouthEast 가 겹치므로 막힌다.
            Assert.IsFalse(
                net.TryEnqueueAtIntersection(V(4, 4), Dir.E, Dir.E, 2, 1),
                "Entry 가 쥔 SouthEast 를 요구하는 직진은 동시에 진입할 수 없다");

            // 서진 직진 = MovementMask(W,W) = NorthEast|NorthWest. SouthEast 와 안 겹친다.
            Assert.IsTrue(
                net.TryEnqueueAtIntersection(V(4, 4), Dir.W, Dir.W, 3, 1),
                "Entry 는 한 칸만 쥔다 — 겹치지 않는 경로는 통과한다");
        }

        // 설계 §6 T2 후반 + 리뷰 P0-3. 앞차가 출구 타일로 넘어갔어도 뒤꽁무니가 교차로에
        // 남아 있는 동안에는 충돌 경로 스폰을 승인하면 안 된다.
        [Test]
        public void SpawnAtIntersection_RearClearanceBlocksConflictingSpawn()
        {
            SimConfig cfg = Cfg();
            var grid = CrossGrid();
            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);

            var routes = new RouteProvider();
            // 북진 직진: (4,3) → (4,4) 교차로 → (4,5) 출구
            routes.Add(10, V(4, 3), V(4, 4), V(4, 5));
            Assert.IsTrue(net.TryEnqueue(V(4, 3), Dir.N, 10));

            // 차가 교차로를 지나 출구 타일에 올라설 때까지 진행시킨다.
            bool reachedExitTile = false;
            for (int tick = 0; tick < 8 && !reachedExitTile; tick++)
            {
                net.Step(routes);
                reachedExitTile =
                    net.TryLocateCar(10, out Vector2Int at, out _, out _)
                    && at == V(4, 5);
            }
            Assert.IsTrue(reachedExitTile, "전제: 차가 출구 타일까지 전진했다");

            // 뒤꽁무니가 아직 교차로 안이다. 같은 경로를 요구하는 스폰은 막혀야 한다.
            Assert.IsFalse(
                net.TryEnqueueAtIntersection(V(4, 4), Dir.N, Dir.N, 11, 1),
                "출구로 넘어간 앞차의 rear clearance 를 무시하고 스폰하면 안 된다");
        }

        // 2차 리뷰 P1 — 이 설계의 핵심 주장("수렴한다")을 잠그는 테스트.
        // Entry 로 앉힌 차가 실제로 Entry→Exit→출구타일로 전진하지 않으면
        // 스톨 위치만 옮긴 것이다. 이 단정이 없으면 전진 배선이 빠진 구현도 통과한다.
        [Test]
        public void SpawnedTurn_AdvancesOutWithinFiniteTicks()
        {
            SimConfig cfg = Cfg();
            var grid = CrossGrid();
            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);

            var routes = new RouteProvider();
            // 북진 진입 → 동쪽으로 우회전해서 (5,4) 로 빠진다.
            routes.Add(21, V(4, 4), V(5, 4));
            Assert.IsTrue(net.TryEnqueueAtIntersection(V(4, 4), Dir.N, Dir.E, 21, 1),
                "전제: 회전 스폰 성공");

            bool left = false;
            for (int tick = 0; tick < 10 && !left; tick++)
            {
                net.Step(routes);
                left = !net.TryLocateCar(21, out Vector2Int at, out _, out _) || at != V(4, 4);
            }
            Assert.IsTrue(left,
                "Entry 로 스폰한 차는 유한 틱 안에 교차로를 빠져나가야 한다 — 이게 수렴 보장이다");
        }

        // 설계 D5 + 2차 리뷰 P0. 로터리는 중심뿐 아니라 "팔"도 범위 밖이다.
        // UsesSharedBudget 이 팔을 제외하지 않으므로 별도 가드가 필요하다.
        [Test]
        public void SpawnAtIntersection_RoundaboutCenterAndArm_BothRejected()
        {
            SimConfig cfg = Cfg();
            var grid = CrossGrid();
            // 팔이 교차로가 되도록 (5,4) 에 세 번째 도로 가지를 붙인다.
            Assert.IsTrue(grid.Place(V(5, 5), TileType.Road));
            Assert.IsTrue(grid.Place(V(5, 3), TileType.Road));
            Assert.IsTrue(grid.IsIntersection(V(5, 4)), "전제: 팔 타일이 grid 교차로다");

            var devices = new FakeDeviceState();
            devices.AddRoundabout(V(4, 4));           // 중심 (4,4), 팔은 상하좌우
            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid, devices);

            Assert.IsFalse(
                net.TryEnqueueAtIntersection(V(4, 4), Dir.N, Dir.N, 5, 1),
                "로터리 중심은 링 예약 모델이라 범위 밖이다");
            Assert.IsFalse(
                net.TryEnqueueAtIntersection(V(5, 4), Dir.E, Dir.E, 6, 1),
                "로터리 팔도 범위 밖이다 — 팔이 교차로여도 링 상태기계를 우회하면 안 된다");
        }

        [Test]
        public void SpawnAtIntersection_NonIntersectionTile_Rejected()
        {
            SimConfig cfg = Cfg();
            var grid = CrossGrid();
            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);

            Assert.IsFalse(
                net.TryEnqueueAtIntersection(V(4, 5), Dir.N, Dir.N, 3, 1),
                "교차로가 아닌 타일은 기존 TryEnqueue 경로를 써야 한다");
        }
    }
}
```

- [x] **Step 2: RED 확인 요청** — 기대: `error CS1061 TryEnqueueAtIntersection`.

- [x] **Step 3: 최소 구현**

3-a. 기존 private `TryEnqueue`(`RoadQueueNetwork.cs:360` 부근)에 노드 출력을 추가한다.

```csharp
        private bool TryEnqueue(
            Vector2Int tile,
            Dir entryDir,
            int carId,
            int occupancyUnits,
            VehicleFootprint footprint) =>
            TryEnqueue(tile, entryDir, carId, occupancyUnits, footprint, out _);

        private bool TryEnqueue(
            Vector2Int tile,
            Dir entryDir,
            int carId,
            int occupancyUnits,
            VehicleFootprint footprint,
            out int node)
        {
            node = NoNode;
            int safeOccupancyUnits = Math.Max(1, occupancyUnits);
            if (carId < 0 || !TryQueueIndex(tile, entryDir, out int queue)
                || !CanAcceptNormally(queue, safeOccupancyUnits)
                || !TryAllocateNode(out node)) return false;
            _cars[node] = carId;
            _occupancyUnits[node] = safeOccupancyUnits;
            _lengthTiles[node] = footprint.LengthTiles;
            _minimumGapTiles[node] = footprint.MinimumGapTiles;
            _movedThisTick[node] = false;
            _blockedTicks[node] = 0;
            AppendNode(queue, node);
            return true;
        }
```

3-b. 스폰 진입점을 추가한다. 스테이지 규칙은 기존 진입(`L1449-1453`)과 **동일하게** 직진→`Exit`, 회전→`Entry`로 맞춘다.

```csharp
        // 신규 출발 전용: 건물 진입로가 교차로일 때 스테이지·exit를 명시해 스폰한다.
        // 재개(resume) 경로는 여기를 쓰지 않는다 — IsSafeResumeTile 은 그대로다(설계 D4).
        // 셀이 겹치면 false 를 돌려 호출자가 다음 틱에 재시도한다. 새 우선권 규칙 없음(D3).
        internal bool TryEnqueueAtIntersection(
            Vector2Int tile,
            Dir entryDir,
            Dir exitDir,
            int carId,
            int occupancyUnits)
        {
            if (!InBounds(tile)) return false;
            int tileIndex = TileIndex(tile);
            // UsesSharedBudget 은 로터리 "중심"만 제외하고 "팔"은 제외하지 않는다
            // (L1642-1643). 팔이 3방 도로를 가져 grid intersection 이 되면 링 상태기계를
            // 우회하게 되므로 여기서 명시적으로 막는다(설계 D5, 2차 리뷰 P0).
            if (!UsesSharedBudget(tileIndex) || IsRoundaboutArm(tileIndex)) return false;

            IntersectionStage stage = entryDir == exitDir
                ? IntersectionStage.Exit
                : IntersectionStage.Entry;
            IntersectionCell requested = IntersectionMicroGrid.OccupancyMask(
                entryDir, exitDir, stage);
            // 캐시(_intersectionOccupancy)를 쓰지 않고 그 타일의 4개 큐를 직접 훑는다.
            // 캐시는 틱 누적 패스(L1075)에서만 갱신돼, 같은 틱에 차가 빠져도 비트가 남는다.
            // 라이브 계산이면 스폰·제거가 같은 틱에 섞여도 항상 정확하다.
            if (IntersectionMicroGrid.Conflicts(
                    CurrentIntersectionOccupancy(tileIndex), requested)) return false;

            int safeUnits = Math.Max(1, occupancyUnits);
            float effectiveLength = Mathf.Max(
                _standardFootprint.LengthTiles,
                safeUnits * _standardHeadwayTiles -
                _standardFootprint.MinimumGapTiles);
            var footprint = new VehicleFootprint(
                safeUnits > 1 ? VehicleSizeClass.Large : _standardFootprint.SizeClass,
                effectiveLength,
                _standardFootprint.WidthTiles,
                _standardFootprint.MinimumGapTiles);

            if (!TryEnqueue(tile, entryDir, carId, safeUnits, footprint, out int node))
                return false;

            _intersectionStages[node] = stage;
            _intersectionMovementExits[node] = exitDir;
            return true;
        }

        // 그 교차로 타일이 지금 실제로 점유 중인 셀. 누적 패스(L1070-1128)와 같은 규칙이되
        // routes 없이 부르므로, 스테이지가 없는 노드는 보수적으로 All 로 본다
        // (스폰을 막고 다음 틱 재시도 — 안전한 방향).
        private IntersectionCell CurrentIntersectionOccupancy(int tileIndex)
        {
            IntersectionCell occupied = IntersectionCell.None;

            // (1) 그 타일 위에 있는 노드들
            int firstQueue = tileIndex * DirectionCount;
            for (int direction = 0; direction < DirectionCount; direction++)
            {
                int node = _heads[firstQueue + direction];
                while (node != NoNode)
                {
                    IntersectionStage stage = _intersectionStages[node];
                    occupied |= stage == IntersectionStage.None
                        ? IntersectionCell.All
                        : IntersectionMicroGrid.OccupancyMask(
                            (Dir)direction,
                            _intersectionMovementExits[node],
                            stage);
                    node = _nextNodes[node];
                }
            }

            // (2) 이미 출구 타일로 넘어갔지만 뒤꽁무니가 아직 교차로 안에 있는 노드들.
            // 기존 누적 패스의 두 번째 루프(L1108-1131)와 같은 규칙이다. 이걸 빠뜨리면
            // 앞차가 빠져나가는 중인데 충돌 경로로 스폰을 승인한다.
            for (int queue = 0; queue < _heads.Length; queue++)
            {
                int node = _heads[queue];
                while (node != NoNode)
                {
                    if (TryDecodeClearingIntersection(
                            _clearingIntersection[node],
                            out int clearingTile,
                            out Dir movementEntry)
                        && clearingTile == tileIndex)
                    {
                        occupied |= IntersectionMicroGrid.MovementMask(
                            movementEntry,
                            _intersectionMovementExits[node]);
                    }
                    node = _nextNodes[node];
                }
            }

            return occupied;
        }
```

연결 리스트 다음 노드 필드는 **`_nextNodes`** 다(`RoadQueueNetwork.cs:104,229,1129`). `_next`라는 필드는 없다.

- [ ] **Step 4: GREEN 확인 요청**

- [x] **Step 5: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Sim/RoadQueueNetwork.cs \
        Assets/Tests/EditMode/IntersectionDepartureTests.cs \
        Assets/Tests/EditMode/IntersectionDepartureTests.cs.meta
git commit -m "[Feat] 교차로 스폰 진입점 — 스테이지·셀 예약을 명시해 진입"
```

---

### Task 3: 출발 경로에 연결

설계 D4. 신규 출발이고 `route[0]`이 교차로일 때만 Task 2를 쓴다.

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Sim/CarSim.cs` (`TryEnqueueRouteStart`, `RoadQueueNetwork.cs:1256-1298` 상당 위치)
- Test: `Assets/Tests/EditMode/IntersectionDepartureTests.cs` (Task 2 파일에 추가)

**Interfaces:**
- Consumes: `RoadNetwork.TryGetDepartureEntryDir`(Task 1), `RoadQueueNetwork.TryEnqueueAtIntersection`(Task 2)

**핵심 제약:** `hasResume == true`(재개)일 때는 **절대** 새 경로로 가지 않는다. `route.Count == 1`이면 exit 방향이 없으므로 스폰하지 않는다(설계 §3 경계조건).

- [x] **Step 1: 실패 테스트 작성 (통합 — 라이브 증상 재현)**

`IntersectionDepartureTests.cs`에 추가한다. 집의 **모든** 프론티지가 교차로인 형상을 만든다.

```csharp
        [Test]
        public void CommuteCar_AllFrontagesAreIntersections_DepartsWithinFiniteTicks()
        {
            SimConfig cfg = Cfg();
            cfg.GridWidth = 10;
            cfg.GridHeight = 10;
            // 2차 리뷰 P0: Step 의 첫 인자는 delta 가 아니라 게임시각이다
            // (CarSim.cs:710 -> UpdateDepartures(gameHour) L785). 출근 창을 명시해
            // 아래 루프가 넘기는 시각이 확실히 창 안에 들도록 한다.
            cfg.MorningStartHour = 6f;
            cfg.MorningEndHour = 10f;
            var grid = new CityGrid(10, 10);

            // y=4 간선 + y=5 우회 → (2,4)(3,4) 가 모두 교차로가 된다.
            for (int x = 1; x <= 8; x++) Assert.IsTrue(grid.Place(V(x, 4), TileType.Road));
            Assert.IsTrue(grid.Place(V(2, 5), TileType.Road));
            Assert.IsTrue(grid.Place(V(3, 5), TileType.Road));
            // 2차 리뷰 P0: 기본 North 배치면 CollectAccessRoads 가 8방을 전부 모아
            // (1,4)·(4,4) 같은 일반 타일까지 프론티지가 된다 → "전부 교차로"가 거짓이 되고
            // 교차로 스폰 분기를 아예 안 탄다. 명시 방향(South)으로 두어
            // HasExplicitPlacementDirection 경로를 타게 하면 앞면만 수집된다.
            Assert.IsTrue(grid.Place(V(2, 2), TileType.House, PlacementDirection.South));
            Assert.IsTrue(grid.Place(V(7, 2), TileType.Office));

            // 전제를 형상 가정이 아니라 실제 수집 결과로 단정한다.
            var roadPre = new RoadNetwork(grid);
            var frontages = new System.Collections.Generic.List<Vector2Int>();
            roadPre.CollectAccessRoads(V(2, 2), frontages);
            Assert.Greater(frontages.Count, 0, "전제: 프론티지가 있어야 한다");
            foreach (Vector2Int f in frontages)
            {
                Assert.IsTrue(grid.IsIntersection(f),
                    $"전제: 수집된 프론티지 {f} 가 전부 교차로여야 이 테스트가 의미 있다");
            }

            var road = new RoadNetwork(grid);
            var demands = new DemandMap(cfg);
            demands.Reassign(grid, road);
            var planner = new RoutePlanner(grid.Width, grid.Height);
            planner.Plan(demands, road, grid, cfg);
            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);
            var sim = new CarSim(cfg);
            // 리뷰 P1: roadNetwork 를 넘기지 않으면 _roadNetwork 가 null 이라(CarSim.cs:279)
            // D2-1 방향 조회를 통째로 건너뛰어도 테스트가 GREEN 이 된다 — 공허한 테스트다.
            // Rebuild 시그니처가 roadNetwork 를 받지 않으면 고치지 말고 감독에게 보고하라.
            sim.Rebuild(demands, planner, net, roadNetwork: road);
            var events = new SimEventBuffer(new SimEventHub());

            // 출근 창 안의 시각을 넘긴다. cfg.TickInterval(0.25시)을 넘기면 창 밖이라
            // 차가 영원히 ParkedHome 이다 — 2차 리뷰가 잡은 거짓 RED 의 원인.
            const float DepartureHour = 7f;
            bool departed = false;
            Dir observedEntry = Dir.N;
            for (int tick = 0; tick < 40 && !departed; tick++)
            {
                sim.Step(DepartureHour, net, events);
                departed = net.TryLocateCar(0, out _, out observedEntry, out int slot)
                    && slot >= 0;
            }

            Assert.AreEqual(CarState.Outbound, sim.GetCar(0).State,
                "전제: 출근 창 안이라 차가 출발 상태여야 한다");
            Assert.IsTrue(departed,
                "프론티지가 전부 교차로여도 통근차는 유한 틱 안에 네트워크에 진입해야 한다");
            // 집 (2,2) 은 진입로의 남쪽이므로 차고 진출 방향은 북(N)이다. exit 는 회사(7,2)
            // 쪽이라 동(E) — 둘이 다르므로 폴백(entry=exit)을 탔다면 이 단정이 깨진다.
            // D2-1 접착이 실제로 동작하는지 검사한다(2차 리뷰 P1).
            Assert.AreEqual(Dir.N, observedEntry,
                "직교 인접이 있으면 exit 폴백이 아니라 차고 진출 방향으로 진입해야 한다");
        }

        // 설계 §6 T4 (2차 리뷰 P1 반영). 대각으로만 닿은 건물은 exit 폴백으로 진입한다.
        //
        // DemandMap 통합으로 쓰면 공허해진다 — 그 형상에서는 일반 직교 프론티지가 먼저
        // 수집돼 DemandMap 이 그걸 고르므로, 폴백 배선이 빠져도 테스트가 통과한다.
        // 그래서 TryEnqueueRouteStart 를 직접 호출해 route[0] 을 교차로로 못 박는다.
        [Test]
        public void Departure_DiagonalOnlyFrontage_UsesExitDirectionAsEntry()
        {
            SimConfig cfg = Cfg();
            var grid = CrossGrid();
            // 집 (2,2) 2x2 → (2,2)(3,2)(2,3)(3,3). 교차로 (4,4) 는 (3,3) 과 대각으로만 닿는다.
            Assert.IsTrue(grid.Place(V(2, 2), TileType.House));
            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);
            var road = new RoadNetwork(grid);

            Assert.IsFalse(road.TryGetDepartureEntryDir(V(2, 2), V(4, 4), out _),
                "전제: 대각으로만 닿아 직교 진출 방향이 없다");

            // route[0] = 교차로 (4,4), route[1] = (5,4) → exit 는 동(E)
            var route = new System.Collections.Generic.List<Vector2Int> { V(4, 4), V(5, 4) };
            bool hasResume = false;
            Assert.IsTrue(CarSim.TryEnqueueRouteStart(
                route, default, ref hasResume, net, 30, out int start,
                road, V(2, 2)));
            Assert.AreEqual(0, start, "출발은 경로 원점에서 시작한다");

            Assert.IsTrue(net.TryLocateCar(30, out Vector2Int at, out Dir entryDir, out _));
            Assert.AreEqual(V(4, 4), at);
            Assert.AreEqual(Dir.E, entryDir,
                "직교 진출 방향이 없으면 exit 방향(E)을 entry 로 쓴다 — D2-2 폴백");
        }
```

- [x] **Step 2: RED 확인 요청** — 기대: `departed` 가 false 로 남아 `Assert.IsTrue` 실패.

- [x] **Step 3: 최소 구현**

`CarSim.TryEnqueueRouteStart`의 `IsSafeResumeTile` 게이트(`CarSim.cs:1282` 부근) **직전**에 신규 출발 분기를 넣는다. 기존 게이트와 그 아래 로직은 그대로 둔다.

```csharp
            // 신규 출발이고 원점이 교차로면 스테이지를 부여해 정식 진입한다(설계 D1·D4).
            // 재개(retryingResume)는 여기 오지 않는다 — 경로상 위치가 모호해 위험하다.
            // route 가 1칸이면 exit 방향을 못 구하므로 오늘 동작(오프네트워크)을 유지한다.
            if (!wasResumeRequest && start == 0 && route.Count > 1
                && net.IsIntersectionSpawnTile(route[0]))
            {
                if (!TryRouteDirection(route[1] - route[0], out Dir spawnExit)) return false;
                // 기본은 exit 폴백(설계 D2-2). 건물 정보가 주어졌고 직교 인접이면 그 방향을 쓴다.
                Dir spawnEntry = spawnExit;
                // originBuilding 이 null 이면 조회 자체를 건너뛴다. default 좌표 (0,0) 은
                // 유효 좌표라 그 옆이 route[0] 이면 엉뚱한 entry 가 나온다(2차 리뷰 P1).
                if (roadNetwork != null
                    && originBuilding.HasValue
                    && roadNetwork.TryGetDepartureEntryDir(
                        originBuilding.Value, route[0], out Dir fromBuilding))
                {
                    spawnEntry = fromBuilding;
                }
                if (!net.TryEnqueueAtIntersection(
                        route[0], spawnEntry, spawnExit, carId, 1))
                {
                    return false;   // 셀이 막혔다 — 다음 틱 재시도(수렴)
                }
                hasResume = false;
                return true;
            }
```

3-a-2. **`retryingResume`을 조건에 쓰면 안 된다 (리뷰 P0-4).** `CarSim.cs:1269-1276`은 재개가
안전한 이전 타일을 못 찾으면 **`hasResume=false; retryingResume=false; start=0`** 으로 되돌린다.
그 상태를 신규 출발로 오인하면 **재개가 교차로 스폰을 타서 설계 D4가 깨진다.**
메서드 진입 시점의 요청 종류를 따로 캡처해 그걸로 판정한다.

```csharp
            start = 0;
            bool wasResumeRequest = hasResume;   // 리뷰 P0-4: 폴백으로 꺼지기 전 원래 요청
            bool retryingResume = hasResume;
```

3-b. **`TryEnqueueRouteStart`는 `internal static`이라 인스턴스 필드에 접근할 수 없다.** 선택적
매개변수 2개를 **끝에** 추가한다. 기본값이 있으므로 **기존 호출자와 기존 테스트는 그대로
컴파일된다**(`RebuildResume_*` 등). `roadNetwork`가 `null`이면 exit 폴백이라 동작도 안전하다.

```csharp
        internal static bool TryEnqueueRouteStart(
            IReadOnlyList<Vector2Int> route,
            Vector2Int resumeTile,
            ref bool hasResume,
            RoadQueueNetwork net,
            int carId,
            out int start,
            RoadNetwork roadNetwork = null,
            Vector2Int? originBuilding = null)
```

3-c. `RoadQueueNetwork`에 판정 헬퍼를 추가한다(`IsSafeResumeTile` 은 건드리지 않는다).

```csharp
        // 신규 출발 스폰이 가능한 교차로 타일인가. 로터리는 중심도 팔도 예약 모델이
        // 달라 제외한다(설계 D5). UsesSharedBudget 은 팔을 제외하지 않으므로 따로 건다.
        internal bool IsIntersectionSpawnTile(Vector2Int tile) =>
            InBounds(tile)
            && UsesSharedBudget(TileIndex(tile))
            && !IsRoundaboutArm(TileIndex(tile));
```

3-d. 인스턴스 호출자 3곳에서 새 인자를 넘긴다. **출발 건물은 진행 방향에 따라 다르다** —
출근은 집(`_sources`), 퇴근은 회사(`_sinks`)가 출발 건물이다.

```csharp
        // 이 차의 이번 여정 출발 건물. 진출 방향 산출용(설계 D2-1).
        // 특수 방문(transient) 차는 RouteIndex 가 -1 이라(CommuteScheduler.cs:50-65)
        // 여기서 default 를 돌려주고, 호출부는 exit 폴백을 쓴다. 특수 트립의 정확한
        // 차고 방향은 이번 범위 밖이다 — 리뷰 P1 지적을 의도적 축소로 수용한다.
        private Vector2Int? DepartureBuilding(int carId)
        {
            CommuteCar car = _scheduler.Cars[carId];
            int ri = car.RouteIndex;
            if (ri < 0) return null;                    // transient — exit 폴백
            var list = car.State == CarState.Outbound ? _sources : _sinks;
            return ri < list.Count ? list[ri] : (Vector2Int?)null;
        }
```

호출부는 기존 인자 뒤에 `, _roadNetwork, DepartureBuilding(carId)`를 붙인다. 대상 3곳:
초기 인큐(`CarSim.cs:1027`), 워치독 재시작(`L1108`), 레스큐(`L1145`).

`_sources`/`_sinks`가 Home/Work 앵커라는 것은 리뷰가 `CarSim.cs:675-676,700-701`에서
확인했다(라이브에서도 `_sources[8] = (2,8)` = 집 앵커).

- [ ] **Step 4: GREEN 확인 요청**

- [x] **Step 5: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Sim/CarSim.cs \
        Assets/01_Scripts/CityFlow/Sim/RoadQueueNetwork.cs \
        Assets/Tests/EditMode/IntersectionDepartureTests.cs
git commit -m "[Feat] 신규 출발 경로에서 교차로 스폰 사용 — 영구 스톨 해소"
```

---

### Task 4: 뒤집히는 기존 테스트 재작성 + 범위 고정

설계 §5·D5. **의도적 설계 변경**임을 테스트와 커밋에 남긴다.

**Files:**
- Modify: `Assets/Tests/EditMode/CarSimTests.cs` (`Departure_SpecialRouteOrigin_StaysOffNetwork`, L352 부근)
- Test: `Assets/Tests/EditMode/IntersectionDepartureTests.cs` (T5·T6 추가)

- [x] **Step 1: 기존 테스트를 새 계약으로 다시 쓴다**

`Departure_SpecialRouteOrigin_StaysOffNetwork`를 아래로 교체한다. **이름도 바꾼다** — 옛 이름이 남으면 계약이 뒤집힌 걸 아무도 모른다.

```csharp
        // 설계 변경 2026-08-03: 교차로 원점에서도 스테이지를 부여해 정식 진입한다.
        // 옛 계약("신규 출발도 오프네트워크 대기")은 진입로가 교차로뿐인 건물의 통근차를
        // 영구 스톨시켰다. 재개(resume) 경로는 그대로 보수적이다 — RebuildResume_* 참조.
        [Test]
        public void Departure_IntersectionRouteOrigin_EntersWithStage()
        {
            SimConfig cfg = Cfg();
            var grid = new CityGrid(5, 5);
            Assert.IsTrue(grid.Place(V(2, 2), TileType.Road));
            Assert.IsTrue(grid.Place(V(1, 2), TileType.Road));
            Assert.IsTrue(grid.Place(V(3, 2), TileType.Road));
            Assert.IsTrue(grid.Place(V(2, 1), TileType.Road));
            Assert.IsTrue(grid.Place(V(2, 3), TileType.Road));
            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);

            var routes = new ResumeRouteProvider();
            routes.Add(12, V(2, 2), V(3, 2));
            bool hasResume = false;

            Assert.IsTrue(CarSim.TryEnqueueRouteStart(
                routes.RouteFor(12),
                default,
                ref hasResume,
                net,
                12,
                out int start));
            Assert.AreEqual(0, start, "출발은 경로 원점에서 시작한다 — 앞 타일로 밀지 않는다");
            Assert.AreEqual(1, TotalQueued(net, grid.Width, grid.Height),
                "교차로 원점에서도 스테이지를 부여해 네트워크에 올라간다");
        }
```

- [x] **Step 1-b: 삭제되는 회귀 증거를 별도 테스트로 보존한다 (리뷰 P0-4)**

옛 테스트의 **전반부**(`hasResume = true`로 부르는 첫 호출)는 "재개는 교차로 원점을 쓰지
않는다"는 D4 계약의 유일한 증거다. Step 1의 교체본이 이걸 지우므로 별도로 남긴다.

```csharp
        // D4 회귀 방지: 재개(resume)는 교차로 출발 스폰 경로를 타지 않는다.
        // CarSim.cs:1269-1276 이 안전한 이전 타일이 없으면 retryingResume 을 끄고 start=0 으로
        // 되돌리는데, 그 상태를 신규 출발로 오인하면 재개가 교차로에 앉는다(리뷰 P0-4).
        [Test]
        public void RebuildResume_IntersectionOrigin_DoesNotUseDepartureSpawn()
        {
            SimConfig cfg = Cfg();
            var grid = new CityGrid(5, 5);
            Assert.IsTrue(grid.Place(V(2, 2), TileType.Road));
            Assert.IsTrue(grid.Place(V(1, 2), TileType.Road));
            Assert.IsTrue(grid.Place(V(3, 2), TileType.Road));
            Assert.IsTrue(grid.Place(V(2, 1), TileType.Road));
            Assert.IsTrue(grid.Place(V(2, 3), TileType.Road));
            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);

            var routes = new ResumeRouteProvider();
            routes.Add(12, V(2, 2), V(3, 2));
            bool hasResume = true;   // 재개 요청

            Assert.IsFalse(CarSim.TryEnqueueRouteStart(
                routes.RouteFor(12),
                V(2, 2),
                ref hasResume,
                net,
                12,
                out _));
            Assert.IsFalse(hasResume, "안전한 이전 타일이 없으면 중간 재개를 포기한다");
            Assert.AreEqual(0, TotalQueued(net, grid.Width, grid.Height),
                "재개는 교차로 스폰을 쓰지 않는다 — 출발 경로와 분리돼 있어야 한다");
        }
```

- [x] **Step 2: 범위 고정 테스트 추가 (D5)**

`IntersectionDepartureTests.cs`에 추가한다.

```csharp
        [Test]
        public void SpawnAtIntersection_RoundaboutTile_StillRejected()
        {
            SimConfig cfg = Cfg();
            var grid = CrossGrid();
            var devices = new FakeDeviceState();
            devices.AddRoundabout(V(4, 4));
            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid, devices);

            Assert.IsFalse(
                net.TryEnqueueAtIntersection(V(4, 4), Dir.N, Dir.N, 5, 1),
                "로터리는 링 예약 모델이 달라 이번 범위 밖이다(설계 D5)");
        }
```

`FakeDeviceState`는 `Assets/Tests/EditMode/RoadQueueDeviceTests.cs:10`에 `internal sealed`로 있고
`AddRoundabout(Vector2Int)`도 있다(`L19`). 같은 어셈블리(`CityFlow.Sim.Tests`)라 그대로 쓴다.
새로 만들지 말 것.

- [x] **Step 3: 재개 불변 확인**

`RebuildResume_*` 계열과 `Departure_WhenAccessRoadIsRamp_EntersHighway`를 **수정하지 않는다.** 그대로 통과해야 한다. 깨지면 재개 경로까지 번진 것이므로 **멈추고 보고**하라.

- [ ] **Step 4: GREEN 확인 요청** — 감독이 전체 스위트를 기준선과 대조한다.

- [x] **Step 5: 커밋**

```bash
git add Assets/Tests/EditMode/CarSimTests.cs \
        Assets/Tests/EditMode/IntersectionDepartureTests.cs
git commit -m "[Test] 교차로 출발 계약 갱신 — 오프네트워크 대기에서 정식 진입으로"
```

---

## 감독 검증 게이트 (워커 금지)

각 태스크 Step 2·4에서 감독이 실행한다.

1. `refresh_unity(compile="request")`
2. `read_console(types=["error"])` → **`error CS` 0건**
3. `run_tests` EditMode `CityFlow.Sim.Tests` → **착수 시점 실측 기준선과 대조**

문서에 적힌 과거 숫자(340·454·528·532)는 전부 낡았다. 착수 시 다시 잰다.
