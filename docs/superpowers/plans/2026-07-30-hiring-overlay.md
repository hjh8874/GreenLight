# 채용 오버레이(A) Implementation Plan

> **For agentic workers:** 이 계획을 태스크 순서대로 실행한다. 각 스텝은 체크박스로 추적한다.
> B(지각 출근)는 이 계획 범위 밖 — #182 머지 후 별도 계획으로 간다.

**Goal:** 정원 미달 회사 위에 `채용중 n/m` 라벨을 띄워 채용 진행이 화면에서 읽히게 한다.

**Architecture:** `BuildingConstructionOverlay`(#181, `Assets/01_Scripts/CityFlow/UI/Controllers/BuildingConstructionOverlay.cs`)를 **그대로 본뜬다** — 같은 발견 경로(Placed + RestoreCompleted + 초기 수집), 같은 갱신(Update 폴링 + GridToWorld + XZ 빌보드), 같은 정리 규칙(조회 실패 = 라벨 제거). 데이터만 `TryGetConstructionProgress01` 대신 `IReadOnlyCityStats.TryGetCompanyStaffing`.

**Tech Stack:** Unity 6000.5.2f1 · C# · NUnit EditMode(기본 에디터 어셈블리, 이름 필터)

**설계:** `docs/superpowers/specs/2026-07-30-hiring-feedback-design.md`

## Global Constraints

- 브랜치: **`feat-hiring-overlay-hwan`** (develop `d50078e` 직분기). 브랜치 변경 금지.
- 회귀 기준선: `CityFlow.Sim.Tests` **423/423** (develop `d50078e`). 부분 실패 허용 없음.
- 검증 순서(고정): `refresh_unity`(compile=request, mode=force) → `read_console`(types=["error"]) —
  `error CS` 포함 메시지만 진짜 에러(`Bridge not running`·`NanumGothic` 폰트는 무시) →
  `run_tests`. 신규 테스트는 `group_names=[".*HiringStatusOverlayTests.*"]` 이름 필터로만 돈다.
- 테스트가 돌았다는 것 ≠ 컴파일 성공(직전 DLL). `read_console` 먼저.
- RED 먼저 증명(CS0246도 RED로 인정). 씬(.unity) 커밋 금지. 신규 파일은 `.meta` 함께.
- **남의 파일 수정 금지** — 이 계획은 신규 파일 4개 + 프리팹 1개뿐이다. 그 외를 만져야 할 상황이면 멈추고 escalation.

## File Structure

| 파일 | 책임 | 변경 |
|---|---|---|
| `Assets/01_Scripts/CityFlow/UI/Controllers/HiringStatusOverlay.cs` | 라벨 수명주기 | **신규** |
| `Assets/02_Prefabs/UI/HiringStatusSystem.prefab` | 끌어다 놓으면 끝 | **신규** |
| `Assets/Tests/ViewEditMode/Editor/HiringStatusOverlayTests.cs` | 등록·제거·만석 숨김 | **신규** |

## 참고 구현 (그대로 본뜰 것)

- `BuildingConstructionOverlay.cs` — 구독 3중 경로·`CollectExistingSites`의 앵커 필터·Update 정리 루프·XZ 빌보드(`space.Plane == XY ? CoordinateRotation : cam.rotation`)·`OnDestroy` 해제
- `BuildingConstructionOverlayTests.cs` — `FakeTileData`·`FakeWorldGrid`·리플렉션 `SetPrivate`/`LabelCount` 패턴. staffing 은 `IReadOnlyCityStats` 페이크를 추가한다(`ActiveVehicleCount`·`TryGetCompanyStaffing` 2멤버 — develop 기준 이 계약에 `LastDayArrivalCount` 는 **없다**, #183 미머지)

### Task 1: 컨트롤러 + 테스트

- [x] **Step 1 (RED):** `HiringStatusOverlayTests.cs` 작성 — `BuildingConstructionOverlayTests` 구조 복제 + `FakeStats : IReadOnlyCityStats`(딕셔너리로 anchor→(filled, capacity)). 단정 4건:
  1. `Initialize` 시 정원 미달 회사 앵커에 라벨 1개(이벤트 없이 — 복원·씬 진입 대비)
  2. 풋프린트당 1개(앵커 필터 — 2x2 에 4개 금지)
  3. 만석 회사(filled==capacity)는 라벨 없음
  4. staffing 조회 실패(철거)는 Update 뒤 라벨 제거 — Update 는 리플렉션 호출 또는 internal seam
  `refresh_unity` → `read_console` 에서 CS0246 확인.
- [x] **Step 2 (구현):** `HiringStatusOverlay.cs` — `BuildingConstructionOverlay` 를 열고 구조를 그대로 옮긴 뒤 데이터 소스만 교체:
  - 등록 조건: `stats.TryGetCompanyStaffing(anchor, out s)` 성공 && `s.Filled < s.Capacity` && `tiles.IsFootprintAnchor(anchor)` && 타일 타입 Office
  - 라벨 텍스트: `$"채용중 {s.Filled}/{s.Capacity}"` — Update 마다 갱신
  - 만석 도달·조회 실패 → 라벨 Destroy + 딕셔너리 제거 (만석은 제거 후 재등장 없음 — 정원 축소로 다시 미달이 되면 다음 Placed/Restore 수집에서 재등록되는 것으로 충분, 상시 재스캔은 하지 않는다: ponytail)
  - `Placed`: `e.Type == TileType.Office && !e.IsRemove` 만 등록 시도
- [x] **Step 3 (GREEN):** 이름 필터 4/4 → `run_tests`(assembly_names=["CityFlow.Sim.Tests"]) 423/423 회귀 0 → `read_console` 0.
- [x] **Step 4 (커밋):** `[Feat] 채용 오버레이 — 정원 미달 회사에 '채용중 n/m' 라벨` (신규 4파일 + meta 명시 목록으로 add)

### Task 2: 프리팹 + 최종 게이트

- [x] **Step 1:** `manage_prefabs`/`execute_code` 로 `HiringStatusSystem.prefab` 조립 — 루트(`HiringStatusOverlay`) + 자식 `HiringLabelTemplate`(TextMeshPro, 비활성). `BuildingConstructionSystem.prefab` 과 같은 구조. 직렬화 필드(labelTemplate) 배선 확인. **씬 인스턴스는 저장 후 삭제**.
- [x] **Step 2:** 게이트 전부 재실행(이름 필터 + Sim.Tests + read_console). `git status` 에 `.unity` 없음 확인.
- [x] **Step 3 (커밋):** `[Feat] 채용 오버레이 프리팹 — 끌어다 놓으면 배선 끝`
- [x] **Step 4:** worker_done — RED 증거·게이트 수치·커밋 해시·계획과 달랐던 점.
