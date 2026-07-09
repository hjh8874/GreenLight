# Tilemap → CityGrid Bake 어댑터 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unity Tilemap에 붓칠한 도시를 게임 시작 시 스캔해 엔진(CityGrid)에 굽는 입력 어댑터. `DebugCitySeeder`(하드코딩) 교체.

**Architecture:** 순수 bake 로직(`TilemapBake`)과 authoring 타일(`CityTile`)을 새 `CityFlow.Authoring` 어셈블리에 두어 EditMode 테스트가 참조·검증 가능하게 하고, Unity 생명주기 껍데기(`TilemapCityBaker` MonoBehaviour)만 Assembly-CSharp에 둔다. 엔진은 `IPlacementService.Place` 계약만 통하고 건드리지 않는다.

**Tech Stack:** Unity 6 (C# 9), UnityEngine.Tilemaps, NUnit EditMode.

## Global Constraints

- 엔진(`SimEngine`/`CityGrid`) 코드 수정 0. 새 엔진 API 0. `IPlacementService.Place`만 사용.
- 좌표 규약: Tilemap 셀 `(x, y)` → 엔진 그리드 `(x, y)` 직결. 범위 밖은 스킵.
- TileType 대상: `Road`, `House`, `Office`만(1차). School 제외. (`CityTile.type`은 제네릭 `TileType`이라 나중에 에셋만 추가.)
- 범위 밖·비-`CityTile`·빈 칸·중복은 조용히 스킵(예외 없음).

---

### Task 1: CityFlow.Authoring 어셈블리 + CityTile 타일

**Files:**
- Create: `Assets/01_Scripts/CityFlow/Authoring/CityFlow.Authoring.asmdef`
- Create: `Assets/01_Scripts/CityFlow/Authoring/CityTile.cs`

**Interfaces:**
- Consumes: `CityFlow.Contracts.TileType` (enum), `UnityEngine.Tilemaps.Tile`
- Produces: `CityFlow.Authoring.CityTile : Tile` with public field `TileType type`

- [ ] **Step 1: asmdef 생성**

`Assets/01_Scripts/CityFlow/Authoring/CityFlow.Authoring.asmdef`:
```json
{
    "name": "CityFlow.Authoring",
    "references": ["CityFlow.Contracts"],
    "autoReferenced": true
}
```
(UnityEngine.Tilemaps는 엔진 모듈이라 자동 참조됨.)

- [ ] **Step 2: CityTile 작성**

`Assets/01_Scripts/CityFlow/Authoring/CityTile.cs`:
```csharp
using UnityEngine;
using UnityEngine.Tilemaps;
using CityFlow.Contracts;

namespace CityFlow.Authoring
{
    // 붓칠 authoring용 타일. 자기 TileType을 들고 있어 bake가 별도 매핑 없이 타입을 읽는다.
    [CreateAssetMenu(fileName = "CityTile", menuName = "CityFlow/Authoring/City Tile")]
    public sealed class CityTile : Tile
    {
        public TileType type = TileType.Road;
    }
}
```

- [ ] **Step 3: 컴파일 확인**

Unity 리컴파일 후 콘솔 에러 0 확인(`read_console` types=Error). `CityTile` 타입이 인식되면 통과.

- [ ] **Step 4: Commit**

```bash
git add Assets/01_Scripts/CityFlow/Authoring/
git commit -m "[Feat] CityFlow.Authoring 어셈블리 + CityTile(자기 타입 아는 타일)"
```

---

### Task 2: TilemapBake 순수 로직 (TDD)

**Files:**
- Create: `Assets/01_Scripts/CityFlow/Authoring/TilemapBake.cs`
- Create: `Assets/Tests/EditMode/TilemapBakeTests.cs`
- Modify: `Assets/Tests/EditMode/CityFlow.Sim.Tests.asmdef` (references에 `CityFlow.Authoring` 추가)

**Interfaces:**
- Consumes: `UnityEngine.Tilemaps.Tilemap`, `CityFlow.Contracts.IPlacementService`, `CityFlow.Authoring.CityTile`
- Produces: `static int TilemapBake.Bake(Tilemap tilemap, IPlacementService placement)` — 배치 성공 칸 수 반환

- [ ] **Step 1: 테스트 asmdef에 Authoring 참조 추가**

`Assets/Tests/EditMode/CityFlow.Sim.Tests.asmdef`의 `references` 배열에 `"CityFlow.Authoring"` 추가:
```json
    "references": [
        "CityFlow.Sim",
        "CityFlow.Contracts",
        "CityFlow.Authoring",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
```

- [ ] **Step 2: 실패 테스트 작성**

`Assets/Tests/EditMode/TilemapBakeTests.cs`:
```csharp
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using CityFlow.Contracts;
using CityFlow.Authoring;

namespace CityFlow.Authoring.Tests
{
    public class TilemapBakeTests
    {
        // Place 호출을 기록하는 테스트용 배치 서비스(20x20 범위 흉내).
        sealed class RecordingPlacement : IPlacementService
        {
            public readonly Dictionary<Vector2Int, TileType> Placed = new Dictionary<Vector2Int, TileType>();
            readonly int w, h;
            public RecordingPlacement(int w, int h) { this.w = w; this.h = h; }
            public bool CanPlace(Vector2Int t, TileType type) =>
                t.x >= 0 && t.x < w && t.y >= 0 && t.y < h && type != TileType.Empty;
            public bool Place(Vector2Int t, TileType type)
            {
                if (!CanPlace(t, type)) return false;
                Placed[t] = type; return true;
            }
            public bool Remove(Vector2Int t) => Placed.Remove(t);
        }

        static CityTile MakeTile(TileType type)
        {
            var t = ScriptableObject.CreateInstance<CityTile>();
            t.type = type; return t;
        }

        static (Tilemap map, GameObject root) NewTilemap()
        {
            var root = new GameObject("grid", typeof(Grid));
            var child = new GameObject("tilemap", typeof(Tilemap));
            child.transform.SetParent(root.transform);
            return (child.GetComponent<Tilemap>(), root);
        }

        [Test]
        public void Bake_PlacesPaintedCityTiles_ByTypeAndCoord()
        {
            var (map, root) = NewTilemap();
            map.SetTile(new Vector3Int(1, 2, 0), MakeTile(TileType.Road));
            map.SetTile(new Vector3Int(3, 4, 0), MakeTile(TileType.House));
            var rec = new RecordingPlacement(20, 20);

            int placed = TilemapBake.Bake(map, rec);

            Assert.AreEqual(2, placed);
            Assert.AreEqual(TileType.Road, rec.Placed[new Vector2Int(1, 2)]);   // 셀=그리드 좌표
            Assert.AreEqual(TileType.House, rec.Placed[new Vector2Int(3, 4)]);
            Object.DestroyImmediate(root);
        }

        [Test]
        public void Bake_SkipsNonCityTiles_AndOutOfBounds()
        {
            var (map, root) = NewTilemap();
            map.SetTile(new Vector3Int(0, 0, 0), ScriptableObject.CreateInstance<Tile>());  // 비-CityTile
            map.SetTile(new Vector3Int(99, 99, 0), MakeTile(TileType.Road));                // 범위 밖(20x20)
            map.SetTile(new Vector3Int(5, 5, 0), MakeTile(TileType.Office));                // 유효
            var rec = new RecordingPlacement(20, 20);

            int placed = TilemapBake.Bake(map, rec);

            Assert.AreEqual(1, placed);
            Assert.AreEqual(TileType.Office, rec.Placed[new Vector2Int(5, 5)]);
            Assert.IsFalse(rec.Placed.ContainsKey(new Vector2Int(99, 99)));
            Object.DestroyImmediate(root);
        }
    }
}
```

- [ ] **Step 3: 컴파일 → 테스트 실패 확인**

`run_tests` EditMode → `TilemapBake` 미정의로 컴파일 실패(RED). 예상: "TilemapBake' does not exist".

- [ ] **Step 4: TilemapBake 구현**

`Assets/01_Scripts/CityFlow/Authoring/TilemapBake.cs`:
```csharp
using UnityEngine;
using UnityEngine.Tilemaps;
using CityFlow.Contracts;

namespace CityFlow.Authoring
{
    // Tilemap을 스캔해 CityTile이 칠해진 칸을 IPlacementService.Place로 엔진에 굽는다(순수 로직).
    // 셀 좌표(x,y) = 엔진 그리드(x,y). 범위 밖·비-CityTile·빈 칸은 Place가 걸러 스킵.
    public static class TilemapBake
    {
        public static int Bake(Tilemap tilemap, IPlacementService placement)
        {
            if (tilemap == null || placement == null) return 0;
            int placed = 0;
            foreach (var cell in tilemap.cellBounds.allPositionsWithin)
            {
                var tile = tilemap.GetTile(cell) as CityTile;   // 빈 칸·비-CityTile은 null → 스킵
                if (tile == null) continue;
                if (placement.Place(new Vector2Int(cell.x, cell.y), tile.type)) placed++;   // 범위 밖은 false→스킵
            }
            return placed;
        }
    }
}
```

- [ ] **Step 5: 테스트 통과 확인**

`run_tests` EditMode → `TilemapBakeTests` 2개 통과(GREEN) + 기존 전체 그린 유지.

- [ ] **Step 6: Commit**

```bash
git add Assets/01_Scripts/CityFlow/Authoring/TilemapBake.cs Assets/Tests/EditMode/TilemapBakeTests.cs Assets/Tests/EditMode/CityFlow.Sim.Tests.asmdef
git commit -m "[Feat] TilemapBake 순수 로직 + EditMode 테스트(타입·좌표·스킵)"
```

---

### Task 3: TilemapCityBaker MonoBehaviour 껍데기

**Files:**
- Create: `Assets/01_Scripts/CityFlow/Debug/TilemapCityBaker.cs`

**Interfaces:**
- Consumes: `CityFlow.Bootstrap.ICityFlowServiceConsumer`, `CityFlow.Bootstrap.CityFlowServices`, `CityFlow.Authoring.TilemapBake`, `UnityEngine.Tilemaps.Tilemap`/`TilemapRenderer`
- Produces: `TilemapCityBaker` MonoBehaviour — 씬에서 `DebugCitySeeder` 대체

- [ ] **Step 1: 껍데기 작성 (테스트 없음 — 순수 Unity 생명주기 배선, 로직은 Task 2에서 검증됨)**

`Assets/01_Scripts/CityFlow/Debug/TilemapCityBaker.cs`:
```csharp
using UnityEngine;
using UnityEngine.Tilemaps;
using CityFlow.Bootstrap;
using CityFlow.Authoring;

namespace CityFlow.DebugTools
{
    // Tilemap에 붓칠한 도시를 게임 시작 시 엔진에 굽는다. DebugCitySeeder(하드코딩) 교체.
    // bake 후 Tilemap은 숨김 — SimTileRenderer가 실제 뷰라 이중 렌더 방지(authoring 전용).
    public sealed class TilemapCityBaker : MonoBehaviour, ICityFlowServiceConsumer
    {
        [SerializeField] private Tilemap sourceTilemap;

        public void Initialize(CityFlowServices services)
        {
            if (!isActiveAndEnabled || sourceTilemap == null) return;

            int placed = TilemapBake.Bake(sourceTilemap, services.Placement);

            var tilemapRenderer = sourceTilemap.GetComponent<TilemapRenderer>();
            if (tilemapRenderer != null) tilemapRenderer.enabled = false;   // authoring용, play 땐 숨김

            Debug.Log($"[TilemapCityBaker] {placed}칸 bake 완료 — Tilemap 숨김");
        }
    }
}
```

- [ ] **Step 2: 컴파일 확인 + 전체 테스트 그린**

`read_console` types=Error → 0. `run_tests` EditMode → 전체 그린(껍데기가 컴파일을 안 깨는지).

- [ ] **Step 3: Commit**

```bash
git add Assets/01_Scripts/CityFlow/Debug/TilemapCityBaker.cs
git commit -m "[Feat] TilemapCityBaker — Tilemap 도시를 엔진에 굽는 MonoBehaviour 껍데기"
```

---

### Task 4: 씬 배선 + CityTile 에셋 + seeder 교체 (에디터 작업)

**Files:**
- Create: `Assets/05_ScriptableObjects/CityFlow/Tiles/RoadTile.asset`, `HouseTile.asset`, `OfficeTile.asset` (CityTile 인스턴스)
- Modify: `Assets/00_Scenes/SimDebug.unity` (Grid+Tilemap 추가, TilemapCityBaker 부착, DebugCitySeeder 제거)

**Interfaces:**
- Consumes: `CityTile` (에셋 생성), `TilemapCityBaker` (컴포넌트 부착)
- Produces: Play 시 Tilemap 도시가 엔진에 baked

- [ ] **Step 1: CityTile 에셋 3개 생성**

`Assets/05_ScriptableObjects/CityFlow/Tiles/`에 `Create > CityFlow > Authoring > City Tile` 3개:
- `RoadTile` → `type = Road`, sprite = 회색 사각(구분되게)
- `HouseTile` → `type = House`, sprite = 파랑 사각
- `OfficeTile` → `type = Office`, sprite = 주황 사각

(sprite는 구분용 단색이면 충분. Unity 내장 `Square` 스프라이트나 단색 32x32.)

- [ ] **Step 2: SimDebug 씬에 Grid+Tilemap 추가**

Hierarchy 우클릭 → `2D Object > Tilemap > Rectangular` → `Grid`+`Tilemap` 생성. Tilemap 이름 `AuthoringTilemap`.

- [ ] **Step 3: 팔레트로 테스트 도시 붓칠**

`Window > 2D > Tile Palette`에 세 CityTile 등록 → AuthoringTilemap에 붓칠:
- 간선 2줄(y=8, y=12, x=2~17) Road · 세로연결(x=6, x=13, y=8~12) Road
- 서쪽 도로 인접 House 여러 칸 · 동쪽 도로 인접 Office 여러 칸
- (좌표는 셀=그리드. `[0,20)×[0,20)` 안에.)

- [ ] **Step 4: TilemapCityBaker 부착 + seeder 제거**

- `CityFlow_Debug` GameObject에 `TilemapCityBaker` 컴포넌트 추가 → `sourceTilemap`에 AuthoringTilemap 할당.
- 같은 GameObject의 `DebugCitySeeder` 컴포넌트 **제거**(Remove Component).

- [ ] **Step 5: Play 검증**

Play → 확인:
- Game 뷰에 붓칠한 도시가 뜸(SimTileRenderer가 baked 엔진 상태 렌더).
- AuthoringTilemap 렌더러는 숨겨져 이중 렌더 없음.
- 콘솔에 `[TilemapCityBaker] N칸 bake 완료`.
- 튜너로 신호 조작 시 처리량 반응(도시가 진짜 엔진에 들어감).

- [ ] **Step 6: Commit**

```bash
git add Assets/05_ScriptableObjects/CityFlow/Tiles/ Assets/00_Scenes/SimDebug.unity
git commit -m "[Feat] SimDebug 씬: Tilemap 붓칠 도시 + baker 배선, seeder 교체"
```

---

## 완료 후
- PR to develop. DebugCitySeeder 클래스는 코드로 남김(참고용) — 씬에서만 교체됨. 후속 정리 여부는 팀 판단.
- 승격 경로: 이 bake 패턴이 나중에 레벨 로더(레벨 Tilemap → 엔진)로 재사용됨.
