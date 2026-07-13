# 설계 — 신호 배치형 전환 (구매 피벗 2단계 엔진 기반)

> 작성: 2026-07-11 (환 위임 — 자율 설계, 결정 근거 명기). 짝 계획: writing-plans로 후속.
> 선행 스택: PR#38 ← 축분리 ← 파밍가드 ← 라우팅 ← 차량겹침. 브랜치 `feat-signal-placement-hwan`.
> 배경: 환 필수 과제 ①"신호등 자동 생성 제거". 팀 구두 협의(구매제 피벗). 1단계(축분리+무신호 간섭)는 완료 — 이 스펙이 그 수학을 라이브로 여는 문.

## 목표 / 스코프

**엔진이 "배치된 곳에만 신호가 존재"하는 모드를 지원**한다. 가격·상점 UI(김건)·경제 연동(진우)은 팀 몫 — 엔진은 배치/철거 API와 세이브 복원만 제공한다.

## 핵심 자율 결정 (근거 포함)

| 결정 | 내용 | 근거 |
|---|---|---|
| **`AutoDetectSignals` 플래그** | SimConfig 신설, **기본 true = 현행 자동 감지 그대로**. false = 배치된 신호만 존재 | 상점 UI가 없는 지금 자동을 끄면 라이브에 신호 0 + 놓을 방법도 없음. 엔진이 양쪽을 지원하고 UI가 붙는 날 asset 한 줄로 전환 — 기존 테스트 120도 무수정 생존 |
| 배치 파라미터 = `greenSlots` | `TryPlaceSignal(tile, int greenSlots)` — 초기 듀티(가로 초록 슬롯). 방향+초 = 이 값 하나로 표현(>주기/2 = 가로 우선). 오프셋은 기존 레버로 후조정 | 환 구상("방향이랑 초 정해서 구매")을 축분리 수학의 실제 노브에 1:1 매핑. 파라미터 두 개로 쪼갤 이유 없음(YAGNI) |
| 신호는 교차로에만 | 배치 조건 = `IsIntersection`. 도로 철거로 교차로가 해제되면 **신호 자동 소멸**(다음 재구축 시) | "신호=교차로 조율 장치" 기존 계약 유지. 환불/보상은 경제(진우) 영역 — 엔진은 소멸만 |
| 세이브 포맷 무변경 | `SignalSaveData`(X,Y,Offset,Green)가 이미 배치 기록 그 자체. 배치 모드 복원 = 저장된 신호를 그대로 배치 | 구세이브(자동 시절 = 전 교차로 신호)를 배치 모드로 열면 그 신호들이 전부 배치된 걸로 복원 — **포맷·마이그레이션 공짜** |
| 이벤트 없음 | 신호 배치/소멸 이벤트 미발행 | 뷰가 매 프레임 `SignalTiles` 폴링(RefreshSignals) — 자동 갱신. YAGNI |

## §1. SignalMap — 배치 모드 재구축

`Rebuild(grid)` 오버로드 추가: `Rebuild(CityGrid grid, IReadOnlyList<Vector2Int> placed)`.
- `placed == null` → 현행 자동 감지(스캔).
- `placed != null` → placed 순서(엔진이 flat 정렬 유지)대로, `grid.IsIntersection`인 타일만 등록. 기존 Signal 객체 보존(오프셋·초록·오버라이드 유지) / 탈락분 제거 — 현행 생존 규약 그대로.
- 결정론: placed 리스트가 순회 순서의 단일 출처(엔진이 flat 오름차순 유지).

## §2. SimEngine — 배치 API + 소유

- `readonly List<Vector2Int> _placedSignals`(flat 정렬 유지) + 조회용 HashSet.
- **`TryPlaceSignal(Vector2Int tile, int greenSlots)`**: 배치 모드에서만(자동 모드 false 반환 — 자동엔 이미 다 있음). 조건 = `_grid.IsIntersection(tile)` && 미배치. 성공 시 등록 → `_signals.Rebuild(grid, _placedSignals)` → `TrySetSignalGreenSlots(tile, greenSlots)`(기존 [1,주기-1] 클램프 재사용). true 반환.
- **`TryRemoveSignal(Vector2Int tile)`**: 배치 모드에서만, 배치된 것만. 등록 해제 → Rebuild.
- **`CanPlaceSignal(Vector2Int tile)`**: UI 버튼 활성용 — 배치 모드 && 교차로 && 미배치.
- **재구축 블록**(Step·SettleOffline·RestoreSnapshot): `_signals.Rebuild(_grid)` → 배치 모드면 먼저 `_placedSignals`에서 비교차로 제거(자동 소멸) 후 `Rebuild(_grid, _placedSignals)`, 자동 모드면 현행. 공통 헬퍼 `RebuildSignals()`로 묶음.
- TopologyDirty 무관(신호는 경로·수요에 영향 없음 — 현행과 동일).

## §3. RestoreSnapshot — 배치 모드 복원

현행: Rebuild(자동 감지) 후 저장된 오프셋/초록 TrySet. 배치 모드: **저장된 신호 타일들 = 배치 목록**으로 `_placedSignals` 재구성(flat 정렬, 비교차로 스킵) → Rebuild(placed) → TrySet 루프(현행 그대로). 자동 모드는 현행 무변경.

## §4. 계약 제안 (ISignalControl — E-1과 같은 "제안 커밋" 관례)

`TryPlaceSignal`/`TryRemoveSignal`/`CanPlaceSignal` 3개를 `ISignalControl`에 추가 — 김건 상점 UI가 붙을 창구. 시그니처 확정은 김건 합의(주석 명기). 기존 8멤버 시그니처 무변경.

## §5. SimConfig

| 필드 | 값 |
|---|---|
| `AutoDetectSignals`(신설) | **true** 🔓 — 상점 UI 도입 시 asset에서 false로 전환(그날 무신호 간섭 λ 수학이 라이브 활성화) |

asset도 true로 명시 기록.

## 파급 (의도)

- 기본값 true = **라이브·기존 테스트(120) 동작 무변경**.
- 배치 모드에서 비로소: 무신호 교차로 간섭(축분리 스펙의 잠복 수학) 라이브 활성 + 코리도어 오버라이드가 배치된 신호만 수집(자동 정합) + "신호를 사는 이유" 3종(간섭 제거·듀티 분배·오버라이드 자격) 완성.

## 비범위

가격/구매 UI/경제 연동(팀), 배치 이벤트/연출, 신호 이설(remove+place 조합으로 충분), 자동→배치 라이브 전환 시점 결정(팀 회의).

## 검증 계획 (EditMode)

- 배치: 교차로 ✓ / 비교차로 ✗ / 중복 ✗ / 자동 모드에서 ✗ / greenSlots 즉시 반영.
- 철거: TryRemoveSignal ✓ / 도로 철거로 교차로 해제 → 다음 틱 신호 자동 소멸.
- 배치 모드 기본: 교차로 있어도 SignalTiles 빈 목록 + 무신호 간섭 활성(S2류 delivered 비교 — 배치가 이김).
- 세이브 왕복(배치 모드): 배치 2개+레버 튜닝 → snapshot → restore → 동일 신호·레버.
- 구세이브 호환: 자동 모드 스냅샷 → 배치 모드 엔진에 복원 → 저장된 신호 전부 배치됨.
- 코리도어: 배치된 라인 3신호에서 오버라이드 코리도어 작동.
- 기존 120 회귀 0(기본 true).
