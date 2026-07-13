# 설계 — SimConfig 런타임 재주입 seam (정책 테크 트리 엔진 기반)

> 작성: 2026-07-12 (환 자율 위임, "고" 승인). 브랜치 `feat-simconfig-seam-hwan`(스택: 샌드박스 위).
> 배경: 정책 테크 트리(재택근무=수요 −15% 등)는 전부 SimConfig 값 델타 — 유일한 장애물이 "config는 엔진 생성 시 1회 주입". 07-11 외부 피드백 검토의 "정책은 SimConfig seam 선행" 판정 이행. 정책 콘텐츠(SO·해금 UI·밸런스)는 팀 몫 — 엔진은 seam만.

## 목표 / 스코프

`SimEngine.ApplyConfig(in SimConfig next)` — 실행 중 밸런스 값 교체. **구조 필드는 보존**(грид 크기·모드 — 정책이 건드릴 영역이 아님). 다음 틱부터 자연 반영.

## 핵심 결정

| 결정 | 내용 | 근거 |
|---|---|---|
| **구조 필드 3종 보존** | `GridWidth`/`GridHeight`/`AutoDetectSignals`는 기존 값 유지(들어온 값 무시) | 그리드 크기는 전 배열(FlowSolver·ArrivalEmitter·BurstDetector·planner)이 생성 시 고정 — 런타임 변경은 재구축 스코프. AutoDetectSignals는 세션 부트 스위치(피벗 날 asset 전환) — 정책이 흔들면 배치 상태가 증발하는 지뢰 |
| DemandMap 갱신 | `_demand`가 config 사본을 보관(용량·풀) → internal `ApplyConfig(in SimConfig)` 추가 | 파일 확인: DemandMap.cs:33 readonly 사본 — seam의 유일한 전파 지점. 나머지(FlowSolver·planner·emitter류)는 호출 시 cfg를 매번 받아 자동 반영 |
| **재계획 강제** | ApplyConfig 후 `_grid.MarkTopologyDirty()`(internal 신설 — CityGrid에 dirty setter가 현재 없음) | 용량·수요풀·라우팅 가중 w 변경은 수요 배정·경로에 영향 — 다음 틱 dirty 블록이 Reassign+Plan을 돌리면 정합. 매 틱 재계획 아님(1회) |
| 반영 시점 = 다음 틱 | 즉시 Resolve 재실행 안 함 | 엔진 파이프라인 순서 보존(계산 중 상태 교체 금지 — 이벤트 재진입 차단과 같은 철학) |
| public 메서드(계약 승격은 제안만) | SimEngine 파사드에 public, 주석에 "정책 서비스(진우) 창구 — 계약 승격은 합의 후" | 소비자가 아직 없음(정책 SO는 팀 몫). YAGNI |

## 테스트 (EditMode — SimConfigSeamTests)

1. `ApplyConfig_DemandDelta_TakesEffectNextTick`: 수요 15% 감소 정책 → delivered 비례 감소(재택근무 시나리오 그대로).
2. `ApplyConfig_CapacityUp_RelievesJam`: 정체 도시에 RoadCapacity 증가 → 혼잡 완화·delivered 상승(차선 확장의 엔진 기반 증명 겸용).
3. `ApplyConfig_PreservesStructuralFields`: GridWidth/AutoDetectSignals를 바꿔 넣어도 무시 — 배치 신호 생존, 그리드 정상.
4. 결정론: 같은 시퀀스(틱→Apply→틱) 2회 = 같은 delivered.

## 비목표

정책 SO/해금 UI/밸런스 수치(진우·김건), 그리드 리사이즈, AutoDetectSignals 런타임 전환, 정책 스택킹 규칙(적용측 소관 — 엔진은 마지막 값만 안다).
