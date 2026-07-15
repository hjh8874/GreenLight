# MM식 로터리 풋프린트 (Approach A) — 설계

**날짜**: 2026-07-15
**작성**: 환 + Claude (브레인스토밍)
**대상 레포**: GreenLight (`~/Gamemaker/GreenLight`), 브랜치 `develop` 기반

## 배경 / 문제
현재 로터리는 **교차로 1타일에 얹는 추상 계수**(용량 ×0.7 + 교차 간섭 λr 0.25). 신호와 체감이 비슷하고, 미니 모터웨이(MM)식 "공간을 차지하는 구조물" 느낌이 없다.

웹 리서치 결론: MM 로터리조차 **풀 물리가 아니라** "공간 비용 + 단순 양보 규칙 + 트레이드오프"다. 우리 흐름 수식은 이미 트레이드오프를 근사한다. 빠진 건 **공간 풋프린트 + 비주얼**뿐. → 풀 시뮬레이션(Cities:Skylines식)은 방치형에 과함.

## 목표 (Approach A)
로터리를 **십자 5칸 풋프린트를 실제로 차지**하는 배치물로 만든다. **흐름·라우팅·세이브 포맷은 무변경** — 순수 "배치 규칙 + 비주얼" 레이어만 추가.

**비목표**: 차가 링을 실제로 도는 에이전트 라우팅(= v2 폴리싱), 풀 물리, 3×3 확장.

## 설계

### ① 풋프린트 & 배치 규칙 (SimEngine)
- 풋프린트 = **중앙(교차로) + 상하좌우 4팔 = 십자 5칸**. 대각 제외.
- 저장 무변경: `_roundaboutSet`에 **center만** 저장. 풋프린트는 `center ± 단위벡터(상하좌우)`로 파생.
- 새 공개 헬퍼 `bool IsInRoundaboutFootprint(Vector2Int tile)`: `tile ∈ _roundaboutSet` **또는** tile의 상하좌우 이웃 중 하나가 center.
- `CanPlaceRoundabout(center)` 확장 조건:
  - center가 교차로 (기존)
  - center가 기존 4형제(_placedSet/_overpassSet/_priorityDirs/_turnSigns)·다른 로터리 풋프린트와 안 겹침
  - **인바운드 팔만 검사**(OOB 팔은 스킵 → 가장자리 교차로는 부분 풋프린트로 허용): 건물(House/Office/School) 아님 **AND** 어떤 장치(signal/overpass/priority/turnsign/oneway/roundabout)도 없음 **AND** 다른 로터리 풋프린트 아님
  - (Road·Empty 팔은 허용 — "건물만 없으면 OK", MM식. OOB 스킵이라 기존 테스트 계약 유지 = 순수 추가)

### ② 흐름 (무변경)
- FlowSolver·RoutePlanner·세이브 전부 그대로. center 1노드 수식(×0.7 + λr) 유지. 팔은 흐름 무관(기존 도로면 그대로 흐르고, 빈칸이면 비통행·장식).

### ③ 배치 충돌 (다른 장치·건물)
- `CanPlaceSignal/Overpass/PriorityRoad/TurnSign/Oneway`: 기존 `!_roundaboutSet.Contains(tile)` → `!IsInRoundaboutFootprint(tile)` (center뿐 아니라 팔도 예약)
- `Place(tile, 건물)`: 풋프린트면 거부 (도로 배치는 허용)
- 철거: center 제거 시 풋프린트 자동 해제(파생이라 공짜)

### ④ 비주얼 (MainCityView)
- `RefreshRoundabouts`: center 마커 1개 → **5칸 링 렌더**(중앙 섬 + 4팔 링 세그먼트), `roundaboutColor` 재사용.
- v1: 차량은 center 직진 통과(현행 유지). 링 도는 애니 = v2.

### ⑤ 검증
- EditMode 테스트 추가(`RoundaboutFootprintTests`): 인바운드 거부, 건물 팔 거부, 장치 겹침 거부, 팔에 타 장치 배치 거부, 철거 후 팔 해제.
- 디버그 리그 `2`키로 수동 배치 + 충돌 확인.

## 리스크 / 트레이드오프
- "빈칸 팔에 링만 그림 = 실제 도로 연결 아님"이라는 의미 오차 → MM도 공간예약이라 허용.
- 팀 공유 심 코드 변경이지만 **흐름/세이브 무변경**이라 진우 코드 리스크 최소. PR로 공유.
- 기존 세이브 호환: center만 저장하므로 구버전 세이브의 로터리도 그대로 로드(풋프린트는 로드 후 파생).

## 파일 영향
- `SimEngine.cs`: 헬퍼 + CanPlaceRoundabout 확장 + 5개 CanPlace 수정 + Place 가드
- `MainCityView.cs`: RefreshRoundabouts/CreateRoundaboutVisual 링 렌더 + 궤도 반경(0.68) + 로터리 무정지 통과(IsRouteVehicleBlocked 점유 배타 스킵)
- `Tests/EditMode/RoundaboutFootprintTests.cs`: 신규

## 튜닝 노트 (2026-07-15 측정 — "이대로 가되 나중에 튜닝 오픈")
**노브는 이미 인스펙터 튜닝 가능**: `SimConfig`의 `RoundaboutCapacityFactor`(cf, 현재 0.7)·`RoundaboutInterference`(λr, 0.25) — `SimConfig_Integrated.asset`에서 조정. 코드 변경 불필요.

**측정된 효과(균형 십자 교차로, 무장치 대비 처리량):**
| 수요 | 무장치 | 로터리 | 차이 |
|---|---|---|---|
| 0.8 (한산) | 14.4 | 14.4 | 0% |
| 1.5 (적당 혼잡) | 14.0 | 18.3 | **+31%** |
| 2.5 (과포화) | 10.1 | 10.1 | 0% |

**핵심**: 효율 E는 ratio>1.0(JamRatio)부터만 떨어져 → 로터리는 **"교차로가 병목인 적당한 균형 혼잡"에서만** 티가 남(스위트스팟 좁음). 한산·과포화·편중에선 차이 0.

**튜닝 방향(체감 넓히려면):**
- `RoundaboutCapacityFactor` 0.7→0.85: 용량 페널티 완화 → 더 넓은 혼잡 범위에서 이득. 단 cf가 1에 가까우면 편중 교차로에서도 신호를 이겨버려 **3분할 전략(균형=로터리/편중=신호/극단=무신호)이 죽음** — cf<1 유지가 균형추.
- `EfficiencyMinRatio`/`JamRatio`(효율 곡선): 낮추면 더 이른 혼잡부터 장치 효과가 보임(전 장치 공통 영향).
- 별개 관측: 과포화(수요 2.5)에서 처리량이 오히려 하락(14→10) — 로터리 무관, 경제/수요 모델 이슈로 추후 조사 대상.

## 현재 상태 / 남은 일 (2026-07-15)
**완료 (fix-infra-prices-sheet-hwan 워킹트리, 미커밋):**
- 풋프린트 + 배치 규칙 + Place 가드 (SimEngine) — EditMode 224/224 통과
- 도로색 링 + 초록 섬 비주얼 (MainCityView.CreateRoundaboutVisual)
- 흐름 역할 검증 (균형=로터리 최적, 측정 +31%)
- 차량 순환 (꺾는 차도 로터리 밟게 하는 bridge 수정 line ~1446 + CCW 궤도) — **동작하나 애니 미완**

**PR 보류** (주석님 요청 — 나중에 올림). 올릴 때 git 주의:
- `SimEngine.cs`: develop과 committed 차이 없음 → **develop에 깔끔히 PR 가능**(풋프린트+규칙)
- `MainCityView.cs`: 브랜치가 develop과 **크게 divergence**(develop이 더 최신) → 내 변경 깔끔히 안 붙음. 애니 재작업과 함께 develop 최신 위에서 처리.
- 새 파일: `RoundaboutFootprintTests.cs`, 이 스펙 문서.

**할일 (deferred):**
1. **로터리 PR** — 주석님 승인 후. SimEngine+테스트+스펙은 clean, MainCityView는 divergence 처리 필요.
2. **차량 애니 재작업 (웨이포인트 방식)** — 현재 "위치 오버라이드"가 취약(진입 스냅=리스폰·속도 빨려듦·충돌 반복). 링 경로를 DisplayRoute 웨이포인트로 심어 일반 주행 로직이 처리하게 = 연속성·속도·충돌 자동 해결. 폐기: 위치블렌드·속도정규화·커스텀충돌. 유지/이식: 링 비주얼·bridge 수정·CCW 각 수식. **교통 흐르는 상태로 라이브 검증 필수**(정적 스샷 불가).
