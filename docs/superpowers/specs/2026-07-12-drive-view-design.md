# 설계 — 드라이브 뷰 (그린웨이브 1인칭 PiP)

> 작성: 2026-07-12 (환 자율 위임 — 로드맵 2026-07-12 "1차 빌드 확장 ③"). 짝 계획: 2026-07-12-drive-view.md.
> 선행 스택: 뷰 팩(`feat-view-popups-hwan`, 151/151) 위. 브랜치 `feat-drive-view-hwan`.
> 배경: 유저가 맞춘 그린웨이브를 1인칭으로 관통하는 화면 구석 PiP — 체류 시간·과시 자극. docs에 선행 기록 없음(팀 신규 제안 — PR에서 소개). 데스크톱 플로팅 컨셉과 시너지.

## 목표 / 스코프

**뷰 전용, 씬 무변경.** 화면 우상단 PiP 카메라가 활성 통근 경로를 따라 저공 1인칭으로 달린다. `D` 키 토글(기본 ON — 경로 있을 때만 렌더). 엔진·계약·세이브·씬 파일 전부 무변경.

## 핵심 결정 (근거 포함)

| 결정 | 내용 | 근거 |
|---|---|---|
| **씬 배선 0 — 런타임 생성** | `MainCityView.Initialize`가 `gameObject.AddComponent<DriveViewCamera>()` 한 줄로 부착, 카메라 GO도 컴포넌트가 런타임 생성 | FlowBurstJuice의 교훈(씬 수동 배선 = 재직렬화 노이즈로 PR 제외됐던 선례)을 원천 회피. 씬 오너 손 안 빌림 |
| 새 파일 `View/DriveViewCamera.cs` | MainCityView에 인라인하지 않음 | MainCityView가 이미 1100줄+ — 독립 관심사(카메라)는 분리. 런타임 AddComponent라 분리해도 배선 비용 0 |
| **PiP = viewport rect** | `camera.rect = (0.72, 0.72, 0.27, 0.27)`, depth = 메인+1 | RenderTexture+Canvas는 UI 의존(김건 영역)이 생김 — URP는 base 카메라 viewport rect를 지원하므로 카메라 하나로 끝 |
| 경로 = 최장 활성 경로 | `simEngine.ActiveRoutes`에서 가장 긴 경로 선택, 끝까지 달리면 재선택 | 최장 경로가 그린웨이브 과시에 최적(신호를 많이 지남). 라운드로빈보다 단순 |
| 카메라는 신호에 안 멈춤 | 일정 속도로 경로 주파 | "한 번도 안 걸리고 관통"이 연출 의도 — 유저가 조율을 잘했다는 환상 강화. 정지 로직은 오히려 스코프+역효과 |
| 저공 시점 | 위치 = 경로점 + z −0.9, 전방 = 진행 방향 + 아래로 살짝(z +0.45 성분), up = Vector3.back | 보드가 XY 평면(카메라쪽이 −z)인 2.5D — 큐브 도시를 스치듯 달리는 그림. 튜닝 필드(높이·틸트)로 노출 |
| 기본 ON, `D` 토글 | 경로 0개면 자동 숨김 | 데모 첫인상용. 기존 키맵(Tab , . [ ] r g v)과 비충돌 |
| AudioListener 없음 | 카메라 GO에 Camera만 | 씬 메인 카메라의 리스너와 중복 방지(Unity 경고) |

## §1. DriveViewCamera (신규, 뷰 전용)

- `Init(SimEngine engine, MainCityView view, float tileSize)` — MainCityView가 Initialize에서 호출(AddComponent 직후). GridToLocal 좌표계는 MainCityView의 로컬이므로 카메라 위치는 view.transform 기준으로 변환(`view.transform.TransformPoint`).
- Update: ①경로 유효성 확인(끝 도달·경로 소멸 시 최장 경로 재선택) ②위상 전진(속도 필드, 기본 2 타일/s) ③위치·회전 계산(경로 보간 + 차선 오프셋 재사용 개념은 생략 — 중앙선 주행) ④`D` 토글.
- 카메라 GO: `new GameObject("DriveViewCamera")` + Camera, rect·depth 설정, 부모 = view.transform.
- OnDestroy: 카메라 GO Destroy.

## §2. 검증 (Play 프로그래매틱 스모크)

- 컴파일 + EditMode 151 그린(회귀 게이트).
- Play: 도시 구성(경로 생김) → 펌핑 → ①"DriveViewCamera" GO·Camera 존재, rect 우상단 ②펌핑 사이 카메라 위치가 변함(주행 증거) ③경로 0개 상태(빈 도시)에서 카메라 비활성. 비포커스 규약, 워킹트리 클린.

## 비목표

신호 정지 반응, 차량 추월/회피 연출, RenderTexture·UI 프레임(김건 영역), 사운드, 씬 파일 변경.
