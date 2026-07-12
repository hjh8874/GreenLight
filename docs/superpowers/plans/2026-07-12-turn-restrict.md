# 턴 제한 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 교차로 표지판(좌회전만/우회전만) — 라우팅 상태 확장(타일×진입방향, 표지판 0개면 레거시 무풍), 신호 공존, 세이브·마커·샌드박스 키.

**Architecture:** 스펙 `docs/superpowers/specs/2026-07-12-turn-restrict-design.md`의 결정 10개가 규범. 5번째 배치 가족(좌표→TurnMode 맵). Search는 이중 경로: 표지판 0개=기존 함수 그대로 / 있으면 [n×4] 상태 확장 탐색(별도 내부 함수로 분리 — 기존 코드 무수정이 회귀 방어).

**Tech Stack:** Unity EditMode NUnit + Unity MCP. TDD 필수.

## Global Constraints

- 브랜치 `feat-turn-restrict-hwan`(스택: feat-oneway-hwan 위). 커밋 접두 `[Feat]`.
- 결정론: flat 정렬, 진입방향 인덱스 고정 순회(E,S,W,N), Dictionary 조회만.
- 회전 정의: 좌회전 = 반시계 90°(E→N, y-up), 우회전 = 시계 90°(E→S). U턴 상시 금지. 표지판 타일 관여 대각 스텝 금지.
- 배타: 로터리·입체와 배타 — **양방향**(표지판이 로터리/입체를 검사 + `CanPlaceRoundabout`/`CanPlaceOverpass`도 표지판 검사. 2026-07-12 정정: 초판의 "3형제 무수정"은 신호 공존 의도가 과확장된 계획 결함) / **신호와 공존**(`CanPlaceSignal`은 무수정). 시작 타일 표지판은 무제약(진입이 아님).
- 표지판 0개 = 기존 Search 함수 그대로 호출(경로·tie-break 완전 동일). 기존 180 그린이 그 증명.
- 비용 함수·혼잡 가중·흐름 수식 무변경. Unity MCP refresh→CS→job 폴링, Thread.Sleep 금지.

---

### Task 1: 배치 API + 세이브 (5번째 가족)

**Files:** Modify `Sim/SimEngine.cs`, `Contracts/ISignalControl.cs`(4종+GetTurnMode, 22종 도달 주석), Create `Contracts/TurnMode.cs`(public enum — Contracts 소속: 계약 표면에 노출), `Contracts/Save/TurnSignSaveData.cs`, Modify `Contracts/Save/SimSaveData.cs`. Test: `Assets/Tests/EditMode/TurnRestrictTests.cs`(신규).

**Interfaces (Produces):** `TurnMode { LeftOnly, RightOnly }` / `TurnSignTiles`/`CanPlaceTurnSign(tile)`/`TryPlaceTurnSign(tile, TurnMode)`/`TryRemoveTurnSign(tile)`/`GetTurnMode(tile)`(`TurnMode?` 또는 bool TryGet — 구현자 판단, 뷰가 쓰기 편한 쪽) / 내부 `Dictionary<Vector2Int,TurnMode> _turnSigns` + flat List. Task 2가 `_turnSigns`를 Plan에 전달.

- [ ] TDD: 스펙 §5의 1(배치/배타/신호 공존 핀/철거/소멸/자동모드)·5(세이브 라운드트립+레거시) 먼저 → RED(CS) → 구현 → 그린. 기하는 OnewayTests/RoundaboutTests의 검증된 Build 재사용.
- [ ] 배치 조건: 배치 모드 && InBounds && 교차로 && `!_roundaboutSet` && `!_overpassSet` && 미배치. **신호는 검사 안 함**(공존). 배치/철거 시 MarkTopologyDirty(+핀 테스트 — TopologyDirtyForTest seam 재사용).
- [ ] 프루닝: RebuildSignals 소멸 블록 5번째(교차로 해제 시).
- [ ] 세이브: `TurnSigns`(X,Y,Mode int) — 복원은 배치 모드 블록 5번째, 배치 조건 재검증(손상 세이브: 로터리/입체 선점 좌표·비교차로 거부), flat Sort.
- [ ] 전체 그린 → 커밋 `[Feat] 턴 제한 배치 API + 세이브 — 신호 공존, 5번째 가족`

### Task 2: 라우팅 상태 확장

**Files:** Modify `Sim/RoutePlanner.cs`, `Sim/SimEngine.cs`(Plan 호출 2곳 전달). Test: TurnRestrictTests 추가.

**Interfaces (Consumes):** `_turnSigns`. **Produces:** `Plan(..., oneways, turnSigns)` — 기존 시그니처 null 위임 체인 연장.

- [ ] TDD: 스펙 §5의 2(P턴 창발 — LeftOnly에서 직진 수요가 우회, **같은 타일 재방문 허용 확인**)·3(U턴·대각 금지, 미도달 무사고)·4(**레거시 무풍** — 표지판 0개 대표 기하에서 경로 리스트 완전 동일 비교)·결정론 먼저 → RED → 구현 → 그린.
- [ ] 구현: `Search` 진입부 분기 — `turnSigns == null || turnSigns.Count == 0` → 기존 탐색 함수(무수정). 아니면 `SearchWithTurnState`(신규 내부 함수): dist/cameFrom `[n×4]`(방향 인덱스 E=0,S=1,W=2,N=3 고정), 시작은 방향 미확립(첫 스텝이 상태 확립), 표지판 타일 진입 상태에서 나가는 스텝은 `Turn(d_in→d_out)==모드`만(U턴 금지 상시, 표지판 관여 대각 금지), **일방통행 필터 3규칙도 이 경로에서 동일 적용**(두 도구 공존!). 재구성은 (타일,방향) 체인 → 타일 리스트(중복 허용).
- [ ] 상태 배열은 생성자 1회 할당(틱 중 new 0 — 재계획 경로지만 관례 유지), 호출 간 리셋은 기존 `_cameFrom` 관례 참고.
- [ ] **FlowSolver 중복 타일 안전 확인**: P턴 경로(타일 재방문)를 AxisWeights/병목/pending이 예외 없이 처리하는지 테스트로 핀(P턴 창발 테스트가 delivered>0까지 확인하면 겸용).
- [ ] 전체 회귀(180+신규) → 커밋 `[Feat] 턴 제한 라우팅 — (타일×진입방향) 상태 확장, 레거시 무풍`

### Task 3: 뷰 + 샌드박스 키

**Files:** Modify `View/MainCityView.cs`(RefreshTurnSigns — 굽은 화살, 신호와 z 분리), `Debug/SandboxPlacementControls.cs`(`5`키 배치/Left↔Right 회전, `0` 철거 체인, 패널 표시).

- [ ] 마커: 몸통 바+꺾인 촉(좌/우 형태는 GetTurnMode), 기존 폴링 수명 규약(에셋 스왑 1함수 수렴). 신호 공존 타일에서 z 오프셋으로 겹침 회피.
- [ ] Play 스모크(샌드박스): 십자 구성 → `5`로 LeftOnly 배치 → 경로가 P턴/우회로 재계획 + DeliveredTotal 유지 확인 → 회전(Right) → 철거. 신호와 같은 타일 공존 시각 확인. config·씬 원복, 워킹트리 클린.
- [ ] 전체 회귀 → 커밋 `[Feat] 턴 제한 뷰 — 굽은 화살 마커 + 샌드박스 5키`

## 완료 기준

- EditMode 180 + 신규(~12) 전부 그린, 레거시 무풍 핀 통과, Play 스모크, 최종 브랜치 리뷰(fable) "Ready to merge", 푸시 + 스택 PR.
