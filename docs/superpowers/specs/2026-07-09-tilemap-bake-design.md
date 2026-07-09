# Tilemap → CityGrid Bake 어댑터 — 설계

> 2026-07-09 · 작성: 환 · 브랜치: `feat-tilemap-bake-hwan`

## 목적
디버그/테스트 도시를 **Unity Tilemap으로 붓칠 authoring**하고, 게임 시작 시 스캔해서 엔진(CityGrid)에 굽는다.
하드코딩된 `DebugCitySeeder`를 교체한다. 지금은 실행 중 `execute_code`로 타일을 손으로 찍어야 테스트 도시가 나오는데(수동·반복), 이걸 시각적 붓칠로 대체한다.

**이중 용도**: "Tilemap 스캔 → TileType 변환" 패턴은 나중에 실제 레벨 로더(레벨을 Tilemap으로 그려 엔진에 굽기)와 동일 — 디버그용으로 시작해 승격 가능.

**스코프 경계**: 순수 입력 어댑터(글루). 엔진(`SimEngine`/`CityGrid`) 코드는 건드리지 않고 `IPlacementService.Place` 계약만 통한다. 새 엔진 API 0개.

## 구성 (각 1책임)

### ① `CityTile : UnityEngine.Tilemaps.Tile` (ScriptableObject)
- Unity 기본 `Tile` 확장 + 필드 하나: `public TileType type;`
- 타일이 자기 타입을 앎 → 별도 매핑 리스트 불필요.
- 팔레트용 에셋 **3개 생성**: Road / House / Office. (School은 1차 제외 — 필드는 제네릭이라 나중에 에셋만 추가하면 됨)
- 위치: `Assets/05_ScriptableObjects/CityFlow/Tiles/` (기존 SO 폴더 규약 따름)

### ② `TilemapCityBaker : MonoBehaviour, ICityFlowServiceConsumer`
- SimDebug 씬에서 `DebugCitySeeder`를 교체.
- 직렬화 참조: `[SerializeField] Tilemap sourceTilemap;`
- `Initialize(services)`에서 bake 실행:
  1. `sourceTilemap.cellBounds` 순회 → 각 셀 `GetTile(pos) as CityTile`
  2. `CityTile`이면 `.type`을 읽어 `services.Placement.Place(new Vector2Int(pos.x, pos.y), type)` 호출
  3. `CityTile` 아님·빈 칸·그리드 범위 밖(Place가 false) → **조용히 스킵**(무사고, 기존 seeder 철학)
  4. bake 후 `sourceTilemap`의 `TilemapRenderer.enabled = false` — SimTileRenderer가 실제 뷰라 이중 렌더 방지. (Tilemap은 authoring 전용, play 땐 숨김)
- 위치: `Assets/01_Scripts/CityFlow/Debug/`

### ③ 좌표 규약
- Tilemap 셀 `(x, y)` → 엔진 그리드 `(x, y)` 직결.
- `[0, W) × [0, H)` 안에 칠하면 반영, 밖은 스킵. (엔진 그리드 기본 20×20)
- 음수/오프셋 셀은 그리드 밖 → Place 실패로 자동 스킵.

## 데이터 흐름
```
붓칠(에디터) → [Tilemap 에셋] → Initialize 1회 스캔 → Place per cell → CityGrid(엔진 소유)
                                                              이후 Tilemap은 연산·렌더 무관(숨김)
```

## 에러 처리
- 그리드 범위 밖 / 이미 찬 칸 → `Place`가 `false` 반환 → 스킵(예외 없음).
- `CityTile`이 아닌 타일(플레인 Tile 등) → 스킵. (선택: 첫 bake 때 개수만 로그)
- `sourceTilemap` 미할당 → 경고 로그 후 조용히 종료(빈 도시).

## 테스트 (EditMode)
bake 로직을 검증 가능한 단위로:
- `TilemapCityBaker.Bake(Tilemap tilemap, IPlacementService placement)` 형태(정적 또는 인스턴스 메서드)로 분리.
- 테스트: EditMode에서 GameObject+Tilemap 생성 → `CityTile`(Road/House/Office) 칠함 → `FakePlacementService`로 Bake 실행 → **fake가 받은 Place 호출 집합이 칠한 셀·타입과 일치**하는지 단언.
- 커버: 타입 읽기 정확성 · 좌표 매핑(셀=그리드) · 범위 밖 스킵 · 비-CityTile 스킵.

## 스코프
**포함**: CityTile SO(3종 에셋) · TilemapCityBaker · 좌표 규약 · bake-후-숨김 · EditMode 테스트 · SimDebug 씬에서 seeder→baker 교체.
**제외(1차)**: School 타일 · Tilemap 오프셋 지정 · 실제 레벨 로더 승격 · 런타임 재-bake.
**교체**: `DebugCitySeeder` 클래스는 코드로 남기되 씬에선 baker 사용. (원하면 후속 삭제)
