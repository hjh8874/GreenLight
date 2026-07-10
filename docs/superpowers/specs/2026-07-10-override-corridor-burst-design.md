# 설계 — 오버라이드 = 그린 코리도어 버스트 + Burst 연출(사운드·카메라)

> 작성: 2026-07-10 (환 브레인스토밍). 짝 계획: writing-plans로 후속 생성.
> 선행: [[E-1 ISignalControl 오버라이드 계약 승격]] (로컬 커밋 `91cc33d`) — 뷰가 오버라이드를 계약으로 조회 가능.
> 대체: 07-09 노트 `todo-2026-07-10-override-burst`의 대상 뷰(SimTileRenderer)가 stale해져 재정렬.

## 배경 / 목표

오버라이드 스킬의 손맛을 "교차로 1개를 20초 유지"(현행)에서 **"라인의 신호 여러 개가 잠깐 초록으로 뚫리는 짧고 강한 버스트"**(환 구상)로 바꾼다. 동시에 FlowBurst(체증 해소 보상)에 청각·카메라 펀치를 더해 "뚫렸다"는 피드백을 강화한다.

두 축, 서로 독립:
- **A. 오버라이드 = 그린 코리도어 버스트** (엔진 + 뷰)
- **B. FlowBurstJuice** (사운드 + 카메라, 순수 연출)

결정론·세이브 불변: A의 엔진 변경은 순수함수 grid walk, B는 뷰 전용.

---

## A. 오버라이드 = 그린 코리도어 버스트

### A-엔진: 코리도어 확장

현행 `SimEngine.TryOverrideSignal(anchor, horizontal)`은 anchor **한 신호**만 강제 초록. 이를 **같은 일자 라인의 교차로 신호 최대 3개**를 함께 강제 초록으로 확장한다.

**코리도어 수집 규칙 ("직진만")**:
- 선택 축 방향으로만 걷는다: `horizontal=true` → anchor.y 행을 x±로, `false` → anchor.x 열을 y±로.
- anchor에서 **연속 도로 타일**을 따라 양방향으로 걸으며, 만나는 **교차로 신호**(SignalMap에 있는 타일)를 수집. 도로가 끊기면 그 방향 종료.
- 직각으로 꺾인 도로의 신호는 제외 — 오직 그 라인 위 직진으로 이어진 신호만("최근접 대신 직진만").
- anchor 포함 **최근접 순 최대 N=3개**. 라인에 2개뿐이면 2, anchor뿐이면 1(우아한 축소).
- 결정론: grid walk가 고정 순서(x/y 증가) + 거리순 → 같은 배치·같은 탭 = 같은 집합.

**적용**: 수집된 모든 신호에 `OverrideUntil = simTime + OverrideDurationSeconds`, 동일 `OverrideHorizontal = horizontal`. 쿨다운은 멤버 각자 `_overrideReadyAt` 설정. **게이트는 anchor 기준**(anchor가 쿨다운 중이면 탭 거절; 코리도어 멤버 개별 쿨다운은 재수집을 막지 않되 자기 탭의 게이트로만 작동).

FlowSolver·GetSignalPhase·IsSignalGreen은 이미 **신호별** `OverrideUntil`을 읽으므로 변경 불필요 — 코리도어는 "더 많은 신호를 오버라이드"할 뿐.

### A-파라미터 (SimConfig)

| 필드 | 현재 | 변경 |
|---|---|---|
| `OverrideDurationSeconds` | 20 | **3** |
| `OverrideCooldownSeconds` | 30 | **60** |
| `OverrideCorridorSignals` (신설) | — | **3** |

업타임 ≈ 3/63 ≈ 5% — 짧고 강한 버스트. asset(SimConfig SO)과 `Default()` 둘 다 갱신.

### A-뷰: 속도 연출 + 특수효과 (MainCityView, 라이브 3D 뷰)

- **속도 부스트**: 오버라이드 활성 신호의 라인 위 차량 렌더 속도↑. 판정 = `signalControl.GetOverrideSecondsLeft(tile) > 0` (E-1 계약 조회 — 구현체 캐스팅 불필요). **순수 시각** — 처리량·코인·결정론 무관.
- **특수효과**: 코리도어 신호에 오버라이드 FX(초록 글로우/펄스). MainCityView의 기존 Burst 비주얼 패턴(스폰→스케일 펄스→소멸) 재사용.
- ⚠️ 차가 빨라지면 간격이 벌어져 "도로가 비어" 보일 수 있음 → 부스트 배율은 보수적으로 시작, 플레이로 튜닝.

---

## B. FlowBurstJuice (사운드 + 카메라 펀치)

**새 컴포넌트** `View/FlowBurstJuice.cs` (`ICityFlowServiceConsumer`), `services.Events.FlowBurst` 구독. 기존 `FlowBurstView`(순수 마커)·MainCityView 버스트 비주얼은 건드리지 않음 — 청각·카메라만 담당하는 독립 유닛(엔진 이벤트만 듣기 때문에 뷰 교체·중복과 무관).

- **사운드**: `SoundManager.Instance?.PlaySfx(burstSfxId, volume)`. `volume`은 `e.Reward` 비례(clamp01). `burstSfxId`는 `[SerializeField]`(기본 `"flow_burst"`). SoundCatalog에 엔트리 없으면 조용히 no-op → 클립 에셋 없어도 무사고, 나중에 아티스트가 추가하면 자동 발동.
- **카메라 펀치**: `Camera.main.DOShakePosition(dur, strength)` (DOTween — Assets/Plugins/Demigiant). `strength`는 Reward 비례, xy만(2D 직교), `SetUpdate(true)`(일시정지 무관). 상한으로 멀미 방지.
- **배선**: CityBootstrap이 다른 컨슈머처럼 자동 Initialize.

---

## 비범위 (명시적 제외)

- **A-2 엔진 용량 부스트**: 오버라이드 중 처리량을 듀티 1 초과로 밀기 — 환 우려("좁은 도로 초과 통과가 어색"). A(속도 연출)만으로 손맛 충분한지 플레이로 본 뒤 별도 판단. 이번 스코프에서 제외.
- **DebugSignalTuner**: 계약 밖 `DeliveredTotal` 의존이라 구현체 캐스팅 유지. 손 안 댐.
- **FlowBurstView / MainCityView 버스트 비주얼 중복 정리**: 별개 관심사. 이번엔 juice만 얹음.

---

## 검증 계획

- **엔진 결정론 테스트(신설 1종)**: 같은 배치(라인에 교차로 3개)에서 anchor 탭 → 코리도어가 정확히 3개(또는 라인 사정에 맞게) 오버라이드, 직각 신호는 제외됨, 같은 입력 = 같은 delivered/해시.
- **기존 테스트 불변**: `OverrideSignal_ForcesAxisGreen_ThenCooldownAndExpiry`는 고립 단일 교차로라 코리도어=1 → assertion 그대로 통과. 전체 EditMode 회귀 없음(현재 baseline 99).
- **플레이 검증(SimDebug/통합 씬)**: ①오버라이드 탭 → 라인 신호 3개 동시 초록 ~3초 ②그 라인 차량 눈에 띄게 빨라짐 + FX ③쿨다운 60초 동안 재탭 거절 ④FlowBurst 시 비프(카탈로그에 클립 있을 때)+카메라 톡, Reward 클수록 세게.

## 구현 순서 (writing-plans에서 상세화)

1. B: `FlowBurstJuice` (독립·안전·엔진 무관) → 커밋
2. A-엔진: SimConfig 파라미터 + 코리도어 수집 + 결정론 테스트 → 커밋
3. A-뷰: MainCityView 속도 부스트 + FX → 커밋
4. 전체 EditMode + 플레이 검증

브랜치: `feat-override-corridor-hwan` (develop 분기, PR to develop).
